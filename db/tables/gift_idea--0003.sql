-- Every submission that predates the column was shared outright.
--
-- There was no way to hold one back before it existed: each of these rows was forwarded to whoever
-- drew the participant at the moment it arrived. FALSE is not a guess about what its writer would
-- have chosen, it is what happened to it.
--
-- Unconditional on purpose: the column was only just added, so every row is holding the NULL it
-- arrived as. One statement, as DSQL requires.
UPDATE gift_idea SET hold_until_asked = FALSE
