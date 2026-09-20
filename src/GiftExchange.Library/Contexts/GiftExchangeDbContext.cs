namespace GiftExchange.Library.Contexts;

/// <summary>
/// Sealed on purpose. The constructor reads <see cref="DbContext.Database"/>, which is virtual, and
/// a derived class could in principle override it and be handed an instance whose own constructor
/// has not run yet. Sealing rules that out, which is why no suppression is needed here.
///
/// Nothing about the setting below can move out of the constructor to avoid the call:
/// AutoSavepointsEnabled lives on DatabaseFacade and has no equivalent on DbContextOptionsBuilder,
/// so there is no OnConfiguring to state it in.
/// </summary>
public sealed class GiftExchangeDbContext : DbContext
{
    public GiftExchangeDbContext(DbContextOptions<GiftExchangeDbContext> options) : base(options)
    {
        // DSQL rejects SAVEPOINT outright ("0A000: unsupported transaction statement"). EF creates
        // one whenever SaveChanges runs inside a transaction that was already open, so it can undo
        // just that save without discarding the surrounding transaction.
        //
        // Losing that costs nothing here: every transaction this application opens goes through
        // GiftExchangeProvider.InTransactionAsync, which abandons the whole transaction on failure
        // rather than trying to continue after a partial one.
        //
        // Postgres supports savepoints, so nothing but a real cluster reveals this.
        Database.AutoSavepointsEnabled = false;
    }

    public DbSet<PersonEntity> Persons => Set<PersonEntity>();

    public DbSet<HatEntity> Hats => Set<HatEntity>();

    public DbSet<ParticipantEntity> Participants => Set<ParticipantEntity>();

    public DbSet<ParticipantEligibleRecipientEntity> ParticipantEligibleRecipients =>
        Set<ParticipantEligibleRecipientEntity>();

    public DbSet<GiftIdeaEntity> GiftIdeas => Set<GiftIdeaEntity>();

    public DbSet<GiftIdeaTokenEntity> GiftIdeaTokens => Set<GiftIdeaTokenEntity>();

    public DbSet<GiftIdeaAskEntity> GiftIdeaAsks => Set<GiftIdeaAskEntity>();

    public DbSet<GiftIdeaEnquiryEntity> GiftIdeaEnquiries => Set<GiftIdeaEnquiryEntity>();

    public DbSet<ContributedGiftIdeaEntity> ContributedGiftIdeas => Set<ContributedGiftIdeaEntity>();

    public DbSet<ParticipantEmailDeliveryEntity> ParticipantEmailDeliveries =>
        Set<ParticipantEmailDeliveryEntity>();

    public DbSet<ParticipantLeaveTokenEntity> ParticipantLeaveTokens =>
        Set<ParticipantLeaveTokenEntity>();

    public DbSet<DoNotAddToExchangeEntity> DoNotAddToExchange => Set<DoNotAddToExchangeEntity>();

    public DbSet<DoNotAddByOrganizerEntity> DoNotAddByOrganizer => Set<DoNotAddByOrganizerEntity>();

    public DbSet<DoNotAddAnywhereEntity> DoNotAddAnywhere => Set<DoNotAddAnywhereEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GiftExchangeDbContext).Assembly);
}
