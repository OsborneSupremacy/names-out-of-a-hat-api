using System.Data;
using Npgsql;

namespace GiftExchange.Library.Providers;

/// <summary>
/// Data access for gift exchanges.
///
/// The public surface still speaks the domain records, which identify people by display name and
/// email address. Storage no longer does: a person is a row of their own, and hats and participants
/// point at it, so this class translates at the boundary. That containment is deliberate — the API
/// contract still carries names, so changing it is a separate job.
///
/// A session arrives carrying an email address, so almost every method here starts by turning one
/// into a person id. Reads do that with a join through the <c>Organizer</c> navigation; writes look
/// the id up first and then filter on it directly, which keeps the SQL behind ExecuteUpdate and
/// ExecuteDelete free of subqueries that DSQL may or may not accept.
/// </summary>
[UsedImplicitly]
public class GiftExchangeProvider
{
    /// <summary>Postgres unique_violation.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>One gift exchange name per organizer.</summary>
    private const string HatNamePerOrganizerIndex = "uq_hat_organizer_name";

    /// <summary>One person per email address, for the whole application.</summary>
    private const string PersonEmailIndex = "uq_person_email";

    /// <summary>One delivery row per SES message id.</summary>
    private const string DeliveryMessageIndex = "uq_participant_email_delivery_message";

    /// <summary>One record per giver of the fact that they asked for ideas about their pick.</summary>
    private const string GiftIdeaEnquiryPairIndex = "uq_gift_idea_enquiry_asker_subject";

    private readonly IDbContextFactory<GiftExchangeDbContext> _contextFactory;

    private readonly ILogger<GiftExchangeProvider> _logger;

    // ReSharper disable once ConvertToPrimaryConstructor
    public GiftExchangeProvider(
        IDbContextFactory<GiftExchangeDbContext> contextFactory,
        ILogger<GiftExchangeProvider> logger
    )
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(string organizerName, ImmutableList<HatMetaData> hats)> GetHatsAsync(string organizerEmail)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // The sentinel person holds the empty address, so an empty one here would match it and go on
        // to return the sentinel hat as though it were theirs. Nothing upstream should send one --
        // the address comes from the authorizer -- which is exactly why it is cheap to refuse.
        if (string.IsNullOrWhiteSpace(organizerEmail))
            return (string.Empty, []);

        // The name comes from the person, not from the newest hat, so somebody who has signed in
        // but not created an exchange yet is still greeted by name.
        var organizer = await context.Persons
            .AsNoTracking()
            .Where(person => person.Email == organizerEmail)
            .Select(person => new { person.PersonId, person.Name })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (organizer is null)
            return (string.Empty, []);

        var hats = await context.Hats
            .AsNoTracking()
            .Where(hat => hat.OrganizerPersonId == organizer.PersonId)
            .OrderByDescending(hat => hat.CreatedAt)
            .Select(hat => new HatMetaData
            {
                HatId = hat.HatId,
                HatName = hat.Name,
                Status = hat.Status,
                StatusUpdatedAt = hat.StatusUpdatedAt
            })
            .ToListAsync()
            .ConfigureAwait(false);

