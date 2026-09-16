import { useState, FormEvent } from 'react'
import { DeleteMyDataRequest } from '../api'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './DestructiveModal.css'
import './DeleteMyDataModal.css'

interface DeleteMyDataModalProps {
  onClose: () => void
  onSubmit: (request: DeleteMyDataRequest) => Promise<void>
}

/**
 * Confirms deleting everything somebody has organized, and says what it does and does not reach.
 *
 * A sibling of {@link DeleteHatModal}, with the same order: what is lost, then that it cannot be
 * undone. The second item matters as much as the first. Somebody who is also a participant in
 * another organizer's exchange is likely to expect this to take them out of it, and it does not —
 * leaving is what the link in that invitation is for, and saying so here is the only place they
 * will find out before they are surprised.
 *
 * Forget me is ticked by default because that is what somebody reaching for this almost always
 * means. Refusing to be added again is not: it is a decision about everybody else's future
 * exchanges, and nobody should make it by not noticing a checkbox.
 */
export function DeleteMyDataModal({ onClose, onSubmit }: DeleteMyDataModalProps) {
  const [forgetMe, setForgetMe] = useState(true)
  const [doNotAddAnywhere, setDoNotAddAnywhere] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    setError('')
    setIsSubmitting(true)

    try {
      await onSubmit({ forgetMe, doNotAddAnywhere })
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete your data')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={isSubmitting ? undefined : onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Delete My Data</h2>
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
          <ul className="destructive-effects">
            <li>
              Every gift exchange you have organized will be permanently deleted, along with everyone
              in it, their gift ideas, and any names already drawn.
            </li>
            <li>
              This does not remove you from gift exchanges organized by other people. To leave one
              of those, use the link in the invitation email you received. From there you can also
              stop that organizer from adding you again.
            </li>
          </ul>

          <label className="delete-data-option">
            <input
              type="checkbox"
              checked={forgetMe}
              onChange={(e) => setForgetMe(e.target.checked)}
              disabled={isSubmitting}
            />
            <span>
              <strong>Forget me</strong>
              <span className="delete-data-option-hint">Removes your name and signs you out.</span>
            </span>
          </label>

          <label className="delete-data-option">
            <input
              type="checkbox"
              checked={doNotAddAnywhere}
              onChange={(e) => setDoNotAddAnywhere(e.target.checked)}
              disabled={isSubmitting}
            />
            <span>
              <strong>Don't let anyone add me to a gift exchange again</strong>
              <span className="delete-data-option-hint">
                Anyone who tries will be told you've asked not to be added.
              </span>
            </span>
          </label>

          <p className="destructive-warning">
            This cannot be undone. Nothing is kept, and not even the administrators of
            namesoutofahat.com can recover it. Deletion may take a minute to finish.
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
              {isSubmitting ? 'Deleting...' : 'Delete My Data'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
