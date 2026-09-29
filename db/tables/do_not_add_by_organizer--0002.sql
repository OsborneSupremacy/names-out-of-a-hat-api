-- Merges the rows that do_not_add_by_organizer--0003.sql is about to make
-- identical.
--
-- The organizer column moves from a spelling to a mailbox key, and two spellings of one inbox
-- that refused, bounced or complained about the same address would become two copies of one row,
-- which the unique index on the table does not allow. The earliest is kept: ids are UUIDv7, so
-- the lowest is the first written, and the first is when it happened.
--
-- The key is ToMailboxKey (src/GiftExchange.Library/Extensions/StringExtensions.cs) spelled in
-- SQL: the local part up to its first '+', and for gmail.com and googlemail.com without dots and
-- on gmail.com. Anything it cannot take apart is left as it was, as ToMailboxKey leaves it.
--
-- One statement, as DSQL requires.
DELETE FROM do_not_add_by_organizer AS later
WHERE EXISTS (
    SELECT 1
    FROM do_not_add_by_organizer AS earlier
    WHERE earlier.email_normalized = later.email_normalized
      AND earlier.do_not_add_by_organizer_id < later.do_not_add_by_organizer_id
      AND CASE
        WHEN position('@' IN earlier.organizer_email_normalized) = 0 OR split_part(split_part(earlier.organizer_email_normalized, '@', 1), '+', 1) = '' THEN earlier.organizer_email_normalized
        WHEN split_part(earlier.organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(earlier.organizer_email_normalized, '@', 1), '+', 1), '.', '') = '' THEN earlier.organizer_email_normalized
        WHEN split_part(earlier.organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(earlier.organizer_email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(earlier.organizer_email_normalized, '@', 1), '+', 1) || '@' || split_part(earlier.organizer_email_normalized, '@', 2)
    END = CASE
        WHEN position('@' IN later.organizer_email_normalized) = 0 OR split_part(split_part(later.organizer_email_normalized, '@', 1), '+', 1) = '' THEN later.organizer_email_normalized
        WHEN split_part(later.organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(later.organizer_email_normalized, '@', 1), '+', 1), '.', '') = '' THEN later.organizer_email_normalized
        WHEN split_part(later.organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(later.organizer_email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(later.organizer_email_normalized, '@', 1), '+', 1) || '@' || split_part(later.organizer_email_normalized, '@', 2)
    END
)
