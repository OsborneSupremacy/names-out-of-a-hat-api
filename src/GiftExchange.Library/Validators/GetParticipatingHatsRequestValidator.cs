namespace GiftExchange.Library.Validators;

internal class GetParticipatingHatsRequestValidator : AbstractValidator<GetParticipatingHatsRequest>
{
    public GetParticipatingHatsRequestValidator()
    {
        RuleFor(x => x.ParticipantEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);
    }
}
