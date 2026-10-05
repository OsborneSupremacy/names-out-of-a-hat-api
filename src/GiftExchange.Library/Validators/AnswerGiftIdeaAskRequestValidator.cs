namespace GiftExchange.Library.Validators;

/// <summary>Only the shape, for the reason <see cref="ShareGiftIdeasRequestValidator"/> gives.</summary>
internal class AnswerGiftIdeaAskRequestValidator : AbstractValidator<AnswerGiftIdeaAskRequest>
{
    public AnswerGiftIdeaAskRequestValidator()
    {
        RuleFor(x => x.ParticipantEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.HatId)
            .NotEmpty();

        RuleFor(x => x.AskId)
            .NotEmpty();

        RuleFor(x => x.Ideas)
            .NotNull();
    }
}
