-- An address that hard-bounced mail from an organizer's exchange, remembered against that
-- organizer.
--
-- The counterpart of organizer_complaint, for the other signal SES judges an account by. A bounce
-- rate above 5% puts the sending account under review and one above 10% can pause it, and an
-- organizer mailing a list they did not collect gets there faster than one attracting complaints:
-- most of a bought list does not exist. participant_email_delivery records the same bounces, but
-- its rows go with the exchange, so this is the part that has to outlive it.
--
-- Permanent bounces only. A transient one is a full mailbox or a server having a bad minute, and
-- says nothing about whether the organizer should have been mailing that address.
--
-- Written by the delivery event function at the moment of the bounce, attributed through
-- organizer_send. Nothing in the application removes a row, for the reason
-- organizer_complaint--0001.sql gives.
--
-- Both addresses are stored lower-cased and trimmed, for the reason given in
-- do_not_add_to_exchange--0001.sql.
CREATE TABLE organizer_bounce (
    organizer_bounce_id        UUID PRIMARY KEY,
    -- The organizer whose exchange sent the message, lower-cased and trimmed.
    organizer_email_normalized VARCHAR(254) NOT NULL,
    -- The address that bounced, lower-cased and trimmed.
    email_normalized           VARCHAR(254) NOT NULL,
    -- When SES says the bounce happened, which is what the standing window is measured against.
    bounced_at                 TIMESTAMPTZ NOT NULL
)
