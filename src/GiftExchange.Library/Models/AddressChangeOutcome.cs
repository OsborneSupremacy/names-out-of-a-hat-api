namespace GiftExchange.Library.Models;

/// <summary>
/// What became of an attempt to correct the address one participant was invited at.
///
/// Neither failure is about the address itself: one says the participant is gone, the other that
/// the address is already somebody else in this exchange.
/// </summary>
public enum AddressChangeOutcome
{
    /// <summary>The participant now points at the new address.</summary>
    Changed,

    /// <summary>Nobody in this exchange is recorded at the address given.</summary>
    ParticipantNotFound,

    /// <summary>
    /// Somebody else in this exchange already has the new address. Two participants cannot share
    /// one, which <c>uq_participant_hat_person</c> enforces underneath.
    /// </summary>
    AddressAlreadyInExchange
}
