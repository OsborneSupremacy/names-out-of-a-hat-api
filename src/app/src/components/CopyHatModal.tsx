import { useMemo, useState, FormEvent } from 'react'
import { Participant } from '../api'
import { displayName } from '../participantNaming'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './CopyHatModal.css'

interface CopyHatModalProps {
  sourceHatName: string
  participants: Participant[]
  onClose: () => void
  onSubmit: (newHatName: string, excludePreviousRecipients: boolean) => Promise<void>
}

/**
 * Suggests a name for the copy.
 *
 * Most of these exchanges are annual and say so in their name, so a year in the source name is
 * bumped by one. Anything else falls back to "(Copy)". Either way it is only a starting point —
 * the field is editable, and the organizer knows what this one is called.
 */
export function suggestCopyName(sourceHatName: string): string {
  const withYearIncremented = sourceHatName.replace(
    // The last standalone four-digit year in the name, so "Christmas 2025" becomes
    // "Christmas 2026" while "Secret Santa 12345" is left alone.
    /(.*)\b(19|20|21)(\d{2})\b/,
    (_match, before: string, century: string, year: string) =>
      `${before}${Number(`${century}${year}`) + 1}`
  )

  const suggestion =
    withYearIncremented === sourceHatName ? `${sourceHatName} (Copy)` : withYearIncremented

  // The name is capped at 50 characters server-side, and a suggestion that fails validation on
  // arrival would be a strange way to open the dialog.
  return suggestion.length <= 50 ? suggestion : suggestion.slice(0, 50).trimEnd()
}

/**
 * Starts the next gift exchange from a revealed one: same people, same rules, nobody drawn yet.
 */
export function CopyHatModal({
  sourceHatName,
  participants,
  onClose,
  onSubmit,
}: CopyHatModalProps) {
  const [hatName, setHatName] = useState(() => suggestCopyName(sourceHatName))
  const [excludePreviousRecipients, setExcludePreviousRecipients] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')

  // Whoever would be left with nobody to draw. The copy is still allowed — the organizer can add
  // people or loosen the rules afterwards — but finding out here beats finding out at the shake.
  const strandedParticipants = useMemo(() => {
    if (!excludePreviousRecipients) {
      return []
    }

    // By address, because two participants may share a name.
    const people = participants.map((participant) => participant.person)

    return participants
      .filter(
        (participant) =>
          participant.eligibleRecipients.filter(
            (recipient) => recipient.email !== participant.pickedRecipient.email
          ).length === 0
      )
      .map((participant) => displayName(participant.person, people))
  }, [participants, excludePreviousRecipients])

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    const trimmedName = hatName.trim()
    if (!trimmedName) {
      setError('Gift Exchange name cannot be empty')
      return
    }

    setError('')
    setIsSubmitting(true)

    try {
      await onSubmit(trimmedName, excludePreviousRecipients)
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to copy gift exchange')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={isSubmitting ? undefined : onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Copy Gift Exchange</h2>
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
          <p className="modal-note">
            This starts a new gift exchange with the same {participants.length}{' '}
            {participants.length === 1 ? 'person' : 'people'} and the same rules about who can draw
            whom. Nobody has drawn a name yet, and <strong>{sourceHatName}</strong> is left as it
            is.
          </p>

          <div className="form-group">
            <label htmlFor="copyHatName">New Gift Exchange Name *</label>
            <input
              type="text"
              id="copyHatName"
              value={hatName}
              onChange={(e) => setHatName(e.target.value)}
              placeholder="e.g., Family Christmas 2027"
              autoFocus
              disabled={isSubmitting}
            />
            {error && <div className="error-text">{error}</div>}
          </div>

          <label className="copy-option">
            <input
              type="checkbox"
              checked={excludePreviousRecipients}
              onChange={(e) => setExcludePreviousRecipients(e.target.checked)}
              disabled={isSubmitting}
            />
            <span>
              <strong>Don't let anyone draw the same person again</strong>
              <span className="copy-option-hint">
                Whoever somebody drew in {sourceHatName} won't be one of their options this time.
              </span>
            </span>
          </label>

          {strandedParticipants.length > 0 && (
            <p className="copy-warning">
              Heads up: {formatNames(strandedParticipants)}{' '}
              {strandedParticipants.length === 1 ? 'has' : 'have'} nobody left to draw with this
              turned on. You can still copy the exchange and sort it out afterwards, by adding
              people or by editing who they're allowed to draw.
            </p>
          )}

          <div className="modal-actions">
            <button
              type="button"
              className="secondary-button"
              onClick={onClose}
              disabled={isSubmitting}
            >
              Cancel
            </button>
            <button type="submit" className="primary-button" disabled={isSubmitting}>
              {isSubmitting ? 'Copying...' : 'Create Copy'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

function formatNames(names: string[]): string {
  if (names.length === 1) return names[0]
  if (names.length === 2) return `${names[0]} and ${names[1]}`
  return `${names.slice(0, -1).join(', ')}, and ${names[names.length - 1]}`
}
