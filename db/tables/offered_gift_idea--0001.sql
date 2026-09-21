-- Ideas one participant offered about another without being asked.
--
-- The third kind of gift idea, and deliberately its own table. gift_idea holds what somebody said
-- about themselves; contributed_gift_idea holds what a third party wrote because somebody asked
-- them to. This holds what a third party wrote because they felt like it, and the difference from
-- contributed_gift_idea is not the mood behind it -- it is who the text is for.
--
-- A contribution is answered to an ask, and gift_idea_ask records the asker, so its recipient is
-- frozen the moment the ask is sent and an organizer editing picks afterwards cannot re-point it.
-- An offer has no ask, because it issues no token and so needs no row to route one; its recipient
-- is whoever holds the subject's name at the moment it is sent, resolved then and never stored.
-- There is no column here for who received it, and there should not be: it was never a fact about
-- the row.
--
-- Append-only, as the other two are. Nothing is edited and nothing is overwritten: each row is one
-- message that was sent, and an abuse report is answerable only against what was actually sent.
--
-- No inbound_message_id. The other two carry one as history from when ideas arrived by email;
-- nothing has ever arrived on this path, and a column that would hold the empty string forever
-- would be inventing a past this table does not have.
CREATE TABLE offered_gift_idea (
    offered_gift_idea_id   UUID PRIMARY KEY,
    -- Who wrote it, and who the reader is told wrote it. Attribution is the point rather than a
    -- side effect: a suggestion the reader cannot weigh is one they cannot act on.
    author_participant_id  UUID NOT NULL,
    -- Who it is about. Never told that any of this happened.
    subject_participant_id UUID NOT NULL,
    -- Length matches gift_idea.ideas and contributed_gift_idea.ideas, and the cap the application
    -- applies before the text reaches Comprehend. All three paths run the same content policy, so
    -- all three need the same room.
    ideas                  VARCHAR(8000) NOT NULL,
    created_at             TIMESTAMPTZ NOT NULL
)
