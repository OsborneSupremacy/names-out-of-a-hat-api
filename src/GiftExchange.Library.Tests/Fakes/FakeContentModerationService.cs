namespace GiftExchange.Library.Tests.Fakes;

internal class FakeContentModerationService : IContentModerationService
{
    public Task<(bool IsValid, string ErrorMessage)> ValidateContentAsync(string text, string fieldName) =>
        Task.FromResult((true, string.Empty));

    public Task<ModerationVerdict> ModerateAsync(string text, string fieldName) =>
        Task.FromResult(ModerationVerdict.Clean);

    /// <summary>Every field set this was asked to check, for tests about when moderation runs.</summary>
    public List<Dictionary<string, string>> ValidatedFieldSets { get; } = [];

    public Task<(bool IsValid, List<string> ErrorMessages)> ValidateMultipleFieldsAsync(Dictionary<string, string> fieldsToValidate)
    {
        ValidatedFieldSets.Add(fieldsToValidate);
        return Task.FromResult((true, new List<string>()));
    }
}
