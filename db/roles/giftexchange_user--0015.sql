-- CRUD on the table recording that somebody asked for gift ideas about their pick.
--
-- A separate grant rather than an edit to --0009, for the reason --0009 gives about --0007: that
-- changeset has run, and Liquibase checksums cover what it said at the time.
--
-- UPDATE is needed as well as INSERT: released_at is stamped on a row that already exists.
GRANT SELECT, INSERT, UPDATE, DELETE
    ON gift_idea_enquiry
    TO giftexchange_user
