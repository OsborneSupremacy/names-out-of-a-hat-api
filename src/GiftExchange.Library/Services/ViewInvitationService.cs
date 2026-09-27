namespace GiftExchange.Library.Services;

/// <summary>
/// A participant's invitation, on the site, for somebody who cannot find the email.
/// </summary>
/// <remarks>
/// Reached from the reminder at the top of a follow-up email, for a reader whose invitation was
/// filed out of sight. It is the invitation as it was composed for them, buttons and all, so that
/// nothing about finding it this way leaves them with less than the email would have.
///
/// Only a GET, and it changes nothing. Mail scanners fetch every link in a delivered email, and a
/// page that does nothing but show what the token already entitles its holder to see is one they
/// are welcome to. It reveals nothing a gift ideas token did not already: the Ask page it leads to
/// is headed with the same name.
///
/// The buttons on it carry the token in the address, and that token was issued as one that proves
/// the invitation was seen. So pressing any of them is what stops the reminder appearing in the
/// emails after it — which is the right trigger, because it is the first thing a person does here
/// that a scanner never would.
///
/// The leave sentence is left out of the fine print. The leave token cannot be recovered — only its
/// hash is kept — and issuing a new one would replace, and so break, the link in the invitation
/// itself. Somebody who wants to leave still has that link, and every reminder is attached to an
/// email from a real exchange they can answer the organizer about.
/// </remarks>
[UsedImplicitly]
internal class ViewInvitationService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly EmailCompositionService _emailCompositionService;

    public ViewInvitationService(
        GiftExchangeProvider giftExchangeProvider,
        EmailCompositionService emailCompositionService
        )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _emailCompositionService = emailCompositionService ?? throw new ArgumentNullException(nameof(emailCompositionService));
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    )
    {
        // Case preserved, as on every page a gift ideas token opens: the token is base64url.
        var token = request.PathParameters is not null
            && request.PathParameters.TryGetValue("token", out var pathToken)
                ? pathToken
                : string.Empty;

        var (found, invitation) = await _giftExchangeProvider
            .FindViewableInvitationAsync(SecretToken.Hash(token))
            .ConfigureAwait(false);

        if (!found)
            return Page(ComposeUnavailable());

        var hat = invitation.Hat;

        var participant = hat.Participants.FirstOrDefault(participant =>
            participant.Person.Email.Equals(invitation.ParticipantEmail, StringComparison.OrdinalIgnoreCase));

        // One page for all of these too, so that a guessed token cannot be told apart from an
        // exchange that has moved on.
        if (participant is null || string.IsNullOrWhiteSpace(participant.PickedRecipient.Email))
            return Page(ComposeUnavailable());

        var body = _emailCompositionService.ComposeEmail(new ComposeInvitationRequest
        {
            Hat = hat,
            ParticipantName = participant.Person.Name,
            PickedName = hat.DisplayNameIn(participant.PickedRecipient),
            PickedEmoji = hat.EmojiFor(participant.PickedRecipient),
            GiftIdeasToken = token,
            LeaveToken = string.Empty,
            IncludeMasthead = false
        });

        return Page(EmailLinkedPage.Compose("Your invitation", body));
    }

    internal static string ComposeUnavailable() =>
        EmailLinkedPage.Compose(
            "This link isn't available",
            """
            <p>We can't show an invitation from this link. The gift exchange may have finished, its
            names may have been drawn again, or it may no longer exist.</p>
            <p>If you're expecting an invitation, look for the most recent email from Names Out Of A
            Hat, or ask the person who organised the exchange.</p>
            """);

    private static APIGatewayProxyResponse Page(string html) =>
        new()
        {
            StatusCode = (int)HttpStatusCode.OK,
            Body = html,
            Headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "text/html; charset=utf-8",
                // It names who this person drew. Nothing between them and us should keep a copy.
                ["Cache-Control"] = "no-store",
                ["X-Robots-Tag"] = "noindex",
                // The token is in the address, and the page links out to the site and to the
                // organizer's address.
                ["Referrer-Policy"] = "no-referrer"
            }
        };
}
