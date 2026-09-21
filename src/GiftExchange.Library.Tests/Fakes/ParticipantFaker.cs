using Bogus;

namespace GiftExchange.Library.Tests.Fakes;

public sealed class ParticipantFaker : Faker<Participant>
{
    public ParticipantFaker()
    {
        RuleFor(p => p.Person, _ => new PersonFaker().Generate());
        RuleFor(f => f.PickedRecipient, _ => Persons.Empty);
        RuleFor(f => f.EligibleRecipients, []);
        // A real face rather than a random string: everything downstream of a participant renders
        // this, and a faked one standing for something the application would never store would make
        // those tests agree with a shape that cannot happen.
        RuleFor(f => f.Emoji, faker => faker.PickRandom(PersonEmoji.All.ToList()));
        // Nothing heard. A faked participant has had nothing sent to them, which is what an empty
        // status means -- see the remarks on Participant.DeliveryStatus.
        RuleFor(f => f.DeliveryStatus, DeliveryStatus.Unknown);
        RuleFor(f => f.DeliveryDetail, string.Empty);
        RuleFor(f => f.DeliveryMessageType, string.Empty);
        RuleFor(f => f.DeliveryOccurredAt, DateTimeOffset.MinValue);
    }
}
