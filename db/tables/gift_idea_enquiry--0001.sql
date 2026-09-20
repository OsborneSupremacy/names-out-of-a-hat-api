-- One participant having asked for gift ideas about the person whose name they drew.
--
-- It exists for submissions held back by gift_idea.hold_until_asked, which are released to that one
-- person and only when they ask. Nothing else recorded the asking durably. Asking somebody other
-- than your own pick writes a gift_idea_ask, but asking the pick themselves wrote nothing at all --
-- a token and an email, plus a DynamoDB throttle row keyed on a different pair and expiring after a
-- week. And the order of events cannot be relied on: somebody may ask before the person whose name
-- they hold has written anything, so held ideas written afterwards need something still standing to
-- consult. That is this table.
--
-- Not a synonym of gift_idea_ask, despite sitting beside it. An ask is a message sent to a third
-- party, with its own token and its own replies; this is a standing fact about a giver and their
-- pick, which no message can carry.
--
-- The subject is a column rather than a derivation, for the reason gift_idea_ask--0001.sql gives: an
-- organizer editing picks after the fact must not silently re-point it. A stale row naming a giver
-- who no longer holds that name matches nobody, so the ideas stay held -- which is the right answer.
--
-- What it retains is new. Who asked for gift ideas about whom used to survive a week in DynamoDB and
-- nowhere else; it now lives until the participant or the exchange is deleted, which is what makes
-- releasing a submission written long after the ask possible. Nothing here is anybody's words, and
-- both cleanup paths sweep it.
--
-- Every column is stated at CREATE, for the reason gift_idea--0001.sql gives: DSQL cannot ALTER
-- COLUMN, so a column added later can be neither defaulted nor made NOT NULL.
CREATE TABLE gift_idea_enquiry (
    gift_idea_enquiry_id   UUID PRIMARY KEY,
    -- Who asked, and so the single person anything released against this row is sent to. Always the
    -- participant who drew the subject: asking is only ever offered about your own pick.
    asker_participant_id   UUID NOT NULL,
    -- Who they asked about. Held here rather than followed back through the asker's pick later.
    subject_participant_id UUID NOT NULL,
    -- When they first asked. One row per pair, so this is the first time and not the latest: asking
    -- again changes nothing about what is owed to them.
    requested_at           TIMESTAMPTZ NOT NULL,
    -- When a held submission was last passed on against this enquiry, or the minimum timestamp if
    -- none ever has been -- absence spelled with a value, as everywhere else in this schema.
    --
    -- A release is sent when the subject's newest submission is held and was written after this
    -- stamp, which is what makes it happen once per submission rather than once per ask, and what
    -- lets a send that was dropped be retried by the next ask. Mail here cannot report failure.
    released_at            TIMESTAMPTZ NOT NULL
)
