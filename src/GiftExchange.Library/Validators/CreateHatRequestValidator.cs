namespace GiftExchange.Library.Validators;

public class CreateHatRequestValidator : AbstractValidator<CreateHatRequest>
{
    public CreateHatRequestValidator()
    {
        RuleFor(x => x.HatName)
            .NotEmpty()
            .Length(3, 50)
            .Matches(@"^[\p{L}\p{N}\s\-'.,&()]+$")
            .WithMessage("'Hat Name' must only contain letters, numbers, spaces, and common punctuation.")
            .NotContainLinks();

        RuleFor(x => x.OrganizerName)
            .NotEmpty()
            .Length(2, 100)
            .Matches(@"^[\p{L}\p{N}\s\-'.,&()]+$")
            .WithMessage("'Organizer Name' must only contain letters, numbers, spaces, and common punctuation.")
            .NotContainLinks();

        RuleFor(x => x.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);
    }
}
