namespace GiftExchange.Library.Entities.Configurations;

internal class GiftIdeaAskEntityConfiguration : IEntityTypeConfiguration<GiftIdeaAskEntity>
{
    public void Configure(EntityTypeBuilder<GiftIdeaAskEntity> builder)
    {
        builder.ToTable("gift_idea_ask");

        builder.HasKey(ask => ask.GiftIdeaAskId);
        builder.Property(ask => ask.GiftIdeaAskId).HasColumnName("gift_idea_ask_id").ValueGeneratedNever();

        // Plain columns with no relationships behind them, for the reason given on
        // GiftIdeaEntity.ParticipantId: a navigation would make EF emit a foreign key, and the test
        // databases would then refuse a delete that DSQL allows.
        builder.Property(ask => ask.AskerParticipantId).HasColumnName("asker_participant_id").IsRequired();
        builder.Property(ask => ask.HelperParticipantId).HasColumnName("helper_participant_id").IsRequired();
        builder.Property(ask => ask.SubjectParticipantId).HasColumnName("subject_participant_id").IsRequired();

        builder
            .Property(ask => ask.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(ask => ask.IssuedAt).HasColumnName("issued_at").IsRequired();

        // A share link carries the token and nothing else, so this index is the whole route from a
        // link to an ask.
        builder
            .HasIndex(ask => ask.TokenHash)
            .HasDatabaseName("uq_gift_idea_ask_hash")
            .IsUnique();

        // Three indexes for one table, because removing a participant has to reach asks in all
        // three of their roles. Deleting a hat needs only the first — every ask in a hat was made
        // by a participant of that hat.
        builder
            .HasIndex(ask => ask.AskerParticipantId)
            .HasDatabaseName("idx_gift_idea_ask_asker");

        builder
            .HasIndex(ask => ask.HelperParticipantId)
            .HasDatabaseName("idx_gift_idea_ask_helper");

        builder
            .HasIndex(ask => ask.SubjectParticipantId)
            .HasDatabaseName("idx_gift_idea_ask_subject");
    }
}
