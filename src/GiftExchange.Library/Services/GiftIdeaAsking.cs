namespace GiftExchange.Library.Services;

/// <summary>
/// The work behind the Ask, wherever the button was pressed: one participant asking for gift ideas
/// about the person whose name they drew, without being named.
/// </summary>
/// <remarks>
/// Two callers. <see cref="AskForGiftIdeasService"/> reaches a route through the token in an email;
/// <see cref="ParticipantGiftIdeasService"/> reaches the same route through a signed-in session.
/// Why the Ask is shaped the way it is — who may be asked, and what that costs in anonymity — is on
/// <see cref="AskForGiftIdeasService"/>, where it was first written down.
/// </remarks>
[UsedImplicitly]
internal class GiftIdeaAsking
{
    /// <summary>
    /// How long a participant has to wait before asking the same person again.
    ///
    /// A week, because the thing being asked for takes days to think about, and because the person
    /// on the receiving end cannot tell repeated asks from nagging — they do not know how many
    /// people are asking, only how often they are being asked. Short enough that somebody who
    /// genuinely got no answer can try again within the life of an exchange.
    ///
    /// Held per pair, so choosing five people costs five separate windows rather than one. See the
    /// remarks on <see cref="IReplyThrottleProvider.TryReserveAskSlotAsync"/>.
    /// </summary>
    private static readonly TimeSpan AskWindow = TimeSpan.FromDays(7);

    /// <summary>Statuses during which there is still somebody to ask.</summary>
    public static readonly ImmutableList<string> AskableStatuses =
        [HatStatus.NamesAssigned, HatStatus.InvitationsSent, HatStatus.CooledOff];

    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly IReplyThrottleProvider _replyThrottleProvider;

    private readonly GiftIdeaEmailCompositionService _composer;

    private readonly AutomaticEmailSender _sender;

    private readonly InvitationReminderService _invitationReminder;

    private readonly AskQuestionPolicy _questionPolicy;

    private readonly IContentModerationService _contentModerationService;

    private readonly ILogger<GiftIdeaAsking> _logger;

