using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

namespace GiftExchange.Library.Services;

/// <summary>
/// Issues a magic link. Always reports success regardless of what happened internally, so the
/// endpoint cannot be used to probe which addresses are known.
/// </summary>
[UsedImplicitly]
internal class RequestMagicLinkService : IApiGatewayHandler
{
    private const string TestRecipient = "osborne.ben@gmail.com";

    private const string SignInUrl = "https://namesoutofahat.com/auth";

    private readonly ILogger<RequestMagicLinkService> _logger;

    private readonly ApiGatewayAdapter _adapter;

    private readonly LoginTokenProvider _loginTokenProvider;

    private readonly IAmazonSimpleEmailService _sesClient;

    private readonly bool _liveMode;

    public RequestMagicLinkService(
        ILogger<RequestMagicLinkService> logger,
        ApiGatewayAdapter adapter,
        LoginTokenProvider loginTokenProvider,
        IAmazonSimpleEmailService sesClient
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _loginTokenProvider = loginTokenProvider ?? throw new ArgumentNullException(nameof(loginTokenProvider));
        _sesClient = sesClient ?? throw new ArgumentNullException(nameof(sesClient));
        _liveMode = EnvReader.TryGetBooleanValue("LIVE_MODE", out var boolOut) && boolOut;
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context) =>
        _adapter.AdaptAsync<RequestMagicLinkRequest, StatusCodeOnlyResponse>(request, ExecuteAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> ExecuteAsync(RequestMagicLinkRequest request)
    {
        var accepted = new Result<StatusCodeOnlyResponse>(
            new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.Accepted },
            HttpStatusCode.Accepted
        );

        if (!await _loginTokenProvider.TryReserveRequestSlotAsync(request.Email).ConfigureAwait(false))
        {
            _logger.LogInformation("Suppressed a magic link request that arrived inside the throttle window.");
            return accepted;
        }

        var token = await _loginTokenProvider
            .CreateLoginTokenAsync(request.Email)
            .ConfigureAwait(false);

        // Same test-mode redirect the invitation handler uses. Worth extracting to one place if
        // this design goes ahead.
        var recipient = _liveMode ? request.Email : TestRecipient;

        // In the fragment rather than the query string. A fragment is never sent to a server, so the
        // token stays out of CloudFront and API Gateway access logs and out of any Referer header
        // the sign-in page might emit — one fewer place a live token comes to rest. The page reads
        // it back out with JavaScript, which is where it was always headed anyway.
        //
        // Uri.EscapeDataString rather than HttpUtility.UrlEncode: the latter spells a space as '+',
        // which is a form-encoding convention that nothing decoding a fragment honours. A base64url
        // token has neither spaces nor anything else needing escaping, so this is about the call
        // being the right one rather than about the tokens we issue today.
        var link = $"{SignInUrl}#token={Uri.EscapeDataString(token)}";

        var html =
            $"""
             {EmailBranding.Masthead()}<br /><br />
             Click below to sign in to Names Out Of A Hat.<br /><br />
             <a href="{link}"><b>Sign in</b></a><br /><br />
             This link works once and expires in 15 minutes.<br /><br />
             If you didn't ask to sign in, you can ignore this email.
             """;

        // SendEmail rather than the raw send the other senders use: this message needs no header of
        // its own, and a display name that is plain ASCII is something Source can carry as it is.
        var sendRequest = new SendEmailRequest
        {
            Source = OutgoingEmail.Sender(string.Empty).ToString(),
            Destination = new Destination { ToAddresses = [recipient] },
            Message = new Message
            {
                Subject = new Content("Your Names Out Of A Hat sign-in link" + (_liveMode ? string.Empty : " - TEST MODE")),
                Body = new Body
                {
                    Html = new Content(html),
                    Text = new Content(EmailPlainText.FromHtml(html))
                }
            }
        };

        try
        {
            await _sesClient.SendEmailAsync(sendRequest).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Still report success: the caller must not be able to tell delivery outcomes apart.
            _logger.LogError(exception, "Failed to send a magic link email.");
        }

        return accepted;
    }
}
