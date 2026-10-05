namespace GiftExchange.Library.Services;

/// <summary>
/// The work behind sharing gift ideas, wherever the button was pressed: checking what was written,
/// storing it, and passing it to the one person it is for.
/// </summary>
/// <remarks>
/// Two callers. <see cref="ShareGiftIdeasService"/> reaches a route through the token in an email;
/// <see cref="ParticipantGiftIdeasService"/> reaches the same route through a signed-in session.
/// Everything after the route is found is the same for both, and lives here so that it stays the
/// same: a rule added to one door and not the other would be a rule with a door right next to it.
///
/// Both kinds of submission come through here, told apart by <see cref="GiftIdeaRoute.IsContribution"/>.
/// Every check is the same for both; what differs is where the text is stored and who it goes to.
///
/// A participant writing about themselves may also ask for their words to be held back until the
/// person who drew them asks for gift ideas. That is the one thing this stores and does not send —
/// released by <see cref="GiftIdeaAsking"/> when the ask comes, or here if the ask came first.
/// Which of the two happened is never reported to the caller: see <see cref="ForwardIfAlreadyAskedAsync"/>.
/// </remarks>
[UsedImplicitly]
internal class GiftIdeaSharing
{
    /// <summary>Statuses during which there is still somebody to share ideas with.</summary>
    public static readonly ImmutableList<string> AcceptingStatuses =
        [HatStatus.NamesAssigned, HatStatus.InvitationsSent, HatStatus.CooledOff];

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly GiftIdeaContentPolicy _contentPolicy;

    private readonly IContentModerationService _contentModerationService;

    private readonly GiftIdeaEmailCompositionService _composer;

    private readonly AutomaticEmailSender _sender;

    private readonly InvitationReminderService _invitationReminder;

    private readonly ILogger<GiftIdeaSharing> _logger;

