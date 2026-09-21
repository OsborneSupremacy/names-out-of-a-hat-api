using GiftExchange.Library.Utility;
using GiftExchange.Library.Validators;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// The face a participant is marked with, from the moment they are added to the moment an organizer
/// changes it, against a real database.
///
/// It used to be derived from the name wherever one was needed, which meant it could not be edited
/// and moved on its own when somebody was renamed. What these pin down is that it is now a stored
/// fact: assigned once, carried into a copy, and changed only by the endpoint that exists to change
/// it — in particular without disturbing the draw, which is the whole reason that endpoint is not
/// part of editing a participant.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ParticipantEmojiTests
{
    private readonly GiftExchangeProvider _provider;

    private readonly EditParticipantEmojiService _sut;

    private readonly HatDataModelFaker _hatFaker = new();

    private readonly AddParticipantRequestFaker _participantFaker = new();

    public ParticipantEmojiTests(PostgresFixture dbFixture)
    {
        var contextFactory = dbFixture.CreateContextFactory();

        var serviceProvider = new ServiceCollection()
            .AddUtilities()
            .AddBusinessServices()
            .AddSingleton(contextFactory)
            .BuildServiceProvider();

        _provider = serviceProvider.GetRequiredService<GiftExchangeProvider>();

        _sut = new EditParticipantEmojiService(
            serviceProvider.GetRequiredService<ApiGatewayAdapter>(),
            new HatPreconditionValidator(
                Substitute.For<ILogger<HatPreconditionValidator>>(),
                _provider,
                new FakeContentModerationService()),
            _provider);
    }

    [Fact]
    public async Task AddingAParticipant_GivesThemAFaceThatIsStored()
    {
        // arrange & act
        var exchange = await SeedAsync(3);

        // assert: what CreateParticipantAsync returned is what a later read finds, rather than
        // something recomputed on the way out.
        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);

        foreach (var created in exchange.Participants)
            hat.Participants
                .Single(participant => participant.Person.Email == created.Person.Email)
                .Emoji.Should().Be(created.Emoji);

        hat.Participants.Should().OnlyContain(participant => PersonEmoji.IsOffered(participant.Emoji));
    }

    /// <summary>
    /// Distinct while there are faces left to be distinct with. Three participants out of twenty
    /// faces is well inside that, so a repeat here would be the rule failing rather than the hat
    /// being large.
    /// </summary>
    [Fact]
    public async Task ParticipantsInOneHat_AreGivenDifferentFaces()
    {
        // arrange & act
        var exchange = await SeedAsync(3);

        // assert
        exchange.Participants
            .Select(participant => participant.Emoji)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task CopyingAHat_CarriesEveryFaceOver()
    {
        // arrange
        var exchange = await SeedAsync(3);

        var copy = _hatFaker.Generate() with { OrganizerEmail = exchange.OrganizerEmail };

        // act
        var copied = await _provider.CopyHatAsync(new CopyHatDataRequest
        {
            SourceHatId = exchange.HatId,
            NewHat = copy,
            ExcludePreviousRecipients = false,
            RefusedEmails = []
        });

        // assert: the same people doing the same thing next year, still wearing the faces the
        // organizer left them with.
        copied.Should().BeTrue();

        var (_, copiedHat) = await _provider.GetHatAsync(copy.OrganizerEmail, copy.HatId);

        foreach (var participant in exchange.Participants)
            copiedHat.Participants
                .Single(candidate => candidate.Person.Email == participant.Person.Email)
                .Emoji.Should().Be(participant.Emoji);
    }

    [Fact]
    public async Task AnOrganizerChangingAFace_ChangesIt()
    {
        // arrange
        var exchange = await SeedAsync(3);
        var target = exchange.Participants[0];
        var chosen = PersonEmoji.All.First(emoji => emoji != target.Emoji);

        // act
        var result = await _sut.EditParticipantEmojiAsync(Request(exchange, target.Person.Email, chosen));

        // assert
        result.IsFaulted.Should().BeFalse();

        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);

        hat.Participants
            .Single(participant => participant.Person.Email == target.Person.Email)
            .Emoji.Should().Be(chosen);
    }

    /// <summary>
    /// The reason this is its own endpoint. Editing a participant's eligibility resets the hat to
    /// IN_PROGRESS and throws the draw away, which for a change of decoration would be absurd.
    /// </summary>
    [Fact]
    public async Task ChangingAFaceAfterTheDraw_LeavesTheDrawAndTheStatusAlone()
    {
        // arrange
        var exchange = await SeedAsync(3);

        for (var index = 0; index < exchange.Participants.Count; index++)
            await _provider.UpdateParticipantPickedRecipientAsync(
                exchange.OrganizerEmail,
                exchange.HatId,
                exchange.Participants[index].Person.Email,
                exchange.Participants[(index + 1) % exchange.Participants.Count].Person.Email);

        await _provider.UpdateHatStatusAsync(exchange.OrganizerEmail, exchange.HatId, HatStatus.InvitationsSent);

        var target = exchange.Participants[0];

        // act
        var result = await _sut.EditParticipantEmojiAsync(
            Request(exchange, target.Person.Email, PersonEmoji.All.First(emoji => emoji != target.Emoji)));

        // assert
        result.IsFaulted.Should().BeFalse();

        var (_, hat) = await _provider.GetHatAsync(exchange.OrganizerEmail, exchange.HatId);

        hat.Status.Should().Be(HatStatus.InvitationsSent);
        hat.Participants
            .Single(participant => participant.Person.Email == target.Person.Email)
            .PickedRecipient.Should().Be(exchange.Participants[1].Person);
    }

    [Fact]
    public async Task AnAddressNobodyInTheExchangeHas_IsNotFound()
    {
        // arrange
        var exchange = await SeedAsync(3);

        // act
        var result = await _sut.EditParticipantEmojiAsync(
            Request(exchange, "stranger@example.com", PersonEmoji.All[0]));

        // assert
        result.IsFaulted.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The column is not free text, and this request is the only way anything could try to make it
    /// so. Refused before the handler, which is why the handler has nothing to say about it.
    /// </summary>
    [Theory]
    [InlineData("🦕")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("")]
    public void AFaceThisApplicationDoesNotOffer_IsRefused(string emoji)
    {
        // arrange
        var validator = new EditParticipantEmojiRequestValidator();

        // act
        var result = validator.Validate(new EditParticipantEmojiRequest
        {
            OrganizerEmail = "ben@example.com",
            HatId = Guid.CreateVersion7(),
            Email = "alice@example.com",
            Emoji = emoji
        });

        // assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void EveryFaceOffered_IsAccepted()
    {
        // arrange
        var validator = new EditParticipantEmojiRequestValidator();

        // act
        var refused = PersonEmoji.All
            .Where(emoji => !validator.Validate(new EditParticipantEmojiRequest
            {
                OrganizerEmail = "ben@example.com",
                HatId = Guid.CreateVersion7(),
                Email = "alice@example.com",
                Emoji = emoji
            }).IsValid);

        // assert
        refused.Should().BeEmpty();
    }

    private static EditParticipantEmojiRequest Request(Exchange exchange, string email, string emoji) =>
        new()
        {
            OrganizerEmail = exchange.OrganizerEmail,
            HatId = exchange.HatId,
            Email = email,
            Emoji = emoji
        };

    private async Task<Exchange> SeedAsync(int participants)
    {
        var hat = _hatFaker.Generate();
        await _provider.CreateHatAsync(hat);

        var created = new List<Participant>();

        foreach (var _ in Enumerable.Range(0, participants))
        {
            var request = _participantFaker.Generate() with
            {
                HatId = hat.HatId,
                OrganizerEmail = hat.OrganizerEmail
            };

            created.Add(await _provider.CreateParticipantAsync(request, [.. created]));
        }

        return new Exchange
        {
            HatId = hat.HatId,
            OrganizerEmail = hat.OrganizerEmail,
            Participants = [.. created]
        };
    }

    private sealed record Exchange
    {
        public required Guid HatId { get; init; }
        public required string OrganizerEmail { get; init; }
        public required ImmutableList<Participant> Participants { get; init; }
    }
}
