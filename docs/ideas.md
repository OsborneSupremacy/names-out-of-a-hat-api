# Ideas

Features considered and deliberately not built yet, with enough of the reasoning to pick them up later without re-deriving it.

## Exchange date

Phase 1 is built: an organizer can give an approximate date for the exchange. It appears in the invitation, a week after it passes the organizer is emailed once asking them to close the exchange, and eighteen months after it passes the exchange is deleted. Everything below builds on that date.

### Reminders (Phase 2)

One reminder to every participant a couple of weeks before the date, repeating who they drew. The repeat is what makes it worth sending: people lose the invitation, and "who did I get again?" otherwise goes to the organizer.

Hold it back until Phase 1 has shown what it does to complaint rates. Participants never signed up for anything, so every email they didn't expect is a chance for a spam report, and complaints count against the organizer's standing and against the sending reputation. Constraints if it goes ahead:

- At most one reminder, and the organizer turns it on per exchange.
- It needs the date to be editable after invitations go out (see below), or a party that moved sends a reminder for the wrong week.
- It goes through the invitation queue with its own message type, so delivery events land on the participant row like any other message. Check the twenty-character limit on `participant_email_delivery.message_type` when naming it.

### Editing the date after invitations go out

Today the date is edited alongside the price range, which is only allowed before invitations are sent. That's fine while the only thing hanging off the date is the close prompt and the purge. Reminders change that. The likely shape is its own endpoint, like correcting an address, allowed until the exchange is closed. It shouldn't resend anything: the invitations already out keep the old date, and that's acceptable for something called approximate.

### Undeliverable notice urgency

The undeliverable-invitation email reads the same whether the exchange is in two months or in three days. Close to the date it could say so, and suggest a phone call rather than a corrected address.

### "Running it again this year?"

Most exchanges are annual. About eleven months after the date, email the organizer offering to copy last year's exchange. This works with the eighteen-month purge, not against it: the copy has to happen while the source hat still exists, and this email is a nudge to do it inside that window. It should respect the organizer's standing and be one email, never a series.

### Dashboard ordering

Sort the organizer's list into upcoming and past exchanges by date instead of by creation time. Hats without a date need a sensible place, probably with the upcoming ones.

### Warn before the purge

The purge is silent. A month before an exchange is deleted, the organizer could be told and offered an export. Worth doing if anybody ever asks where an exchange went.

### Holding the reveal until the date

Considered and rejected for now. The cool-off already stops an accidental close, and a date the organizer called approximate is a poor lock: if the party moves back a week, a date-based lock reveals the picks early. The close prompt covers the useful part.
