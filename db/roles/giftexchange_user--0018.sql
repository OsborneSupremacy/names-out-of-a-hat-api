-- Authorizes the exchange date sweep Lambda to connect as giftexchange_user.
--
-- The counterpart of --0013, for the function that runs once a day to prompt organizers to close
-- exchanges past their date and to delete exchanges past the retention window. Its IAM role carries
-- dsql:DbConnect, which only permits opening a connection; this decides which database role it may
-- open one as.
--
-- Without it the failure is quiet in both directions: nobody is prompted, and nothing is deleted
-- that the terms say will be.
AWS IAM GRANT giftexchange_user TO '${exchange_date_sweep_role_arn}'
