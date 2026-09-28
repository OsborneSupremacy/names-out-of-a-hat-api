using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;

namespace GiftExchange.Library.Providers;

/// <summary>
/// Asks SES whether an address is on the account-level suppression list, so that an organizer is
/// stopped from adding somebody no email can reach.
/// </summary>
/// <remarks>
/// The suppression list rather than this application's own delivery records, because it is the
/// thing that actually decides. SES adds an address when it hard-bounces and from then on drops
/// every message to it before it leaves, reporting each one as a bounce -- which is what an
/// organizer sees as somebody who "never gets our emails". Our own rows cannot answer the same
/// question as well: they go when an exchange is deleted, and a corrected address carries the old
/// one's bounce with it. The list survives both, and removing an address from it in the console is
/// all it takes to let somebody back in.
///
/// It is the whole account's list, not this application's. That is the right scope for the
/// question: an address suppressed because of another sender in the account is dropped for this one
/// too. It relies on account-level suppression being on for BOUNCE, which it is by default and which
/// the giftexchange-outbound configuration set does not override.
///
/// Fails open. SES being unreachable is no reason to stop an organizer adding their sister, and a
/// suppressed address that slips through costs one bounce, which the delivery column then shows.
/// </remarks>
[UsedImplicitly]
internal class SuppressionListProvider : ISuppressionListProvider
{
    /// <summary>
    /// What an organizer is told. Written for the two cases that land here: a typo, and an address
    /// that is right but that mail cannot reach, about which there is nothing either of us can do.
    /// </summary>
    public const string RefusalMessage =
        "Emails to this address can't be delivered. Please check it for typos. If it's correct, something beyond our control is stopping mail from reaching it, so unfortunately it can't be used in a gift exchange.";

    private readonly IAmazonSimpleEmailServiceV2 _ses;

    private readonly ILogger<SuppressionListProvider> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public SuppressionListProvider(IAmazonSimpleEmailServiceV2 ses, ILogger<SuppressionListProvider> logger)
    {
        _ses = ses ?? throw new ArgumentNullException(nameof(ses));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsSuppressedAsync(string email)
    {
        var normalized = email.ToNormalizedEmail();

        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            await _ses
                .GetSuppressedDestinationAsync(new GetSuppressedDestinationRequest { EmailAddress = normalized })
                .ConfigureAwait(false);

            // Any answer at all means it is on the list. The reason -- bounce or complaint -- is not
            // passed on: a complaint is already covered by the do-not-add list, checked first, and
            // either way the mail is not going to arrive.
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not check the SES suppression list; letting the address through.");
            return false;
        }
    }
}
