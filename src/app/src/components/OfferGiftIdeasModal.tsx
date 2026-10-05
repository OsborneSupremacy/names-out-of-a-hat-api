import { useState, FormEvent } from 'react'
import { OfferCandidate } from '../api'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './ParticipantGiftIdeas.css'

/** The server refuses longer; see GiftIdeaContentPolicy.MaxLength. */
export const GIFT_IDEAS_MAX_LENGTH = 2000

interface OfferGiftIdeasModalProps {
  candidates: OfferCandidate[]
  onClose: () => void
  onSubmit: (subjectParticipantId: string, ideas: string) => Promise<void>
}

/**
 * Offering gift ideas about somebody else in the exchange, unasked.
 *
 * The confirmation says the same thing whether or not the ideas went anywhere, as the email's page
 * does. Somebody who offered ideas about each participant in turn and watched for a difference
 * would otherwise learn who has nobody shopping for them, which is a piece of the draw.
 */
export function OfferGiftIdeasModal({ candidates, onClose, onSubmit }: OfferGiftIdeasModalProps) {
  const [subjectId, setSubjectId] = useState('')
  const [ideas, setIdeas] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [sharedAbout, setSharedAbout] = useState<string | null>(null)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    if (!subjectId) {
      setError('Choose who these ideas are about.')
      return
    }

    setError('')
    setIsSubmitting(true)

    try {
      await onSubmit(subjectId, ideas)
      setSharedAbout(candidates.find((candidate) => candidate.participantId === subjectId)?.name ?? '')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to share your gift ideas')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={isSubmitting ? undefined : onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Share Ideas About Someone Else</h2>
          <button className="close-button" onClick={onClose} aria-label="Close" disabled={isSubmitting}>
            ×
          </button>
        </div>

        {sharedAbout !== null ? (
          <div className="gift-ideas-results">
            <p>
              Thanks. Your ideas go to the person shopping for {sharedAbout} and to nobody else — not{' '}
              {sharedAbout}, and not the organizer. They'll see the ideas came from you. We won't tell you
              who they are.
            </p>
            <div className="modal-actions">
              <button type="button" className="primary-button" onClick={onClose}>
                Done
              </button>
            </div>
          </div>
        ) : (
          <form onSubmit={handleSubmit}>
            <p className="modal-note">
              If you know what somebody in the exchange would like, we'll pass your ideas to whoever is
              shopping for them. <strong>They'll see the ideas came from you.</strong> We won't tell you
              who they are, and we won't tell the person your ideas are about.
            </p>

            <fieldset className="gift-ideas-choices" disabled={isSubmitting}>
              <legend>Who are these ideas about?</legend>
              {candidates.map((candidate) => (
                <label className="gift-ideas-choice" key={candidate.participantId}>
                  <input
                    type="radio"
                    name="offer-subject"
                    value={candidate.participantId}
                    checked={subjectId === candidate.participantId}
                    onChange={() => setSubjectId(candidate.participantId)}
                  />
                  <span>{candidate.name}</span>
                </label>
              ))}
              {/* Said once, under the list, rather than against the missing name: spelling out who
                  has been left out would name them on a screen somebody may be reading over a
                  shoulder. */}
              <p className="gift-ideas-hint">The person whose name you drew isn't on this list.</p>
            </fieldset>

            <div className="form-group">
              <label htmlFor="offer-ideas">What do you think they'd like?</label>
              <textarea
                id="offer-ideas"
                className="gift-ideas-textarea"
                value={ideas}
                onChange={(e) => setIdeas(e.target.value)}
                maxLength={GIFT_IDEAS_MAX_LENGTH}
                rows={6}
                disabled={isSubmitting}
              />
              <p className="gift-ideas-hint">
                Links are welcome — paste the full web address rather than a shortened one. Please don't
                mention the name of the person you drew.
              </p>
            </div>

            {error && <div className="error-text gift-ideas-error">{error}</div>}

            <div className="modal-actions">
              <button type="button" className="secondary-button" onClick={onClose} disabled={isSubmitting}>
                Cancel
              </button>
              <button type="submit" className="primary-button" disabled={isSubmitting || !ideas.trim()}>
                {isSubmitting ? 'Sharing...' : 'Share'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}
