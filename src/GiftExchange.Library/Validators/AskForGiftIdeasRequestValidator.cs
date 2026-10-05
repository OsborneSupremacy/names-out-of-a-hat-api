namespace GiftExchange.Library.Validators;

/// <summary>
/// Only the shape. The question is the question policy's to judge, and an empty list is let through
/// to be answered as "choose at least one person".
/// </summary>
internal class AskForGiftIdeasRequestValidator : AbstractValidator<AskForGiftIdeasRequest>
{
    public AskForGiftIdeasRequestValidator()
    {
        RuleFor(x => x.ParticipantEmail)
            .NotEmpty()
            .EmailAddress()
            .Length(5, 254);

        RuleFor(x => x.HatId)
            .NotEmpty();

        // Nobody can be asked who is not in the exchange, so a longer list than an exchange can hold
        // is not a list anybody made by ticking boxes.
        RuleFor(x => x.ParticipantIds)
            .NotNull()
            .Must(ids => ids.Count <= ParticipantLimit.MaxParticipants)
            .WithMessage($"You can't ask more than {ParticipantLimit.MaxParticipants} people.");

        RuleFor(x => x.Question)
            .NotNull();
    }
}
