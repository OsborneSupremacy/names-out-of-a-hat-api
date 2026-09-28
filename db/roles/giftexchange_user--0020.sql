-- Read, append and purge on organizer_send.
--
-- DELETE, unlike organizer_complaint, because the daily sweep drops rows older than the standing
-- window, past which they judge nothing. Nothing else deletes from it: not deleting an exchange,
-- and not an organizer deleting their data. No UPDATE, because a send is a fact about a moment.
GRANT SELECT, INSERT, DELETE ON organizer_send TO giftexchange_user
