import { useState, FormEvent } from 'react'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './DestructiveModal.css'
import './ParticipantGiftIdeas.css'

interface LeaveGiftExchangeModalProps {
  hatName: string
  organizerName: string
  /**
   * False once the exchange has cooled off or closed, when nobody is sent back to the hat. The
   * dialog says what leaving costs only where it costs it.
   */
  showsConsequences: boolean
  onClose: () => void
  onSubmit: (blockOrganizer: boolean, blockAnywhere: boolean) => Promise<void>
}

/**
 * Confirms leaving, and offers the two wider refusals the leave page in an invitation offers.
 *
 * Both boxes start unticked, as they do on that page. They only ever add a refusal, and nothing in
 * the application can take one away again, so they are something to choose rather than something
 * to notice and untick.
 */
export function LeaveGiftExchangeModal({
  hatName,
  organizerName,
  showsConsequences,
  onClose,
  onSubmit,
}: LeaveGiftExchangeModalProps) {
  const [blockOrganizer, setBlockOrganizer] = useState(false)
  const [blockAnywhere, setBlockAnywhere] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    setError('')
    setIsSubmitting(true)

    try {
      await onSubmit(blockOrganizer, blockAnywhere)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to leave the gift exchange')
      setIsSubmitting(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={isSubmitting ? undefined : onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Leave Gift Exchange?</h2>
          <button className="close-button" onClick={onClose} aria-label="Close" disabled={isSubmitting}>
            ×
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          <p className="modal-note">
            You're about to leave <strong>{hatName}</strong>.
          </p>

          <ul className="destructive-effects">
            {showsConsequences ? (
              <>
                <li>
                  {organizerName} will be told that you left, and will need to draw names again, so
                  everybody still in the exchange gets a new name.
                </li>
                <li>Everybody else will be told that somebody left, but not who.</li>
              </>
            ) : (
              <li>The exchange has already finished, so nothing changes for anybody else.</li>
            )}
            <li>You can't be added back to this gift exchange.</li>
          </ul>

          <fieldset className="gift-ideas-choices" disabled={isSubmitting}>
            <legend>While you're here</legend>
            <p className="gift-ideas-hint">
              Neither of these is required. Both last indefinitely, and there's no way to undo them —
              the way back in is for somebody to ask you first.
            </p>
            <label className="gift-ideas-choice">
              <input
                type="checkbox"
                checked={blockOrganizer}
                onChange={(e) => setBlockOrganizer(e.target.checked)}
              />
              <span>Don't let {organizerName} add me to gift exchanges again</span>
            </label>
            <label className="gift-ideas-choice">
              <input
                type="checkbox"
                checked={blockAnywhere}
                onChange={(e) => setBlockAnywhere(e.target.checked)}
              />
              <span>Don't let anyone add me to a gift exchange</span>
            </label>
          </fieldset>

          {error && <div className="error-text destructive-error">{error}</div>}

          <div className="modal-actions">
            <button type="button" className="secondary-button" onClick={onClose} disabled={isSubmitting}>
              Stay In
            </button>
            <button type="submit" className="danger-button" disabled={isSubmitting}>
              {isSubmitting ? 'Leaving...' : 'Leave Gift Exchange'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
