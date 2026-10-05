namespace GiftExchange.Library.Validators;

/// <summary>
/// Only the shape, for the reason <see cref="ShareGiftIdeasRequestValidator"/> gives. An empty
/// subject is let through to be answered as "choose who these ideas are about".
/// </summary>
internal class OfferGiftIdeasRequestValidator : AbstractValidator<OfferGiftIdeasRequest>
{
    public OfferGiftIdeasRequestValidator()
    {
        RuleFor(x => x.ParticipantEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.HatId)
            .NotEmpty();

        RuleFor(x => x.Ideas)
            .NotNull();
    }
}
