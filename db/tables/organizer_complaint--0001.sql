-- Somebody who marked mail from an organizer's exchange as spam, remembered against that organizer.
--
-- participant_email_delivery already says a message was complained about, but it cannot be what an
-- organizer is judged by: its rows go when the participant or the exchange does, so an organizer
-- whose invitations drew complaints could clear the record by deleting the exchange that sent them.
-- This table is the part that has to outlive that. It is written by the delivery event function at
-- the moment of the complaint, while the participant row still leads back to the organizer, and
-- nothing in the application removes it -- not deleting an exchange, and not an organizer deleting
-- their data, which would otherwise be the same way out with a different button.
--
-- Keyed by the organizer's address rather than by person_id, for the reason
-- do_not_add_by_organizer--0001.sql gives: the send path has the address and nothing else, and the
-- two tables are read together.
--
-- Both addresses are stored lower-cased and trimmed, for the reason given in
-- do_not_add_to_exchange--0001.sql.
CREATE TABLE organizer_complaint (
    organizer_complaint_id     UUID PRIMARY KEY,
    -- The organizer whose exchange sent the message, lower-cased and trimmed.
    organizer_email_normalized VARCHAR(254) NOT NULL,
    -- Who complained, lower-cased and trimmed.
    email_normalized           VARCHAR(254) NOT NULL,
    -- When SES says the complaint was made, which is what the standing window is measured against.
    complained_at              TIMESTAMPTZ NOT NULL
)
