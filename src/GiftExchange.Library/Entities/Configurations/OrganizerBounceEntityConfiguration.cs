namespace GiftExchange.Library.Entities.Configurations;

internal class OrganizerBounceEntityConfiguration : IEntityTypeConfiguration<OrganizerBounceEntity>
{
    public void Configure(EntityTypeBuilder<OrganizerBounceEntity> builder)
    {
        builder.ToTable("organizer_bounce");

        builder.HasKey(bounce => bounce.OrganizerBounceId);
        builder
            .Property(bounce => bounce.OrganizerBounceId)
            .HasColumnName("organizer_bounce_id")
            .ValueGeneratedNever();

        builder
            .Property(bounce => bounce.OrganizerEmailNormalized)
            .HasColumnName("organizer_email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder
            .Property(bounce => bounce.EmailNormalized)
            .HasColumnName("email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(bounce => bounce.BouncedAt).HasColumnName("bounced_at").IsRequired();

        // The organizer leads, as on uq_organizer_complaint: the only question asked of this table
        // is how many addresses have bounced one organizer's mail.
        builder
            .HasIndex(bounce => new { bounce.OrganizerEmailNormalized, bounce.EmailNormalized })
            .HasDatabaseName("uq_organizer_bounce")
            .IsUnique();
    }
}
