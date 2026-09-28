-- Read and append on organizer_bounce.
--
-- Granted as organizer_complaint is, for the same reasons: INSERT for the delivery event function,
-- SELECT for that and for the send path, no UPDATE because a bounce is a fact about a moment, and
-- no DELETE because nothing in the application is meant to be able to clear one. Reinstating an
-- organizer is done by hand, as somebody with more than this role.
GRANT SELECT, INSERT ON organizer_bounce TO giftexchange_user
