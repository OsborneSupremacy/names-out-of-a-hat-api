namespace GiftExchange.Library.Validators;

internal class CopyHatRequestValidator : AbstractValidator<CopyHatRequest>
{
    public CopyHatRequestValidator()
    {
        RuleFor(x => x.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.HatId)
            .NotEmpty();

        // Same rules as naming a brand new gift exchange, because that is what the copy is.
        RuleFor(x => x.NewHatName)
            .NotEmpty()
            .Length(3, 50)
            .Matches(@"^[\p{L}\p{N}\s\-'.,&()]+$")
            .WithMessage("'New Hat Name' must only contain letters, numbers, spaces, and common punctuation.")
            .NotContainLinks();
    }
}