    public GiftIdeaAsking(
        GiftExchangeProvider giftExchangeProvider,
        IReplyThrottleProvider replyThrottleProvider,
        GiftIdeaEmailCompositionService composer,
        AutomaticEmailSender sender,
        InvitationReminderService invitationReminder,
        AskQuestionPolicy questionPolicy,
        IContentModerationService contentModerationService,
        ILogger<GiftIdeaAsking> logger
    )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _replyThrottleProvider = replyThrottleProvider ?? throw new ArgumentNullException(nameof(replyThrottleProvider));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _invitationReminder = invitationReminder ?? throw new ArgumentNullException(nameof(invitationReminder));
        _questionPolicy = questionPolicy ?? throw new ArgumentNullException(nameof(questionPolicy));
        _contentModerationService = contentModerationService ?? throw new ArgumentNullException(nameof(contentModerationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Whether this route has anybody to ask about: an exchange still running, and a pick.
    /// </summary>
    /// <remarks>
    /// Both callers answer a false here with the same thing they answer an unknown route with, so
    /// that the difference never tells anybody whether a route is real.
    /// </remarks>
    public static bool CanAsk(GiftIdeaRoute route) =>
        AskableStatuses.Contains(route.HatStatus)
        && route.SenderPickedRecipientParticipantId != Guid.Empty
        && !string.IsNullOrWhiteSpace(route.SenderPickedRecipient.Email);

    /// <summary>
    /// Asks everybody chosen who may be asked, and says what became of each of them.
    /// </summary>
    /// <param name="route">The asker. <see cref="CanAsk"/> is expected to have said yes.</param>
    /// <param name="chosen">
    /// Participant ids from a form the asker could edit. Each survives only if the database agrees it
    /// belongs to this asker's exchange.
    /// </param>
    /// <param name="question">Already trimmed. Empty when they did not write one.</param>
    public async Task<AskRoundResult> AskAsync(GiftIdeaRoute route, ImmutableList<Guid> chosen, string question)
    {
        // Resolved against the database rather than against the list just rendered. The form came
        // back from a browser, and what a browser sends is whatever it was told to send.
        var targets = await _giftExchangeProvider
            .FindAskTargetsAsync(route.HatId, route.ParticipantId, chosen)
            .ConfigureAwait(false);

        var chosenIds = targets.Select(target => target.ParticipantId).ToImmutableList();

        // Ticking nobody is a slip, and the useful response to a slip is the form again.
        if (targets.IsEmpty)
            return Unasked(AskRoundOutcome.NobodyChosen, AskQuestionOutcome.Accepted, chosenIds);

        // Before anything else happens, including the release of held ideas and the claiming of
        // throttle slots. A question sent back to be fixed should cost the asker nothing, and a
        // week's wait on somebody they never actually asked would be a strange price for a typo.
        var questionOutcome = await CheckQuestionAsync(question, route).ConfigureAwait(false);

        if (questionOutcome != AskQuestionOutcome.Accepted)
            return Unasked(AskRoundOutcome.QuestionRefused, questionOutcome, chosenIds);

        // Before anybody is asked, because this is what the asker is owed already. Recorded even if
        // every ask below is then refused by the throttle: the throttle governs how often somebody
        // may be mailed, and what is written down here is that this participant asked.
        var released = await ReleaseHeldIdeasAsync(route).ConfigureAwait(false);

        var attempts = ImmutableList.CreateBuilder<AskAttempt>();

        foreach (var target in targets)
            attempts.Add(await AskOneAsync(route, target, question).ConfigureAwait(false));

        var outcomes = attempts.ToImmutable();

        // Only when something did not happen. Everything that went through is already on the page
        // in front of them, and a second copy by email of a round that worked is noise; a round
        // that fell short is worth having in writing, because the page is gone the moment they
        // close the tab.
        if (outcomes.Any(attempt => !attempt.Sent))
            await _sender.SendAsync(
                    route.Sender.Email,
                    GiftIdeaEmailCompositionService.AskPartiallySentSubject,
                    _composer.ComposeAskSummary(route.DisplayNameOf(route.SenderPickedRecipient), outcomes))
                .ConfigureAwait(false);

        return new AskRoundResult
        {
            Outcome = AskRoundOutcome.Asked,
            QuestionOutcome = AskQuestionOutcome.Accepted,
            Chosen = chosenIds,
            Attempts = outcomes,
            ReleasedHeldIdeas = released
        };
    }

    private static AskRoundResult Unasked(
        AskRoundOutcome outcome,
        AskQuestionOutcome questionOutcome,
        ImmutableList<Guid> chosen
    ) =>
        new()
        {
            Outcome = outcome,
            QuestionOutcome = questionOutcome,
            Chosen = chosen,
            Attempts = [],
            ReleasedHeldIdeas = false
        };

    /// <summary>
    /// Writes down that this participant asked about their pick, and passes on anything the pick
    /// wrote and asked us to hold until somebody did.
    /// </summary>
    /// <remarks>
    /// The record outlives the round of asking, and has to. A participant may write held ideas long
    /// after they were asked for — asking the pick directly used to leave nothing behind but a
    /// throttle entry that expires within the week — and the share page consults this to decide
    /// whether anybody is waiting.
    ///
    /// The release is decided by comparing the submission against
    /// <see cref="GiftIdeaEnquiryEntity.ReleasedAt"/> rather than by whether the enquiry was new.
    /// Nothing here can tell whether an email went out, so a release that was dropped has to be
    /// repeatable, and one that arrived must not be sent twice. The comparison gives both: once per
    /// submission, and again if a later submission replaces it.
    ///
    /// The newest submission decides, and only then its flag. Somebody who held ideas back and later
    /// shared outright has already had the later version delivered, and the earlier held one is not
    /// what they want their giver reading now.
    ///
    /// The pick is never told any of this. Being told would be being told that whoever holds their
    /// name has been asking about them, which is the one thing asking other people instead of them was
    /// meant to avoid.
    /// </remarks>
    private async Task<bool> ReleaseHeldIdeasAsync(GiftIdeaRoute route)
    {
        var subjectParticipantId = route.SenderPickedRecipientParticipantId;

        var enquiry = await _giftExchangeProvider
            .RecordGiftIdeaEnquiryAsync(new RecordGiftIdeaEnquiryRequest
            {
                // The subject cannot be empty here: CanAsk has already said no when this participant
                // has no pick, so there is nothing to re-derive.
                AskerParticipantId = route.ParticipantId,
                SubjectParticipantId = subjectParticipantId
            })
            .ConfigureAwait(false);

        var latest = await _giftExchangeProvider
            .GetLatestGiftIdeaAsync(subjectParticipantId)
            .ConfigureAwait(false);

        if (!latest.HoldUntilAsked
            || string.IsNullOrEmpty(latest.Ideas)
            || latest.CreatedAt <= enquiry.ReleasedAt)
            return false;

        await _sender.SendAsync(
                route.Sender.Email,
                GiftIdeaEmailCompositionService.ForwardSubject(route.DisplayNameOf(route.SenderPickedRecipient)),
                _composer.ComposeHeldForward(
                    route.DisplayNameOf(route.SenderPickedRecipient), route.HatName, latest.Ideas))
            .ConfigureAwait(false);

        // After the send, so that a message that never went out is tried again by the next ask.
        await _giftExchangeProvider
            .MarkGiftIdeaEnquiryReleasedAsync(new MarkGiftIdeaEnquiryReleasedRequest
            {
                AskerParticipantId = route.ParticipantId,
                SubjectParticipantId = subjectParticipantId,
                ReleasedAt = DateTimeOffset.UtcNow
            })
            .ConfigureAwait(false);

        _logger.LogInformation("Released a held gift ideas submission to somebody who asked.");

        return true;
    }

    /// <summary>
    /// Asks one person, and says what became of it.
    /// </summary>
    /// <remarks>
    /// The throttle is claimed before anything else happens, so a refusal costs nothing — no email
    /// composed, and no token issued, which matters because a token issued for an ask that was
    /// never sent would be a live address nobody had been given.
    /// </remarks>
    private async Task<AskAttempt> AskOneAsync(GiftIdeaRoute route, AskTarget target, string question)
    {
        var slot = await _replyThrottleProvider
            .TryReserveAskSlotAsync(new ReserveAskSlotRequest
            {
                AskerParticipantId = route.ParticipantId,
                TargetParticipantId = target.ParticipantId,
                Window = AskWindow
            })
            .ConfigureAwait(false);

        if (!slot.Reserved)
        {
            _logger.LogInformation("Suppressed an Ask inside the throttle window.");

            return Refused(route.DisplayNameOf(target.Person), slot.PreviouslyReservedAt);
        }

        await SendAskAsync(route, target, question).ConfigureAwait(false);

        return Sent(route.DisplayNameOf(target.Person));
    }

    /// <summary>
    /// Sends whichever of the two asks this person is due.
    /// </summary>
    /// <remarks>
    /// Both name nobody. Asking somebody what they would like gives nothing away at all; asking
    /// somebody what a third person would like gives away that the asker drew that third person,
    /// which is the trade the page has already put to them.
    /// </remarks>
    private Task SendAskAsync(GiftIdeaRoute route, AskTarget target, string question) =>
        target.IsTheirPick switch
        {
            true => AskThemWhatTheyWouldLikeAsync(route, target, question),
            false => AskThemAboutThePickAsync(route, target, question)
        };

    /// <summary>
    /// Asks the person whose name the asker drew what they would like, for themselves.
    /// </summary>
    /// <remarks>
    /// A token of their own, issued alongside any they already hold rather than over them. Theirs
    /// cannot be reconstructed — only its hash was kept — so this is the only way to put a working
    /// SHARE GIFT IDEAS link into an email they did not originally receive.
    /// </remarks>
    private async Task AskThemWhatTheyWouldLikeAsync(GiftIdeaRoute route, AskTarget target, string question)
    {
        var giftIdeasToken = await _giftExchangeProvider
            .IssueGiftIdeaTokenAsync(new IssueGiftIdeaTokenRequest
            {
                ParticipantId = target.ParticipantId,
                // This email may be the first thing from us they have read, so sharing ideas from
                // it says nothing about whether they have seen their invitation.
                ProvesInvitationSeen = false
            })
            .ConfigureAwait(false);

        var invitationReminder = await _invitationReminder
            .ComposeForAsync(target.ParticipantId)
            .ConfigureAwait(false);

        await _sender.SendAsync(
                target.Person.Email,
                GiftIdeaEmailCompositionService.AskSubject(route.HatName),
                _composer.ComposeAsk(new ComposeAskRequest
                {
                    HatName = route.HatName,
                    GiftIdeasToken = giftIdeasToken,
                    Question = question,
                    InvitationReminder = invitationReminder
                }))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks somebody else in the exchange what they think the asker's pick would like.
    /// </summary>
    /// <remarks>
    /// The subject is written into the ask rather than followed back through the asker's pick
    /// later, so that the name in this email and the name the reply is filed under stay the same
    /// even if the organizer edits the draw afterwards.
    /// </remarks>
    private async Task AskThemAboutThePickAsync(GiftIdeaRoute route, AskTarget target, string question)
    {
        var askToken = await _giftExchangeProvider
            .IssueGiftIdeaAskAsync(
                route.ParticipantId,
                target.ParticipantId,
                route.SenderPickedRecipientParticipantId)
            .ConfigureAwait(false);

        var invitationReminder = await _invitationReminder
            .ComposeForAsync(target.ParticipantId)
            .ConfigureAwait(false);

        await _sender.SendAsync(
                target.Person.Email,
                GiftIdeaEmailCompositionService.ContributionAskSubject(route.DisplayNameOf(route.SenderPickedRecipient)),
                _composer.ComposeContributionAsk(new ComposeContributionAskRequest
                {
                    HatName = route.HatName,
                    SubjectName = route.DisplayNameOf(route.SenderPickedRecipient),
                    AskToken = askToken,
                    Question = question,
                    InvitationReminder = invitationReminder
                }))
            .ConfigureAwait(false);
    }

    /// <summary>An ask that went out.</summary>
    /// <remarks>
    /// The date is <see cref="DateTimeOffset.MinValue"/> because it means "no earlier ask stood in
    /// the way", which is a different fact from a date nobody recorded — and the callers reporting
    /// this never read the date off a sent attempt anyway.
    /// </remarks>
    private static AskAttempt Sent(string name) =>
        new()
        {
            Name = name,
            Sent = true,
            PreviouslyAskedAt = DateTimeOffset.MinValue
        };

    /// <summary>An ask the throttle refused, with the date it is refusing on behalf of.</summary>
    private static AskAttempt Refused(string name, DateTimeOffset previouslyAskedAt) =>
        new()
        {
            Name = name,
            Sent = false,
            PreviouslyAskedAt = previouslyAskedAt
        };

    /// <summary>
    /// Whether the question may go out with the asks.
    /// </summary>
    /// <remarks>
    /// Cheapest first, so a question refused on a rule this application can apply itself never
    /// reaches Comprehend — and no question at all never reaches it either.
    /// </remarks>
    private async Task<AskQuestionOutcome> CheckQuestionAsync(string question, GiftIdeaRoute route)
    {
        var policyOutcome = _questionPolicy.Check(question, route.Sender.Name);

        if (policyOutcome != AskQuestionOutcome.Accepted || string.IsNullOrEmpty(question))
            return policyOutcome;

        var verdict = await _contentModerationService
            .ModerateAsync(question, "question")
            .ConfigureAwait(false);

        // An outage is still a refusal, since nothing unchecked is sent. It is told apart so the
        // page says to try again rather than to reword something that may be perfectly fine.
        return verdict switch
        {
            ModerationVerdict.Clean => AskQuestionOutcome.Accepted,
            ModerationVerdict.Toxic => AskQuestionOutcome.RejectedInappropriateContent,
            _ => AskQuestionOutcome.RejectedModerationUnavailable
        };
    }
}
