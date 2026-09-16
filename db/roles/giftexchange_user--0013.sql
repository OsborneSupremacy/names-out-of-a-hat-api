-- Authorizes the data deletion Lambda to connect as giftexchange_user.
--
-- The counterpart of --0011, for the function that drains the data deletion queue. Somebody asking
-- to delete their data is answered by the API function, which queues the work; this one does it,
-- deleting their exchanges one transaction at a time. Its IAM role carries dsql:DbConnect, which
-- only permits opening a connection; this decides which database role it may open one as.
--
-- Without it the failure is the worst kind for this feature: the person has been told their data is
-- being deleted, and every message ends on the dead-letter queue with nothing deleted at all.
AWS IAM GRANT giftexchange_user TO '${data_deletion_role_arn}'
