namespace GiftExchange.Library.Validators;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(2, 100)
            .Matches(@"^[\p{L}\p{N}\s\-'.,&()]+$")
            .WithMessage("'Name' must only contain letters, numbers, spaces, and common punctuation.")
            .NotContainLinks();
    }
}
