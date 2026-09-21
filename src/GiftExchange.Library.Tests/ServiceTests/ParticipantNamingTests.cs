namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// How one participant is named to somebody else: by name alone, unless somebody else in the
/// exchange shares it.
/// </summary>
public class ParticipantNamingTests
{
    private static readonly Person Ana = new() { Name = "Ana", Email = "ana@example.com" };

    private static readonly Person FirstSam = new() { Name = "Sam", Email = "sam.one@example.com" };

    private static readonly Person SecondSam = new() { Name = "Sam", Email = "sam.two@example.com" };

    [Fact]
    public void DisplayName_GivenANameNobodyElseHas_IsTheNameAlone()
    {
        ParticipantNaming.DisplayName(Ana, [Ana, FirstSam]).Should().Be("Ana");
    }

    [Fact]
    public void DisplayName_GivenANameSomebodyElseHas_AddsTheAddress()
    {
        ParticipantNaming.DisplayName(FirstSam, [Ana, FirstSam, SecondSam])
            .Should().Be("Sam (sam.one@example.com)");
    }

    /// <summary>
    /// Compared the way two names would read to a person, so "sam " and "SAM" are the same name.
    /// </summary>
    [Fact]
    public void DisplayName_ComparesNamesIgnoringCaseAndSurroundingSpace()
    {
        var shouting = new Person { Name = " SAM ", Email = "sam.three@example.com" };

        ParticipantNaming.DisplayName(FirstSam, [FirstSam, shouting])
            .Should().Be("Sam (sam.one@example.com)");
    }

    /// <summary>
    /// The person being named is usually in the list they are being compared with. They do not
    /// share a name with themselves.
    /// </summary>
    [Fact]
    public void DisplayName_DoesNotCountThePersonThemselves()
    {
        ParticipantNaming.DisplayName(FirstSam, [FirstSam]).Should().Be("Sam");
    }

    [Fact]
    public void DisplayName_GivenNobody_IsEmpty()
    {
        ParticipantNaming.DisplayName(Persons.Empty, [FirstSam, SecondSam]).Should().BeEmpty();
    }
}
