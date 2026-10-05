namespace GiftExchange.Library.Validators;

internal class LeaveGiftExchangeRequestValidator : AbstractValidator<LeaveGiftExchangeRequest>
{
    public LeaveGiftExchangeRequestValidator()
    {
        RuleFor(x => x.ParticipantEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.HatId)
            .NotEmpty();
    }
}
