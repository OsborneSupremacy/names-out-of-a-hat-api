using System.Diagnostics;
using Bogus;

namespace GiftExchange.Library.Services;

/// <summary>
/// Draws the names. Given the participants and their eligible recipients, produces an assignment in
/// which everybody gives to exactly one person and receives from exactly one person — a derangement
/// of the participants — subject to whichever <see cref="DrawType"/> the organizer chose.
/// </summary>
/// <remarks>
/// Randomized and without backtracking, on purpose. A draw has to look arbitrary to the people in
/// it, and a solver that fills the hardest slot first would produce a defensible assignment that
/// is also, year after year, the same assignment. So each attempt walks the participants in a
/// random order taking random eligible picks, and an attempt that strands somebody is thrown away
/// and reseeded rather than unwound. That trade is only affordable because the graphs are dense:
/// a real exchange excludes a handful of pairs out of n squared, so almost every attempt succeeds
/// and the retries are there for the configurations that are merely tight rather than impossible.
/// A genuinely unsatisfiable set of exclusions exhausts the attempts and is reported as a failure,
/// which is the same outcome the caller would want from an exact solver, arrived at without one.
/// </remarks>
internal static class HatShakerService
{
    internal static ShakeHatResponse Shake(ShakeHatRequest request)
    {
        for (var attempt = 0; attempt < request.Attempts; attempt++)
        {
            var seed = new Randomizer().Int();
#if DEBUG
            Debug.WriteLine(seed);
#endif
            var response = ShakeOnce(request.Participants, request.DrawType, seed);

            if (response.Success)
                return response;
        }

        return ShakeHatResponse.Failed;
    }

    private static ShakeHatResponse ShakeOnce(
        ImmutableList<Participant> participants,
        string drawType,
        int seed
    )
    {
        Randomizer.Seed = new Random(seed);
        var faker = new Faker();

        // Clear any previous picks. A re-shake arrives with last shake's assignment still on the
        // records, and nothing below reads PickedRecipient, but leaving it set would mean the
        // failure paths return participants carrying a stale draw.
        var cleared = participants
            .Select(participant => participant with { PickedRecipient = Persons.Empty })
            .ToImmutableList();

        return drawType switch
        {
            var value when value == DrawType.SingleCycle => ShakeSingleCycle(cleared, faker),
            var value when value == DrawType.NoMutualPairs => ShakeCycleCover(cleared, faker, forbidMutualPairs: true),
            _ => ShakeCycleCover(cleared, faker, forbidMutualPairs: false)
        };
    }

    /// <summary>
    /// The general draw: hand out recipients until everybody has one, taking each giver in random
    /// order and giving them a random recipient nobody has taken yet. What comes out is a set of
    /// disjoint cycles of whatever lengths fall out — which is exactly what "anything goes" means.
    /// </summary>
    /// <param name="forbidMutualPairs">
    /// When true, a giver may not be given the one person who has already been given them. That
    /// single check is enough to rule out 2-cycles entirely rather than merely usually: a mutual
    /// pair needs both halves, and whichever half is assigned second is the one this refuses. There
    /// is no need to inspect the finished assignment for pairs, because none can have been built.
    /// </param>
    private static ShakeHatResponse ShakeCycleCover(
        ImmutableList<Participant> participants,
        Faker faker,
        bool forbidMutualPairs
    )
    {
        // Keyed by address, which is what identifies somebody within a hat. Names are not: two
        // people in one exchange may answer to the same one.
        var assignedGivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignedRecipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Giver address to the address they drew, for the mutual-pair check.
        var pickedByGiverEmail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var assigned = new List<Participant>();

        foreach (var _ in participants)
        {
            var giver = faker.PickRandom(
                participants.Where(participant => !assignedGivers.Contains(participant.Person.Email))
            );

            var eligibleRecipients = giver.EligibleRecipients
                .Where(recipient => !assignedRecipients.Contains(recipient.Email))
                .Where(recipient => !forbidMutualPairs || !HasDrawn(pickedByGiverEmail, recipient.Email, giver.Person.Email))
                .ToList();

            if (eligibleRecipients.Count == 0)
                return ShakeHatResponse.Failed;

            var pick = faker.PickRandom(eligibleRecipients);

            assigned.Add(giver with { PickedRecipient = pick });

            assignedGivers.Add(giver.Person.Email);
            assignedRecipients.Add(pick.Email);
            pickedByGiverEmail[giver.Person.Email] = pick.Email;
        }

        return new ShakeHatResponse { Success = true, Participants = assigned.ToImmutableList() };
    }

    /// <summary>
    /// Everybody in one chain. Starts somewhere at random and walks to a random eligible person
    /// nobody has been to yet, until the walk has been everywhere, then closes the loop back to
    /// where it started.
    /// </summary>
    /// <remarks>
    /// Built rather than filtered for. The obvious alternative — draw as usual and discard anything
    /// that came out in more than one piece — degrades with the size of the exchange, because the
    /// share of derangements that are a single cycle falls away as roughly e/n: about a quarter at
    /// ten people, a twentieth at fifty. Constructing the cycle keeps the odds tied to the
    /// exclusions instead, which is the thing that actually makes a draw hard.
    ///
    /// Two ways to fail, and they are different. The walk can arrive somewhere with nobody
    /// unvisited left to go to, or it can visit everybody and find the last person is not allowed
    /// to draw the first. Both are the same answer to the caller — try again with another seed.
    /// </remarks>
    private static ShakeHatResponse ShakeSingleCycle(ImmutableList<Participant> participants, Faker faker)
    {
        var byEmail = participants.ToDictionary(
            participant => participant.Person.Email,
            StringComparer.OrdinalIgnoreCase);

        // The type argument is stated because Bogus also has a params overload, which an
        // ImmutableList<T> otherwise binds to as a single item.
        var start = faker.PickRandom<Participant>(participants);
        var current = start;

        var chain = new List<Participant> { start };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start.Person.Email };

        while (chain.Count < participants.Count)
        {
            var candidates = current.EligibleRecipients
                .Where(recipient => !visited.Contains(recipient.Email))
                .ToList();

            if (candidates.Count == 0)
                return ShakeHatResponse.Failed;

            var nextEmail = faker.PickRandom(candidates).Email;

            // An eligible recipient naming somebody who is not in the hat would be a data fault
            // rather than a tight draw. Failing the attempt reports it the same way as any other
            // dead end instead of throwing out of the shaker.
            if (!byEmail.TryGetValue(nextEmail, out var next))
                return ShakeHatResponse.Failed;

            chain.Add(next);
            visited.Add(nextEmail);
            current = next;
        }

        if (!current.EligibleRecipients.Any(recipient => recipient.Email.ContentEquals(start.Person.Email)))
            return ShakeHatResponse.Failed;

        var assigned = chain
            .Select((participant, position) => participant with
            {
                PickedRecipient = chain[(position + 1) % chain.Count].Person
            })
            .ToImmutableList();

        return new ShakeHatResponse { Success = true, Participants = assigned };
    }

    private static bool HasDrawn(
        Dictionary<string, string> pickedByGiverEmail,
        string giverEmail,
        string recipientEmail
    ) =>
        pickedByGiverEmail.TryGetValue(giverEmail, out var theirPick) && theirPick.ContentEquals(recipientEmail);
}
