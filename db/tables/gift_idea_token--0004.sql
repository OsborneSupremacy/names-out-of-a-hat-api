-- Which tokens were existing before these columns did: one is an invitation's, and the rest were
-- issued by an Ask.
--
-- The earliest surviving token for each participant is the one their invitation carried. Every path
-- that sends an invitation starts from nothing -- the bulk issue at send deletes the participant's
-- tokens first, and an address correction revokes all of them before the resend issues its own --
-- so anything issued after that for the same participant can only have come from an Ask, and an
-- Ask's token proves nothing about whether the invitation was read.
--
-- first_used_at is -infinity for every row, which is what Npgsql writes for DateTimeOffset.MinValue.
-- Nothing recorded use before this, so the rows read as unused. The cost is that somebody who has
-- already pressed a button in a current exchange is shown the reminder once more than they needed.
--
-- One statement, as DSQL requires.
UPDATE gift_idea_token AS token
SET proves_invitation_seen = NOT EXISTS (
        SELECT 1
        FROM gift_idea_token AS earlier
        WHERE earlier.participant_id = token.participant_id
          AND earlier.issued_at < token.issued_at
    ),
    first_used_at = '-infinity'::TIMESTAMPTZ
