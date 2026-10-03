namespace GiftExchange.Library.Entities.Configurations;

internal class PersonEntityConfiguration : IEntityTypeConfiguration<PersonEntity>
{
    public void Configure(EntityTypeBuilder<PersonEntity> builder)
    {
        builder.ToTable("person");

        builder.HasKey(person => person.PersonId);
        builder.Property(person => person.PersonId).HasColumnName("person_id").ValueGeneratedNever();

        builder.Property(person => person.Name).HasColumnName("name").HasMaxLength(100).IsRequired();

        // Lower-cased on the way in, as person--0005 did to every row written before this. The
        // session carries the address lower-cased, and an organizer types it however they like, so
        // without this "Sam@Example.com" added to an exchange and "sam@example.com" signing in were
        // two people under a case-sensitive unique index.
        //
        // A converter rather than a call at each write, because EF also runs it over every value
        // compared against the column: a lookup by whatever spelling a request carried finds the
        // row, and seeks uq_person_email to do it, without each query having to remember to.
        builder.Property(person => person.Email)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired()
            .HasConversion(email => email.ToNormalizedEmail(), email => email);

        // No navigation behind it, for the reason every other id in this schema has none: EF would
        // emit a foreign key wherever the provider takes one, and this codebase does not use them.
        // A self-reference is also the one shape that could not be written in a single insert if it
        // were constrained, since the row would have to exist before it could point at itself.
        builder.Property(person => person.AddedByPersonId).HasColumnName("added_by_person_id").IsRequired();

        // Email is the identity of a person, so this is the index that makes the table a directory
        // rather than a list. Every lookup starts here: a session carries an email address, and the
        // person id is found from it.
        builder
            .HasIndex(person => person.Email)
            .HasDatabaseName("uq_person_email")
            .IsUnique();

        // The row meaning "nobody". Seeded here so that every database built from this model has
        // it, which is how the test suite gets it; Liquibase seeds the real one.
        builder.HasData(NoRecord.Person());
    }
}
