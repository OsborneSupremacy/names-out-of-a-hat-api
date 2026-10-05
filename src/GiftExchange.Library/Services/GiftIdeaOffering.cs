namespace GiftExchange.Library.Services;

/// <summary>
/// The work behind offering gift ideas about another participant without being asked, wherever the
/// button was pressed.
/// </summary>
/// <remarks>
/// Two callers. <see cref="OfferGiftIdeasService"/> reaches a route through the participant's own
/// token in an email; <see cref="ParticipantGiftIdeasService"/> reaches the same route through a
/// signed-in session. Why offering exists, and why only the participant's own token opens it, is on
/// <see cref="OfferGiftIdeasService"/>.
///
/// <b>Nothing this returns says whether anything was sent.</b> See <see cref="ForwardAsync"/>.
/// </remarks>
[UsedImplicitly]
internal class GiftIdeaOffering
{
    /// <summary>
    /// How long one participant must wait before offering ideas about the same person again.
    /// </summary>
    /// <remarks>
    /// A week, matching the Ask. This is the only limit on a message nobody asked for, so it is
    /// doing more work here than it does there: it caps what any one inbox receives from any one
    /// sender, which is the number that matters.
    /// </remarks>
    private static readonly TimeSpan OfferWindow = TimeSpan.FromDays(7);

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IReplyThrottleProvider _replyThrottleProvider;

    private readonly GiftIdeaContentPolicy _contentPolicy;

    private readonly IContentModerationService _contentModerationService;

    private readonly GiftIdeaEmailCompositionService _composer;

    private readonly AutomaticEmailSender _sender;

    private readonly InvitationReminderService _invitationReminder;

    private readonly ILogger<GiftIdeaOffering> _logger;

