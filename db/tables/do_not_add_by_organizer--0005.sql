-- Rewrites the refused address in do_not_add_by_organizer from a spelling to a mailbox key, so a
-- refusal holds for every spelling that reaches the same inbox.
--
-- The key is ToMailboxKey (src/GiftExchange.Library/Extensions/StringExtensions.cs) spelled in
-- SQL, as in organizer_send--0002.sql.
--
-- One statement, as DSQL requires.
UPDATE do_not_add_by_organizer
SET email_normalized = CASE
        WHEN position('@' IN email_normalized) = 0 OR split_part(split_part(email_normalized, '@', 1), '+', 1) = '' THEN email_normalized
        WHEN split_part(email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(email_normalized, '@', 1), '+', 1), '.', '') = '' THEN email_normalized
        WHEN split_part(email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(email_normalized, '@', 1), '+', 1) || '@' || split_part(email_normalized, '@', 2)
    END
WHERE email_normalized <> CASE
        WHEN position('@' IN email_normalized) = 0 OR split_part(split_part(email_normalized, '@', 1), '+', 1) = '' THEN email_normalized
        WHEN split_part(email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(email_normalized, '@', 1), '+', 1), '.', '') = '' THEN email_normalized
        WHEN split_part(email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(email_normalized, '@', 1), '+', 1) || '@' || split_part(email_normalized, '@', 2)
    END
