-- CRUD on offered_gift_idea, less the U.
--
-- SELECT, INSERT and DELETE only. The table is append-only, so nothing updates a row in place, and
-- it has no counterpart to gift_idea_enquiry.released_at or to the token hash RevokeGiftIdeaLinksAsync
-- overwrites -- the two reasons the earlier grants needed UPDATE. DELETE is here for the sweeps that
-- remove a participant or a whole exchange.
GRANT SELECT, INSERT, DELETE ON offered_gift_idea TO giftexchange_user
