namespace GiftExchange.Library.Tests.ServiceTests;

[Collection(PostgresCollection.Name)]
public class ValidationServiceTests
{
    private readonly PersonFaker _personFaker;


    private readonly ValidationService _sut;

    // ReSharper disable once ConvertConstructorToMemberInitializers
    public ValidationServiceTests(PostgresFixture dbFixture)
    {
        DotEnv.Load();

        var contextFactory = dbFixture.CreateContextFactory();

        _personFaker = new PersonFaker();

        IServiceProvider serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .AddSingleton<IContentModerationService, FakeContentModerationService>()
            .BuildServiceProvider();

        _sut = (serviceProvider
            .GetRequiredKeyedService<IApiGatewayHandler>("post/hat/validate") as ValidationService)!;
    }

    [Fact]
    public async Task Validate_GivenHatWithTooFewParticipants_ReturnsInvalidResult()
    {
        // arrange
        var hat = new Hat
        {
            Id = Guid.NewGuid(),
            Name = string.Empty,
            Status = HatStatus.InProgress,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = Persons.Empty,
            Participants = [
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = _personFaker.Generate(),
                    EligibleRecipients = [],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = _personFaker.Generate(),
                    EligibleRecipients = [],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                }
            ],
            InvitationsQueuedDate = DateTimeOffset.MinValue
        };

        // act
        var result = await _sut.ValidateAsync(hat);

        // assert
        result.Value.Success.Should().BeFalse();
        result.Value.Errors.First().Should().Be("A gift exchange of this type needs at least three people.");
    }

    [Fact]
    public async Task Validate_GivenParticipantWithNoEligibleParticipants_ReturnsInvalidResult()
    {
        var people = _personFaker.Generate(3);

        // arrange
        var hat = new Hat
        {
            Id = Guid.NewGuid(),
            Name = string.Empty,
            Status = HatStatus.InProgress,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = Persons.Empty,
            Participants = [
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[0],
                    EligibleRecipients = [ people[1], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[1],
                    EligibleRecipients = [ people[0], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[2],
                    EligibleRecipients = [],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                }
            ],
            InvitationsQueuedDate = DateTimeOffset.MinValue
        };

        // act
        var result = await _sut.ValidateAsync(hat);

        // assert
        result.Value.Success.Should().BeFalse();
        result.Value.Errors.First().Should().Be($"{people[2].Name} must have at least two eligible recipients.");
    }

    [Fact]
    public async Task Validate_GivenParticipantWhoIsNotAnEligibleRecipient_ReturnsInvalidResult()
    {
        var people = _personFaker.Generate(4);

        // arrange
        var hat = new Hat
        {
            Id = Guid.NewGuid(),
            Name = string.Empty,
            Status = HatStatus.InProgress,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = Persons.Empty,
            Participants = [
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[0],
                    EligibleRecipients = [ people[1], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[1],
                    EligibleRecipients = [ people[0], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[2],
                    EligibleRecipients = [ people[0], people[1] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[3],
                    EligibleRecipients = [ people[1], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                }
            ],
            InvitationsQueuedDate = DateTimeOffset.MinValue
        };

        // act
        var result = await _sut.ValidateAsync(hat);

        // assert
        result.Value.Success.Should().BeFalse();
        result.Value.Errors.First().Should().Be($"{people[3].Name} is not an eligible recipient for any participant. Their name will not be picked.");
    }

    [Fact]
    public async Task Validate_GivenValidHat_ReturnsValidResult()
    {
        var people = _personFaker.Generate(4);

        // arrange
        var hat = new Hat
        {
            Id = Guid.NewGuid(),
            Name = string.Empty,
            Status = HatStatus.InProgress,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = Persons.Empty,
            Participants = [
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[0],
                    EligibleRecipients = [ people[1], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[1],
                    EligibleRecipients = [ people[0], people[3] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[2],
                    EligibleRecipients = [ people[0], people[3] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[3],
                    EligibleRecipients = [ people[1], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                }
            ],
            InvitationsQueuedDate = DateTimeOffset.MinValue
        };

        // act
        var result = await _sut.ValidateAsync(hat);

        // assert
        result.Value.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_GivenParticipantEligibleForOnlyOneOtherParticipant_ReturnsInvalidResult()
    {
        var people = _personFaker.Generate(4);

        // arrange
        var hat = new Hat
        {
            Id = Guid.NewGuid(),
            Name = string.Empty,
            Status = HatStatus.InProgress,
            AdditionalInformation = string.Empty,
            PriceRange = string.Empty,
            Organizer = Persons.Empty,
            Participants = [
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[0],
                    EligibleRecipients = [ people[1], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[1],
                    EligibleRecipients = [ people[0], people[3] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[2],
                    EligibleRecipients = [ people[0], people[1] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                },
                new Participant
                {
                    PickedRecipient = Persons.Empty,
                    Person = people[3],
                    EligibleRecipients = [ people[0], people[2] ],
                    Emoji = string.Empty,
                    DeliveryStatus = DeliveryStatus.Unknown,
                    DeliveryDetail = string.Empty,
                    DeliveryMessageType = string.Empty,
                    DeliveryOccurredAt = DateTimeOffset.MinValue
                }
            ],
            InvitationsQueuedDate = DateTimeOffset.MinValue
        };

        // act
        var result = await _sut.ValidateAsync(hat);

        // assert
        result.Value.Success.Should().BeFalse();
        result.Value.Errors.First().Should().Be($"{people[3].Name} is only an eligible recipient for one participant. This makes their assignment deterministic.");
    }
}

