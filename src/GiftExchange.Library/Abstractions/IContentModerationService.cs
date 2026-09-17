namespace GiftExchange.Library.Abstractions;

internal interface IContentModerationService
{
    public Task<(bool IsValid, string ErrorMessage)> ValidateContentAsync(string text, string fieldName);

    /// <summary>
    /// Checks text, telling a refusal apart from a check that could not be made. Fails closed:
    /// nothing that was not checked is ever <see cref="ModerationVerdict.Clean"/>.
    /// </summary>
    public Task<ModerationVerdict> ModerateAsync(string text, string fieldName);

    public Task<(bool IsValid, List<string> ErrorMessages)> ValidateMultipleFieldsAsync(
        Dictionary<string, string> fieldsToValidate);
}
