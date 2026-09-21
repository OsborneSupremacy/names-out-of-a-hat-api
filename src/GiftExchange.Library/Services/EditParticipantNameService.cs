namespace GiftExchange.Library.Services;

/// <summary>
/// Changes the name one participant is known by.
/// </summary>
/// <remarks>
/// Its own endpoint rather than part of <see cref="EditParticipantService"/>, which edits
/// eligibility and resets the hat to IN_PROGRESS as it does — the same reason
/// <see cref="EditParticipantAddressService"/> and <see cref="EditParticipantEmojiService"/> are
/// their own. A rename touches nothing the draw is made of: eligibility and picks are participant
/// ids, and the names in the domain records are read off the person row every time. Resetting a
/// shaken hat over a typo would throw away a draw for no reason at all.
///
/// Every status is valid here for that reason. The one thing an organizer is not told, because it
/// would be noise, is that email already sent still carries the old name — an invitation is a
/// message that has arrived. What is still to be written, the announcement at the end, uses
/// whatever everybody is called by then.
///
/// The reach is the part worth stating plainly, and the interface in front of this should: a name
/// belongs to the person, not to their place in one exchange, so renaming somebody here renames
/// them in every exchange they are in, including ones this organizer does not run. That is not a
/// side effect of the endpoint but of a person being one row — the same thing that lets an
/// organizer correct a name once instead of once per hat.
///
/// Which is exactly why not every organizer may. Having somebody in your exchange is not standing
/// to say what they are called; introducing them is. So the rename is refused for a participant
/// this organizer neither is nor added, and the check itself lives in
/// <c>GiftExchangeProvider.RenamePersonAsync</c> alongside the write, because the same rule has to
/// hold for <see cref="UpdateProfileService"/> and for the add path that would otherwise be a way
/// around both.
/// </remarks>
[UsedImplicitly]
internal class EditParticipantNameService : IApiGatewayHandler
{
    private readonly ILogger<EditParticipantNameService> _logger;

    private readonly ApiGatewayAdapter _adapter;

    private readonly HatPreconditionValidator _hatPreconditionValidator;

    private readonly GiftExchangeProvider _giftExchangeProvider;

    public EditParticipantNameService(
        ILogger<EditParticipantNameService> logger,
        ApiGatewayAdapter adapter,
        HatPreconditionValidator hatPreconditionValidator,
        GiftExchangeProvider giftExchangeProvider
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _hatPreconditionValidator = hatPreconditionValidator ?? throw new ArgumentNullException(nameof(hatPreconditionValidator));
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
    }

    public Task<APIGatewayProxyResponse> FunctionHandler(
        APIGatewayProxyRequest request,
        ILambdaContext context
    ) =>
        _adapter.AdaptAsync<EditParticipantNameRequest, StatusCodeOnlyResponse>(
            request,
            EditParticipantNameAsync);

    internal async Task<Result<StatusCodeOnlyResponse>> EditParticipantNameAsync(
        EditParticipantNameRequest request
    )
    {
        // Moderated, unlike the face next to it: this is free text an organizer typed, and every
        // other participant reads it in their invitation. The same field is moderated on the way in
        // by AddParticipantService, and an edit that skipped the check would be the way around it.
        var hatPreconditionResult = await _hatPreconditionValidator
            .ValidateAsync(new HatPreconditionRequest
            {
                HatId = request.HatId,
                OrganizerEmail = request.OrganizerEmail,
                FieldsToModerate = new Dictionary<string, string>
                {
                    { "participant name", request.Name }
                },
                ValidHatStatuses = HatStatuses.All
            })
            .ConfigureAwait(false);

        if (!hatPreconditionResult.PreconditionsMet)
            return new Result<StatusCodeOnlyResponse>(
                new AggregateException(hatPreconditionResult.PreconditionFailureMessage.FailureMessage),
                hatPreconditionResult.PreconditionFailureMessage.StatusCode);

        // Checked here rather than left to the provider, which knows about people and not about
        // who is in which exchange. Without it an organizer could rename anybody whose address they
        // could guess, as long as they had added them to something once — the provider would find
        // the person, find the standing, and never ask whether this exchange had anything to do
        // with it.
        var participant = hatPreconditionResult.Hat.Participants
            .SingleOrDefault(candidate => candidate.Person.Email.ContentEquals(request.Email));

        if (participant is null)
            return new Result<StatusCodeOnlyResponse>(
                new KeyNotFoundException($"Participant with email `{request.Email}` not found"),
                HttpStatusCode.NotFound);

        var change = await _giftExchangeProvider
            .RenamePersonAsync(new RenamePersonRequest
            {
                Email = request.Email,
                Name = request.Name,
                RequestedByEmail = request.OrganizerEmail
            })
            .ConfigureAwait(false);

        if (change.Outcome != NameChangeOutcome.Changed)
            return Failure(change, request);

        _logger.LogInformation(
            "Person {PersonId} was renamed by the organizer of hat {HatId}.",
            change.PersonId,
            request.HatId);

        return new Result<StatusCodeOnlyResponse>(
            new StatusCodeOnlyResponse { StatusCode = HttpStatusCode.OK },
            HttpStatusCode.OK);
    }

    private static Result<StatusCodeOnlyResponse> Failure(
        RenamePersonResponse change,
        EditParticipantNameRequest request
    ) =>
        change.Outcome switch
        {
            NameChangeOutcome.PersonNotFound => new Result<StatusCodeOnlyResponse>(
                new KeyNotFoundException($"Participant with email `{request.Email}` not found"),
                HttpStatusCode.NotFound),

            // Forbidden rather than Conflict, for the reason AddParticipantService answers a
            // refused address with it: a conflict says the request collided with something and
            // could be retried differently, and no name will make this one work. It is not about
            // the name at all.
            NameChangeOutcome.NotTheirNameToChange => new Result<StatusCodeOnlyResponse>(
                new InvalidOperationException(NotYoursMessage(change.PreviousName)),
                HttpStatusCode.Forbidden),

            _ => new Result<StatusCodeOnlyResponse>(
                new InvalidOperationException("The name could not be changed."),
                HttpStatusCode.InternalServerError)
        };

    /// <summary>
    /// Says why a rename is not this organizer's to make, without saying whose it is.
    /// </summary>
    /// <remarks>
    /// Naming the other organizer would answer a question the refusal did not raise and hand over
    /// somebody else's involvement in an exchange this organizer cannot see. What the message owes
    /// them is the reason and the two remedies, both of which are true without anybody being named.
    /// </remarks>
    private static string NotYoursMessage(string currentName) =>
        $"{currentName} was added to a gift exchange by somebody else, and a name belongs to the person rather than to one exchange — so this one is not yours to change. They can change it themselves, and so can whoever first added them.";
}
