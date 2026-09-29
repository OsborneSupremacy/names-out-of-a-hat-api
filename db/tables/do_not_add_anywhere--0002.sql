-- Merges the rows that do_not_add_anywhere--0003.sql is about to make
-- identical: two spellings of one inbox refusing the same thing. The earliest is kept.
--
-- The key is ToMailboxKey (src/GiftExchange.Library/Extensions/StringExtensions.cs) spelled in
-- SQL, as in organizer_send--0002.sql.
--
-- One statement, as DSQL requires.
DELETE FROM do_not_add_anywhere AS later
WHERE EXISTS (
    SELECT 1
    FROM do_not_add_anywhere AS earlier
    WHERE earlier.do_not_add_anywhere_id < later.do_not_add_anywhere_id
      AND CASE
        WHEN position('@' IN earlier.email_normalized) = 0 OR split_part(split_part(earlier.email_normalized, '@', 1), '+', 1) = '' THEN earlier.email_normalized
        WHEN split_part(earlier.email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(earlier.email_normalized, '@', 1), '+', 1), '.', '') = '' THEN earlier.email_normalized
        WHEN split_part(earlier.email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(earlier.email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(earlier.email_normalized, '@', 1), '+', 1) || '@' || split_part(earlier.email_normalized, '@', 2)
    END = CASE
        WHEN position('@' IN later.email_normalized) = 0 OR split_part(split_part(later.email_normalized, '@', 1), '+', 1) = '' THEN later.email_normalized
        WHEN split_part(later.email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(later.email_normalized, '@', 1), '+', 1), '.', '') = '' THEN later.email_normalized
        WHEN split_part(later.email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(later.email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(later.email_normalized, '@', 1), '+', 1) || '@' || split_part(later.email_normalized, '@', 2)
    END
)
