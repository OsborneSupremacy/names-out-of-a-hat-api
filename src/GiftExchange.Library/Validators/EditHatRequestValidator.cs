namespace GiftExchange.Library.Validators;

public class EditHatRequestValidator : AbstractValidator<EditHatRequest>
{
    public EditHatRequestValidator()
    {
        RuleFor(x => x.HatId)
            .NotEmpty();

        RuleFor(x => x.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(3, 50)
            .Matches(@"^[\p{L}\p{N}\s\-'.,&()]+$")
            .WithMessage("'Name' must only contain letters, numbers, spaces, and common punctuation.");

        RuleFor(x => x.AdditionalInformation)
            .MaximumLength(2000)
            .Must(x => string.IsNullOrEmpty(x) || (!x.Contains('<') && !x.Contains('>') && !x.Contains('\0')))
            .WithMessage("'Additional Information' must not contain HTML or control characters.");

        RuleFor(x => x.PriceRange)
            .MaximumLength(50)
            .Matches(@"^[\p{L}\p{N}\s\-$€£¥.,/]+$")
            .When(x => !string.IsNullOrEmpty(x.PriceRange))
            .WithMessage("'Price Range' must only contain letters, numbers, spaces, currency symbols, and common punctuation.");

        // Only the far bound is checked here. Whether a date is too early depends on what was
        // stored before -- a date that has passed is fine to keep, and wrong to set -- so that
        // check belongs to EditHatService, which has the hat in hand.
        RuleFor(x => x.ExchangeDate)
            .Must(date => date == DateOnly.MinValue || date <= ExchangeDates.Latest(DateTimeOffset.UtcNow))
            .WithMessage("'Exchange Date' must be within the next two years.");
    }
}
