import { useState, FormEvent } from 'react'
// The generic modal chrome classes live alongside the create-hat modal. Worth extracting into a
// shared Modal.css if a third modal turns up.
import './CreateHatModal.css'

interface EditNameModalProps {
  currentName: string
  onClose: () => void
  onSubmit: (name: string) => Promise<void>
}

export function EditNameModal({ currentName, onClose, onSubmit }: EditNameModalProps) {
  const [name, setName] = useState(currentName)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    const trimmedName = name.trim()
    if (!trimmedName) {
      setError('Your name cannot be empty')
      return
    }

    setError('')
    setIsSubmitting(true)

    try {
      await onSubmit(trimmedName)
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update your name')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Edit Your Name</h2>
          <button className="close-button" onClick={onClose} aria-label="Close">
            ×
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label htmlFor="profileName">Your Name *</label>
            <input
              type="text"
              id="profileName"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="How participants will see you"
              autoFocus
              disabled={isSubmitting}
            />
            {error && <div className="error-text">{error}</div>}
          </div>

          {/*
            * "take part in", not "organize". A name belongs to the person rather than to one
            * exchange, so this reaches exchanges somebody else runs and you are only a participant
            * in.
            */}
          <p className="modal-note">
            This updates your name in every gift exchange you take part in, including ones somebody
            else organizes. Invitations that have already been sent will still show your old name.
          </p>

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
              {isSubmitting ? 'Saving...' : 'Save'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
