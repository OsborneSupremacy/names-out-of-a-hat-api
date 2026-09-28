import { useMemo, useState, FormEvent } from 'react'
import { Participant } from '../api'
import { isUndeliverable } from '../deliveryStatus'
import { displayName, formatNames } from '../participantNaming'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './DestructiveModal.css'
import './RevealHatModal.css'

interface RevealHatModalProps {
  hatName: string
  participants: Participant[]
  onClose: () => void
  onSubmit: () => Promise<void>
}

/**
 * Confirms revealing the picks, and warns first if somebody never got their invitation.
 *
 * A dialog rather than the confirm() this used to be, because the second half of that is a list of
 * names and a way to act on it, and a browser prompt can offer neither.
 *
 * Only a failed invitation is warned about, not a failed email of any kind. The warning is that
 * somebody does not know who they drew, and that is only true when the message that told them is
 * the one that came back.
 */
export function RevealHatModal({ hatName, participants, onClose, onSubmit }: RevealHatModalProps) {
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')

  const undelivered = useMemo(() => {
    const people = participants.map((participant) => participant.person)

    return participants
      .filter(
        (participant) =>
          participant.deliveryMessageType === 'INVITATION' &&
          isUndeliverable(participant.deliveryStatus)
      )
      .map((participant) => displayName(participant.person, people))
  }, [participants])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    setError('')
    setIsSubmitting(true)

    try {
      await onSubmit()
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to reveal the picked names')
    } finally {
      setIsSubmitting(false)
    }
  }

  const one = undelivered.length === 1

  return (
    <div className="modal-overlay" onClick={isSubmitting ? undefined : onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Reveal Picked Names</h2>
          <button
            className="close-button"
            onClick={onClose}
            aria-label="Close"
            disabled={isSubmitting}
          >
            ×
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          {undelivered.length > 0 && (
            <div className="reveal-undelivered" role="alert">
              <p>
                <strong>
                  {one ? 'The invitation to ' : 'The invitations to '}
                  {formatNames(undelivered)} couldn't be delivered.
                </strong>
              </p>
              <p>
                {one
                  ? "Please check their email address. If it's correct, their email provider may be refusing mail from this app, or they may have unsubscribed from it."
                  : "Please check their email addresses. If they're correct, their email providers may be refusing mail from this app, or they may have unsubscribed from it."}
              </p>
              <p>
                Since they haven't received {one ? 'their invitation' : 'their invitations'}, they
                don't know whose name they were assigned. You can correct an address from the
                participant list, and the invitation will be sent again.
              </p>
            </div>
          )}

          <p className="modal-note">
            This finishes <strong>{hatName}</strong> and shows everybody who drew whom.
          </p>

          <ul className="destructive-effects">
            <li>Every participant will be emailed to say the gift exchange has finished.</li>
            <li>The email includes the full list of who picked whose name.</li>
          </ul>

          <p className="destructive-warning">
            This cannot be undone, so only do it once the gift exchange has actually happened.
            {undelivered.length > 0 && ' Are you sure you want to reveal the picked names?'}
          </p>

          {error && <div className="error-text destructive-error">{error}</div>}

          <div className="modal-actions">
            <button
              type="button"
              className="secondary-button"
              onClick={onClose}
              disabled={isSubmitting}
            >
              Cancel
            </button>
            <button type="submit" className="danger-button" disabled={isSubmitting}>
              {isSubmitting
                ? 'Revealing...'
                : undelivered.length > 0
                  ? 'Reveal Anyway'
                  : 'Reveal Picked Names'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
