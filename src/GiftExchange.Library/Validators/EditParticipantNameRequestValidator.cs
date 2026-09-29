namespace GiftExchange.Library.Validators;

public class EditParticipantNameRequestValidator : AbstractValidator<EditParticipantNameRequest>
{
    public EditParticipantNameRequestValidator()
    {
        RuleFor(x => x.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.HatId)
            .NotEmpty();

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        // The same rule AddParticipantRequestValidator applies, because this is the same field
        // being written by the same person. A name an organizer could type when adding somebody
        // and then not be able to correct would be the worse of the two states to be in.
        RuleFor(x => x.Name)
            .NotEmpty()
            .Length(2, 100)
            .Matches(@"^[\p{L}\p{N}\s\-'.,&()]+$")
            .WithMessage("'Name' must only contain letters, numbers, spaces, and common punctuation.")
            .NotContainLinks();
    }
}
