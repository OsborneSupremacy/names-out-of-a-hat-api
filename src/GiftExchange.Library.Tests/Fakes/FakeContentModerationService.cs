namespace GiftExchange.Library.Tests.Fakes;

internal class FakeContentModerationService : IContentModerationService
{
    public Task<(bool IsValid, string ErrorMessage)> ValidateContentAsync(string text, string fieldName) =>
        Task.FromResult((true, string.Empty));

    public Task<ModerationVerdict> ModerateAsync(string text, string fieldName) =>
        Task.FromResult(ModerationVerdict.Clean);

    public Task<(bool IsValid, List<string> ErrorMessages)> ValidateMultipleFieldsAsync(Dictionary<string, string> fieldsToValidate) =>
        Task.FromResult((true, new List<string>()));
}
