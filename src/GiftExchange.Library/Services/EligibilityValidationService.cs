namespace GiftExchange.Library.Services;

internal static class EligibilityValidationService
{
    public static Task<Result<ValidateHatResponse>> Validate(IList<Participant> participants)
    {
        var errors = new List<string>();

        var people = participants.Select(x => x.Person).ToList();

        // Two participants may share a name, and a message about "Sam" is no use when there are two.
        string Named(Person person) => ParticipantNaming.DisplayName(person, people);

        errors.AddRange(
            participants
                .Where(p => p.EligibleRecipients.Count < 2)
                .Select(p => $"{Named(p.Person)} must have at least two eligible recipients.")
        );

        if (errors.Any())
            return Task.FromResult(
                new Result<ValidateHatResponse>(new ValidateHatResponse { Success = false, Errors = errors.ToImmutableList() }, HttpStatusCode.OK)
            );

        // By address, which is what identifies somebody within a hat. Names are not unique.
        var recipientEligibilityCounts = participants
            .SelectMany(p => p.EligibleRecipients)
            .GroupBy(recipient => recipient.Email.TrimNullSafe(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var person in people)
            if(!recipientEligibilityCounts.TryGetValue(person.Email.TrimNullSafe(), out var eligibleForCount) || eligibleForCount == 0)
                errors.Add($"{Named(person)} is not an eligible recipient for any participant. Their name will not be picked.");
            else if (eligibleForCount == 1)
                errors.Add($"{Named(person)} is only an eligible recipient for one participant. This makes their assignment deterministic.");

        if (errors.Any())
            return Task.FromResult(
                new Result<ValidateHatResponse>(new ValidateHatResponse { Success = false, Errors = errors.ToImmutableList() }, HttpStatusCode.OK)
            );

        return Task.FromResult(new Result<ValidateHatResponse>(
            new ValidateHatResponse { Success = true, Errors = [] }, HttpStatusCode.OK)
        );
    }
}
