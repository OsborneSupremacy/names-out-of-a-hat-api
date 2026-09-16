namespace GiftExchange.Library.Validators;

public class DeleteMyDataRequestValidator : AbstractValidator<DeleteMyDataRequest>
{
    public DeleteMyDataRequestValidator()
    {
        RuleFor(x => x.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);
    }
}
