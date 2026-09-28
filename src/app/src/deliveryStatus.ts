/**
 * User-facing labels for what SES said happened to a participant's email.
 *
 * The wire values are the server's vocabulary and stay as they are; this is the only place the UI
 * decides what to call them, for the reason hatStatus.ts gives.
 *
 * The empty status is the one that matters most to get right. It means nothing has been heard yet
 * — which is the state for the first seconds after a send, and the permanent state for a message
 * whose events were lost — and it is emphatically not "this person did not receive it". An
 * organizer who reads it as the latter goes and pesters somebody who is holding their invitation,
 * so the label says what is true about our knowledge rather than about the participant.
 */
const DELIVERY_STATUS_LABELS: Record<string, string> = {
  '': 'No confirmation yet',
  SENT: 'Sent',
  DELAYED: 'Delayed, still trying',
  DELIVERED: 'Delivered',
  COMPLAINED: 'Marked as spam',
  REJECTED: 'Rejected',
  FAILED: 'Failed to send',
  BOUNCED: 'Bounced',
}

/**
 * What to call each of the things this application sends, when saying which one a status is about.
 *
 * An exchange sends more than one message to the same person and the status shown is the newest of
 * them, so an organizer reading "Delivered" beside somebody who says they never saw their
 * invitation is owed the answer to "delivered what". After an exchange closes that word is often
 * about the announcement, sent weeks after the invitation that went astray.
 *
 * INVITATION and COMPLETION borrow the words EditAddressModal already uses for the same two
 * emails. The two leave notices are named as one thing on purpose: they are the same event told to
 * different people, and the distinction between them is ours rather than the reader's.
 *
 * UNSPECIFIED is deliberately absent. It means a send reached SES without a type tag, which is a
 * mistake in this codebase rather than a fact about the participant, and naming it in the table
 * would ask an organizer to make sense of it.
 */
const MESSAGE_TYPE_LABELS: Record<string, string> = {
  INVITATION: 'Invitation',
  COMPLETION: 'Announcement',
  PARTICIPANT_LEFT: 'Someone-left notice',
  ORGANIZER_LEFT_NOTE: 'Someone-left notice',
}

/** The empty string for anything unnamed above, which is the signal to render nothing. */
export function formatDeliveryMessageType(messageType: string): string {
  return MESSAGE_TYPE_LABELS[messageType] ?? ''
}

/**
 * Which statuses the organizer can actually do something about.
 *
 * Only these show the detail underneath. For the rest the detail is either empty or says nothing
 * an organizer would act on, and a row of explanations nobody needs would bury the one that
 * matters.
 */
const ACTIONABLE_STATUSES = ['BOUNCED', 'REJECTED', 'FAILED']

/**
 * Whether this status says the message is known not to have arrived.
 *
 * The same three statuses as {@link ACTIONABLE_STATUSES}, and the same set the server refuses a
 * copy over. COMPLAINED is not among them: somebody who marked a message as spam received it.
 */
export function isUndeliverable(status: string): boolean {
  return ACTIONABLE_STATUSES.includes(status)
}

/** Grouped for colour: good, waiting, or wrong. */
export type DeliveryTone = 'good' | 'neutral' | 'bad'

export function deliveryTone(status: string): DeliveryTone {
  if (status === 'DELIVERED') return 'good'
  if (ACTIONABLE_STATUSES.includes(status) || status === 'COMPLAINED') return 'bad'
  return 'neutral'
}

export function formatDeliveryStatus(status: string): string {
  return (
    DELIVERY_STATUS_LABELS[status] ??
    status
      .split('_')
      .map((word) => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
      .join(' ')
  )
}

/**
 * Whether the reason underneath is worth showing.
 *
 * The detail itself is written by whatever mail server rejected the message — it passes through no
 * moderation on the way here, ours or anybody's. Rendering it as React text rather than as markup
 * is what keeps that safe.
 */
export function showsDeliveryDetail(status: string, detail: string): boolean {
  return detail.length > 0 && ACTIONABLE_STATUSES.includes(status)
}
