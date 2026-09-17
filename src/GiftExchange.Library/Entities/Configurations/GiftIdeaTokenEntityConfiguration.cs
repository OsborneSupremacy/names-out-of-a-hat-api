namespace GiftExchange.Library.Entities.Configurations;

internal class GiftIdeaTokenEntityConfiguration : IEntityTypeConfiguration<GiftIdeaTokenEntity>
{
    public void Configure(EntityTypeBuilder<GiftIdeaTokenEntity> builder)
    {
        builder.ToTable("gift_idea_token");

        builder.HasKey(token => token.GiftIdeaTokenId);
        builder.Property(token => token.GiftIdeaTokenId).HasColumnName("gift_idea_token_id").ValueGeneratedNever();

        // No relationship behind it, for the reason given on GiftIdeaEntity.ParticipantId.
        builder.Property(token => token.ParticipantId).HasColumnName("participant_id").IsRequired();

        builder
            .Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(token => token.IssuedAt).HasColumnName("issued_at").IsRequired();

        // A share link carries the token and nothing else, so this is the one path that reaches a
        // participant without a hat id.
        builder
            .HasIndex(token => token.TokenHash)
            .HasDatabaseName("uq_gift_idea_token_hash")
            .IsUnique();

        // Several live tokens per participant is the intended state, not an oversight. An Ask has
        // to put a working SHARE GIFT IDEAS link into an email the recipient never received,
        // and only the hash of their existing token is kept, so a new one is issued alongside it
        // rather than over it — every address anyone has ever been sent keeps working.
        //
        // Not unique, therefore. It exists so the cleanup in DeleteParticipantAsync and
        // DeleteHatAsync can find a participant's tokens without a scan.
        builder
            .HasIndex(token => token.ParticipantId)
            .HasDatabaseName("idx_gift_idea_token_participant");
    }
}
