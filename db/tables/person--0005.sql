-- Every address in person, lower-cased and trimmed.
--
-- The session has always carried the address this way (LoginTokenProvider.NormalizeEmail), but a
-- participant's row held it the way the organizer typed it. Under a case-sensitive unique index
-- that made "Sam@Example.com", added to an exchange, and "sam@example.com", signing in, two
-- different people -- and the one signing in could not find the exchanges the other was in.
-- PersonEntityConfiguration lower-cases every write from here on; this does the rows already
-- written.
--
-- ToNormalizedEmail (src/GiftExchange.Library/Extensions/StringExtensions.cs) spelled in SQL. It
-- uses ToLowerInvariant, and lower() follows the database's collation instead; the two agree on
-- every ASCII address, which is every address validation has let in so far.
--
-- Only the rows that change, so the statement touches as few as it can. The changeset's
-- precondition has already established that none of them lands on a spelling another row holds,
-- which uq_person_email would otherwise refuse halfway through.
--
-- The sentinel's address is the empty string, which is already its own normal form.
--
-- One statement, as DSQL requires.
UPDATE person
SET email = lower(btrim(email))
WHERE email <> lower(btrim(email))
