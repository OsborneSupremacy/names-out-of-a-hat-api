namespace GiftExchange.Library.Services;

/// <summary>
/// The work behind leaving a gift exchange, wherever the button was pressed.
/// </summary>
/// <remarks>
/// Two callers. <see cref="LeaveGiftExchangeService"/> reaches a route through the leave token in an
/// invitation; <see cref="ParticipantLeaveService"/> reaches the same route through a signed-in
/// session. Why leaving exists and why organizers cannot do it is on
/// <see cref="LeaveGiftExchangeService"/>.
///
/// The order of operations below is deliberate: refusals are written before the participant is
/// removed. The failure mode of the other order is somebody removed from the exchange and freely
/// re-addable, which is precisely the thing they came here to prevent; the failure mode of this one
/// is somebody blocked from an exchange they are still in, which the next attempt fixes.
/// </remarks>
[UsedImplicitly]
internal class GiftExchangeLeaving
{
    /// <summary>
    /// Statuses in which the picks everybody has been told are still the operative ones, and so the
    /// statuses in which the rest of the exchange has to be told to disregard theirs.
    /// </summary>
    /// <remarks>
    /// Only <c>INVITATIONS_SENT</c>. Before it, nobody has been told a name, so there is nothing to
    /// disregard; after it — the cool-off period and beyond — the exchange has either happened or
    /// is about to, and mailing everybody to say a name they have already shopped for is void would
    /// be worse than the removal it is reporting.
    /// </remarks>
    private static readonly ImmutableList<string> BroadcastStatuses = [HatStatus.InvitationsSent];

    /// <summary>
    /// Statuses that go back to <c>IN_PROGRESS</c> when somebody leaves, because the exchange still
    /// has a draw ahead of it and the one it holds is now wrong.
    /// </summary>
    /// <remarks>
    /// <c>READY_TO_CLOSE</c> and <c>CLOSED</c> are deliberately absent. Reopening a finished
    /// exchange would ask an organizer to redraw names for gifts that have already changed hands.
    /// <c>IN_PROGRESS</c> is absent because it is already there.
    /// </remarks>
    private static readonly ImmutableList<string> RedrawStatuses =
        [HatStatus.ReadyForAssignment, HatStatus.NamesAssigned, HatStatus.InvitationsSent];

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly LeaveEmailCompositionService _emailCompositionService;

    private readonly IEmailQueue _emailQueue;

