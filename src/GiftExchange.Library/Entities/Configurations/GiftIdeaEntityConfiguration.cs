namespace GiftExchange.Library.Entities.Configurations;

internal class GiftIdeaEntityConfiguration : IEntityTypeConfiguration<GiftIdeaEntity>
{
    public void Configure(EntityTypeBuilder<GiftIdeaEntity> builder)
    {
        builder.ToTable("gift_idea");

        builder.HasKey(giftIdea => giftIdea.GiftIdeaId);
        builder.Property(giftIdea => giftIdea.GiftIdeaId).HasColumnName("gift_idea_id").ValueGeneratedNever();

        // Mapped as a plain column with no relationship behind it. See the remarks on the property:
        // a navigation would make EF emit a foreign key, and the test databases would then refuse a
        // delete that DSQL allows.
        builder.Property(giftIdea => giftIdea.ParticipantId).HasColumnName("participant_id").IsRequired();

        builder
            .Property(giftIdea => giftIdea.Ideas)
            .HasColumnName("ideas")
            .HasMaxLength(8000)
            .IsRequired();

        builder.Property(giftIdea => giftIdea.CreatedAt).HasColumnName("created_at").IsRequired();

        builder
            .Property(giftIdea => giftIdea.HoldUntilAsked)
            .HasColumnName("hold_until_asked")
            .IsRequired();

        builder
            .Property(giftIdea => giftIdea.InboundMessageId)
            .HasColumnName("inbound_message_id")
            .HasMaxLength(255)
            .IsRequired();

        // Both questions this table is asked start from a participant: their newest submission, and
        // whether they have made one at all. Not unique — accumulating rows is the point.
        builder
            .HasIndex(giftIdea => new { giftIdea.ParticipantId, giftIdea.CreatedAt })
            .HasDatabaseName("idx_gift_idea_participant");
    }
}
