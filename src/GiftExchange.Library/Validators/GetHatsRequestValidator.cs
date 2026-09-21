namespace GiftExchange.Library.Validators;

internal class GetHatsRequestValidator : AbstractValidator<GetHatsRequest>
{
    public GetHatsRequestValidator()
    {
        RuleFor(x => x.OrganizerEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);
    }
}