        return (organizer.Name, hats.ToImmutableList());
    }

    /// <returns>true if the hat was created, false if the organizer already has one by that name.</returns>
    public async Task<bool> CreateHatAsync(HatDataModel hatDataModel)
    {
        // Introducing themselves: an organizer creating an exchange is the one case where the
        // person being written and the person writing them are the same.
        var organizerPersonId = await ResolvePersonIdAsync(new ResolvePersonRequest
            {
                Email = hatDataModel.OrganizerEmail,
                Name = hatDataModel.OrganizerName,
                IntroducedByEmail = hatDataModel.OrganizerEmail
            })
            .ConfigureAwait(false);

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // Creation is where the hat takes its first status, so the two timestamps are the same
        // reading rather than two calls to the clock a few microseconds apart.
        var createdAt = DateTimeOffset.UtcNow;

        context.Hats.Add(new HatEntity
        {
            HatId = hatDataModel.HatId,
            OrganizerPersonId = organizerPersonId,
            Name = hatDataModel.HatName,
            NameNormalized = Normalize(hatDataModel.HatName),
            Status = hatDataModel.Status,
            StatusUpdatedAt = createdAt,
            AdditionalInformation = hatDataModel.AdditionalInformation,
            PriceRange = hatDataModel.PriceRange,
            InvitationsQueuedAt = DateTimeOffset.MinValue,
            InvitationsSentFromIp = string.Empty,
            CreatedAt = createdAt,
            CopiedFromHatId = Guid.Empty
        });

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolationOf(exception, HatNamePerOrganizerIndex))
        {
            // The caller checks for a duplicate name first; this closes the window between that
            // check and this write.
            return false;
        }
    }

    /// <summary>
    /// Writes a new hat holding the same people and the same eligibility rules as an existing
    /// one, with nobody assigned a recipient. Everything lands in one transaction, so a failure
    /// part way through cannot leave a hat with half its participants.
    ///
    /// Participant rows are re-created with new ids, but they point at the same people: a copy is a
    /// second exchange among the same group, not a second set of humans. The eligibility rows are
    /// translated through the mapping from old participant id to new. Matching on name would have
    /// been simpler and wrong: two people in a hat may share a display name.
    ///
    /// The copy records the hat it came from. Nothing reads that yet — it is what a future rule
    /// along the lines of "nobody draws the same person two years running" would need, and it can
    /// only be captured at the moment the copy is made.
    /// </summary>
    /// <param name="request">
    /// The source hat, which is only read; the copy to write, whose organizer scopes that read; and
    /// the two rules deciding who is left out.
    /// </param>
    /// <returns>true if the copy was written, false if the organizer already has a hat by that name.</returns>
    internal async Task<bool> CopyHatAsync(CopyHatDataRequest request)
    {
        var sourceHatId = request.SourceHatId;
        var newHat = request.NewHat;

        try
        {
            // Resolved before the transaction opens: the organizer already exists, since they own
            // the hat being copied, but this is where a person is written and it does not belong
            // inside the unit of work that writes the copy.
            var organizerPersonId = await ResolvePersonIdAsync(new ResolvePersonRequest
                {
                    Email = newHat.OrganizerEmail,
                    Name = newHat.OrganizerName,
                    IntroducedByEmail = newHat.OrganizerEmail
                })
                .ConfigureAwait(false);

            await InTransactionAsync(async context =>
            {
                var source = await context.Hats
                    .AsNoTracking()
                    .Include(hat => hat.Participants).ThenInclude(participant => participant.EligibleRecipients)
                    .Include(hat => hat.Participants).ThenInclude(participant => participant.Person)
                    .SingleAsync(hat => hat.HatId == sourceHatId && hat.OrganizerPersonId == organizerPersonId)
                    .ConfigureAwait(false);

                // Whoever has refused an invitation from this organizer does not come along in the
                // copy. The caller decides who that is; this only has to leave them out, and leave
                // the organizer in regardless — they are a participant of their own exchange, and a
                // list they joined for somebody else's is not a reason to remove them from it.
                var carriedOver = source.Participants
                    .Where(participant => participant.PersonId == organizerPersonId
                                          || !request.RefusedEmails.Contains(participant.Person.Email.ToNormalizedEmail()))
                    .ToList();

                // As in CreateHatAsync: the copy takes its first status as it is written, so both
                // timestamps are the one reading.
                var copiedAt = DateTimeOffset.UtcNow;

                context.Hats.Add(new HatEntity
                {
                    HatId = newHat.HatId,
                    OrganizerPersonId = organizerPersonId,
                    Name = newHat.HatName,
                    NameNormalized = Normalize(newHat.HatName),
                    Status = newHat.Status,
                    StatusUpdatedAt = copiedAt,
                    AdditionalInformation = newHat.AdditionalInformation,
                    PriceRange = newHat.PriceRange,
                    InvitationsQueuedAt = DateTimeOffset.MinValue,
                    InvitationsSentFromIp = string.Empty,
                    CreatedAt = copiedAt,
                    // Copying a copy records the hat it came from, not the one at the head of the
                    // chain, so the chain stays walkable one link at a time.
                    CopiedFromHatId = sourceHatId
                });

                var newParticipantIds = carriedOver
                    .ToDictionary(participant => participant.ParticipantId, _ => Guid.CreateVersion7());

                foreach (var participant in carriedOver)
                    context.Participants.Add(new ParticipantEntity
                    {
                        ParticipantId = newParticipantIds[participant.ParticipantId],
                        HatId = newHat.HatId,
                        PersonId = participant.PersonId,
                        // A copy has not been shaken, which is the whole point of making one.
                        PickedRecipientParticipantId = Guid.Empty,
                        // Carried over rather than drawn again. The copy is the same people doing
                        // the same thing next year, and an organizer who has already given
                        // everybody a face they recognise should not have to do it twice.
                        Emoji = participant.Emoji
                    });

                foreach (var participant in carriedOver)
                foreach (var eligibility in participant.EligibleRecipients)
                {
                    if (request.ExcludePreviousRecipients
                        && eligibility.EligibleParticipantId == participant.PickedRecipientParticipantId)
                        continue;

                    // Nothing enforces referential integrity in DSQL, so a row pointing at a
                    // participant who is no longer in the hat is copied as nothing at all rather
                    // than taken on faith. A recipient left out of the copy on purpose lands here
                    // too, which is why the warning says what it does rather than claiming
                    // something went wrong.
                    if (!newParticipantIds.TryGetValue(eligibility.EligibleParticipantId, out var eligibleId))
                    {
                        _logger.LogInformation(
                            "Skipped an eligibility row while copying hat {SourceHatId}: recipient {EligibleParticipantId} is not being carried over.",
                            sourceHatId,
                            eligibility.EligibleParticipantId);
                        continue;
                    }

                    context.ParticipantEligibleRecipients.Add(new ParticipantEligibleRecipientEntity
                    {
                        ParticipantEligibleRecipientId = Guid.CreateVersion7(),
                        ParticipantId = newParticipantIds[participant.ParticipantId],
                        EligibleParticipantId = eligibleId
                    });
                }

                await context.SaveChangesAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolationOf(exception, HatNamePerOrganizerIndex))
        {
            // The caller checks the name first; this closes the window between that check and
            // this write, as it does for a hat created from scratch.
            return false;
        }
    }

    public async Task<(bool exists, Guid hatId)> DoesHatAlreadyExistAsync(string organizerEmail, string hatName)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var organizerPersonId = await FindPersonIdByEmailAsync(context, organizerEmail).ConfigureAwait(false);

        if (organizerPersonId == Guid.Empty)
            return (false, Guid.Empty);

        var normalized = Normalize(hatName);

        var hatId = await context.Hats
            .AsNoTracking()
            .Where(hat => hat.OrganizerPersonId == organizerPersonId && hat.NameNormalized == normalized)
            .Select(hat => hat.HatId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return hatId == Guid.Empty ? (false, Guid.Empty) : (true, hatId);
    }

    /// <summary>
    /// How many exchanges this organizer has created since a given moment, and when the first of
    /// them was made.
    /// </summary>
    /// <remarks>
    /// Counted from the rows the organizer owns rather than from a tally kept somewhere else, which
    /// has one consequence worth naming: deleting an exchange gives the slot back. That is the
    /// right answer for what this limit is for. It exists to bound how much an account can pile up,
    /// not to ration attempts, and somebody who deletes a mistake and makes it again has piled up
    /// nothing.
    ///
    /// The timestamps come back rather than a COUNT and a MIN, because the window holds a handful
    /// of rows and one round trip that returns them is cheaper than two aggregates that can
    /// disagree with each other.
    /// </remarks>
    internal async Task<CountHatsCreatedSinceResponse> CountHatsCreatedSinceAsync(
        CountHatsCreatedSinceRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.OrganizerEmail))
            return NoneCreated;

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var organizerPersonId = await FindPersonIdByEmailAsync(context, request.OrganizerEmail)
            .ConfigureAwait(false);

        // Nobody by that address has ever been written, so they have created nothing. The first
        // exchange of a new organizer's life comes through here.
        if (organizerPersonId == Guid.Empty)
            return NoneCreated;

        var createdAt = await context.Hats
            .AsNoTracking()
            .Where(hat => hat.OrganizerPersonId == organizerPersonId && hat.CreatedAt >= request.Since)
            .Select(hat => hat.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);

        return new CountHatsCreatedSinceResponse
        {
            Count = createdAt.Count,
            EarliestCreatedAt = createdAt.Count == 0 ? DateTimeOffset.MinValue : createdAt.Min()
        };
    }

    /// <summary>An organizer with nothing inside the window.</summary>
    private static CountHatsCreatedSinceResponse NoneCreated =>
        new() { Count = 0, EarliestCreatedAt = DateTimeOffset.MinValue };

    public async Task<(bool exists, Hat hat)> GetHatAsync(string organizerEmail, Guid hatId)
    {
        if (string.IsNullOrWhiteSpace(organizerEmail) || hatId == Guid.Empty)
            return (false, Hats.Empty);

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var hat = await context.Hats
            .AsNoTracking()
            .Include(entity => entity.Organizer)
            .Include(entity => entity.Participants).ThenInclude(participant => participant.Person)
            .Include(entity => entity.Participants).ThenInclude(participant => participant.EligibleRecipients)
            .ThenInclude(eligible => eligible.EligibleParticipant).ThenInclude(participant => participant.Person)
            .SingleOrDefaultAsync(entity => entity.HatId == hatId && entity.Organizer.Email == organizerEmail)
            .ConfigureAwait(false);

        if (hat is null)
            return (false, Hats.Empty);

        var deliveries = await LatestDeliveriesAsync(context, hat.Participants).ConfigureAwait(false);

        return (true, new Hat
        {
            Id = hat.HatId,
            Name = hat.Name,
            Status = hat.Status,
            AdditionalInformation = hat.AdditionalInformation,
            PriceRange = hat.PriceRange,
            Organizer = new Person { Name = hat.Organizer.Name, Email = hat.Organizer.Email },
            Participants = ToDomain(hat.Participants, deliveries),
            InvitationsQueuedDate = hat.InvitationsQueuedAt
        });
    }

    /// <summary>
    /// The most recent thing SES said about each of these participants, for the organizer's view.
    /// </summary>
    /// <remarks>
    /// The most recent of any type, rather than the invitation's. The question an organizer is
    /// asking is always "did the last thing I sent them arrive", and an invitation that bounced
    /// stays visible anyway, because the completion email to the same broken address bounces too.
    ///
    /// Newest is picked here rather than in the query. Grouping and taking a maximum per group is
    /// exactly the shape that turns into a lateral join or a window function, neither of which is
    /// worth betting on against DSQL, and the row count is one hat's participants times the two
    /// messages an exchange sends — small enough that the choice does not matter.
    /// </remarks>
    private static async Task<Dictionary<Guid, ParticipantEmailDeliveryEntity>> LatestDeliveriesAsync(
        GiftExchangeDbContext context,
        ICollection<ParticipantEntity> participants
    )
    {
        var participantIds = participants
            .Select(participant => participant.ParticipantId)
            .ToList();

        if (participantIds.Count == 0)
            return [];

        var rows = await context.ParticipantEmailDeliveries
            .AsNoTracking()
            .Where(delivery => participantIds.Contains(delivery.ParticipantId))
            .ToListAsync()
            .ConfigureAwait(false);

        return rows
            .GroupBy(delivery => delivery.ParticipantId)
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(delivery => delivery.OccurredAt)!);
    }

    /// <summary>
    /// The whole of one exchange, identifiers and all, for the organizer to take away.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetHatAsync"/> rather than a flag on it, because the two want
    /// different things. The domain <see cref="Hat"/> identifies people by display name, which is
    /// what every screen and every email needs; an export wants the ids underneath, so that the
    /// document can say who drew whom without leaning on names being unique.
    ///
    /// Nothing is withheld here. What may leave is <c>ExportHatService</c>'s decision, and this is
    /// the read it decides about.
    /// </remarks>
    internal async Task<ExportHatDataResponse> ExportHatAsync(ExportHatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizerEmail) || request.HatId == Guid.Empty)
            return new ExportHatDataResponse { Exists = false, Hat = ExportedHats.Empty };

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var hat = await context.Hats
            .AsNoTracking()
            .Include(entity => entity.Organizer)
            .Include(entity => entity.Participants).ThenInclude(participant => participant.Person)
            .Include(entity => entity.Participants).ThenInclude(participant => participant.EligibleRecipients)
            .ThenInclude(eligible => eligible.EligibleParticipant).ThenInclude(participant => participant.Person)
            .SingleOrDefaultAsync(entity => entity.HatId == request.HatId
                                            && entity.Organizer.Email == request.OrganizerEmail)
            .ConfigureAwait(false);

        if (hat is null)
            return new ExportHatDataResponse { Exists = false, Hat = ExportedHats.Empty };

        var deliveries = await LatestDeliveriesAsync(context, hat.Participants).ConfigureAwait(false);

        // A pick is a participant id with no navigation behind it, so what it stands for comes from
        // the rest of the hat -- the same lookup ToDomain builds, for the same reason.
        var participantsById = hat.Participants
            .ToDictionary(participant => participant.ParticipantId);

        return new ExportHatDataResponse
        {
            Exists = true,
            Hat = new ExportedHat
            {
                HatId = hat.HatId,
                Name = hat.Name,
                Status = hat.Status,
                AdditionalInformation = hat.AdditionalInformation,
                PriceRange = hat.PriceRange,
                CreatedAt = hat.CreatedAt,
                InvitationsQueuedAt = hat.InvitationsQueuedAt,
                CopiedFromHatId = hat.CopiedFromHatId,
                Organizer = ToExported(hat.Organizer),
                // Ordered so that two exports of an unchanged exchange are the same document. The
                // row order a query happens to return is not something to hand somebody diffing
                // last week's file against this week's.
                Participants =
                [
                    .. hat.Participants
                        .OrderBy(participant => participant.Person.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(participant => ToExported(participant, participantsById, deliveries))
                ]
            }
        };
    }

    private static ExportedPerson ToExported(PersonEntity person) =>
        new()
        {
            PersonId = person.PersonId,
            Name = person.Name,
            Email = person.Email
        };

    private static ExportedParticipant ToExported(
        ParticipantEntity participant,
        IReadOnlyDictionary<Guid, ParticipantEntity> participantsById,
        IReadOnlyDictionary<Guid, ParticipantEmailDeliveryEntity> deliveries
    )
    {
        var delivery = deliveries.GetValueOrDefault(participant.ParticipantId);

        return new ExportedParticipant
        {
            ParticipantId = participant.ParticipantId,
            Person = ToExported(participant.Person),
            PickedRecipient = ToReference(participant.PickedRecipientParticipantId, participantsById),
            Emoji = participant.Emoji,
            EligibleRecipients =
            [
                .. participant.EligibleRecipients
                    .Select(row => new ExportedParticipantReference
                    {
                        ParticipantId = row.EligibleParticipantId,
                        Name = row.EligibleParticipant.Person.Name
                    })
                    .OrderBy(reference => reference.Name, StringComparer.OrdinalIgnoreCase)
            ],
            DeliveryStatus = delivery?.Status ?? Models.DeliveryStatus.Unknown,
            DeliveryDetail = delivery?.Detail ?? string.Empty,
            DeliveryMessageType = delivery?.MessageType ?? string.Empty,
            DeliveryOccurredAt = delivery?.OccurredAt ?? DateTimeOffset.MinValue
        };
    }

    /// <summary>
    /// Guid.Empty is in no hat, so an undrawn participant falls through to the empty reference
    /// without being asked about separately.
    /// </summary>
    private static ExportedParticipantReference ToReference(
        Guid participantId,
        IReadOnlyDictionary<Guid, ParticipantEntity> participantsById
    ) =>
        participantsById.TryGetValue(participantId, out var participant)
            ? new ExportedParticipantReference
            {
                ParticipantId = participant.ParticipantId,
                Name = participant.Person.Name
            }
            : ExportedParticipantReferences.Empty;

    public async Task EditHatAsync(EditHatRequest request)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var organizerPersonId = await FindPersonIdByEmailAsync(context, request.OrganizerEmail).ConfigureAwait(false);

        if (organizerPersonId == Guid.Empty)
            return;

        await context.Hats
            .Where(hat => hat.HatId == request.HatId && hat.OrganizerPersonId == organizerPersonId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hat => hat.Name, request.Name)
                .SetProperty(hat => hat.NameNormalized, Normalize(request.Name))
                .SetProperty(hat => hat.AdditionalInformation, request.AdditionalInformation)
                .SetProperty(hat => hat.PriceRange, request.PriceRange))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the hat and everything hanging off it. DSQL has no ON DELETE CASCADE, so the
    /// order is explicit, and the whole thing runs in one transaction.
    ///
    /// People are left alone. They exist independently of any one exchange, and the participants
    /// being removed here may well be in somebody else's hat. <see cref="DeleteMyDataAsync"/> is
    /// the delete that also removes the people nothing refers to any more.
    ///
    /// Does nothing unless the hat belongs to the organizer asking.
    /// </summary>
    public Task DeleteHatAsync(DeleteHatRequest request) =>
        InTransactionAsync(async context =>
        {
            var organizerPersonId = await FindPersonIdByEmailAsync(context, request.OrganizerEmail).ConfigureAwait(false);

            if (organizerPersonId == Guid.Empty)
                return;

            // Ownership is established before anything is removed, not as part of removing the hat
            // last. Scoping only that final statement meant the two below ran against a hat id
            // nobody had checked: passing somebody else's emptied their exchange of participants
            // and eligibility while leaving the hat itself standing, and passing the all-zero id
            // reached the sentinel participant. Nothing upstream covers this — DeleteHatService
            // hands the request straight here.
            var ownsHat = await context.Hats
                .AnyAsync(hat => hat.HatId == request.HatId && hat.OrganizerPersonId == organizerPersonId)
                .ConfigureAwait(false);

            if (!ownsHat)
                return;

            await DeleteHatCoreAsync(context, request.HatId).ConfigureAwait(false);
        });

    /// <summary>
    /// Everything <see cref="DeleteHatAsync"/> removes, without the ownership check. Callers must
    /// have established that the hat is the caller's to delete before reaching this.
    /// </summary>
    private static async Task DeleteHatCoreAsync(GiftExchangeDbContext context, Guid hatId)
    {
        var participantIds = context.Participants
            .Where(participant => participant.HatId == hatId)
            .Select(participant => participant.ParticipantId);

        await context.ParticipantEligibleRecipients
            .Where(row => participantIds.Contains(row.ParticipantId)
                          || participantIds.Contains(row.EligibleParticipantId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Gift ideas and the tokens that route them go with the exchange they were written for.
        // Nothing cleans these up on our behalf: they are mapped without navigations precisely
        // so that no foreign key exists, so the sweep is ours to do.
        await context.GiftIdeas
            .Where(giftIdea => participantIds.Contains(giftIdea.ParticipantId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        await context.GiftIdeaTokens
            .Where(token => participantIds.Contains(token.ParticipantId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        await context.ParticipantLeaveTokens
            .Where(token => participantIds.Contains(token.ParticipantId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // What SES said about the mail sent to these participants. It is a record about an
        // exchange that is being removed, and no longer answers any question once the addresses
        // it describes are gone.
        await context.ParticipantEmailDeliveries
            .Where(delivery => participantIds.Contains(delivery.ParticipantId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Asks, and the suggestions written in reply to them. Filtering on the asker alone is
        // enough here: all three participants named by an ask belong to the hat that is going,
        // so nothing is left behind by only looking at one of them.
        //
        // The ids are read out before either delete rather than left as a subquery. The
        // contributions have to be found through the asks, and once the asks are gone there is
        // nothing left to find them by.
        var askIds = await context.GiftIdeaAsks
            .AsNoTracking()
            .Where(ask => participantIds.Contains(ask.AskerParticipantId))
            .Select(ask => ask.GiftIdeaAskId)
            .ToListAsync()
            .ConfigureAwait(false);

        await context.ContributedGiftIdeas
            .Where(contribution => askIds.Contains(contribution.GiftIdeaAskId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        await context.GiftIdeaAsks
            .Where(ask => askIds.Contains(ask.GiftIdeaAskId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Who asked for ideas about their pick. The asker alone reaches all of them, for the reason
        // the asks above give: both participants an enquiry names belong to the hat that is going.
        await context.GiftIdeaEnquiries
            .Where(enquiry => participantIds.Contains(enquiry.AskerParticipantId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        await context.Participants
            .Where(participant => participant.HatId == hatId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Refusals of an exchange that no longer exists. They cannot stop anybody being added to
        // it, and they are the addresses of the people who were in it, which is exactly what a
        // delete promises to take away.
        await context.DoNotAddToExchange
            .Where(block => block.HatId == hatId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Any copy taken from this hat would otherwise point at an exchange that no longer
        // exists. Clearing it says "not a copy", which is true once the source is gone, and is
        // the same cleanup DeleteParticipantAsync does for a pick. Only this organizer's hats
        // can be affected: CopyHatAsync will not copy a hat you do not own.
        await context.Hats
            .Where(hat => hat.CopiedFromHatId == hatId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(hat => hat.CopiedFromHatId, Guid.Empty))
            .ConfigureAwait(false);

        await context.Hats
            .Where(hat => hat.HatId == hatId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes every exchange somebody organizes, the people who were only ever in those exchanges,
    /// and, when asked, the person themselves.
    /// </summary>
    /// <remarks>
    /// One transaction per exchange rather than one for the lot. DSQL caps the rows a single
    /// transaction may modify, and an organizer of many years could pass it on their own. Each
    /// exchange goes whole or not at all, and a retry of the message simply finds fewer of them.
    ///
    /// The people an exchange leaves orphaned are removed inside that exchange's transaction, not
    /// collected and swept at the end. A sweep at the end would be lost to a failure partway: the
    /// retry could no longer see which people the exchanges already gone had held.
    ///
    /// Only exchanges created by <see cref="DataDeletionMessage.RequestedAt"/>. The message waits on
    /// a queue, and an exchange somebody starts in that time is not one they asked to delete.
    ///
    /// A person is only ever removed when nothing points at them. Somebody in an exchange another
    /// organizer runs is still shown there by name, and removing the row would take them out of
    /// that list without anybody deciding to.
    /// </remarks>
    internal async Task DeleteMyDataAsync(DataDeletionMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Email))
            return;

        Guid personId;
        List<Guid> hatIds;

        await using (var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false))
        {
            personId = await FindPersonIdByEmailAsync(context, message.Email).ConfigureAwait(false);

            if (personId == Guid.Empty)
                return;

            hatIds = await context.Hats
                .AsNoTracking()
                .Where(hat => hat.OrganizerPersonId == personId && hat.CreatedAt <= message.RequestedAt)
                .Select(hat => hat.HatId)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        foreach (var hatId in hatIds)
            await InTransactionAsync(async context =>
            {
                // Read before the exchange goes, because afterwards nothing says who was in it.
                var personIds = await context.Participants
                    .AsNoTracking()
                    .Where(participant => participant.HatId == hatId)
                    .Select(participant => participant.PersonId)
                    .Distinct()
                    .ToListAsync()
                    .ConfigureAwait(false);

                await DeleteHatCoreAsync(context, hatId).ConfigureAwait(false);

                // The caller is left to the ForgetMe step below, which is the only place they are
                // theirs to remove.
                await DeleteUnreferencedPersonsAsync(
                        context,
                        [.. personIds.Where(id => id != personId)])
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

        if (message.ForgetMe)
            await InTransactionAsync(context => DeleteUnreferencedPersonsAsync(context, [personId]))
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Removes those of these people that no exchange refers to, as organizer or as participant.
    /// </summary>
    /// <remarks>
    /// The sentinel is never among them, whatever is passed: every column meaning "nobody" points
    /// at it, and it holds nothing to delete.
    /// </remarks>
    private static async Task DeleteUnreferencedPersonsAsync(GiftExchangeDbContext context, List<Guid> personIds)
    {
        personIds.Remove(Guid.Empty);

        if (personIds.Count == 0)
            return;

        await context.Persons
            .Where(person => personIds.Contains(person.PersonId)
                             && !context.Participants.Any(participant => participant.PersonId == person.PersonId)
                             && !context.Hats.Any(hat => hat.OrganizerPersonId == person.PersonId))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a gift ideas routing token to every participant in a hat, storing only the hash of
    /// each, and hands back the tokens in the clear so they can be put into the invitations.
    ///
    /// This is the only moment the plaintext exists on this side. Nothing can reproduce it
    /// afterwards, which is the point: once the invitations are sent, the token lives in the
    /// participant's mailbox and nowhere else.
    /// </summary>
    /// <returns>Token by participant email address. Addresses are unique across the application.</returns>
    public async Task<ImmutableDictionary<string, string>> IssueGiftIdeaTokensAsync(Guid hatId)
    {
        var issued = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);

        await InTransactionAsync(async context =>
        {
            // InTransactionAsync retries the whole delegate on a fresh context, so a second attempt
            // has to start from nothing. Without this, a retry would return tokens from the failed
            // attempt alongside the ones actually written, and half the invitations would carry an
            // address that routes nowhere.
            issued.Clear();

            var participants = await context.Participants
                .AsNoTracking()
                .Where(participant => participant.HatId == hatId)
                .Select(participant => new { participant.ParticipantId, participant.Person.Email })
                .ToListAsync()
                .ConfigureAwait(false);

            if (participants.Count == 0)
                return;

            var participantIds = participants
                .Select(participant => participant.ParticipantId)
                .ToList();

            // Reissuing replaces. One live token each is what uq_gift_idea_token_participant says,
            // and leaving the old row would keep an address alive that still writes to this
            // participant after they had been given a new one.
            await context.GiftIdeaTokens
                .Where(token => participantIds.Contains(token.ParticipantId))
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            foreach (var participant in participants)
            {
                var token = SecretToken.Create();

                context.GiftIdeaTokens.Add(new GiftIdeaTokenEntity
                {
                    GiftIdeaTokenId = Guid.CreateVersion7(),
                    ParticipantId = participant.ParticipantId,
                    TokenHash = SecretToken.Hash(token),
                    IssuedAt = DateTimeOffset.UtcNow
                });

                issued[participant.Email] = token;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        return issued.ToImmutable();
    }

    /// <summary>
    /// Issues one leave token per participant in a hat, and hands back the plaintext.
    /// </summary>
    /// <remarks>
    /// Called alongside <see cref="IssueGiftIdeaTokensAsync"/> when invitations go out, and for the
    /// same reason: this is the first moment there is an email going to each of these people to
    /// carry a token, and a token nobody has been told is only a row.
    ///
    /// The organizer is skipped. There is no leaving an exchange you are running, and the surest
    /// way to enforce that is for no token of theirs to exist — a service that checked a flag could
    /// forget to, and one that has nothing to look up cannot. Their invitation is composed with an
    /// empty token, and the leave sentence in the fine print is simply not written.
    ///
    /// As with the gift ideas tokens, this is the only moment the plaintext exists on this side.
    /// </remarks>
    /// <returns>Token by participant email address. Addresses are unique across the application.</returns>
    public async Task<ImmutableDictionary<string, string>> IssueLeaveTokensAsync(Guid hatId)
    {
        var issued = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);

        await InTransactionAsync(async context =>
        {
            // Reset rather than accumulate, for the reason IssueGiftIdeaTokensAsync gives: the
            // delegate is replayed whole on a fresh context, and tokens from a failed attempt would
            // otherwise be handed out alongside the ones actually written.
            issued.Clear();

            var organizerPersonId = await context.Hats
                .AsNoTracking()
                .Where(hat => hat.HatId == hatId)
                .Select(hat => hat.OrganizerPersonId)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            var participants = await context.Participants
                .AsNoTracking()
                .Where(participant => participant.HatId == hatId
                                      && participant.PersonId != organizerPersonId)
                .Select(participant => new { participant.ParticipantId, participant.Person.Email })
                .ToListAsync()
                .ConfigureAwait(false);

            if (participants.Count == 0)
                return;

            var participantIds = participants
                .Select(participant => participant.ParticipantId)
                .ToList();

            // Reissuing replaces. Invitations can be sent more than once, and leaving the old row
            // would keep a link alive in an email whose picks are no longer the ones in the hat.
            await context.ParticipantLeaveTokens
                .Where(token => participantIds.Contains(token.ParticipantId))
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            foreach (var participant in participants)
            {
                // The opaque length, not the legible one. This token only ever travels inside a
                // link, so nothing is paid for the extra characters, and what it authorises —
                // removing somebody from an exchange — is worth the wider space.
                var token = SecretToken.Create(SecretToken.OpaqueTokenBytes);

                context.ParticipantLeaveTokens.Add(new ParticipantLeaveTokenEntity
                {
                    ParticipantLeaveTokenId = Guid.CreateVersion7(),
                    ParticipantId = participant.ParticipantId,
                    TokenHash = SecretToken.Hash(token),
                    IssuedAt = DateTimeOffset.UtcNow
                });

                issued[participant.Email] = token;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        return issued.ToImmutable();
    }

    /// <summary>
    /// Resolves a leave link to the participant it removes, the exchange they are in, and the
    /// organizer who has to be told, from the hash of the token in the link.
    /// </summary>
    /// <param name="tokenHash">
    /// <see cref="SecretToken.Hash"/> of the token taken from the path. The plaintext is never
    /// passed here — nothing stored could be compared against it.
    /// </param>
    public async Task<(bool found, LeaveRoute route)> FindLeaveRouteAsync(string tokenHash)
    {
        // An empty hash is not a lookup worth making, as in FindGiftIdeaRouteAsync.
        if (string.IsNullOrWhiteSpace(tokenHash))
            return (false, LeaveRoutes.Empty);

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // One join stated here, because the hop from a token to a participant crosses an id with no
        // navigation behind it; the hops to a person, a hat and the hat's organizer are navigations
        // and EF emits those joins itself.
        var match = await context.ParticipantLeaveTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Join(
                context.Participants,
                token => token.ParticipantId,
                participant => participant.ParticipantId,
                (_, participant) => new
                {
                    participant.ParticipantId,
                    participant.Hat.HatId,
                    HatName = participant.Hat.Name,
                    participant.Hat.Status,
                    LeaverName = participant.Person.Name,
                    LeaverEmail = participant.Person.Email,
                    OrganizerName = participant.Hat.Organizer.Name,
                    OrganizerEmail = participant.Hat.Organizer.Email
                })
            .SingleOrDefaultAsync()
            .ConfigureAwait(false);

        if (match is null)
            return (false, LeaveRoutes.Empty);

        return (true, new LeaveRoute
        {
            ParticipantId = match.ParticipantId,
            HatId = match.HatId,
            HatName = match.HatName,
            HatStatus = match.Status,
            Leaver = new Person { Name = match.LeaverName, Email = match.LeaverEmail },
            Organizer = new Person { Name = match.OrganizerName, Email = match.OrganizerEmail }
        });
    }

    /// <summary>
    /// Which of these addresses have refused this particular exchange.
    /// </summary>
    /// <remarks>
    /// One of three lookups that are deliberately separate methods rather than one query over three
    /// tables. Each opens its own context, so <see cref="DoNotAddService"/> can run all three at
    /// once — which is the point: the three are independent questions, and asking them in sequence
    /// would cost three round trips to answer what one round trip's worth of latency covers.
    ///
    /// Returns the blocked subset rather than a boolean, so the single-address callers and the
    /// copy-a-whole-exchange caller share one implementation.
    /// </remarks>
    /// <param name="normalizedEmails">Addresses already through <c>ToNormalizedEmail</c>.</param>
    public async Task<ImmutableList<string>> FindBlockedByExchangeAsync(
        ImmutableList<string> normalizedEmails,
        Guid hatId
    )
    {
        if (normalizedEmails.IsEmpty || hatId == Guid.Empty)
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var blocked = await context.DoNotAddToExchange
            .AsNoTracking()
            .Where(block => block.HatId == hatId && normalizedEmails.Contains(block.EmailNormalized))
            .Select(block => block.EmailNormalized)
            .ToListAsync()
            .ConfigureAwait(false);

        return [.. blocked];
    }

    /// <summary>
    /// Which of these addresses have refused this particular organizer, whatever the exchange.
    /// </summary>
    /// <param name="normalizedEmails">Addresses already through <c>ToNormalizedEmail</c>.</param>
    /// <param name="normalizedOrganizerEmail">The organizer's address, likewise.</param>
    public async Task<ImmutableList<string>> FindBlockedByOrganizerAsync(
        ImmutableList<string> normalizedEmails,
        string normalizedOrganizerEmail
    )
    {
        if (normalizedEmails.IsEmpty || string.IsNullOrWhiteSpace(normalizedOrganizerEmail))
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var blocked = await context.DoNotAddByOrganizer
            .AsNoTracking()
            .Where(block => block.OrganizerEmailNormalized == normalizedOrganizerEmail
                            && normalizedEmails.Contains(block.EmailNormalized))
            .Select(block => block.EmailNormalized)
            .ToListAsync()
            .ConfigureAwait(false);

        return [.. blocked];
    }

    /// <summary>
    /// Which of these addresses have refused gift exchanges altogether.
    /// </summary>
    /// <param name="normalizedEmails">Addresses already through <c>ToNormalizedEmail</c>.</param>
    public async Task<ImmutableList<string>> FindBlockedAnywhereAsync(ImmutableList<string> normalizedEmails)
    {
        if (normalizedEmails.IsEmpty)
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var blocked = await context.DoNotAddAnywhere
            .AsNoTracking()
            .Where(block => normalizedEmails.Contains(block.EmailNormalized))
            .Select(block => block.EmailNormalized)
            .ToListAsync()
            .ConfigureAwait(false);

        return [.. blocked];
    }

    /// <summary>
    /// Writes down what somebody refused on their way out of an exchange.
    /// </summary>
    /// <remarks>
    /// All three lists under one transaction, so a leaver cannot end up blocked from the exchange
    /// but not from the organizer they asked to be blocked from because a connection dropped in
    /// between.
    ///
    /// Idempotent twice over, because leaving twice is an ordinary thing to happen here — two tabs,
    /// a double submit, a scanner that follows a POST. Each insert is guarded by a read, which
    /// handles the sequential case; the unique indexes handle the concurrent one, where two
    /// transactions both read nothing and both go on to write. A violation on any of these three
    /// means the row this was asked to write already exists, so there is nothing to report and
    /// nothing to retry.
    /// </remarks>
    internal async Task RecordDoNotAddAsync(RecordDoNotAddRequest request)
    {
        try
        {
            await RecordDoNotAddCoreAsync(request).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            _logger.LogInformation("A do-not-add refusal was already recorded.");
        }
    }

    private Task RecordDoNotAddCoreAsync(RecordDoNotAddRequest request) =>
        InTransactionAsync(async context =>
        {
            var email = request.Email.ToNormalizedEmail();

            if (string.IsNullOrWhiteSpace(email))
                return;

            var now = DateTimeOffset.UtcNow;

            // Always. Leaving an exchange is itself the statement that they do not want to be in
            // it, so this one is not a checkbox and is not conditional on anything.
            if (request.HatId != Guid.Empty)
            {
                var alreadyBlockedFromExchange = await context.DoNotAddToExchange
                    .AnyAsync(block => block.EmailNormalized == email && block.HatId == request.HatId)
                    .ConfigureAwait(false);

                if (!alreadyBlockedFromExchange)
                    context.DoNotAddToExchange.Add(new DoNotAddToExchangeEntity
                    {
                        DoNotAddToExchangeId = Guid.CreateVersion7(),
                        HatId = request.HatId,
                        EmailNormalized = email,
                        CreatedAt = now
                    });
            }

            var organizerEmail = request.OrganizerEmail.ToNormalizedEmail();

            if (request.BlockOrganizer && !string.IsNullOrWhiteSpace(organizerEmail))
            {
                var alreadyBlockedByOrganizer = await context.DoNotAddByOrganizer
                    .AnyAsync(block => block.EmailNormalized == email
                                       && block.OrganizerEmailNormalized == organizerEmail)
                    .ConfigureAwait(false);

                if (!alreadyBlockedByOrganizer)
                    context.DoNotAddByOrganizer.Add(new DoNotAddByOrganizerEntity
                    {
                        DoNotAddByOrganizerId = Guid.CreateVersion7(),
                        OrganizerEmailNormalized = organizerEmail,
                        EmailNormalized = email,
                        CreatedAt = now
                    });
            }

            if (request.BlockAnywhere)
            {
                var alreadyBlockedAnywhere = await context.DoNotAddAnywhere
                    .AnyAsync(block => block.EmailNormalized == email)
                    .ConfigureAwait(false);

                if (!alreadyBlockedAnywhere)
                    context.DoNotAddAnywhere.Add(new DoNotAddAnywhereEntity
                    {
                        DoNotAddAnywhereId = Guid.CreateVersion7(),
                        EmailNormalized = email,
                        CreatedAt = now
                    });
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
        });

    /// <summary>
    /// The participant id behind each address in a hat, so that an outbound message can be tagged
    /// with who it is going to.
    /// </summary>
    /// <remarks>
    /// Keyed by address because that is what the sending path already has: both callers iterate the
    /// hat's participants, and a domain <see cref="Participant"/> carries a person and not an id.
    /// Adding the id to that record would put a storage key into the API contract, which this class
    /// exists to keep out of it, so the lookup is made once per send rather than the record widened
    /// for everybody.
    ///
    /// Addresses are unique across the application, so they are unique within a hat.
    /// </remarks>
    public async Task<ImmutableDictionary<string, Guid>> GetParticipantIdsByEmailAsync(Guid hatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var participants = await context.Participants
            .AsNoTracking()
            .Where(participant => participant.HatId == hatId)
            .Select(participant => new { participant.ParticipantId, participant.Person.Email })
            .ToListAsync()
            .ConfigureAwait(false);

        return participants.ToImmutableDictionary(
            participant => participant.Email,
            participant => participant.ParticipantId,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Changes the face one participant is marked with, and reports whether there was anybody to
    /// change it on.
    /// </summary>
    /// <remarks>
    /// The participant is found first and the write is then keyed by their id, rather than the one
    /// statement that filtering on <c>Person.Email</c> would have produced. That statement is an
    /// UPDATE joined to another table, and this file's premise is that Postgres accepting something
    /// says nothing about whether DSQL will -- so the write is left as the plainest single-table
    /// statement there is, and the lookup is a read like any other.
    ///
    /// No transaction around the pair. The row could be deleted between the two, in which case the
    /// update matches nothing and this reports the same "not found" it would have reported a moment
    /// earlier. Nothing is left half done, because there is only one write.
    ///
    /// Scoped by hat id and address because that is what the caller has: the API identifies
    /// participants by the address they are recorded at, and the hat is what ownership was
    /// established against.
    /// </remarks>
    /// <returns>false when no participant in that hat holds that address.</returns>
    internal async Task<bool> UpdateParticipantEmojiAsync(UpdateParticipantEmojiRequest request)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var participantId = await FindParticipantIdByEmailAsync(context, request.HatId, request.ParticipantEmail)
            .ConfigureAwait(false);

        if (participantId == Guid.Empty)
            return false;

        var updated = await context.Participants
            .Where(participant => participant.ParticipantId == participantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(participant => participant.Emoji, request.Emoji))
            .ConfigureAwait(false);

        return updated > 0;
    }

    /// <summary>
    /// Moves one participant onto a different email address, leaving everything else about them
    /// alone.
    /// </summary>
    /// <remarks>
    /// The participant row survives; only its <c>person_id</c> changes. That matters more than it
    /// looks. Removing and re-adding somebody — which is the only way an organizer can do this
    /// today — runs <see cref="DeleteParticipantAsync"/>, which clears eligibility in both
    /// directions and nulls any pick pointing at them: after the hat is shaken that silently breaks
    /// the draw, leaving somebody buying for nobody with nothing to say so. Re-pointing keeps the
    /// pick, the eligibility, and the delivery history, and the delivery column walks from bounced
    /// to delivered on its own because <c>participant_id</c> never moved.
    ///
    /// The person the address belongs to is found or created, never renamed. A person is global —
    /// they may be in other exchanges, and their address may be how an organizer signs in — so
    /// renaming one to fit this hat would reach well beyond the exchange being edited. The cost is
    /// that moving somebody onto an address that already belongs to a person adopts that person's
    /// name, which can collide with another participant here; that is refused rather than resolved.
    ///
    /// Moving the row does not revoke the links the old address was sent. The caller does that,
    /// through <see cref="RevokeGiftIdeaLinksAsync"/> and <see cref="IssueLeaveTokenAsync"/>, before
    /// it resends.
    /// </remarks>
    internal async Task<UpdateParticipantAddressResponse> UpdateParticipantAddressAsync(
        UpdateParticipantAddressRequest request
    )
    {
        var response = UpdateParticipantAddressResponses.For(AddressChangeOutcome.ParticipantNotFound);

        await InTransactionAsync(async context =>
        {
            // Reset, because InTransactionAsync replays the whole delegate on a fresh context.
            response = UpdateParticipantAddressResponses.For(AddressChangeOutcome.ParticipantNotFound);

            var participant = await context.Participants
                .Include(row => row.Person)
                .SingleOrDefaultAsync(row => row.HatId == request.HatId
                                             && row.Person.Email == request.CurrentEmail)
                .ConfigureAwait(false);

            if (participant is null)
                return;

            var existingPerson = await context.Persons
                .SingleOrDefaultAsync(person => person.Email == request.NewEmail)
                .ConfigureAwait(false);

            // A brand new address takes the name the participant already had, which is the whole
            // point of an address correction: the person did not change, only where to reach them.
            var name = existingPerson?.Name ?? participant.Person.Name;

            var others = await context.Participants
                .AsNoTracking()
                .Where(row => row.HatId == request.HatId && row.ParticipantId != participant.ParticipantId)
                .Select(row => new { row.PersonId, row.Person.Name })
                .ToListAsync()
                .ConfigureAwait(false);

            if (existingPerson is not null && others.Any(other => other.PersonId == existingPerson.PersonId))
            {
                response = UpdateParticipantAddressResponses.For(AddressChangeOutcome.AddressAlreadyInExchange);
                return;
            }

            if (others.Any(other => other.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                // Named, because the message an organizer sees has to quote the name they collided
                // with rather than say only that something did.
                response = UpdateParticipantAddressResponses.For(AddressChangeOutcome.NameAlreadyInExchange)
                    with { Name = name };
                return;
            }

            if (existingPerson is null)
            {
                var introducedById = await FindPersonIdByEmailAsync(context, request.OrganizerEmail)
                    .ConfigureAwait(false);

                var personId = Guid.CreateVersion7();

                var person = new PersonEntity
                {
                    PersonId = personId,
                    Name = name,
                    Email = request.NewEmail,
                    // The organizer typing the correction is introducing this address, so the name
                    // on it is theirs to fix again if they get it wrong twice. Their own row exists
                    // by now — ownership of the hat was established against it — so the fallback to
                    // a self-reference is only there because nothing here should assume that.
                    AddedByPersonId = introducedById == Guid.Empty ? personId : introducedById
                };

                context.Persons.Add(person);
                participant.PersonId = person.PersonId;
            }
            else
            {
                participant.PersonId = existingPerson.PersonId;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);

            response = new UpdateParticipantAddressResponse
            {
                Outcome = AddressChangeOutcome.Changed,
                ParticipantId = participant.ParticipantId,
                Name = name
            };
        }).ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// Whether this person's name is that person's to change.
    /// </summary>
    /// <remarks>
    /// Two people, and no more. Themselves, because the row is identified by an address they hold;
    /// and whoever introduced them, because they are the one who typed the name in the first place
    /// and the one an organizer correcting a typo almost always is.
    ///
    /// Everybody else is refused, and the case that makes that worth enforcing is two organizers
    /// with a participant in common. A name is stored once and read into every exchange the person
    /// appears in, so without this the second organizer can rename somebody in the first's
    /// exchange — repeatedly, and invisibly to everybody except the person themselves.
    ///
    /// The all-zero id is refused rather than treated as a match. It is the sentinel's, and it is
    /// also what a caller with no person row resolves to; either way it is nobody, and nobody is
    /// not entitled to a name.
    /// </remarks>
    private static bool MayRename(PersonEntity person, Guid byPersonId) =>
        byPersonId != Guid.Empty
        && (person.PersonId == byPersonId || person.AddedByPersonId == byPersonId);

    /// <summary>
    /// Changes the name somebody goes by, and reports what stopped it if anything did.
    /// </summary>
    /// <remarks>
    /// The one place a name is written. Both endpoints that change one arrive here — an organizer
    /// editing a participant, and somebody editing their own profile — because a name is a fact
    /// about a person rather than about a membership, and having two spellings of the same write
    /// was how the two came to disagree about what they checked.
    ///
    /// Nothing about a draw moves. Eligibility and picks are participant ids, and the names the
    /// domain records carry are projected off the person row at read time, so renaming somebody
    /// after a hat is shaken leaves the hat shaken. That is the difference between this and
    /// removing and re-adding them, which is what an organizer had to do before.
    ///
    /// The reach is what the two checks are for. A rename is felt in every exchange the person
    /// appears in, including ones the caller does not run, so it asks whether the caller is
    /// entitled to make it at all, and then whether the new name is free everywhere it would land
    /// rather than only in the exchange being edited. A name free here and taken there would leave
    /// a stranger's exchange with two people answering to the same thing and an announcement that
    /// cannot say which one you drew.
    ///
    /// Somebody setting their own name before the application has a row for them is introducing
    /// themselves, and that is written rather than refused. It is the state a person is in between
    /// signing in and creating their first exchange, and the alternative — reporting that the
    /// person whose name they are setting does not exist — describes the situation accurately and
    /// helps nobody.
    /// </remarks>
    internal async Task<RenamePersonResponse> RenamePersonAsync(RenamePersonRequest request)
    {
        // As in ResolvePersonIdAsync: the empty address is the sentinel's, and a rename reaching it
        // would give the row meaning "nobody" a name.
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("A person needs an email address; the empty one belongs to the sentinel row.", nameof(request));

        var isSelf = request.Email.ContentEquals(request.RequestedByEmail);

        await using (var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false))
        {
            var known = await context.Persons
                .AsNoTracking()
                .AnyAsync(person => person.Email == request.Email)
                .ConfigureAwait(false);

            if (!known)
            {
                if (!isSelf)
                    return RenamePersonResponses.For(NameChangeOutcome.PersonNotFound);

                // Nobody the application has never heard of takes part in anything, so there is
                // nothing for the name to collide with and no check worth running. Written through
                // the same method every other introduction goes through, which is what handles two
                // requests racing to write one address.
                var introducedId = await ResolvePersonIdAsync(new ResolvePersonRequest
                {
                    Email = request.Email,
                    Name = request.Name,
                    IntroducedByEmail = request.Email
                }).ConfigureAwait(false);

                return new RenamePersonResponse
                {
                    Outcome = NameChangeOutcome.Changed,
                    PersonId = introducedId,
                    PreviousName = string.Empty,
                    ConflictingHatNames = [],
                    ConflictsElsewhere = false
                };
            }
        }

        var response = RenamePersonResponses.For(NameChangeOutcome.PersonNotFound);

        await InTransactionAsync(async context =>
        {
            // Reset, because InTransactionAsync replays the whole delegate on a fresh context.
            response = RenamePersonResponses.For(NameChangeOutcome.PersonNotFound);

            var person = await context.Persons
                .SingleOrDefaultAsync(candidate => candidate.Email == request.Email)
                .ConfigureAwait(false);

            if (person is null)
                return;

            var requesterId = isSelf
                ? person.PersonId
                : await FindPersonIdByEmailAsync(context, request.RequestedByEmail).ConfigureAwait(false);

            if (!MayRename(person, requesterId))
            {
                response = RenamePersonResponses.For(NameChangeOutcome.NotTheirNameToChange)
                    with { PreviousName = person.Name };
                return;
            }

            // Every exchange this person is part of, which is the full extent of what renaming them
            // touches. Organized as well as taken part in: an organizer is a participant of their
            // own exchange today, and a rule that quietly depended on that would be one more thing
            // to remember if it ever stopped being true.
            var hatIds = await context.Participants
                .AsNoTracking()
                .Where(row => row.PersonId == person.PersonId)
                .Select(row => row.HatId)
                .Union(context.Hats
                    .AsNoTracking()
                    .Where(hat => hat.OrganizerPersonId == person.PersonId)
                    .Select(hat => hat.HatId))
                .ToListAsync()
                .ConfigureAwait(false);

            var normalized = Normalize(request.Name);

            // Anybody in one of those exchanges who already answers to the new name. The person
            // being renamed is excluded by person id rather than by participant id, so they are
            // excluded in all of their exchanges at once — which is what makes a change of
            // capitalisation, or a rename to the name they already have, an accepted no-op rather
            // than a collision with themselves.
            //
            // ToLower, not ToLowerInvariant: only the former translates to SQL.
            var conflicts = await context.Participants
                .AsNoTracking()
                .Where(row => hatIds.Contains(row.HatId)
                              && row.PersonId != person.PersonId
                              && row.Person.Name.ToLower() == normalized)
                .Select(row => new { HatName = row.Hat.Name, row.Hat.OrganizerPersonId })
                .ToListAsync()
                .ConfigureAwait(false);

            if (conflicts.Count > 0)
            {
                response = new RenamePersonResponse
                {
                    Outcome = NameChangeOutcome.NameAlreadyInExchange,
                    PersonId = Guid.Empty,
                    PreviousName = person.Name,
                    ConflictingHatNames = conflicts
                        .Where(conflict => conflict.OrganizerPersonId == requesterId)
                        .Select(conflict => conflict.HatName)
                        .Distinct()
                        .ToImmutableList(),
                    ConflictsElsewhere = conflicts
                        .Any(conflict => conflict.OrganizerPersonId != requesterId)
                };

                return;
            }

            var previousName = person.Name;

            person.Name = request.Name;

            await context.SaveChangesAsync().ConfigureAwait(false);

            response = new RenamePersonResponse
            {
                Outcome = NameChangeOutcome.Changed,
                PersonId = person.PersonId,
                PreviousName = previousName,
                ConflictingHatNames = [],
                ConflictsElsewhere = false
            };
        }).ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// Records what SES said about one message, creating the row if this is the first thing heard
    /// about it and moving it forwards if it is not.
    /// </summary>
    /// <remarks>
    /// Forwards only. Neither SES nor SNS orders what it publishes, so a Delivery can be handed to
    /// us after the Send it followed; a row that simply took the latest event to arrive would flap
    /// between statuses for no reason visible in the data. <see cref="DeliveryStatuses.RankOf"/>
    /// decides what counts as forwards, and an event that does not get further is dropped.
    ///
    /// The read and the write are in one transaction so that two events for the same message
    /// arriving at once collide rather than interleave: DSQL aborts one of them as a serialization
    /// failure, and the execution strategy behind <see cref="InTransactionAsync"/> replays it
    /// against what the winner wrote. The one case that leaves is two first-events racing to insert,
    /// which is a unique violation rather than a conflict, so it is retried here by hand.
    /// </remarks>
    /// <returns>Whether anything was written.</returns>
    public async Task<bool> RecordDeliveryEventAsync(ParticipantEmailDelivery delivery)
    {
        if (delivery.ParticipantId == Guid.Empty || string.IsNullOrWhiteSpace(delivery.SesMessageId))
        {
            _logger.LogWarning(
                "Ignoring a delivery event with no participant or no message id. Status was {Status}.",
                delivery.Status);
            return false;
        }

        // Two attempts, not more. The second runs only when a concurrent insert won the race, and
        // by then the row exists — so the retry takes the update path and cannot lose again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await TryRecordDeliveryEventAsync(delivery).ConfigureAwait(false);
            }
            catch (DbUpdateException exception) when (IsUniqueViolationOf(exception, DeliveryMessageIndex))
            {
                _logger.LogInformation(
                    "Another event inserted the row for message {MessageId} first. Re-reading it.",
                    delivery.SesMessageId);
            }
        }

        _logger.LogError(
            "Gave up recording a {Status} event for message {MessageId}.",
            delivery.Status,
            delivery.SesMessageId);

        return false;
    }

    private async Task<bool> TryRecordDeliveryEventAsync(ParticipantEmailDelivery delivery)
    {
        var written = false;

        await InTransactionAsync(async context =>
        {
            // InTransactionAsync replays the whole delegate on a fresh context, so the result has
            // to be reset rather than accumulated -- the same reason IssueGiftIdeaTokensAsync
            // clears its builder.
            written = false;

            var existing = await context.ParticipantEmailDeliveries
                .SingleOrDefaultAsync(row => row.SesMessageId == delivery.SesMessageId)
                .ConfigureAwait(false);

            if (existing is null)
            {
                context.ParticipantEmailDeliveries.Add(new ParticipantEmailDeliveryEntity
                {
                    ParticipantEmailDeliveryId = Guid.CreateVersion7(),
                    ParticipantId = delivery.ParticipantId,
                    MessageType = delivery.MessageType,
                    SesMessageId = delivery.SesMessageId,
                    Status = delivery.Status,
                    Detail = delivery.Detail,
                    OccurredAt = delivery.OccurredAt,
                    UpdatedAt = DateTimeOffset.UtcNow
                });

                await context.SaveChangesAsync().ConfigureAwait(false);
                written = true;
                return;
            }

            if (DeliveryStatuses.RankOf(delivery.Status) < DeliveryStatuses.RankOf(existing.Status))
                return;

            existing.Status = delivery.Status;
            existing.Detail = delivery.Detail;
            existing.OccurredAt = delivery.OccurredAt;
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            // The type is only ever written here, never corrected, so a row first created by an
            // event that carried no type tag is finished by the next one that does.
            if (existing.MessageType == EmailMessageType.Unspecified)
                existing.MessageType = delivery.MessageType;

            await context.SaveChangesAsync().ConfigureAwait(false);
            written = true;
        }).ConfigureAwait(false);

        return written;
    }

    /// <summary>
    /// Issues one more routing token for a single participant, alongside any they already hold.
    /// </summary>
    /// <remarks>
    /// Alongside, not instead of. An Ask has to put a working SHARE GIFT IDEAS link in front of
    /// somebody who never asked for one, and their existing token cannot be reconstructed — only
    /// its hash was kept, which is the entire point of keeping it that way. Replacing the row would
    /// hand them a new link while silently killing the one already sitting in their invitation,
    /// so a second is added and both keep working. Lookup is by token hash and never by
    /// participant, so nothing downstream has to know how many there are.
    /// </remarks>
    /// <returns>The token in the clear. Only ever available here.</returns>
    public async Task<string> IssueGiftIdeaTokenAsync(Guid participantId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var token = SecretToken.Create();

        context.GiftIdeaTokens.Add(new GiftIdeaTokenEntity
        {
            GiftIdeaTokenId = Guid.CreateVersion7(),
            ParticipantId = participantId,
            TokenHash = SecretToken.Hash(token),
            IssuedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync().ConfigureAwait(false);

        return token;
    }

    /// <summary>
    /// Stops every gift ideas link this participant has been sent from working: the ones about
    /// themselves, and every ask that came to them about somebody else.
    /// </summary>
    /// <remarks>
    /// For an address correction, which happens because earlier mail went to the wrong inbox, and
    /// the links in that mail are the whole credential. Whoever reads that inbox could otherwise
    /// keep writing ideas in this participant's name.
    ///
    /// The two kinds are closed off differently. A gift ideas token is referenced by nothing, so it
    /// is deleted. An ask is where a contribution records who wrote it and who it is about, so
    /// deleting one would orphan what was already shared. Its hash is overwritten with the hash of
    /// a token that is thrown away instead. The row survives and no link can reach it. Each ask
    /// gets its own throwaway token, because the hash column is unique.
    /// </remarks>
    public async Task RevokeGiftIdeaLinksAsync(Guid participantId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        await context.GiftIdeaTokens
            .Where(token => token.ParticipantId == participantId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        var asks = await context.GiftIdeaAsks
            .Where(ask => ask.HelperParticipantId == participantId)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var ask in asks)
            ask.TokenHash = SecretToken.Hash(SecretToken.Create());

        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a leave token for a single participant, replacing any they already hold.
    /// </summary>
    /// <remarks>
    /// Replaces, where <see cref="IssueGiftIdeaTokenAsync"/> adds. The two differ because what the
    /// tokens authorise differs: several live gift ideas links are the intended state, since an
    /// Ask has to put a working one in front of somebody who never received theirs, and an address
    /// correction closes all of them off at once through <see cref="RevokeGiftIdeaLinksAsync"/>. A
    /// stale leave link removes somebody, and the only caller
    /// here is an address correction — which happens precisely because the earlier invitation went
    /// to the wrong inbox. Leaving that one live would let whoever holds it take the participant
    /// out of the exchange.
    ///
    /// The caller decides who gets one; this does not check. The organizer exclusion lives in
    /// <see cref="IssueLeaveTokensAsync"/>, which is the path that issues them in bulk, and in the
    /// one caller of this method.
    /// </remarks>
    /// <returns>The token in the clear. Only ever available here.</returns>
    public async Task<string> IssueLeaveTokenAsync(Guid participantId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        await context.ParticipantLeaveTokens
            .Where(token => token.ParticipantId == participantId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        var token = SecretToken.Create(SecretToken.OpaqueTokenBytes);

        context.ParticipantLeaveTokens.Add(new ParticipantLeaveTokenEntity
        {
            ParticipantLeaveTokenId = Guid.CreateVersion7(),
            ParticipantId = participantId,
            TokenHash = SecretToken.Hash(token),
            IssuedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync().ConfigureAwait(false);

        return token;
    }

    /// <summary>
    /// Resolves a gift ideas link to the participant it was issued to and the participant their
    /// ideas are for, from the hash of the token in the link.
    /// </summary>
    /// <param name="tokenHash">
    /// <see cref="SecretToken.Hash"/> of the token taken from the link. The plaintext is never
    /// passed here — nothing stored could be compared against it.
    /// </param>
    public async Task<(bool found, GiftIdeaRoute route)> FindGiftIdeaRouteAsync(string tokenHash)
    {
        // An empty hash is not a lookup worth making. It cannot match a stored row, since every one
        // holds a real digest, but refusing it here says so outright rather than relying on that.
        if (string.IsNullOrWhiteSpace(tokenHash))
            return (false, GiftIdeaRoutes.Empty);

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // Two joins, not five. The hops to a person and to a hat are navigations, so EF emits those
        // joins itself; only the two that cross an id with no navigation behind it are stated here,
        // and both are deliberate — a token names a participant and a pick names a participant,
        // neither through a foreign key.
        //
        // Both are inner joins, which is what the sentinel participant and person are for: a
        // participant who has not drawn anybody carries the all-zero id, and that id names a real
        // row whose name is the empty string.
        var match = await context.GiftIdeaTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Join(
                context.Participants,
                token => token.ParticipantId,
                participant => participant.ParticipantId,
                (_, participant) => participant)
            .Join(
                context.Participants,
                participant => participant.PickedRecipientParticipantId,
                picked => picked.ParticipantId,
                (participant, picked) => new
                {
                    participant.ParticipantId,
                    participant.Hat.HatId,
                    HatName = participant.Hat.Name,
                    participant.Hat.Status,
                    SenderName = participant.Person.Name,
                    SenderEmail = participant.Person.Email,
                    PickedParticipantId = picked.ParticipantId,
                    PickedName = picked.Person.Name,
                    PickedEmail = picked.Person.Email
                })
            .SingleOrDefaultAsync()
            .ConfigureAwait(false);

        if (match is null)
            return (false, GiftIdeaRoutes.Empty);

        // Who drew the sender, which is the inverse of a pick and so cannot be reached by following
        // one. Read separately rather than joined above: this is the one part that may legitimately
        // find nothing, and an inner join would have discarded the whole match along with it.
        //
        // Ordered, because an organizer editing picks can leave two participants holding the same
        // name. Which of them receives a forward is arbitrary either way, but an arbitrary answer
        // here would also decide whether a held submission is released, and a decision of that kind
        // should at least be the same one every time it is made.
        var giver = await context.Participants
            .AsNoTracking()
            .Where(participant => participant.PickedRecipientParticipantId == match.ParticipantId)
            .OrderBy(participant => participant.ParticipantId)
            .Select(participant => new
            {
                participant.ParticipantId,
                participant.Person.Name,
                participant.Person.Email
            })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return (true, new GiftIdeaRoute
        {
            ParticipantId = match.ParticipantId,
            HatId = match.HatId,
            HatName = match.HatName,
            HatStatus = match.Status,
            Sender = new Person { Name = match.SenderName, Email = match.SenderEmail },
            SenderPickedRecipient = new Person { Name = match.PickedName, Email = match.PickedEmail },
            SenderPickedRecipientParticipantId = match.PickedParticipantId,
            // Their own words about themselves, so the sender is the subject. The contribution
            // lookup below is the only place these two come apart.
            Subject = new Person { Name = match.SenderName, Email = match.SenderEmail },
            Giver = giver is null
                ? Persons.Empty
                : new Person { Name = giver.Name, Email = giver.Email },
            GiverParticipantId = giver?.ParticipantId ?? Guid.Empty,
            AskId = Guid.Empty
        });
    }

    /// <summary>
    /// Resolves a gift ideas link to the ask it answers, from the hash of the token in the link. The counterpart of <see cref="FindGiftIdeaRouteAsync"/> for the case where
    /// somebody is writing about another participant rather than themselves.
    /// </summary>
    /// <remarks>
    /// Deliberately a second method against a second table rather than one lookup over both. The
    /// two token kinds mean genuinely different things — one names a participant, the other names a
    /// three-way arrangement between participants — and a query that returned either would have to
    /// leave half its columns empty on each path, which is how the wrong one eventually gets read.
    /// The caller tries this only after the other has found nothing, and the token spaces do not
    /// overlap.
    /// </remarks>
    /// <param name="tokenHash">
    /// <see cref="SecretToken.Hash"/> of the token taken from the link, as above.
    /// </param>
    public async Task<(bool found, GiftIdeaRoute route)> FindGiftIdeaContributionRouteAsync(string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            return (false, GiftIdeaRoutes.Empty);

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // Four joins for the four participants an ask involves — the helper, the helper's own pick,
        // the subject and the asker. Each one's name and address arrives through the Person
        // navigation rather than a join of its own, and so does the hat, which is why this is four
        // rather than the nine an id-by-id reading would suggest.
        //
        // Every join is inner and every one of them is safe. The asker and the subject are rows the
        // ask cannot outlive — removing either participant deletes the ask — and the helper's own
        // pick resolves through the sentinel participant when they have not drawn anybody, exactly
        // as it does above.
        var match = await context.GiftIdeaAsks
            .AsNoTracking()
            .Where(ask => ask.TokenHash == tokenHash)
            .Join(
                context.Participants,
                ask => ask.HelperParticipantId,
                helper => helper.ParticipantId,
                (ask, helper) => new { ask, helper })
            .Join(
                context.Participants,
                row => row.helper.PickedRecipientParticipantId,
                helperPick => helperPick.ParticipantId,
                (row, helperPick) => new { row.ask, row.helper, helperPick })
            .Join(
                context.Participants,
                row => row.ask.SubjectParticipantId,
                subject => subject.ParticipantId,
                (row, subject) => new { row.ask, row.helper, row.helperPick, subject })
            .Join(
                context.Participants,
                row => row.ask.AskerParticipantId,
                asker => asker.ParticipantId,
                (row, asker) => new
                {
                    row.ask.GiftIdeaAskId,
                    row.helper.ParticipantId,
                    row.helper.Hat.HatId,
                    HatName = row.helper.Hat.Name,
                    row.helper.Hat.Status,
                    SenderName = row.helper.Person.Name,
                    SenderEmail = row.helper.Person.Email,
                    HelperPickParticipantId = row.helperPick.ParticipantId,
                    HelperPickName = row.helperPick.Person.Name,
                    HelperPickEmail = row.helperPick.Person.Email,
                    SubjectName = row.subject.Person.Name,
                    SubjectEmail = row.subject.Person.Email,
                    AskerParticipantId = asker.ParticipantId,
                    AskerName = asker.Person.Name,
                    AskerEmail = asker.Person.Email
                })
            .SingleOrDefaultAsync()
            .ConfigureAwait(false);

        if (match is null)
            return (false, GiftIdeaRoutes.Empty);

        return (true, new GiftIdeaRoute
        {
            ParticipantId = match.ParticipantId,
            HatId = match.HatId,
            HatName = match.HatName,
            HatStatus = match.Status,
            Sender = new Person { Name = match.SenderName, Email = match.SenderEmail },
            // Still the helper's own pick, not the subject. This feeds the check on what the helper
            // must not leak about themselves, which is a separate question from who they are
            // writing about.
            SenderPickedRecipient = new Person { Name = match.HelperPickName, Email = match.HelperPickEmail },
            SenderPickedRecipientParticipantId = match.HelperPickParticipantId,
            Subject = new Person { Name = match.SubjectName, Email = match.SubjectEmail },
            // The asker. They drew the subject, which is why they asked, so this is the same person
            // the ordinary path finds by looking for whoever holds the subject's name.
            Giver = new Person { Name = match.AskerName, Email = match.AskerEmail },
            GiverParticipantId = match.AskerParticipantId,
            AskId = match.GiftIdeaAskId
        });
    }

    /// <summary>
    /// Everybody in a hat that a participant could ask for gift ideas: all of them but themselves.
    /// </summary>
    /// <remarks>
    /// Their own pick is included and marked, rather than being handled separately, because from
    /// the asker's side it is one list of people and one choice. Whether a given name leads to
    /// "what would you like?" or "what do you think they'd like?" is this application's problem,
    /// not theirs.
    ///
    /// Names only, no addresses: see <see cref="AskCandidate"/>.
    /// </remarks>
    public async Task<ImmutableList<AskCandidate>> ListAskCandidatesAsync(Guid hatId, Guid askerParticipantId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var asker = await context.Participants
            .AsNoTracking()
            .Where(participant => participant.ParticipantId == askerParticipantId)
            .Select(participant => new { participant.PickedRecipientParticipantId })
            .SingleOrDefaultAsync()
            .ConfigureAwait(false);

        if (asker is null)
            return [];

        var candidates = await context.Participants
            .AsNoTracking()
            .Where(participant => participant.HatId == hatId
                                  && participant.ParticipantId != askerParticipantId)
            .Select(participant => new { participant.ParticipantId, participant.Person.Name })
            .ToListAsync()
            .ConfigureAwait(false);

        // Sorted here rather than in SQL: the pick comes first whatever it is called, and a
        // database collation has no way to know that.
        return
        [
            .. candidates
                .Select(candidate => new AskCandidate
                {
                    ParticipantId = candidate.ParticipantId,
                    Name = candidate.Name,
                    IsTheirPick = candidate.ParticipantId == asker.PickedRecipientParticipantId
                })
                .OrderByDescending(candidate => candidate.IsTheirPick)
                .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>
    /// Resolves the ids an asker chose to the people behind them, keeping only the ones they were
    /// entitled to choose.
    /// </summary>
    /// <remarks>
    /// The filtering is the point, not the lookup. The ids arrive in a form submission, and a form
    /// this application rendered is still something the sender can edit before posting it back, so
    /// membership of the asker's own hat is checked here rather than assumed from the page having
    /// offered it. An id belonging to another exchange, or to the asker themselves, is dropped
    /// silently: there is nothing to report to somebody who has edited a form by hand.
    ///
    /// Ordered as <see cref="ListAskCandidatesAsync"/> orders them, so that what the results page
    /// lists back reads in the same order as the page they chose from.
    /// </remarks>
    public async Task<ImmutableList<AskTarget>> FindAskTargetsAsync(
        Guid hatId,
        Guid askerParticipantId,
        ImmutableList<Guid> chosenParticipantIds
    )
    {
        if (chosenParticipantIds.IsEmpty)
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var asker = await context.Participants
            .AsNoTracking()
            .Where(participant => participant.ParticipantId == askerParticipantId)
            .Select(participant => new { participant.PickedRecipientParticipantId })
            .SingleOrDefaultAsync()
            .ConfigureAwait(false);

        if (asker is null)
            return [];

        var targets = await context.Participants
            .AsNoTracking()
            .Where(participant => participant.HatId == hatId
                                  && participant.ParticipantId != askerParticipantId
                                  && chosenParticipantIds.Contains(participant.ParticipantId))
            .Select(participant => new
            {
                participant.ParticipantId,
                participant.Person.Name,
                participant.Person.Email
            })
            .ToListAsync()
            .ConfigureAwait(false);

        return
        [
            .. targets
                .Select(target => new AskTarget
                {
                    ParticipantId = target.ParticipantId,
                    Person = new Person { Name = target.Name, Email = target.Email },
                    IsTheirPick = target.ParticipantId == asker.PickedRecipientParticipantId
                })
                .OrderByDescending(target => target.IsTheirPick)
                .ThenBy(target => target.Person.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
    }

    /// <summary>
    /// Records one participant asking another for ideas about a third, and issues the token the
    /// helper will write back on.
    /// </summary>
    /// <remarks>
    /// Added alongside any earlier ask between the same two people rather than replacing it, for
    /// the reason <see cref="IssueGiftIdeaTokenAsync"/> gives: the token in an ask already sent
    /// cannot be reconstructed, so overwriting the row would kill an address sitting in somebody's
    /// inbox. Every ask anybody has been sent keeps working, and each one carries its own subject.
    /// </remarks>
    /// <returns>The token in the clear. Only ever available here.</returns>
    public async Task<string> IssueGiftIdeaAskAsync(
        Guid askerParticipantId,
        Guid helperParticipantId,
        Guid subjectParticipantId
    )
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var token = SecretToken.Create();

        context.GiftIdeaAsks.Add(new GiftIdeaAskEntity
        {
            GiftIdeaAskId = Guid.CreateVersion7(),
            AskerParticipantId = askerParticipantId,
            HelperParticipantId = helperParticipantId,
            SubjectParticipantId = subjectParticipantId,
            TokenHash = SecretToken.Hash(token),
            IssuedAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync().ConfigureAwait(false);

        return token;
    }

    /// <summary>
    /// Appends a suggestion somebody made about another participant. Nothing is overwritten, for
    /// the reasons contributed_gift_idea--0001.sql gives.
    /// </summary>
    public async Task<Guid> AddContributedGiftIdeaAsync(Guid askId, string ideas)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var contribution = new ContributedGiftIdeaEntity
        {
            ContributedGiftIdeaId = Guid.CreateVersion7(),
            GiftIdeaAskId = askId,
            Ideas = ideas,
            CreatedAt = DateTimeOffset.UtcNow,
            // Nothing arrives by email any more, so there is no message to point back at.
            InboundMessageId = string.Empty
        };

        context.ContributedGiftIdeas.Add(contribution);

        await context.SaveChangesAsync().ConfigureAwait(false);

        return contribution.ContributedGiftIdeaId;
    }

    /// <summary>
    /// Appends a submission. Nothing is overwritten: the newest row for a participant is the one
    /// that counts, and the ones before it stay for the reasons gift_idea--0001.sql gives.
    /// </summary>
    /// <remarks>
    /// Whether it is held back is written here and never touched again. A row that says it was held
    /// keeps saying so after it has been passed on, for the reason
    /// <see cref="GiftIdeaEntity.HoldUntilAsked"/> gives; what has been passed on is
    /// <see cref="GiftIdeaEnquiryEntity.ReleasedAt"/>'s business.
    /// </remarks>
    internal async Task<Guid> AddGiftIdeaAsync(AddGiftIdeaRequest request)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var giftIdea = new GiftIdeaEntity
        {
            GiftIdeaId = Guid.CreateVersion7(),
            ParticipantId = request.ParticipantId,
            Ideas = request.Ideas,
            CreatedAt = DateTimeOffset.UtcNow,
            HoldUntilAsked = request.HoldUntilAsked,
            InboundMessageId = string.Empty
        };

        context.GiftIdeas.Add(giftIdea);

        await context.SaveChangesAsync().ConfigureAwait(false);

        return giftIdea.GiftIdeaId;
    }

    /// <summary>
    /// What a participant most recently shared about themselves, whether it is being held back, and
    /// whether anything of theirs has ever gone out outright.
    /// </summary>
    /// <remarks>
    /// Newest by <c>created_at</c>, which is the value gift_idea--0001.sql says decides the winner.
    /// The flag is read off that same row rather than summarised over all of them: sharing outright
    /// after holding something back replaces what was held.
    ///
    /// The second read is what lets the page keep its promise honestly. "These will never be seen by
    /// anyone" is untrue for somebody who has already shared once without holding back, because
    /// nothing recalls an email. Read from their own submissions, never from an enquiry, which would
    /// leak that their giver had asked.
    /// </remarks>
    internal async Task<GetLatestGiftIdeaResponse> GetLatestGiftIdeaAsync(Guid participantId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var latest = await context.GiftIdeas
            .AsNoTracking()
            .Where(giftIdea => giftIdea.ParticipantId == participantId)
            .OrderByDescending(giftIdea => giftIdea.CreatedAt)
            .Select(giftIdea => new { giftIdea.Ideas, giftIdea.HoldUntilAsked, giftIdea.CreatedAt })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        var sharedOutright = await context.GiftIdeas
            .AsNoTracking()
            .AnyAsync(giftIdea => giftIdea.ParticipantId == participantId && !giftIdea.HoldUntilAsked)
            .ConfigureAwait(false);

        return new GetLatestGiftIdeaResponse
        {
            Ideas = latest?.Ideas ?? string.Empty,
            HoldUntilAsked = latest?.HoldUntilAsked ?? false,
            CreatedAt = latest?.CreatedAt ?? DateTimeOffset.MinValue,
            HasSharedOutrightBefore = sharedOutright
        };
    }

    /// <summary>
    /// Whether a participant has asked for gift ideas about the person whose name they drew.
    /// </summary>
    /// <remarks>
    /// The question the share page asks before passing on a held submission, and the only thing that
    /// releases one. An answer of false is why nothing is sent, and it is never shown to the writer
    /// either way: telling them would be telling them their giver had been asking about them.
    /// </remarks>
    internal async Task<bool> HasAskedForGiftIdeasAsync(HasAskedForGiftIdeasRequest request)
    {
        // Neither id can match a real row, and a pick that does not exist cannot have been asked
        // about. Stated outright rather than left to the query to discover.
        if (request.AskerParticipantId == Guid.Empty || request.SubjectParticipantId == Guid.Empty)
            return false;

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        return await context.GiftIdeaEnquiries
            .AsNoTracking()
            .AnyAsync(enquiry =>
                enquiry.AskerParticipantId == request.AskerParticipantId
                && enquiry.SubjectParticipantId == request.SubjectParticipantId)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes down that a participant asked for gift ideas about their pick, if it is not written
    /// down already, and reports the enquiry as it now stands.
    /// </summary>
    /// <remarks>
    /// One row per pair. Asking again is still the same standing fact, so nothing is appended and
    /// <see cref="GiftIdeaEnquiryEntity.RequestedAt"/> stays the first time.
    ///
    /// The read and the insert share a transaction, because two rounds of asking seconds apart would
    /// otherwise both find nothing and both write. A concurrent insert that beats this one still
    /// says exactly what this call would have said, so the violation is caught and the row that won
    /// is read instead.
    /// </remarks>
    internal async Task<RecordGiftIdeaEnquiryResponse> RecordGiftIdeaEnquiryAsync(RecordGiftIdeaEnquiryRequest request)
    {
        try
        {
            return await RecordGiftIdeaEnquiryCoreAsync(request).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolationOf(exception, GiftIdeaEnquiryPairIndex))
        {
            var existing = await FindGiftIdeaEnquiryAsync(
                    request.AskerParticipantId,
                    request.SubjectParticipantId)
                .ConfigureAwait(false);

            // Defensive. The row the violation named has to be there, and if it somehow is not, the
            // safe answer is one that releases nothing rather than one that invents a date.
            return existing ?? new RecordGiftIdeaEnquiryResponse
            {
                RequestedAt = DateTimeOffset.UtcNow,
                ReleasedAt = DateTimeOffset.UtcNow
            };
        }
    }

    private async Task<RecordGiftIdeaEnquiryResponse> RecordGiftIdeaEnquiryCoreAsync(
        RecordGiftIdeaEnquiryRequest request
    )
    {
        RecordGiftIdeaEnquiryResponse? recorded = null;

        await InTransactionAsync(async context =>
        {
            // Reset, because InTransactionAsync replays the whole delegate on a fresh context.
            recorded = null;

            var existing = await context.GiftIdeaEnquiries
                .AsNoTracking()
                .Where(enquiry =>
                    enquiry.AskerParticipantId == request.AskerParticipantId
                    && enquiry.SubjectParticipantId == request.SubjectParticipantId)
                .OrderBy(enquiry => enquiry.RequestedAt)
                .Select(enquiry => new RecordGiftIdeaEnquiryResponse
                {
                    RequestedAt = enquiry.RequestedAt,
                    ReleasedAt = enquiry.ReleasedAt
                })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (existing is not null)
            {
                recorded = existing;
                return;
            }

            var requestedAt = DateTimeOffset.UtcNow;

            context.GiftIdeaEnquiries.Add(new GiftIdeaEnquiryEntity
            {
                GiftIdeaEnquiryId = Guid.CreateVersion7(),
                AskerParticipantId = request.AskerParticipantId,
                SubjectParticipantId = request.SubjectParticipantId,
                RequestedAt = requestedAt,
                // Nothing has been passed on against an enquiry that has only just been made.
                ReleasedAt = DateTimeOffset.MinValue
            });

            await context.SaveChangesAsync().ConfigureAwait(false);

            recorded = new RecordGiftIdeaEnquiryResponse
            {
                RequestedAt = requestedAt,
                ReleasedAt = DateTimeOffset.MinValue
            };
        }).ConfigureAwait(false);

        return recorded!;
    }

    /// <summary>
    /// Stamps when a held submission was passed on, so the next ask does not send it again.
    /// </summary>
    /// <remarks>
    /// After the send rather than before it: mail from here cannot report failure, so a release that
    /// never arrived is worth repeating and one that did is not.
    /// </remarks>
    internal async Task MarkGiftIdeaEnquiryReleasedAsync(MarkGiftIdeaEnquiryReleasedRequest request)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        await context.GiftIdeaEnquiries
            .Where(enquiry =>
                enquiry.AskerParticipantId == request.AskerParticipantId
                && enquiry.SubjectParticipantId == request.SubjectParticipantId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(enquiry => enquiry.ReleasedAt, request.ReleasedAt))
            .ConfigureAwait(false);
    }

    private async Task<RecordGiftIdeaEnquiryResponse?> FindGiftIdeaEnquiryAsync(
        Guid askerParticipantId,
        Guid subjectParticipantId
    )
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        return await context.GiftIdeaEnquiries
            .AsNoTracking()
            .Where(enquiry =>
                enquiry.AskerParticipantId == askerParticipantId
                && enquiry.SubjectParticipantId == subjectParticipantId)
            .OrderBy(enquiry => enquiry.RequestedAt)
            .Select(enquiry => new RecordGiftIdeaEnquiryResponse
            {
                RequestedAt = enquiry.RequestedAt,
                ReleasedAt = enquiry.ReleasedAt
            })
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// What somebody most recently suggested in answer to one ask, or the empty string if they have
    /// not answered it.
    /// </summary>
    /// <remarks>
    /// Per ask rather than per helper. The same person can be asked about two different
    /// participants, and what they said about one of them is no answer about the other.
    /// </remarks>
    public async Task<string> GetLatestContributedGiftIdeaAsync(Guid askId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        return await context.ContributedGiftIdeas
            .AsNoTracking()
            .Where(contribution => contribution.GiftIdeaAskId == askId)
            .OrderByDescending(contribution => contribution.CreatedAt)
            .Select(contribution => contribution.Ideas)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false) ?? string.Empty;
    }

    public async Task UpdateHatStatusAsync(string organizerEmail, Guid hatId, string newStatus)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var organizerPersonId = await FindPersonIdByEmailAsync(context, organizerEmail).ConfigureAwait(false);

        if (organizerPersonId == Guid.Empty)
            return;

        // Read here rather than left in the expression tree, where Npgsql would translate it to
        // now() and the stamp would come from the cluster's clock instead of this one. The same
        // reading is what MarkInvitationsAsQueuedAsync writes and returns.
        var statusUpdatedAt = DateTimeOffset.UtcNow;

        await context.Hats
            .Where(hat => hat.HatId == hatId && hat.OrganizerPersonId == organizerPersonId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hat => hat.Status, newStatus)
                .SetProperty(hat => hat.StatusUpdatedAt, statusUpdatedAt))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Puts an exchange back to where a newly created one starts: the same people, everybody
    /// allowed to draw everybody, nobody holding a name, and the status at the beginning again.
    /// </summary>
    /// <remarks>
    /// The status is read inside the transaction rather than taken on trust from the caller's
    /// earlier check. An organizer with two tabs can send invitations and reset at the same moment,
    /// and half of this having run would leave invitations naming picks that no longer exist.
    /// Answering false rather than throwing lets the caller report the race as the conflict it is.
    ///
    /// Nothing is deleted but the eligibility rows, which are immediately rewritten. Gift ideas,
    /// tokens and delivery history belong to invitations that, by the status check above, have not
    /// been sent.
    /// </remarks>
    internal async Task<bool> ResetHatAsync(ResetHatRequest request)
    {
        var wasReset = false;

        await InTransactionAsync(async context =>
        {
            var organizerPersonId = await FindPersonIdByEmailAsync(context, request.OrganizerEmail)
                .ConfigureAwait(false);

            if (organizerPersonId == Guid.Empty)
            {
                _logger.LogError(
                    "Hat {HatId} was not reset: {OrganizerEmail} is not a known organizer.",
                    request.HatId,
                    request.OrganizerEmail);
                return;
            }

            var status = await context.Hats
                .AsNoTracking()
                .Where(hat => hat.HatId == request.HatId && hat.OrganizerPersonId == organizerPersonId)
                .Select(hat => hat.Status)
                .SingleOrDefaultAsync()
                .ConfigureAwait(false);

            if (status is null || !HatStatuses.BeforeInvitationsSent.Contains(status))
            {
                _logger.LogError(
                    "Hat {HatId} was not reset: it is at {HatStatus}, which is past the point where resetting is possible.",
                    request.HatId,
                    status ?? "(no such hat)");
                return;
            }

            var participantIds = await context.Participants
                .Where(participant => participant.HatId == request.HatId)
                .Select(participant => participant.ParticipantId)
                .ToListAsync()
                .ConfigureAwait(false);

            await context.ParticipantEligibleRecipients
                .Where(row => participantIds.Contains(row.ParticipantId))
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            // Everybody may draw everybody but themselves, which is what a hat looks like before an
            // organizer narrows it -- the same wiring AddParticipantService gives a new arrival.
            foreach (var participantId in participantIds)
            foreach (var eligibleId in participantIds.Where(candidate => candidate != participantId))
                context.ParticipantEligibleRecipients.Add(new ParticipantEligibleRecipientEntity
                {
                    ParticipantEligibleRecipientId = Guid.CreateVersion7(),
                    ParticipantId = participantId,
                    EligibleParticipantId = eligibleId
                });

            await context.Participants
                .Where(participant => participant.HatId == request.HatId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(participant => participant.PickedRecipientParticipantId, Guid.Empty))
                .ConfigureAwait(false);

            var statusUpdatedAt = DateTimeOffset.UtcNow;

            await context.Hats
                .Where(hat => hat.HatId == request.HatId && hat.OrganizerPersonId == organizerPersonId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(hat => hat.Status, HatStatus.InProgress)
                    .SetProperty(hat => hat.StatusUpdatedAt, statusUpdatedAt))
                .ConfigureAwait(false);

            await context.SaveChangesAsync().ConfigureAwait(false);

            wasReset = true;
        }).ConfigureAwait(false);

        return wasReset;
    }

    public async Task<Participant> CreateParticipantAsync(
        AddParticipantRequest request,
        ImmutableList<Participant> existingParticipants
    )
    {
        // Introduced by the organizer, which resolves to themselves when they are adding
        // themselves. Somebody the application already knows keeps the name they already have
        // unless this organizer is entitled to state it — see ResolvePersonIdAsync.
        var personId = await ResolvePersonIdAsync(new ResolvePersonRequest
        {
            Email = request.Email,
            Name = request.Name,
            IntroducedByEmail = request.OrganizerEmail
        }).ConfigureAwait(false);

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // Chosen from what the hat is already wearing, which the caller has in hand. Read from
        // those records rather than queried for: this is the same list the caller checked for a
        // duplicate name against, and asking the database again would only be a second opinion on
        // a decision that does not need one -- two people added at the same instant may pick the
        // same face either way, and a face is decoration.
        var emoji = PersonEmoji.Assign(existingParticipants.Select(existing => existing.Emoji));

        var participant = new ParticipantEntity
        {
            ParticipantId = Guid.CreateVersion7(),
            HatId = request.HatId,
            PersonId = personId,
            PickedRecipientParticipantId = Guid.Empty,
            Emoji = emoji
        };

        context.Participants.Add(participant);

        var existingEmails = existingParticipants
            .Select(existing => existing.Person.Email)
            .ToList();

        // The new participant may draw everyone already in the hat.
        var eligibleIds = await context.Participants
            .Where(candidate => candidate.HatId == request.HatId
                                && existingEmails.Contains(candidate.Person.Email))
            .Select(candidate => candidate.ParticipantId)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var eligibleId in eligibleIds)
            context.ParticipantEligibleRecipients.Add(new ParticipantEligibleRecipientEntity
            {
                ParticipantEligibleRecipientId = Guid.CreateVersion7(),
                ParticipantId = participant.ParticipantId,
                EligibleParticipantId = eligibleId
            });

        await context.SaveChangesAsync().ConfigureAwait(false);

        return new Participant
        {
            PickedRecipient = string.Empty,
            Person = new Person { Name = request.Name, Email = request.Email },
            Emoji = emoji,
            // Nothing has been sent to somebody who was added a moment ago.
            DeliveryStatus = Models.DeliveryStatus.Unknown,
            DeliveryDetail = string.Empty,
            DeliveryMessageType = string.Empty,
            DeliveryOccurredAt = DateTimeOffset.MinValue,
            EligibleRecipients = existingParticipants
                .Select(existing => existing.Person.Name)
                .ToImmutableList()
        };
    }

    public async Task<ImmutableList<Participant>> GetParticipantsAsync(string organizerEmail, Guid hatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var participants = await LoadParticipantsAsync(context, organizerEmail, hatId).ConfigureAwait(false);

        return ToDomain(participants);
    }

    public async Task<(bool participantExists, Participant participant)> GetParticipantAsync(
        string requestOrganizerEmail,
        Guid requestHatId,
        string requestParticipantEmail
    )
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        // The whole hat is loaded even though one participant is wanted. A pick is a participant id
        // and the domain record carries a name, so resolving it means having that participant in
        // hand; a hat holds a few dozen people at most, so fetching them all is cheaper than a
        // second round trip for the one being drawn.
        var participants = await LoadParticipantsAsync(context, requestOrganizerEmail, requestHatId)
            .ConfigureAwait(false);

        var participant = participants
            .SingleOrDefault(entity => entity.Person.Email == requestParticipantEmail);

        return participant is null
            ? (false, Participants.Empty)
            : (true, ToDomain(participant, NamesByParticipantId(participants)));
    }

    public async Task AddParticipantEligibleRecipientAsync(
        string organizerEmail,
        Guid hatId,
        string participantEmail,
        string recipientName
    )
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var participantId = await FindParticipantIdByEmailAsync(context, hatId, participantEmail).ConfigureAwait(false);
        var recipientId = await FindParticipantIdByNameAsync(context, hatId, recipientName).ConfigureAwait(false);

        if (participantId == Guid.Empty || recipientId == Guid.Empty)
        {
            _logger.LogWarning("Could not add an eligible recipient to hat {HatId}: participant or recipient not found.", hatId);
            return;
        }

        context.ParticipantEligibleRecipients.Add(new ParticipantEligibleRecipientEntity
        {
            ParticipantEligibleRecipientId = Guid.CreateVersion7(),
            ParticipantId = participantId,
            EligibleParticipantId = recipientId
        });

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Already eligible. The unique index makes this idempotent rather than a duplicate row.
        }
    }

    /// <summary>
    /// Replaces a participant's eligibility list. An empty list is simply no rows.
    /// </summary>
    public Task UpdateEligibleRecipientsAsync(
        string organizerEmail,
        Guid hatId,
        string participantEmail,
        ImmutableList<string> eligibleRecipients
    ) =>
        InTransactionAsync(async context =>
        {
            var participantId = await FindParticipantIdByEmailAsync(context, hatId, participantEmail).ConfigureAwait(false);

            if (participantId == Guid.Empty)
            {
                _logger.LogWarning("Could not update eligible recipients for {ParticipantEmail}: not found in hat {HatId}.", participantEmail, hatId);
                return;
            }

            await context.ParticipantEligibleRecipients
                .Where(row => row.ParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            var normalized = eligibleRecipients.Select(Normalize).ToList();

            // ToLower, not ToLowerInvariant: see FindParticipantIdByNameAsync.
            var recipientIds = await context.Participants
                .Where(candidate => candidate.HatId == hatId
                                    && normalized.Contains(candidate.Person.Name.ToLower()))
                .Select(candidate => candidate.ParticipantId)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var recipientId in recipientIds)
                context.ParticipantEligibleRecipients.Add(new ParticipantEligibleRecipientEntity
                {
                    ParticipantEligibleRecipientId = Guid.CreateVersion7(),
                    ParticipantId = participantId,
                    EligibleParticipantId = recipientId
                });

            await context.SaveChangesAsync().ConfigureAwait(false);
        });

    /// <summary>
    /// Removes a participant from a hat, along with their eligibility rows in both directions, any
    /// pick pointing at them, and everything they shared. Without foreign keys nothing cleans up on
    /// our behalf, and a dangling picked_recipient_participant_id would survive the delete.
    ///
    /// The person stays. Leaving a hat is not ceasing to exist, and they may be in another one.
    /// </summary>
    public Task DeleteParticipantAsync(string requestOrganizerEmail, Guid requestHatId, string requestEmail) =>
        InTransactionAsync(async context =>
        {
            var participantId = await FindParticipantIdByEmailAsync(context, requestHatId, requestEmail).ConfigureAwait(false);

            if (participantId == Guid.Empty)
                return;

            await context.ParticipantEligibleRecipients
                .Where(row => row.ParticipantId == participantId
                              || row.EligibleParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            // What they shared goes with them, and so does the address that let them share it.
            // Leaving the token behind would keep a live mailbox pointed at a participant who is no
            // longer in the hat.
            await context.GiftIdeas
                .Where(giftIdea => giftIdea.ParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            await context.GiftIdeaTokens
                .Where(token => token.ParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            // Their leave link too. A token whose participant is gone routes nowhere, and what has
            // to survive a removal is the refusal on the do_not_add lists, not the credential —
            // those rows are deliberately left standing.
            await context.ParticipantLeaveTokens
                .Where(token => token.ParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            // What SES said about the mail sent to them. An organizer who removes somebody and adds
            // them back has a new participant, and starting that one with the delivery history of a
            // person who is no longer in the exchange would be a claim about a message never sent
            // to them.
            await context.ParticipantEmailDeliveries
                .Where(delivery => delivery.ParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            // Every ask that names them, in any of its three roles. Not only the ones they made:
            // an ask they were asked to answer would otherwise leave a live address pointed at
            // somebody who has left the exchange, and an ask about them would leave a helper being
            // invited to suggest gifts for a person who is no longer in it.
            var askIds = await context.GiftIdeaAsks
                .AsNoTracking()
                .Where(ask => ask.AskerParticipantId == participantId
                              || ask.HelperParticipantId == participantId
                              || ask.SubjectParticipantId == participantId)
                .Select(ask => ask.GiftIdeaAskId)
                .ToListAsync()
                .ConfigureAwait(false);

            await context.ContributedGiftIdeas
                .Where(contribution => askIds.Contains(contribution.GiftIdeaAskId))
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            await context.GiftIdeaAsks
                .Where(ask => askIds.Contains(ask.GiftIdeaAskId))
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            // Enquiries in both directions, unlike the hat sweep, which can rely on the whole
            // exchange going at once. Theirs, which now name nobody; and everybody else's about
            // them, which would otherwise hold a submission open for a participant who is gone.
            await context.GiftIdeaEnquiries
                .Where(enquiry => enquiry.AskerParticipantId == participantId
                                  || enquiry.SubjectParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);

            await context.Participants
                .Where(participant => participant.PickedRecipientParticipantId == participantId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(participant => participant.PickedRecipientParticipantId, Guid.Empty))
                .ConfigureAwait(false);

            await context.Participants
                .Where(participant => participant.ParticipantId == participantId)
                .ExecuteDeleteAsync()
                .ConfigureAwait(false);
        });

    public async Task UpdateParticipantPickedRecipientAsync(
        string organizerEmail,
        Guid hatId,
        string participantEmail,
        string pickedRecipientName
    )
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var participantId = await FindParticipantIdByEmailAsync(context, hatId, participantEmail).ConfigureAwait(false);

        if (participantId == Guid.Empty)
            return;

        // An empty name clears the pick, and so does a name nobody in the hat goes by — both end up
        // as the all-zero id, which is what "has not drawn" looks like.
        var pickedId = string.IsNullOrWhiteSpace(pickedRecipientName)
            ? Guid.Empty
            : await FindParticipantIdByNameAsync(context, hatId, pickedRecipientName).ConfigureAwait(false);

        await context.Participants
            .Where(participant => participant.ParticipantId == participantId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(participant => participant.PickedRecipientParticipantId, pickedId))
            .ConfigureAwait(false);
    }

    public async Task RemoveParticipantFromEligibleRecipientsAsync(
        string organizerEmail,
        Guid hatId,
        string participantNameToRemove
    )
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var participantId = await FindParticipantIdByNameAsync(context, hatId, participantNameToRemove).ConfigureAwait(false);

        if (participantId == Guid.Empty)
            return;

        // Leaving someone with no eligible recipients is representable. It is a validation problem
        // for EligibilityValidationService to report, not a write that cannot be stored.
        await context.ParticipantEligibleRecipients
            .Where(row => row.EligibleParticipantId == participantId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
    }

    public async Task<DateTimeOffset> MarkInvitationsAsQueuedAsync(
        string organizerEmail,
        Guid hatId,
        string sentFromIpAddress
    )
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var invitationsQueuedAt = DateTimeOffset.UtcNow;

        var organizerPersonId = await FindPersonIdByEmailAsync(context, organizerEmail).ConfigureAwait(false);

        // An unknown address resolves to the all-zero id, which is the sentinel hat's organizer. It
        // could never satisfy the status check below, but a write scoped to "nobody" is not a write
        // worth attempting.
        if (organizerPersonId == Guid.Empty)
        {
            _logger.LogError("Hat {HatId} was not marked as queued: {OrganizerEmail} is not a known organizer.", hatId, organizerEmail);
            return invitationsQueuedAt;
        }

        // Conditional on the expected status, so two concurrent sends cannot both mark the hat.
        var updated = await context.Hats
            .Where(hat => hat.HatId == hatId
                          && hat.OrganizerPersonId == organizerPersonId
                          && hat.Status == HatStatus.NamesAssigned)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hat => hat.Status, HatStatus.InvitationsSent)
                .SetProperty(hat => hat.StatusUpdatedAt, invitationsQueuedAt)
                .SetProperty(hat => hat.InvitationsQueuedAt, invitationsQueuedAt)
                .SetProperty(hat => hat.InvitationsSentFromIp, sentFromIpAddress))
            .ConfigureAwait(false);

        if (updated == 0)
            _logger.LogError("Hat {HatId} was not in {ExpectedStatus} when invitations were queued, so it was left unchanged.", hatId, HatStatus.NamesAssigned);

        return invitationsQueuedAt;
    }

    public async Task TryTransitionHatToCooledOffAsync(string organizerEmail, Guid hatId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var organizerPersonId = await FindPersonIdByEmailAsync(context, organizerEmail).ConfigureAwait(false);

        if (organizerPersonId == Guid.Empty)
        {
            _logger.LogError("Couldn't update HatStatus to READY_TO_CLOSE for hat {hatId}: {OrganizerEmail} is not a known organizer.", hatId, organizerEmail);
            return;
        }

        var statusUpdatedAt = DateTimeOffset.UtcNow;

        var updated = await context.Hats
            .Where(hat => hat.HatId == hatId
                          && hat.OrganizerPersonId == organizerPersonId
                          && hat.Status == HatStatus.InvitationsSent)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(hat => hat.Status, HatStatus.CooledOff)
                .SetProperty(hat => hat.StatusUpdatedAt, statusUpdatedAt))
            .ConfigureAwait(false);

        if (updated == 0)
            _logger.LogError("Couldn't update HatStatus to READY_TO_CLOSE for hat {hatId}, since it does not have expected status. Will not retry.", hatId);
    }

    /// <summary>
    /// Every participant in a hat, with the person behind each one and the people they may draw.
    /// </summary>
    private static async Task<List<ParticipantEntity>> LoadParticipantsAsync(
        GiftExchangeDbContext context,
        string organizerEmail,
        Guid hatId
    ) =>
        await context.Participants
            .AsNoTracking()
            .Include(participant => participant.Person)
            .Include(participant => participant.EligibleRecipients)
            .ThenInclude(row => row.EligibleParticipant).ThenInclude(participant => participant.Person)
            .Where(participant => participant.HatId == hatId
                                  && participant.Hat.Organizer.Email == organizerEmail)
            .ToListAsync()
            .ConfigureAwait(false);

    /// <summary>
    /// The person id registered to this email address, or <see cref="Guid.Empty"/> if nobody holds
    /// it. Absence is a value rather than a null here for the same reason it is in the schema.
    /// </summary>
    private static async Task<Guid> FindPersonIdByEmailAsync(GiftExchangeDbContext context, string email) =>
        await context.Persons
            .Where(person => person.Email == email)
            .Select(person => person.PersonId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

    /// <summary>
    /// The id of the person registered to this email address, writing them in if they are new to
    /// the application.
    /// </summary>
    /// <remarks>
    /// Runs in its own context and its own transaction, ahead of whatever the caller is about to
    /// write, because a person is not part of any one exchange. That has two consequences worth
    /// stating.
    ///
    /// The first is that the race is handled here rather than left to the caller. Two requests can
    /// both find nobody and both try to write the same address; the unique index on person.email
    /// lets exactly one through, and the loser reads back the id the winner just created instead of
    /// failing. Introducing somebody twice at once is a collision with an obvious resolution, not
    /// an error to report.
    ///
    /// The second is that a person can outlive the write that introduced them — a hat rejected for
    /// a duplicate name leaves the organizer behind in the directory. A person with no hats and no
    /// participations is inert, and the next attempt finds them rather than writing them again.
    ///
    /// The supplied name wins only when the caller has standing to say what this person is called:
    /// they are that person, or they introduced them. That used to be unconditional, and it is the
    /// larger half of what made a name something two organizers could fight over — a rename refused
    /// by <see cref="RenamePersonAsync"/> could be had anyway by removing somebody and adding them
    /// back under the name you wanted, because the add path wrote whatever it was handed. It no
    /// longer does. An organizer adding somebody the application already knows gets them under the
    /// name they already have, silently, which is the same thing that happens when a participant is
    /// moved onto an address that belongs to an existing person.
    /// </remarks>
    private async Task<Guid> ResolvePersonIdAsync(ResolvePersonRequest request)
    {
        // The sentinel person holds the empty address. Reaching here with one would find it and
        // give it a name, and the caller would go on to write a hat owned by "nobody". Both are
        // worse than stopping: every address that gets this far has been through validation, so an
        // empty one is a bug upstream rather than input to accommodate.
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("A person needs an email address; the empty one belongs to the sentinel row.", nameof(request));

        var isSelf = request.Email.ContentEquals(request.IntroducedByEmail);

        await using (var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false))
        {
            // The all-zero id when they are introducing themselves, and also when the introducer is
            // somebody this table has never heard of — which should not happen, since an organizer
            // has a row before they can add anybody. Both read as "nobody else", and the row that
            // gets written points at itself.
            var introducedById = isSelf
                ? Guid.Empty
                : await FindPersonIdByEmailAsync(context, request.IntroducedByEmail).ConfigureAwait(false);

            var existing = await context.Persons
                .FirstOrDefaultAsync(candidate => candidate.Email == request.Email)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                // Never reassigned: whoever introduced somebody keeps that standing, and adding
                // them to a second exchange does not transfer it to whoever did the adding.
                if (MayRename(existing, isSelf ? existing.PersonId : introducedById))
                {
                    existing.Name = request.Name;
                    await context.SaveChangesAsync().ConfigureAwait(false);
                }

                return existing.PersonId;
            }

            var personId = Guid.CreateVersion7();

            var person = new PersonEntity
            {
                PersonId = personId,
                Name = request.Name,
                Email = request.Email,
                AddedByPersonId = introducedById == Guid.Empty ? personId : introducedById
            };

            context.Persons.Add(person);

            try
            {
                await context.SaveChangesAsync().ConfigureAwait(false);
                return person.PersonId;
            }
            catch (DbUpdateException exception) when (IsUniqueViolationOf(exception, PersonEmailIndex))
            {
                _logger.LogInformation("Another request introduced this person first; reading back the id it wrote.");
            }
        }

        // A fresh context, because the one above failed a save and still holds the person it could
        // not write.
        await using var reread = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        return await FindPersonIdByEmailAsync(reread, request.Email).ConfigureAwait(false);
    }

    private static async Task<Guid> FindParticipantIdByEmailAsync(
        GiftExchangeDbContext context,
        Guid hatId,
        string email
    ) =>
        await context.Participants
            .Where(participant => participant.HatId == hatId && participant.Person.Email == email)
            .Select(participant => participant.ParticipantId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

    /// <summary>
    /// Name lookups exist because the domain records still identify participants by name. They
    /// are only unambiguous because AddParticipantService refuses duplicate names within a hat.
    /// </summary>
    /// <remarks>
    /// ToLower rather than ToLowerInvariant, and deliberately so. Inside a LINQ expression tree
    /// this is never executed as a .NET string operation: EF translates it to SQL lower() and
    /// Postgres does the folding under the column's collation, so there is no .NET culture
    /// involved to get wrong. ToLowerInvariant has no translation at all and throws at query time.
    /// Use ToLowerInvariant everywhere the comparison really does happen in memory — see
    /// <see cref="Normalize"/>.
    /// </remarks>
    private static async Task<Guid> FindParticipantIdByNameAsync(
        GiftExchangeDbContext context,
        Guid hatId,
        string name
    )
    {
        var normalized = Normalize(name);

        return await context.Participants
            .Where(participant => participant.HatId == hatId
                                  && participant.Person.Name.ToLower() == normalized)
            .Select(participant => participant.ParticipantId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// No delivery information. What the reads that do not ask for it get -- every participant
    /// comes back with an empty status, which reads as "nothing heard" and never as "not delivered".
    /// </summary>
    private static readonly Dictionary<Guid, ParticipantEmailDeliveryEntity> NoDeliveries = [];

    private static ImmutableList<Participant> ToDomain(ICollection<ParticipantEntity> participants) =>
        ToDomain(participants, NoDeliveries);

    private static ImmutableList<Participant> ToDomain(
        ICollection<ParticipantEntity> participants,
        IReadOnlyDictionary<Guid, ParticipantEmailDeliveryEntity> deliveries
    )
    {
        var namesByParticipantId = NamesByParticipantId(participants);

        return participants
            .Select(participant => ToDomain(participant, namesByParticipantId, deliveries))
            .ToImmutableList();
    }

    /// <summary>
    /// A pick is stored as a participant id and the domain record carries a name, so translating
    /// one needs the rest of the hat. There is no navigation to follow instead: a reference
    /// navigation would have made EF emit a foreign key, and the all-zero id standing for "has not
    /// drawn" would fail it.
    /// </summary>
    private static Dictionary<Guid, string> NamesByParticipantId(ICollection<ParticipantEntity> participants) =>
        participants.ToDictionary(
            participant => participant.ParticipantId,
            participant => participant.Person.Name);

    private static Participant ToDomain(
        ParticipantEntity participant,
        IReadOnlyDictionary<Guid, string> namesByParticipantId
    ) =>
        ToDomain(participant, namesByParticipantId, NoDeliveries);

    private static Participant ToDomain(
        ParticipantEntity participant,
        IReadOnlyDictionary<Guid, string> namesByParticipantId,
        IReadOnlyDictionary<Guid, ParticipantEmailDeliveryEntity> deliveries
    )
    {
        // No row is the ordinary state before anything has been sent, and it stays the state for a
        // while after: SES publishes asynchronously. It reads as "nothing heard", never as "not
        // delivered" -- see the remarks on DeliveryStatus.
        var delivery = deliveries.GetValueOrDefault(participant.ParticipantId);

        return new Participant
        {
            // Guid.Empty is in no hat, so an undrawn participant falls through to the empty name
            // without being asked about separately.
            PickedRecipient = namesByParticipantId.GetValueOrDefault(
                participant.PickedRecipientParticipantId,
                string.Empty),
            Person = new Person { Name = participant.Person.Name, Email = participant.Person.Email },
            Emoji = participant.Emoji,
            EligibleRecipients = participant.EligibleRecipients
                .Select(row => row.EligibleParticipant.Person.Name)
                .ToImmutableList(),
            DeliveryStatus = delivery?.Status ?? Models.DeliveryStatus.Unknown,
            DeliveryDetail = delivery?.Detail ?? string.Empty,
            DeliveryMessageType = delivery?.MessageType ?? string.Empty,
            DeliveryOccurredAt = delivery?.OccurredAt ?? DateTimeOffset.MinValue
        };
    }

    private static string Normalize(string value) => value.TrimNullSafe().ToLowerInvariant();

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };

    /// <summary>
    /// A unique violation on one named index. Catching the index by name keeps a collision the
    /// caller can act on apart from one it cannot.
    /// </summary>
    private static bool IsUniqueViolationOf(DbUpdateException exception, string constraintName) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation } postgres
        && postgres.ConstraintName == constraintName;

    /// <summary>
    /// Runs several statements as one unit, retried as a whole if the strategy decides to.
    ///
    /// The execution strategy wrapper is required because retries are enabled: EF refuses a
    /// user-initiated transaction otherwise, since it cannot safely replay one it did not open.
    ///
    /// Every attempt gets its own context. Sharing one would leave entities from a failed attempt
    /// sitting in the change tracker as Added, so a retry would insert them a second time and
    /// collide with the unique index. It also keeps the retried work from closing over a context
    /// whose lifetime is owned by this method.
    /// </summary>
    internal async Task InTransactionAsync(Func<GiftExchangeDbContext, Task> work)
    {
        // Exists only to read the configured strategy, which comes from the provider rather than
        // from any connection. It stays alive for the duration because the strategy uses it for
        // diagnostics while executing.
        await using var strategyContext = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);

        var strategy = strategyContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
            // REPEATABLE READ is stated explicitly because it is the only level DSQL accepts.
            // Npgsql translates the default into READ COMMITTED, which DSQL rejects outright with
            // "0A000: Unsupported isolation level". Postgres accepts both, so the test suite cannot
            // catch this — it was only visible against a real cluster.
            await using var transaction = await context.Database
                .BeginTransactionAsync(IsolationLevel.RepeatableRead)
                .ConfigureAwait(false);

            await work(context).ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
