-- One address an organizer's exchange sent mail to, remembered against that organizer.
--
-- Three jobs, all of which need a record that outlives the exchange. It is what the send path
-- counts to cap how many distinct people one organizer can mail. It is how a complaint or a bounce
-- finds its organizer: SES events name a participant, and the participant row goes when the
-- exchange does, so an organizer who sent and then deleted used to leave every later complaint with
-- nobody to answer for it. And it is the denominator a bounce rate needs, which nothing else keeps
-- once the delivery rows have gone with their exchange.
--
-- Nothing in the application removes a row early -- not deleting an exchange, and not an organizer
-- deleting their data, for the reason organizer_complaint--0001.sql gives. The daily sweep drops
-- rows older than the standing window, past which they judge nothing and there is no reason to hold
-- somebody's address.
--
-- The organizer's own address is never written. They are a participant of their own exchange and
-- receive an invitation like everybody else, but mailing yourself is not reach.
--
-- Not unique on participant_id: correcting an address resends to the same participant at a new
-- address, and that is a second recipient.
--
-- Both addresses are stored lower-cased and trimmed, for the reason given in
-- do_not_add_to_exchange--0001.sql.
CREATE TABLE organizer_send (
    organizer_send_id          UUID PRIMARY KEY,
    -- The participant the message was addressed to, as tagged on the send.
    participant_id             UUID NOT NULL,
    -- The organizer whose exchange sent the message, lower-cased and trimmed.
    organizer_email_normalized VARCHAR(254) NOT NULL,
    -- Who it was sent to, lower-cased and trimmed.
    email_normalized           VARCHAR(254) NOT NULL,
    -- When it was queued. What both the send limit and the standing window are measured against.
    sent_at                    TIMESTAMPTZ NOT NULL
)
