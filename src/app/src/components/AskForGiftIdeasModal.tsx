import { useState, FormEvent } from 'react'
import { AskCandidate, AskForGiftIdeasResponse } from '../api'
import { formatAbsoluteTime } from '../relativeTime'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './ParticipantGiftIdeas.css'

/** The server refuses a longer question; see AskQuestionPolicy.MaxLength. */
const QUESTION_MAX_LENGTH = 300

/**
 * Below this many people, asking somebody other than your pick narrows the field enough that they
 * may work out who asked. The same threshold the email's ask page uses.
 */
const SMALL_EXCHANGE_THRESHOLD = 6

/** "Robin, Sam and Taylor", as the email's ask page says it (see NameFormatting.ToSentenceList). */
function toSentenceList(names: string[]): string {
  return names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

interface AskForGiftIdeasModalProps {
  pickName: string
  candidates: AskCandidate[]
  onClose: () => void
  onSubmit: (participantIds: string[], question: string) => Promise<AskForGiftIdeasResponse>
}

/**
 * Asking for gift ideas about the caller's pick, from the pick or from anybody else in the exchange.
 *
 * The same choices, in the same order and with the same warning, as the page the ASK button in an
 * invitation opens: the pick first and ticked, everybody else after, and — in a small exchange —
 * the plain statement that asking somebody else may give the asker away. That is put before the
 * names rather than under them, because a caveat read after three boxes are ticked is small print.
 *
 * What happened is shown in the dialog afterwards rather than as a toast, because the result names
 * people individually: who was asked, and who was held back by the weekly limit and since when.
 */
export function AskForGiftIdeasModal({ pickName, candidates, onClose, onSubmit }: AskForGiftIdeasModalProps) {
  const [chosen, setChosen] = useState<Set<string>>(
    () => new Set(candidates.filter((candidate) => candidate.isTheirPick).map((candidate) => candidate.participantId))
  )
  const [question, setQuestion] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<AskForGiftIdeasResponse | null>(null)

  const pick = candidates.filter((candidate) => candidate.isTheirPick)
  const others = candidates.filter((candidate) => !candidate.isTheirPick)
  // The caller is not in the list of candidates, so the exchange is one bigger than it.
  const isSmallExchange = candidates.length + 1 < SMALL_EXCHANGE_THRESHOLD

  const toggle = (participantId: string, checked: boolean) => {
    setChosen((current) => {
      const next = new Set(current)
      if (checked) next.add(participantId)
      else next.delete(participantId)
      return next
    })
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    if (chosen.size === 0) {
      setError('Choose at least one person to ask.')
      return
    }

    setError('')
    setIsSubmitting(true)

    try {
      setResult(await onSubmit([...chosen], question.trim()))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to ask for gift ideas')
    } finally {
      setIsSubmitting(false)
    }
  }

  const renderChoice = (candidate: AskCandidate, note: string) => (
    <label className="gift-ideas-choice" key={candidate.participantId}>
      <input
        type="checkbox"
        checked={chosen.has(candidate.participantId)}
        onChange={(e) => toggle(candidate.participantId, e.target.checked)}
      />
      <span>
        <strong>{candidate.name}</strong>
        <span className="gift-ideas-choice-hint">{note}</span>
      </span>
    </label>
  )

  return (
    <div className="modal-overlay" onClick={isSubmitting ? undefined : onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Gift Ideas for {pickName}</h2>
          <button className="close-button" onClick={onClose} aria-label="Close" disabled={isSubmitting}>
            ×
          </button>
        </div>

        {result ? (
          <AskResults pickName={pickName} result={result} onClose={onClose} />
        ) : (
          <form onSubmit={handleSubmit}>
            <p className="modal-note">
              Choose who to ask. <strong>Your name won't be revealed to any of them</strong>, and
              anything they share comes to your inbox and shows up on this page.
            </p>

            <fieldset className="gift-ideas-choices" disabled={isSubmitting}>
              {pick.length > 0 && (
                <>
                  <legend>Ask {pickName} directly</legend>
                  {pick.map((candidate) =>
                    renderChoice(candidate, "We'll ask what they'd like, without saying who wanted to know.")
                  )}
                </>
              )}

              {others.length > 0 && (
                <>
                  <p className="gift-ideas-choices-heading">
                    Or ask anyone else what they think {pickName} would like
                  </p>
                  {isSmallExchange && (
                    <p className="gift-ideas-warning">
                      <strong>Worth knowing in a group this size:</strong> we won't say who asked, but
                      whoever you ask knows it wasn't them and wasn't {pickName} — so in an exchange
                      this small they may well work out that it was you.
                    </p>
                  )}
                  {others.map((candidate) => renderChoice(candidate, `We'll ask for ideas about ${pickName}.`))}
                </>
              )}
            </fieldset>

            <div className="form-group">
              <label htmlFor="ask-question">
                Anything particular you'd like to know about {pickName}?{' '}
                <span className="gift-ideas-optional">optional</span>
              </label>
              <textarea
                id="ask-question"
                className="gift-ideas-textarea gift-ideas-textarea-short"
                value={question}
                onChange={(e) => setQuestion(e.target.value)}
                maxLength={QUESTION_MAX_LENGTH}
                rows={3}
                placeholder="e.g. What shirt size do they wear?"
                disabled={isSubmitting}
              />
              <p className="gift-ideas-hint">
                <strong>Be careful not to reveal who you are</strong> — don't sign it, and don't mention
                anything only you would know. Everyone you've ticked gets the same question, so write it
                about {pickName} rather than to them. No links.
              </p>
            </div>

            {error && <div className="error-text gift-ideas-error">{error}</div>}

            <p className="gift-ideas-hint">
              You can ask each person once a week, so nobody ends up being nagged. Anyone who replies
              will be named to you, so you'll know whose suggestion is whose.
            </p>

            <div className="modal-actions">
              <button type="button" className="secondary-button" onClick={onClose} disabled={isSubmitting}>
                Cancel
              </button>
              <button type="submit" className="primary-button" disabled={isSubmitting}>
                {isSubmitting ? 'Asking...' : 'Ask'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}

interface AskResultsProps {
  pickName: string
  result: AskForGiftIdeasResponse
  onClose: () => void
}

/** Who was asked, and who wasn't and why: the same report the email's ask page gives. */
function AskResults({ pickName, result, onClose }: AskResultsProps) {
  const sent = result.attempts.filter((attempt) => attempt.sent)
  const skipped = result.attempts.filter((attempt) => !attempt.sent)
  // Names arrive already told apart where two people share one, so the pick's is unique.
  const askedPick = sent.some((attempt) => attempt.name === pickName)
  const askedOthers = sent.filter((attempt) => attempt.name !== pickName).map((attempt) => attempt.name)

  return (
    <div className="gift-ideas-results">
      {result.releasedHeldIdeas && (
        <p>
          {pickName} had already written down some gift ideas, to be passed on if anyone asked. They're
          on this page now, and we've emailed them to you too.
        </p>
      )}

      {sent.length > 0 ? (
        <>
          {askedPick && <p>We've asked {pickName} what they'd like, without saying who wanted to know.</p>}
          {askedOthers.length > 0 && (
            <p>
              We've asked {toSentenceList(askedOthers)} for ideas about {pickName}, without saying who
              wanted to know.
            </p>
          )}
          <p>Anything they share will arrive in your inbox and show up on this page.</p>
        </>
      ) : (
        <p>We didn't ask anyone this time.</p>
      )}

      {skipped.length > 0 && (
        <>
          <p>We didn't ask these people, because you asked them recently:</p>
          <ul>
            {skipped.map((attempt) => (
              <li key={attempt.name}>
                {attempt.name}
                {formatAbsoluteTime(attempt.previouslyAskedAt)
                  ? ` — asked ${formatAbsoluteTime(attempt.previouslyAskedAt)}`
                  : ' — asked recently'}
              </li>
            ))}
          </ul>
          <p>You can ask each of them again after a week.</p>
        </>
      )}

      <div className="modal-actions">
        <button type="button" className="primary-button" onClick={onClose}>
          Done
        </button>
      </div>
    </div>
  )
}
