using System.Text.RegularExpressions;
using Bogus;

namespace GiftExchange.Library.Tests.Fakes;

/// <summary>
/// Bogus generates unbounded text, but the columns are bounded and Postgres enforces it where
/// DynamoDB did not. These keep generated values inside the same limits the validators enforce,
/// so the fakers produce data the application would actually accept.
/// </summary>
internal static class FakeValues
{
    /// <summary>
    /// The suffix keeps hat names unique per organizer, which the unique index now requires and
    /// which matters more since every test class shares one database.
    /// </summary>
    /// <remarks>
    /// Scrubbed down to what CreateHatRequestValidator allows. Bogus draws these words from a pool
    /// that includes things like "24/365", and a slash is outside the validator's set -- so roughly
    /// one hat in a few hundred was a name the application would have refused, and a test seeding
    /// one directly through the provider would never have noticed. Replaced with a space rather
    /// than dropped, so "24/365" reads as two words instead of one number nobody wrote.
    /// </remarks>
    public static string HatName(Faker faker) =>
        Truncate(
            DisallowedInHatName.Replace($"{faker.Random.Words(2)} {faker.Random.AlphaNumeric(6)}", " "),
            50);

    /// <summary>
    /// Everything outside the set CreateHatRequestValidator.HatName matches, which is the same set
    /// every other hat-name validator uses.
    /// </summary>
    private static readonly Regex DisallowedInHatName = new(@"[^\p{L}\p{N}\s\-'.,&()]");

    /// <summary>
    /// Bogus derives an address from the name it generated, so two faked people who land on the
    /// same name land on the same address. That used to be a duplicate within one hat at worst.
    /// Now that person.email identifies somebody for the whole application, it would mean two
    /// unrelated test subjects sharing one row — and the second one renaming the first.
    /// </summary>
    public static string Email(Faker faker) =>
        $"{faker.Random.AlphaNumeric(8)}.{faker.Person.Email}".ToLowerInvariant();

    /// <summary>
    /// A person's name, suffixed so that two faked people cannot land on the same one.
    /// </summary>
    /// <remarks>
    /// The same problem <see cref="Email"/> solves, and for a closely related reason. Bogus draws
    /// first names from a pool small enough that a hat seeded with a handful of participants
    /// regularly gets two of the same, and participants within a gift exchange must have distinct
    /// names -- so the collision does not fail where it happened. It surfaces later, somewhere
    /// else, as whatever that test happened to be asserting: an eligible-recipients list with a
    /// duplicate entry in ResetHatTests, a 409 about a duplicate name where AddParticipantTests
    /// expected one about the participant limit, or a 201 that never came. Three classes, failing
    /// differently, roughly one full run in five.
    ///
    /// Inside the rule AddParticipantRequestValidator enforces -- letters, digits and spaces are
    /// all it needs -- and short enough that the 100 character bound is never in question. The name
    /// stays readable so a failure message still reads like it is about a person.
    /// </remarks>
    public static string Name(Faker faker) =>
        Truncate($"{faker.Person.FirstName} {faker.Random.AlphaNumeric(8)}", 100);

    public static string PriceRange(Faker faker) => Truncate(faker.Random.Words(3), 50);

    public static string AdditionalInformation(Faker faker) => Truncate(faker.Random.Words(5), 2000);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}
