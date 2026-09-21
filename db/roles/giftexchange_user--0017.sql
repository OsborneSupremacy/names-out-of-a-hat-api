-- Read and append on organizer_complaint.
--
-- INSERT for the delivery event function, which writes a row per complaint, and SELECT for that
-- and for the send path, which counts them. No UPDATE, because a complaint is a fact about a
-- moment; no DELETE, because nothing in the application is meant to be able to clear one.
-- Reinstating an organizer is done by hand, as somebody with more than this role.
GRANT SELECT, INSERT ON organizer_complaint TO giftexchange_user
