using GiftExchange.Library.Validators;

namespace GiftExchange.Library.Tests.ServiceTests;

/// <summary>
/// Organizer text goes out from our domain to people who asked for nothing, so none of it may carry
/// a link.
/// </summary>
public class OrganizerTextRulesTests
{
    private readonly EditHatRequestValidator _editHat = new();

    [Theory]
    [InlineData("Claim your gift card at https://example.com/claim before Friday")]
    [InlineData("Details at www.example.com")]
    [InlineData("Sign up: bit.ly/abc123")]
    public void EditHat_GivenAdditionalInformationWithALink_IsRefused(string additionalInformation)
    {
        // act
        var result = _editHat.Validate(EditHat(additionalInformation: additionalInformation));

        // assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.ErrorMessage.Contains("must not contain links"));
    }

    [Theory]
    // The link pattern's own guard against version numbers and dotted words.
    [InlineData("Bring something under $25. Node.js fans, no more laptop stickers please.")]
    [InlineData("We meet at 5 p.m. on the 24th, i.e. after dinner.")]
    public void EditHat_GivenOrdinaryInstructions_IsAccepted(string additionalInformation) =>
        _editHat.Validate(EditHat(additionalInformation: additionalInformation)).IsValid.Should().BeTrue();

    [Fact]
    public void EditHat_GivenANameThatIsABareHost_IsRefused() =>
        _editHat.Validate(EditHat(name: "www.example.com")).IsValid.Should().BeFalse();

    [Fact]
    public void AddParticipant_GivenANameThatIsABareHost_IsRefused() =>
        new AddParticipantRequestValidator()
            .Validate(new AddParticipantRequest
            {
                OrganizerEmail = "organizer@example.com",
                HatId = Guid.NewGuid(),
                Name = "www.example.com",
                Email = "someone@example.com"
            })
            .IsValid.Should().BeFalse();

    private static EditHatRequest EditHat(string name = "Family 2026", string additionalInformation = "") =>
        new()
        {
            HatId = Guid.NewGuid(),
            OrganizerEmail = "organizer@example.com",
            Name = name,
            AdditionalInformation = additionalInformation,
            PriceRange = "$25",
            ExchangeDate = DateOnly.MinValue
        };
}