    public GiftIdeaSharing(
        GiftExchangeProvider giftExchangeProvider,
        GiftIdeaContentPolicy contentPolicy,
        IContentModerationService contentModerationService,
        GiftIdeaEmailCompositionService composer,
        AutomaticEmailSender sender,
        InvitationReminderService invitationReminder,
        ILogger<GiftIdeaSharing> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _contentPolicy = contentPolicy ?? throw new ArgumentNullException(nameof(contentPolicy));
        _contentModerationService = contentModerationService ?? throw new ArgumentNullException(nameof(contentModerationService));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _invitationReminder = invitationReminder ?? throw new ArgumentNullException(nameof(invitationReminder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks the submission, and if nothing stops it, stores it and passes it on.
    /// </summary>
    /// <remarks>
    /// Stored before anything is sent. If sending fails after this, the submission still exists;
    /// the reverse would lose what somebody wrote.
    ///
    /// The hold is ignored outright on a contribution rather than merely unoffered, so a hand-made
    /// request cannot hold back ideas that were asked for.
    /// </remarks>
    /// <returns>
    /// <see cref="GiftIdeaSubmissionOutcome.Shared"/> when it was stored, whether or not it has gone
    /// anywhere yet; otherwise the reason it was refused, with nothing stored or sent.
    /// </returns>
    public async Task<GiftIdeaSubmissionOutcome> ShareAsync(GiftIdeaRoute route, string ideas, bool holdUntilAsked)
    {
        var held = holdUntilAsked && !route.IsContribution;

        var outcome = await CheckAsync(ideas, route).ConfigureAwait(false);

        if (outcome != GiftIdeaSubmissionOutcome.Shared)
        {
            _logger.LogInformation("Refused a gift ideas submission: {Outcome}", outcome);
            return outcome;
        }

        await StoreAsync(route, ideas, held).ConfigureAwait(false);

        if (held)
            await ForwardIfAlreadyAskedAsync(route, ideas).ConfigureAwait(false);
        else
            await ForwardAsync(route, ideas).ConfigureAwait(false);

        return GiftIdeaSubmissionOutcome.Shared;
    }

    /// <summary>
    /// Passes a held submission on after all, when the person it is for has already asked.
    /// </summary>
    /// <remarks>
    /// Asking and writing can happen in either order, and a submission written after the ask is owed
    /// to somebody who is waiting for it. Nothing about this reaches the writer: they are told the
    /// same thing either way, because the difference between the two is the fact that their giver
    /// asked, and that is precisely what asking somebody else about them was meant to keep quiet.
    ///
    /// The release is stamped so that the next round of asking does not send the same text again.
    /// </remarks>
    private async Task ForwardIfAlreadyAskedAsync(GiftIdeaRoute route, string ideas)
    {
        // Nobody has drawn them, so nobody can have asked. The submission is already stored.
        if (route.GiverParticipantId == Guid.Empty)
            return;

        var asked = await _giftExchangeProvider
            .HasAskedForGiftIdeasAsync(new HasAskedForGiftIdeasRequest
            {
                AskerParticipantId = route.GiverParticipantId,
                SubjectParticipantId = route.ParticipantId
            })
            .ConfigureAwait(false);

        if (!asked)
        {
            _logger.LogInformation("Held a gift ideas submission until somebody asks for it.");
            return;
        }

        await ForwardAsync(route, ideas).ConfigureAwait(false);

        await _giftExchangeProvider
            .MarkGiftIdeaEnquiryReleasedAsync(new MarkGiftIdeaEnquiryReleasedRequest
            {
                AskerParticipantId = route.GiverParticipantId,
                SubjectParticipantId = route.ParticipantId,
                ReleasedAt = DateTimeOffset.UtcNow
            })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether there is anything about this submission that stops it being passed on.
    /// </summary>
    /// <remarks>
    /// Cheapest first, so text refused on a rule this application can apply itself never reaches
    /// Comprehend.
    /// </remarks>
    private async Task<GiftIdeaSubmissionOutcome> CheckAsync(string ideas, GiftIdeaRoute route)
    {
        // The writer's own pick, even on a contribution about somebody else. The check is about
        // what the writer must not leak, and the person reading this knows who wrote it.
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

    /// <summary>
    /// Writes the submission to whichever table it belongs in.
    /// </summary>
    /// <remarks>
    /// Two tables, because the two are not the same claim. What somebody says about themselves is
    /// theirs; what somebody says about another participant is a suggestion made to the one person
    /// who asked for it, and must never be read back as the subject's own words.
    /// </remarks>
    private Task<Guid> StoreAsync(GiftIdeaRoute route, string ideas, bool holdUntilAsked) =>
        route.IsContribution switch
        {
            true => _giftExchangeProvider.AddContributedGiftIdeaAsync(route.AskId, ideas),
            false => _giftExchangeProvider.AddGiftIdeaAsync(new AddGiftIdeaRequest
            {
                ParticipantId = route.ParticipantId,
                Ideas = ideas,
                HoldUntilAsked = holdUntilAsked
            })
        };

    private async Task ForwardAsync(GiftIdeaRoute route, string ideas)
    {
        // Nobody has drawn this participant, so there is nobody to forward to. The submission is
        // already stored. A contribution always has somebody — the asker — so this is only ever
        // reached on the ordinary path.
        if (string.IsNullOrWhiteSpace(route.Giver.Email))
        {
            _logger.LogInformation("Stored a gift ideas submission with nobody yet to forward it to.");
            return;
        }

        // Subject and body chosen together, so that the two cannot be made to disagree about which
        // kind of message this is.
        var (subject, body) = route.IsContribution switch
        {
            true => (
                GiftIdeaEmailCompositionService.ContributionForwardSubject(route.DisplayNameOf(route.Sender), route.DisplayNameOf(route.Subject)),
                _composer.ComposeContributionForward(route.DisplayNameOf(route.Sender), route.DisplayNameOf(route.Subject), route.HatName, ideas)),
            false => (
                GiftIdeaEmailCompositionService.ForwardSubject(route.DisplayNameOf(route.Sender)),
                _composer.ComposeForward(
                    route.DisplayNameOf(route.Sender),
                    route.HatName,
                    ideas,
                    // Only on this path. A contribution goes back to whoever asked for it, and
                    // they pressed a button to ask.
                    await _invitationReminder.ComposeForAsync(route.GiverParticipantId).ConfigureAwait(false)))
        };

        await _sender.SendAsync(route.Giver.Email, subject, body).ConfigureAwait(false);
    }
}