    private readonly ILogger<GiftExchangeLeaving> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public GiftExchangeLeaving(
        GiftExchangeProvider giftExchangeProvider,
        LeaveEmailCompositionService emailCompositionService,
        IEmailQueue emailQueue,
        ILogger<GiftExchangeLeaving> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _emailCompositionService = emailCompositionService ?? throw new ArgumentNullException(nameof(emailCompositionService));
        _emailQueue = emailQueue ?? throw new ArgumentNullException(nameof(emailQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Whether leaving still costs the rest of the exchange a redraw.
    /// </summary>
    /// <remarks>
    /// Read by both confirmations and by the organizer's email, so that none of them describes a
    /// redraw for an exchange that has already been revealed. Broader than
    /// <see cref="RedrawStatuses"/> on purpose: an exchange that has not been shaken yet has a draw
    /// ahead of it too, and saying so is accurate even though no status changes.
    /// </remarks>
    public static bool ShowsConsequences(string hatStatus) =>
        hatStatus != HatStatus.CooledOff && hatStatus != HatStatus.Closed;

    /// <summary>
    /// Takes the participant out of the exchange, records the refusals they asked for, and tells
    /// whoever has to be told.
    /// </summary>
    /// <returns>False when the exchange could not be found, in which case nothing was done.</returns>
    public async Task<bool> LeaveAsync(LeaveRoute route, bool blockOrganizer, bool blockAnywhere)
    {
        // Read while they are still in it. Everything below needs the exchange as it was — who to
        // write to, and what the hat looked like when the message was composed — and after the
        // delete the leaver is no longer in the list and the participant ids are the survivors'
        // only by accident.
        var (hatExists, hat) = await _giftExchangeProvider
            .GetHatAsync(route.Organizer.Email, route.HatId)
            .ConfigureAwait(false);

        if (!hatExists)
            return false;

        var participantIds = await _giftExchangeProvider
            .GetParticipantIdsByEmailAsync(route.HatId)
            .ConfigureAwait(false);

        // First, and before the removal. See the class remarks: the failure mode of the other
        // ordering is exactly what somebody came here to prevent.
        await _giftExchangeProvider
            .RecordDoNotAddAsync(new RecordDoNotAddRequest
            {
                Email = route.Leaver.Email,
                HatId = route.HatId,
                OrganizerEmail = route.Organizer.Email,
                BlockOrganizer = blockOrganizer,
                BlockAnywhere = blockAnywhere
            })
            .ConfigureAwait(false);

        await _giftExchangeProvider
            .RemoveParticipantFromEligibleRecipientsAsync(
                route.Organizer.Email,
                route.HatId,
                route.Leaver.Name)
            .ConfigureAwait(false);

        // Takes their leave token with it, so the email's page cannot be submitted twice into two
        // removals. A second submission from either door finds nothing.
        await _giftExchangeProvider
            .DeleteParticipantAsync(route.Organizer.Email, route.HatId, route.Leaver.Email)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Participant {ParticipantId} left hat {HatId}, which was {HatStatus}.",
            route.ParticipantId,
            route.HatId,
            route.HatStatus);

        if (RedrawStatuses.Contains(route.HatStatus))
            await _giftExchangeProvider
                .UpdateHatStatusAsync(route.Organizer.Email, route.HatId, HatStatus.InProgress)
                .ConfigureAwait(false);

        await NotifyAsync(hat, route, participantIds).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Tells the exchange, and tells the organizer.
    /// </summary>
    /// <remarks>
    /// Both go through the invitation queue rather than the automatic sender, so a bounce or a
    /// complaint on either is recorded against the participant the way an invitation's is. The
    /// organizer is a participant of their own exchange, so their notice carries a real participant
    /// id and is tagged apart from the one everybody got.
    ///
    /// The broadcast goes out even where the queue has nothing to say about whether it arrived. It
    /// is fanned out the way invitations are — one enqueue each, awaited together — because a
    /// participant whose message failed to queue should not stop the rest from being told.
    /// </remarks>
    private async Task NotifyAsync(
        Hat hat,
        LeaveRoute route,
        ImmutableDictionary<string, Guid> participantIds
    )
    {
        var enqueueTasks = new List<Task>();

        if (BroadcastStatuses.Contains(route.HatStatus))
        {
            // Composed once. Every copy is identical, which is not only cheaper but part of the
            // design: a message that varied by recipient is a message that could be compared.
            var notice = _emailCompositionService.ComposeParticipantNotice(hat);
            var subject = LeaveEmailCompositionService.GetParticipantSubject(hat);

            var remaining = hat.Participants
                .Where(participant => !participant.Person.Email.ContentEquals(route.Leaver.Email));

            enqueueTasks.AddRange(remaining.Select(participant => _emailQueue.EnqueueAsync(
                new GiftExchangeEmailRequest
                {
                    HatId = route.HatId,
                    OrganizerEmail = route.Organizer.Email,
                    RecipientEmail = participant.Person.Email,
                    ParticipantId = participantIds.GetValueOrDefault(participant.Person.Email, Guid.Empty),
                    MessageType = EmailMessageType.ParticipantLeft,
                    Subject = subject,
                    HtmlBody = notice,
                    SenderName = route.Organizer.Name
                })));
        }

        // Always, whatever the status. An organizer whose exchange has finished still needs to know
        // that somebody took themselves out of it, and still needs the advice at the end of it.
        enqueueTasks.Add(_emailQueue.EnqueueAsync(new GiftExchangeEmailRequest
        {
            HatId = route.HatId,
            OrganizerEmail = route.Organizer.Email,
            RecipientEmail = route.Organizer.Email,
            ParticipantId = participantIds.GetValueOrDefault(route.Organizer.Email, Guid.Empty),
            MessageType = EmailMessageType.OrganizerParticipantLeft,
            Subject = LeaveEmailCompositionService.GetOrganizerSubject(hat),
            HtmlBody = _emailCompositionService
                .ComposeOrganizerNotice(hat, route.Leaver, ShowsConsequences(route.HatStatus))
        }));

        await Task.WhenAll(enqueueTasks).ConfigureAwait(false);
    }
}
