namespace GiftExchange.Library.Entities.Configurations;

internal class GiftIdeaEnquiryEntityConfiguration : IEntityTypeConfiguration<GiftIdeaEnquiryEntity>
{
    public void Configure(EntityTypeBuilder<GiftIdeaEnquiryEntity> builder)
    {
        builder.ToTable("gift_idea_enquiry");

        builder.HasKey(enquiry => enquiry.GiftIdeaEnquiryId);

        builder
            .Property(enquiry => enquiry.GiftIdeaEnquiryId)
            .HasColumnName("gift_idea_enquiry_id")
            .ValueGeneratedNever();

        // Plain columns with no relationships behind them, for the reason given on
        // GiftIdeaEntity.ParticipantId: a navigation would make EF emit a foreign key, and the test
        // databases would then refuse a delete that DSQL allows.
        builder.Property(enquiry => enquiry.AskerParticipantId).HasColumnName("asker_participant_id").IsRequired();
        builder.Property(enquiry => enquiry.SubjectParticipantId).HasColumnName("subject_participant_id").IsRequired();

        builder.Property(enquiry => enquiry.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(enquiry => enquiry.ReleasedAt).HasColumnName("released_at").IsRequired();

        // The question both the Ask and the share page bring: has this giver asked about this
        // participant. One row per pair, because asking twice is still one standing fact.
        builder
            .HasIndex(enquiry => new { enquiry.AskerParticipantId, enquiry.SubjectParticipantId })
            .HasDatabaseName("uq_gift_idea_enquiry_asker_subject")
            .IsUnique();

        // Removing a participant has to reach the rows naming them as a subject too. No third index:
        // the asker side is the leading column of the one above, and deleting a hat filters on the
        // asker alone, since both participants an enquiry names belong to the same exchange.
        builder
            .HasIndex(enquiry => enquiry.SubjectParticipantId)
            .HasDatabaseName("idx_gift_idea_enquiry_subject");
    }
}
