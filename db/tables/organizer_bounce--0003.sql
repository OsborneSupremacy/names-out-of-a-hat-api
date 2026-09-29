-- Rewrites the organizer column of organizer_bounce from a spelling to a mailbox key.
--
-- Every question asked of this table is about an organizer, and an organizer is an inbox rather
-- than one way of writing it. Keyed by spelling, me+1@gmail.com and me+2@gmail.com were two
-- organizers with two sets of limits and two clean records. From here the application writes and
-- reads the key, and this brings the rows already here into line with it.
--
-- The key is ToMailboxKey (src/GiftExchange.Library/Extensions/StringExtensions.cs) spelled in
-- SQL: the local part up to its first '+', and for gmail.com and googlemail.com without dots and
-- on gmail.com. Anything it cannot take apart is left as it was, as ToMailboxKey leaves it.
--
-- One statement, as DSQL requires.
UPDATE organizer_bounce
SET organizer_email_normalized = CASE
        WHEN position('@' IN organizer_email_normalized) = 0 OR split_part(split_part(organizer_email_normalized, '@', 1), '+', 1) = '' THEN organizer_email_normalized
        WHEN split_part(organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(organizer_email_normalized, '@', 1), '+', 1), '.', '') = '' THEN organizer_email_normalized
        WHEN split_part(organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(organizer_email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(organizer_email_normalized, '@', 1), '+', 1) || '@' || split_part(organizer_email_normalized, '@', 2)
    END
WHERE organizer_email_normalized <> CASE
        WHEN position('@' IN organizer_email_normalized) = 0 OR split_part(split_part(organizer_email_normalized, '@', 1), '+', 1) = '' THEN organizer_email_normalized
        WHEN split_part(organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') AND replace(split_part(split_part(organizer_email_normalized, '@', 1), '+', 1), '.', '') = '' THEN organizer_email_normalized
        WHEN split_part(organizer_email_normalized, '@', 2) IN ('gmail.com', 'googlemail.com') THEN replace(split_part(split_part(organizer_email_normalized, '@', 1), '+', 1), '.', '') || '@gmail.com'
        ELSE split_part(split_part(organizer_email_normalized, '@', 1), '+', 1) || '@' || split_part(organizer_email_normalized, '@', 2)
    END
