namespace GiftExchange.Library.Entities.Configurations;

internal class OrganizerComplaintEntityConfiguration : IEntityTypeConfiguration<OrganizerComplaintEntity>
{
    public void Configure(EntityTypeBuilder<OrganizerComplaintEntity> builder)
    {
        builder.ToTable("organizer_complaint");

        builder.HasKey(complaint => complaint.OrganizerComplaintId);
        builder
            .Property(complaint => complaint.OrganizerComplaintId)
            .HasColumnName("organizer_complaint_id")
            .ValueGeneratedNever();

        builder
            .Property(complaint => complaint.OrganizerEmailNormalized)
            .HasColumnName("organizer_email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder
            .Property(complaint => complaint.EmailNormalized)
            .HasColumnName("email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(complaint => complaint.ComplainedAt).HasColumnName("complained_at").IsRequired();

        // The organizer leads, unlike the do-not-add lists: the only question asked of this table
        // is how many people have complained about one organizer.
        builder
            .HasIndex(complaint => new { complaint.OrganizerEmailNormalized, complaint.EmailNormalized })
            .HasDatabaseName("uq_organizer_complaint")
            .IsUnique();
    }
}
