namespace GiftExchange.Library.Entities.Configurations;

internal class OrganizerSendEntityConfiguration : IEntityTypeConfiguration<OrganizerSendEntity>
{
    public void Configure(EntityTypeBuilder<OrganizerSendEntity> builder)
    {
        builder.ToTable("organizer_send");

        builder.HasKey(send => send.OrganizerSendId);
        builder
            .Property(send => send.OrganizerSendId)
            .HasColumnName("organizer_send_id")
            .ValueGeneratedNever();

        builder.Property(send => send.ParticipantId).HasColumnName("participant_id").IsRequired();

        builder
            .Property(send => send.OrganizerEmailNormalized)
            .HasColumnName("organizer_email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder
            .Property(send => send.EmailNormalized)
            .HasColumnName("email_normalized")
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(send => send.SentAt).HasColumnName("sent_at").IsRequired();

        // How many people one organizer has mailed inside a window: the send limit and the
        // standing check.
        builder
            .HasIndex(send => new { send.OrganizerEmailNormalized, send.SentAt })
            .HasDatabaseName("idx_organizer_send_organizer");

        // Which organizer a complaint or a bounce belongs to, once the participant row is gone.
        builder
            .HasIndex(send => send.ParticipantId)
            .HasDatabaseName("idx_organizer_send_participant");

        // The daily sweep's purge by age.
        builder
            .HasIndex(send => send.SentAt)
            .HasDatabaseName("idx_organizer_send_sent_at");
    }
}
