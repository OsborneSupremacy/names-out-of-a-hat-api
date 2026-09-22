-- Authorizes the undeliverable-invitations Lambda to connect as giftexchange_user.
--
-- Missing since the function was added. It reads one hat and its participants' delivery rows, and
-- its IAM role carries dsql:DbConnect, which only permits opening a connection; this decides which
-- database role it may open one as. Without it every check failed to connect, the handler swallowed
-- the exception by design, and no organizer was ever told an invitation bounced.
AWS IAM GRANT giftexchange_user TO '${undeliverable_invitations_role_arn}'
