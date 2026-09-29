namespace GiftExchange.Library.Validators;

/// <summary>
/// Rules for text an organizer writes that is then mailed to other people.
/// </summary>
internal static class OrganizerTextRules
{
    /// <summary>
    /// Refuses anything that reads as a link.
    /// </summary>
    /// <remarks>
    /// Stricter than <see cref="Services.GiftIdeaContentPolicy"/>, for the reason
    /// <see cref="Services.AskQuestionPolicy"/> is: everything an organizer writes goes out from
    /// our domain to a list of addresses they chose, to people who asked for nothing, and signing in
    /// takes only an inbox. A link there is what turns this application into a way of sending
    /// somebody else's phishing under our name, and moderation cannot tell a polite scam from a
    /// polite reminder. The names, price range and instructions of a gift exchange have no need of
    /// one.
    ///
    /// The names and the price range already refuse the ':' and '/' a full link needs, but not a
    /// bare "www." host, so they get this rule too.
    /// </remarks>
    public static IRuleBuilderOptions<T, string> NotContainLinks<T>(this IRuleBuilder<T, string> rule) =>
        rule
            .Must(text => string.IsNullOrEmpty(text) || GiftIdeaContentPolicy.FindLinks(text).IsEmpty)
            .WithMessage("'{PropertyName}' must not contain links. Everything in it is emailed to your participants.");
}
