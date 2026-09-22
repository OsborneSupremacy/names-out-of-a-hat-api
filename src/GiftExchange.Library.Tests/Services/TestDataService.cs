
namespace GiftExchange.Library.Tests.Services;

internal class TestDataService
{
    private readonly GiftExchangeProvider _giftExchangeProvider;

    private readonly HatDataModelFaker _hatDataModelFaker;

    public TestDataService(
        GiftExchangeProvider giftExchangeProvider
        )
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
        _hatDataModelFaker = new HatDataModelFaker();
    }

    public Task<bool> CreateHatAsync(HatDataModel newHat) =>
        _giftExchangeProvider.CreateHatAsync(newHat);

    public async Task<Hat> CreateTestHatAsync()
    {
        var newHat = _hatDataModelFaker.Generate();

        await _giftExchangeProvider.CreateHatAsync(newHat);

        return new Hat
        {
            Id = newHat.HatId,
            Name = newHat.HatName,
            Status = HatStatus.InProgress,
            AdditionalInformation = newHat.AdditionalInformation,
            PriceRange = newHat.PriceRange,
            Organizer = new Person {
                Email = newHat.OrganizerEmail,
                Name = newHat.OrganizerName
            },
            Participants = [],
            InvitationsQueuedDate = DateTimeOffset.MinValue,
            ExchangeDate = DateOnly.MinValue
        };
    }

    public async Task<Hat> GetHatAsync(string organizerEmail, Guid hatId)
    {
        var (_, hat) = await _giftExchangeProvider
            .GetHatAsync(organizerEmail, hatId);
        return hat;
    }

    public Task<Participant> CreateParticipantAsync(
        AddParticipantRequest request,
        ImmutableList<Participant> existingParticipants
        ) => _giftExchangeProvider.CreateParticipantAsync(request, existingParticipants);

    public async Task<Participant> GetParticipantAsync(
        string organizerEmail,
        Guid hatId,
        string participantUtEmail
        )
    {
        var (_, participant) = await _giftExchangeProvider
            .GetParticipantAsync(organizerEmail, hatId, participantUtEmail);
        return participant;
    }
}
