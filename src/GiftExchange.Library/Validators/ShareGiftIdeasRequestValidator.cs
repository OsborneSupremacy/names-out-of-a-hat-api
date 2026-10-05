namespace GiftExchange.Library.Validators;

/// <summary>
/// Only the shape. What was written is the content policy's to judge, and it explains a refusal in
/// the words the email page uses, which a validation error here would not.
/// </summary>
internal class ShareGiftIdeasRequestValidator : AbstractValidator<ShareGiftIdeasRequest>
{
    public ShareGiftIdeasRequestValidator()
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
