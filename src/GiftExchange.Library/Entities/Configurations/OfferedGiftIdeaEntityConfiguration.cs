namespace GiftExchange.Library.Entities.Configurations;

internal class OfferedGiftIdeaEntityConfiguration : IEntityTypeConfiguration<OfferedGiftIdeaEntity>
{
    public void Configure(EntityTypeBuilder<OfferedGiftIdeaEntity> builder)
    {
        builder.ToTable("offered_gift_idea");

        builder.HasKey(offer => offer.OfferedGiftIdeaId);

        builder
            .Property(offer => offer.OfferedGiftIdeaId)
            .HasColumnName("offered_gift_idea_id")
            .ValueGeneratedNever();

        // No relationship behind either id, for the reason given on GiftIdeaEntity.ParticipantId.
        builder
            .Property(offer => offer.AuthorParticipantId)
            .HasColumnName("author_participant_id")
            .IsRequired();

        builder
            .Property(offer => offer.SubjectParticipantId)
            .HasColumnName("subject_participant_id")
            .IsRequired();

        builder
            .Property(offer => offer.Ideas)
            .HasColumnName("ideas")
            .HasMaxLength(8000)
            .IsRequired();

        builder.Property(offer => offer.CreatedAt).HasColumnName("created_at").IsRequired();

        // Two indexes, one per role, because removing a participant has to reach what they wrote as
        // well as what was written about them. Neither is unique -- accumulating rows is the point,
        // as it is in gift_idea. Deleting a whole hat needs only the author side: both participants
        // an offer names belong to the same exchange.
        builder
            .HasIndex(offer => new { offer.AuthorParticipantId, offer.CreatedAt })
            .HasDatabaseName("idx_offered_gift_idea_author");

        builder
            .HasIndex(offer => offer.SubjectParticipantId)
            .HasDatabaseName("idx_offered_gift_idea_subject");
    }
}