    public GiftIdeaOffering(
        GiftExchangeProvider giftExchangeProvider,
        IReplyThrottleProvider replyThrottleProvider,
        GiftIdeaContentPolicy contentPolicy,
        IContentModerationService contentModerationService,
        GiftIdeaEmailCompositionService composer,
        AutomaticEmailSender sender,
        InvitationReminderService invitationReminder,
        ILogger<GiftIdeaOffering> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _replyThrottleProvider = replyThrottleProvider ?? throw new ArgumentNullException(nameof(replyThrottleProvider));
        _contentPolicy = contentPolicy ?? throw new ArgumentNullException(nameof(contentPolicy));
        _contentModerationService = contentModerationService ?? throw new ArgumentNullException(nameof(contentModerationService));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _invitationReminder = invitationReminder ?? throw new ArgumentNullException(nameof(invitationReminder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks the offer, and if nothing stops it, stores it and passes it on.
    /// </summary>
    /// <remarks>
    /// The order is deliberate and is not the Ask's. That one claims its throttle slot first,
    /// reasoning that a refusal costs nothing — true there, where nobody has written anything yet.
    /// Here the participant has already typed a paragraph, and spending their week on a submission
    /// that was then refused for a shortened link would be indefensible. So: resolve, then check,
    /// then claim, then store, then send.
    /// </remarks>
    /// <param name="route">The participant's own route. Never a contribution's.</param>
    /// <param name="subjectParticipantId">From a form the sharer could edit.</param>
    /// <param name="ideas">Already trimmed.</param>
    public async Task<OfferResult> OfferAsync(GiftIdeaRoute route, Guid subjectParticipantId, string ideas)
    {
        // Re-resolved against the database rather than trusted from the page, for the reason
        // FindOfferTargetAsync gives: this form was rendered by us and edited by them.
        var (found, target) = subjectParticipantId == Guid.Empty || route.IsContribution
            ? (false, OfferTargets.Empty)
            : await _giftExchangeProvider
                .FindOfferTargetAsync(new FindOfferTargetRequest
                {
                    HatId = route.HatId,
                    SharerParticipantId = route.ParticipantId,
                    SubjectParticipantId = subjectParticipantId
                })
                .ConfigureAwait(false);

        // One answer for "you picked nobody" and for "you picked somebody you may not pick". The
        // second is only reachable by editing the form, and somebody who has done that is not owed
        // an explanation of which name they were not allowed to use.
        if (!found)
            return Result(OfferOutcome.NobodyChosen, OfferTargets.Empty);

        var outcome = await CheckAsync(ideas, route).ConfigureAwait(false);

        if (outcome != GiftIdeaSubmissionOutcome.Shared)
        {
            _logger.LogInformation("Refused an offer of gift ideas: {Outcome}", outcome);

            return Result(OfferOutcome.Refused, target) with { Refusal = outcome };
        }

        var slot = await _replyThrottleProvider
            .TryReserveOfferSlotAsync(new ReserveOfferSlotRequest
            {
                SharerParticipantId = route.ParticipantId,
                SubjectParticipantId = target.SubjectParticipantId,
                Window = OfferWindow
            })
            .ConfigureAwait(false);

        // Nothing is stored either. An offer that was not passed on is not a record of anything,
        // and keeping it would make the next week's submission look like a repeat.
        if (!slot.Reserved)
            return Result(OfferOutcome.AlreadyOffered, target) with { PreviouslyOfferedAt = slot.PreviouslyReservedAt };

        // Stored before anything is sent. If sending fails after this the text still exists; the
        // reverse would lose what somebody wrote.
        await _giftExchangeProvider
            .AddOfferedGiftIdeaAsync(new AddOfferedGiftIdeaRequest
            {
                AuthorParticipantId = route.ParticipantId,
                SubjectParticipantId = target.SubjectParticipantId,
                Ideas = ideas
            })
            .ConfigureAwait(false);

        await ForwardAsync(route, target, ideas).ConfigureAwait(false);

        return Result(OfferOutcome.Offered, target);
    }

    private static OfferResult Result(OfferOutcome outcome, OfferTarget target) =>
        new()
        {
            Outcome = outcome,
            Refusal = GiftIdeaSubmissionOutcome.Shared,
            SubjectParticipantId = target.SubjectParticipantId,
            SubjectName = target.SubjectName,
            PreviouslyOfferedAt = DateTimeOffset.MinValue
        };

    /// <summary>
    /// Sends the offer to whoever holds the subject's name, when there is somebody and it is not
    /// the sender.
    /// </summary>
    /// <remarks>
    /// Both silent outcomes return without touching the result, which is the whole point:
    /// <see cref="OfferAsync"/> reports the same thing whichever of the three happened, so nobody can
    /// learn the draw by offering ideas about each participant in turn and watching what changes.
    ///
    /// The second check is unreachable today and is kept anyway. The candidate filter and the
    /// giver lookup both read <see cref="ParticipantEntity.PickedRecipientParticipantId"/>, so a
    /// subject that survived the first cannot resolve to the sender as its giver. They are separate
    /// queries that could be changed apart, though, and what this prevents — an offer coming back
    /// to its own author, carrying the name of the person they drew — is worth one comparison.
    ///
    /// Neither log line names anybody. A log that said who had no giver would be a record of the
    /// draw.
    /// </remarks>
    private async Task ForwardAsync(GiftIdeaRoute route, OfferTarget target, string ideas)
    {
        if (string.IsNullOrWhiteSpace(target.Giver.Email))
        {
            _logger.LogInformation("Stored an offer of gift ideas with nobody yet to forward it to.");
            return;
        }

        if (target.GiverParticipantId == route.ParticipantId)
        {
            _logger.LogInformation("Stored an offer of gift ideas that would have returned to its sender.");
            return;
        }

        var invitationReminder = await _invitationReminder
            .ComposeForAsync(target.GiverParticipantId)
            .ConfigureAwait(false);

        await _sender.SendAsync(
                target.Giver.Email,
                GiftIdeaEmailCompositionService.ContributionForwardSubject(route.DisplayNameOf(route.Sender), target.SubjectName),
                _composer.ComposeOfferedIdeasForward(
                    route.DisplayNameOf(route.Sender), target.SubjectName, route.HatName, ideas, invitationReminder))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether there is anything about this submission that stops it being passed on.
    /// </summary>
    /// <remarks>
    /// The same two checks in the same order <see cref="GiftIdeaSharing"/> runs, cheapest first, so
    /// text refused on a rule this application can apply itself never reaches Comprehend.
    ///
    /// The name looked for is the sender's own pick, not the subject. The check is about what the
    /// sender must not leak, and the two cannot collide: the subject is somebody the sender did not
    /// draw, which is exactly who may be offered.
    /// </remarks>
    private async Task<GiftIdeaSubmissionOutcome> CheckAsync(string ideas, GiftIdeaRoute route)
    {
        var policyOutcome = _contentPolicy.Check(ideas, route.SenderPickedRecipient.Name);

        if (policyOutcome != GiftIdeaSubmissionOutcome.Shared)
            return policyOutcome;

        var verdict = await _contentModerationService
            .ModerateAsync(ideas, "gift ideas")
            .ConfigureAwait(false);

        // An outage is still a refusal, since nothing unchecked is forwarded. It is told apart so
        // the page says to try again rather than to reword something that may be perfectly fine.
        return verdict switch
        {
            ModerationVerdict.Clean => GiftIdeaSubmissionOutcome.Shared,
            ModerationVerdict.Toxic => GiftIdeaSubmissionOutcome.RejectedInappropriateContent,
            _ => GiftIdeaSubmissionOutcome.RejectedModerationUnavailable
        };
    }
}
