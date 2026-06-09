using Amazon.SQS;
using Amazon.SQS.Model;

namespace GiftExchange.Library.Services;

[UsedImplicitly]
internal class EnqueueInvitationsService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly ApiGatewayAdapter _adapter;

    private readonly JsonService _jsonService;

    private readonly EmailCompositionService _emailCompositionService;

    private readonly IAmazonSQS _sqsClient;

    private readonly ISchedulerService _schedulerService;

    private readonly string _queueUrl;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    public EnqueueInvitationsService(
        GiftExchangeProvider giftExchangeProvider,
        ApiGatewayAdapter adapter,
        HatPreconditionValidator hatPreconditionValidator,
        JsonService jsonService,
        EmailCompositionService emailCompositionService,
        IAmazonSQS sqsClient,
        ISchedulerService schedulerService
        )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _emailCompositionService =
            emailCompositionService ?? throw new ArgumentNullException(nameof(emailCompositionService));
        _sqsClient = sqsClient ?? throw new ArgumentNullException(nameof(sqsClient));
        _queueUrl = EnvReader.GetStringValue("INVITATIONS_QUEUE_URL");
        _schedulerService = schedulerService ?? throw new ArgumentNullException(nameof(schedulerService));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context) =>
        _adapter.AdaptAsync<SendInvitationsRequest, StatusCodeOnlyResponse>(request, ExecuteAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> ExecuteAsync(SendInvitationsRequest request)
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = [],
                ValidHatStatuses = [ HatStatus.NamesAssigned ]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        var hat = hatPreconditionResult.Hat;

        var messageGroupId = $"group-hat-{hat.Id}-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}";

        var sqsTasks = new List<Task>();

        foreach(var participant in hat.Participants)
        {
            var invitation = new GiftExchangeEmailRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                HtmlBody = _emailCompositionService
                    .ComposeEmail(hat, participant.Person.Name, participant.PickedRecipient),
                RecipientEmail = participant.Person.Email,
                Subject = EmailCompositionService.GetSubject(hat)
            };

            var jsonInvitation = _jsonService.SerializeDefault(invitation);

            var sqsRequest = new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = jsonInvitation,
                MessageGroupId = messageGroupId
            };

            sqsTasks.Add(_sqsClient.SendMessageAsync(sqsRequest));
        }

        await Task.WhenAll(sqsTasks)
            .ConfigureAwait(false);

        var invitationsQueuedAt = await _giftExchangeProvider
            .MarkInvitationsAsQueuedAsync(request.OrganizerEmail, request.HatId)
            .ConfigureAwait(false);

        await _schedulerService.CreateCooledOffScheduleAsync(request, invitationsQueuedAt)
            .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK}, HttpStatusCode.OK);
    }
}
