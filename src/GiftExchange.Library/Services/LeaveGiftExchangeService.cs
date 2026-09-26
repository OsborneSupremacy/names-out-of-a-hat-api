using System.Web;

namespace GiftExchange.Library.Services;

/// <summary>
/// Leaving: one participant taking themselves out of a gift exchange they did not ask to be in.
/// </summary>
/// <remarks>
/// Until this existed there was no way out. An organizer can type any address into an exchange, the
/// invitation arrives unannounced, and the only recourse was to write to them and hope. Nothing in
/// this application stopped a send either — a bounce or a complaint was recorded and shown, and the
/// next invitation went out regardless.
///
/// Two endpoints for one action, and the split is the security design rather than an accident of
/// REST, for the same reason the Ask is split. Following a link in an email is a GET, and mail
/// security scanners fetch those on delivery. A GET that removed the participant would fire before
/// the reader had opened anything: somebody pulled out of an exchange they had not yet read they
/// were in, everybody else told to disregard a name, and the organizer sent back to the hat. So the
/// GET renders a form, and the POST behind the button on it does the work.
///
/// Not available to organizers, and the way that is enforced is that no leave token is ever issued
/// for one. A flag would have to be checked, and a check can be forgotten; a lookup that finds
/// nothing cannot be. An organizer who somehow reaches this address gets the same page as a guessed
/// token.
///
/// The order of operations below is deliberate: refusals are written before the participant is
/// removed. The failure mode of the other order is somebody removed from the exchange and freely
/// re-addable, which is precisely the thing they came here to prevent; the failure mode of this one
/// is somebody blocked from an exchange they are still in, which the next attempt fixes.
/// </remarks>
[UsedImplicitly]
internal class LeaveGiftExchangeService : IApiGatewayHandler
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

    private readonly LeavePageComposer _pageComposer;

    private readonly LeaveEmailCompositionService _emailCompositionService;

    private readonly IEmailQueue _emailQueue;

    private readonly ILogger<LeaveGiftExchangeService> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public LeaveGiftExchangeService(
        GiftExchangeProvider giftExchangeProvider,
        LeavePageComposer pageComposer,
        LeaveEmailCompositionService emailCompositionService,
        IEmailQueue emailQueue,
        ILogger<LeaveGiftExchangeService> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _pageComposer = pageComposer ?? throw new ArgumentNullException(nameof(pageComposer));
        _emailCompositionService = emailCompositionService ?? throw new ArgumentNullException(nameof(emailCompositionService));
        _emailQueue = emailQueue ?? throw new ArgumentNullException(nameof(emailQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        // Case preserved. The token is base64url, so "aB" and "Ab" are different tokens, and the
        // ordinary instinct to normalise an identifier out of a URL would break every leave link.
        var token = request.PathParameters is not null
            && request.PathParameters.TryGetValue("token", out var pathToken)
                ? pathToken
                : string.Empty;

        var (found, route) = await _giftExchangeProvider
            .FindLeaveRouteAsync(SecretToken.Hash(token))
            .ConfigureAwait(false);

        // Unknown, spent, or an organizer's guess: one page, and the sameness is the point. The
        // difference between them would tell somebody holding a guessed token whether it named a
        // real participant, which here is worth more than it is on the Ask — a token that resolves
        // is a token that removes somebody.
        if (!found)
            return Page(LeavePageComposer.ComposeUnavailable());

        return request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            ? await LeaveAsync(request, route).ConfigureAwait(false)
            : Page(_pageComposer.ComposeConfirm(route, token, ShowsConsequences(route.HatStatus)));
    }

    private async Task<APIGatewayProxyResponse> LeaveAsync(APIGatewayProxyRequest request, LeaveRoute route)
    {
        var blockOrganizer = IsTicked(request, LeavePageComposer.BlockOrganizerField);
        var blockAnywhere = IsTicked(request, LeavePageComposer.BlockAnywhereField);

        // Read while they are still in it. Everything below needs the exchange as it was — who to
        // write to, and what the hat looked like when the message was composed — and after the
        // delete the leaver is no longer in the list and the participant ids are the survivors'
        // only by accident.
        var (hatExists, hat) = await _giftExchangeProvider
            .GetHatAsync(route.Organizer.Email, route.HatId)
            .ConfigureAwait(false);

        if (!hatExists)
            return Page(LeavePageComposer.ComposeUnavailable());

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

        // Takes their leave token with it, so this page cannot be submitted twice into two
        // removals. A second submission finds nothing and gets the unavailable page.
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

        return Page(_pageComposer.ComposeLeft(route, blockOrganizer, blockAnywhere));
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

    /// <summary>
    /// Whether leaving still costs the rest of the exchange a redraw.
    /// </summary>
    /// <remarks>
    /// Read by the confirm page and by the organizer's email, so that neither describes a redraw
    /// for an exchange that has already been revealed. Broader than
    /// <see cref="RedrawStatuses"/> on purpose: an exchange that has not been shaken yet has a draw
    /// ahead of it too, and saying so is accurate even though no status changes.
    /// </remarks>
    private static bool ShowsConsequences(string hatStatus) =>
        hatStatus != HatStatus.CooledOff && hatStatus != HatStatus.Closed;

    /// <summary>
    /// Whether one checkbox came back ticked.
    /// </summary>
    /// <remarks>
    /// Tolerant, as the Ask's form parsing is: an unreadable body means nothing was ticked, which
    /// is the safe reading of it. These two boxes only ever add a refusal, so failing to see one
    /// costs somebody a refusal they asked for and the page tells them what was recorded; misreading
    /// junk as a tick would opt somebody out of every gift exchange they are ever invited to.
    /// </remarks>
    private static bool IsTicked(APIGatewayProxyRequest request, string field)
    {
        var body = request.Body ?? string.Empty;

        if (request.IsBase64Encoded && body.Length > 0)
        {
            try
            {
                body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return HttpUtility.ParseQueryString(body).GetValues(field) is not null;
    }

    /// <summary>
    /// Every outcome is a 200 carrying a page, including the ones that did nothing.
    /// </summary>
    /// <remarks>
    /// The same reasoning as the Ask's: a status code would be read by the scanner that fetched
    /// this before any person did, and there is nobody for a 404 to inform. The reader is a human
    /// looking at a browser tab.
    /// </remarks>
    private static APIGatewayProxyResponse Page(string html) =>
        new()
        {
            StatusCode = (int)HttpStatusCode.OK,
            Body = html,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "text/html; charset=utf-8",
                // A cached confirm page shown after the fact would offer to do something that has
                // already been done, and a cached result page would report an outcome twice.
                ["Cache-Control"] = "no-store"
            }
        };
}
