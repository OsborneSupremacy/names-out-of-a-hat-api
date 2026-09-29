namespace GiftExchange.Library.Services;

/// <summary>
/// Whether somebody has said they do not want to be added to a gift exchange, checked before every
/// path that adds one.
/// </summary>
/// <remarks>
/// Three lists, asked at once. They are independent questions against independent tables — has this
/// person refused this exchange, this organizer, or all of it — and running them in sequence would
/// spend three round trips on something that answers in the time of one. Each provider method opens
/// its own <c>DbContext</c> from the factory, which is what makes that safe; a shared context is not
/// thread-safe and is the reason the factory exists at all.
///
/// The three are never distinguished to the caller. A single refusal message covers all of them on
/// purpose: an organizer who could tell "they blocked you" from "they blocked everybody" could
/// learn, by typing an address into a new exchange, a fact the person deliberately did not tell
/// them. What the organizer needs to know is that this address is not available to them, and that
/// is the whole of what they are told.
///
/// Fails open, unlike <c>ReplyThrottleProvider</c>, which fails closed. The comparison is worth
/// stating because the two look alike and the trade runs the other way. A throttle that fails open
/// sends mail somebody has already had; this check failing closed would refuse every add for as
/// long as the database was unreachable, which breaks the ordinary use of the application to
/// enforce a rule against a person who is probably not in the request at all. Exceptions therefore
/// propagate rather than being swallowed into a refusal — the caller's own error handling turns
/// them into a 500, which is honest, rather than into "this person has asked not to be added",
/// which would not be.
/// </remarks>
[UsedImplicitly]
internal class DoNotAddService
{
    /// <summary>
    /// What an organizer is told when an address is on any of the three lists.
    /// </summary>
    /// <remarks>
    /// Deliberately says nothing about which list, when it was added, or by whom. See the class
    /// remarks: the difference between the three is exactly what an organizer must not be able to
    /// probe for.
    ///
    /// Worded as something the person did rather than something this application decided, because
    /// that is what happened, and because an organizer who reads it as a bug will write in about a
    /// bug.
    /// </remarks>
    public const string RefusalMessage = "This person has asked not to be added to gift exchanges.";

    private readonly GiftExchangeProvider _giftExchangeProvider;

    // ReSharper disable once ConvertToPrimaryConstructor
    public DoNotAddService(GiftExchangeProvider giftExchangeProvider)
    {
        _giftExchangeProvider = giftExchangeProvider ?? throw new ArgumentNullException(nameof(giftExchangeProvider));
    }

    /// <summary>
    /// Which of these addresses may not be added, as mailbox keys.
    /// </summary>
    /// <remarks>
    /// Returns the keys rather than the addresses as they were passed in, because that is the only
    /// form in which two spellings of one inbox are one thing -- and a refusal is a person's, so
    /// <c>me+1@</c> refusing has to refuse <c>me+2@</c> too. Callers comparing against their own
    /// input should key it with <c>ToMailboxKey</c>; the single-address callers use
    /// <see cref="IsRefusedAsync"/> and never have to.
    /// </remarks>
    public async Task<ImmutableHashSet<string>> FindRefusedAsync(DoNotAddCheckRequest request)
    {
        var emails = request.Emails
            .Select(email => email.ToMailboxKey())
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct()
            .ToImmutableList();

        if (emails.IsEmpty)
            return [];

        var organizerEmail = request.OrganizerEmail.ToMailboxKey();

        // Started together and awaited together. Nothing here depends on anything else here, and
        // the tasks are begun before the first await for that reason — writing them as three
        // sequential awaits would look almost identical and cost three times as much.
        var byExchange = _giftExchangeProvider.FindBlockedByExchangeAsync(emails, request.HatId);
        var byOrganizer = _giftExchangeProvider.FindBlockedByOrganizerAsync(emails, organizerEmail);
        var anywhere = _giftExchangeProvider.FindBlockedAnywhereAsync(emails);

        var lists = await Task.WhenAll(byExchange, byOrganizer, anywhere).ConfigureAwait(false);

        return [.. lists.SelectMany(blocked => blocked)];
    }

    /// <summary>
    /// Whether one address may not be added. What every caller but the exchange copier wants.
    /// </summary>
    public async Task<bool> IsRefusedAsync(string email, string organizerEmail, Guid hatId)
    {
        var refused = await FindRefusedAsync(new DoNotAddCheckRequest
            {
                Emails = [email],
                OrganizerEmail = organizerEmail,
                HatId = hatId
            })
            .ConfigureAwait(false);

        return !refused.IsEmpty;
    }
}
