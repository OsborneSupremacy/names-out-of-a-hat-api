-- Withdraws the inbound mail Lambda's permission to connect as giftexchange_user.
--
-- The counterpart of --0008. Gift ideas are shared from a page served by the application function
-- now, and the inbound mail function and its IAM role are gone. A grant to an ARN that no longer
-- names a role authorises nothing today, but IAM role ARNs are reusable by name, so a role created
-- later with the same name would inherit it.
AWS IAM REVOKE giftexchange_user FROM '${inbound_mail_role_arn}'
