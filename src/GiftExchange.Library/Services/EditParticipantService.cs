namespace GiftExchange.Library.Services;

[UsedImplicitly]
internal class EditParticipantService : IApiGatewayHandler
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly ApiGatewayAdapter _adapter;

    public EditParticipantService(
        GiftExchangeProvider giftExchangeProvider,
        HatPreconditionValidator hatPreconditionValidator,
        ApiGatewayAdapter adapter
        )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<EditParticipantRequest, StatusCodeOnlyResponse>(request, EditParticipantAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> EditParticipantAsync(
        EditParticipantRequest request
        )
    {
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = [],
                ValidHatStatuses = [HatStatus.InProgress, HatStatus.ReadyForAssignment, HatStatus.NamesAssigned]
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        var hat = hatPreconditionResult.Hat;

        var (participantExists, participant) = await _giftExchangeProvider
            .GetParticipantAsync(
                request.OrganizerEmail,
                request.HatId,
                request.Email
            )
            .ConfigureAwait(false);

        if(!participantExists)
            return new Result<StatusCodeOnlyResponse>(new KeyNotFoundException($"Participant with email `{request.Email}` not found"), HttpStatusCode.NotFound);

        if(request.EligibleRecipients.Count == 0)
            return new Result<StatusCodeOnlyResponse>(new ArgumentException("Participant must have at least one eligible recipient"), HttpStatusCode.BadRequest);

        // By address, because that is what identifies somebody within a hat. Two participants may
        // share a name, so a list of names could not say which of them was meant.
        if(request.EligibleRecipients.Any(email => email.ContentEquals(participant.Person.Email)))
            return new Result<StatusCodeOnlyResponse>(new ArgumentException("Participant cannot set themselves as an eligible recipient"), HttpStatusCode.BadRequest);

        var otherParticipants = hat.Participants
            .Where(p => !p.Person.Email.ContentEquals(participant.Person.Email))
            .Select(p => p.Person)
            .ToImmutableList();

        var invalidRecipients = request.EligibleRecipients
            .Where(email => !otherParticipants.Any(p => p.Email.ContentEquals(email)))
            .ToImmutableList();

        if (invalidRecipients.Any())
        {
            var errorMessage = $"""
                                One or more provided recipients are not part of this gift exchange.

                                Gift exchange participants: {string.Join(", ", otherParticipants.Select(p => p.Email))}
                                Provided Recipients: {string.Join(", ", request.EligibleRecipients)}
                                Invalid Recipients: {string.Join(", ", invalidRecipients)}


                                """;

            return new Result<StatusCodeOnlyResponse>(new ArgumentException(errorMessage), HttpStatusCode.BadRequest);
        }

        // Written as the addresses the hat holds rather than as they were sent, so that a
        // difference in capitalisation does not quietly drop somebody the organizer ticked.
        var eligibleRecipientEmails = otherParticipants
            .Where(p => request.EligibleRecipients.Any(email => email.ContentEquals(p.Email)))
            .Select(p => p.Email)
            .ToImmutableList();

        await _giftExchangeProvider
            .UpdateEligibleRecipientsAsync(
                request.OrganizerEmail,
                request.HatId,
                request.Email,
                eligibleRecipientEmails
            )
            .ConfigureAwait(false);

        if (hat.Status != HatStatus.InProgress)
            await _giftExchangeProvider.UpdateHatStatusAsync(request.OrganizerEmail, request.HatId, HatStatus.InProgress)
                .ConfigureAwait(false);

        return new Result<StatusCodeOnlyResponse>(new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK}, HttpStatusCode.OK);
    }
}
