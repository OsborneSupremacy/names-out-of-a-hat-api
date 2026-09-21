namespace GiftExchange.Library.Entities.Configurations;

internal class DoNotAddByOrganizerEntityConfiguration : IEntityTypeConfiguration<DoNotAddByOrganizerEntity>
{
    public void Configure(EntityTypeBuilder<DoNotAddByOrganizerEntity> builder)
    {
        builder.ToTable("do_not_add_by_organizer");

        builder.HasKey(block => block.DoNotAddByOrganizerId);
        builder
            .Property(block => block.DoNotAddByOrganizerId)
            .HasColumnName("do_not_add_by_organizer_id")
            .ValueGeneratedNever();

        builder
            .Property(block => block.OrganizerEmailNormalized)
            .HasColumnName("organizer_email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder
            .Property(block => block.EmailNormalized)
            .HasColumnName("email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(block => block.CreatedAt).HasColumnName("created_at").IsRequired();

        // As uq_do_not_add_to_exchange, with the organizer's address as the narrowing.
        builder
            .HasIndex(block => new { block.EmailNormalized, block.OrganizerEmailNormalized })
            .HasDatabaseName("uq_do_not_add_by_organizer")
            .IsUnique();

        // The other way round, for OrganizerStandingChecker: how many people have refused one
        // organizer inside a window. The unique index leads with the refusing address and cannot
        // answer that without a scan.
        builder
            .HasIndex(block => new { block.OrganizerEmailNormalized, block.CreatedAt })
            .HasDatabaseName("idx_do_not_add_by_organizer_organizer");
    }
}
