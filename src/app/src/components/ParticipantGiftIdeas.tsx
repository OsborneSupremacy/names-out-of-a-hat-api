import { useState, FormEvent } from 'react'
import {
  answerGiftIdeaAsk,
  askForGiftIdeas,
  GiftIdeaAskForYou,
  offerGiftIdeas,
  ParticipantGiftIdeas as GiftIdeas,
  shareGiftIdeas,
} from '../api'
import { formatAbsoluteTime, formatRelativeTime } from '../relativeTime'
import { AskForGiftIdeasModal } from './AskForGiftIdeasModal'
import { GIFT_IDEAS_MAX_LENGTH, OfferGiftIdeasModal } from './OfferGiftIdeasModal'
import './ParticipantGiftIdeas.css'

interface ParticipantGiftIdeasProps {
  userEmail: string
  hatId: string
  /** Empty when the caller has not drawn anybody, which hides everything about a pick. */
  pickName: string
  giftIdeas: GiftIdeas
  /** Reads the exchange again, so what was just shared shows up where it belongs. */
  onChanged: () => Promise<void>
}

/**
 * Everything the gift ideas buttons in an email do, on the participant's own page, and everything
 * those emails have told them so far.
 *
 * Four panels, in the order somebody is likeliest to want them: what they would like themselves,
 * what is known about the person they are shopping for, what other people have asked them, and
 * ideas about anybody else. The last two only appear when there is something in them or something
 * to do, so a quiet exchange shows two panels rather than four.
 *
 * Nothing written by another participant is ever turned into a link here, for the reason the
 * emails do not: the reader should see where an address goes rather than words wrapped around it.
 * React escapes text, and nothing below asks it not to.
 */
export function ParticipantGiftIdeas({ userEmail, hatId, pickName, giftIdeas, onChanged }: ParticipantGiftIdeasProps) {
  const [showAsk, setShowAsk] = useState(false)
  const [showOffer, setShowOffer] = useState(false)

  const showAsksForYou = giftIdeas.asksForYou.length > 0
  const showOffers = giftIdeas.yourOffers.length > 0 || (giftIdeas.canShare && giftIdeas.offerCandidates.length > 0)

  return (
    <div className="gift-ideas-section">
      <h3>Gift Ideas</h3>

      <YourIdeasPanel userEmail={userEmail} hatId={hatId} giftIdeas={giftIdeas} onChanged={onChanged} />

      {pickName && (
        <section className="info-card gift-ideas-panel" aria-labelledby="gift-ideas-for-pick">
          <div className="gift-ideas-panel-header">
            <h4 id="gift-ideas-for-pick">Ideas for {pickName}</h4>
            {giftIdeas.canAsk && giftIdeas.askCandidates.length > 0 && (
              <button type="button" className="secondary-button gift-ideas-small-button" onClick={() => setShowAsk(true)}>
                Ask for Gift Ideas
              </button>
            )}
          </div>

          {giftIdeas.fromYourPick ? (
            <Ideas
              heading={`In ${pickName}'s own words`}
              ideas={giftIdeas.fromYourPick}
              sharedAt={giftIdeas.fromYourPickSharedAt}
            />
          ) : (
            <p className="text-muted gift-ideas-empty">{pickName} hasn't shared any gift ideas with you yet.</p>
          )}

          {giftIdeas.aboutYourPick.map((suggestion, index) => (
            <Ideas
              key={`${suggestion.from}-${index}`}
              heading={suggestion.wasAskedFor ? `From ${suggestion.from}, who you asked` : `From ${suggestion.from}`}
              ideas={suggestion.ideas}
              sharedAt={suggestion.sharedAt}
            />
          ))}

          {giftIdeas.askedAboutYourPick.some((helper) => !helper.hasAnswered) && (
            <p className="gift-ideas-hint">
              Still waiting to hear from{' '}
              {giftIdeas.askedAboutYourPick
                .filter((helper) => !helper.hasAnswered)
                .map((helper) => helper.name)
                .join(', ')}
              .
            </p>
          )}
        </section>
      )}

      {showAsksForYou && (
        <section className="info-card gift-ideas-panel" aria-labelledby="gift-ideas-asked-of-you">
          <h4 id="gift-ideas-asked-of-you">Asked of You</h4>
          <p className="gift-ideas-hint">
            Somebody shopping for these people would like your ideas. We haven't told you who, and we
            won't. <strong>They'll see the ideas came from you</strong>; nobody else will.
          </p>
          {giftIdeas.asksForYou.map((ask) => (
            <AnswerForm
              key={ask.askId}
              userEmail={userEmail}
              hatId={hatId}
              ask={ask}
              canShare={giftIdeas.canShare}
              onChanged={onChanged}
            />
          ))}
        </section>
      )}

      {showOffers && (
        <section className="info-card gift-ideas-panel" aria-labelledby="gift-ideas-about-others">
          <div className="gift-ideas-panel-header">
            <h4 id="gift-ideas-about-others">Ideas About Someone Else</h4>
            {giftIdeas.canShare && giftIdeas.offerCandidates.length > 0 && (
              <button type="button" className="secondary-button gift-ideas-small-button" onClick={() => setShowOffer(true)}>
                Share Ideas
              </button>
            )}
          </div>

          {giftIdeas.yourOffers.length === 0 ? (
            <p className="text-muted gift-ideas-empty">
              Know what somebody else in the exchange would like? Share it with whoever is shopping for them.
            </p>
          ) : (
            giftIdeas.yourOffers.map((offer) => (
              <Ideas
                key={offer.subjectName}
                heading={`What you said about ${offer.subjectName}`}
                ideas={offer.ideas}
                sharedAt={offer.sharedAt}
              />
            ))
          )}
        </section>
      )}

      {showAsk && (
        <AskForGiftIdeasModal
          pickName={pickName}
          candidates={giftIdeas.askCandidates}
          onClose={() => setShowAsk(false)}
          onSubmit={async (participantIds, question) => {
            const result = await askForGiftIdeas(userEmail, hatId, participantIds, question)
            await onChanged()
            return result
          }}
        />
      )}

      {showOffer && (
        <OfferGiftIdeasModal
          candidates={giftIdeas.offerCandidates}
          onClose={() => setShowOffer(false)}
          onSubmit={async (subjectId, ideas) => {
            await offerGiftIdeas(userEmail, hatId, subjectId, ideas)
            await onChanged()
          }}
        />
      )}
    </div>
  )
}

interface YourIdeasPanelProps {
  userEmail: string
  hatId: string
  giftIdeas: GiftIdeas
  onChanged: () => Promise<void>
}

/**
 * The participant's own ideas, for whoever drew them: the SHARE GIFT IDEAS button, in place.
 *
 * Starts filled with what they last wrote, so changing their mind is an edit rather than starting
 * again — and the hold box starts as they last left it, for the same reason it does on the email
 * page: a box that had unticked itself would make the next press an immediate send.
 */
function YourIdeasPanel({ userEmail, hatId, giftIdeas, onChanged }: YourIdeasPanelProps) {
  const [ideas, setIdeas] = useState(giftIdeas.yourIdeas)
  const [holdUntilAsked, setHoldUntilAsked] = useState(giftIdeas.holdUntilAsked)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [confirmation, setConfirmation] = useState('')

  const hasShared = giftIdeas.yourIdeas !== ''
  const unchanged = ideas.trim() === giftIdeas.yourIdeas && holdUntilAsked === giftIdeas.holdUntilAsked

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    setError('')
    setConfirmation('')
    setIsSubmitting(true)

    try {
      await shareGiftIdeas(userEmail, hatId, ideas, holdUntilAsked)
      await onChanged()
      // The same words whether or not anybody has asked: telling the writer that held ideas went
      // straight out would tell them their giver had been asking about them.
      setConfirmation(
        holdUntilAsked
          ? "Saved. We'll pass these on to the person who picked your name only if they ask for gift ideas."
          : 'Shared with the person who picked your name.'
      )
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to share your gift ideas')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <section className="info-card gift-ideas-panel" aria-labelledby="gift-ideas-yours">
      <h4 id="gift-ideas-yours">Your Gift Ideas</h4>
      <p className="gift-ideas-hint">
        Your ideas go to the person who picked your name, and <strong>nobody else</strong> — not the
        organizer, and nobody else in the exchange.
      </p>

      {giftIdeas.canShare ? (
        <form onSubmit={handleSubmit}>
          <textarea
            aria-label="Your gift ideas"
            className="gift-ideas-textarea"
            value={ideas}
            onChange={(e) => {
              setIdeas(e.target.value)
              setConfirmation('')
            }}
            maxLength={GIFT_IDEAS_MAX_LENGTH}
            rows={6}
            placeholder="What would you like? Links are welcome."
            disabled={isSubmitting}
          />

          <label className="gift-ideas-choice gift-ideas-hold">
            <input
              type="checkbox"
              checked={holdUntilAsked}
              onChange={(e) => {
                setHoldUntilAsked(e.target.checked)
                setConfirmation('')
              }}
              disabled={isSubmitting}
            />
            <span>
              Only share if the person who has my name asks
              <span className="gift-ideas-choice-hint">
                If they never ask for gift ideas, these will never be seen by anyone.
                {giftIdeas.hasSharedOutrightBefore &&
                  " You've already shared ideas once, and we can't take those back — this applies to what you send from here on."}
              </span>
            </span>
          </label>

          {error && <div className="error-text gift-ideas-error">{error}</div>}
          {confirmation && (
            <p className="gift-ideas-confirmation" role="status">
              {confirmation}
            </p>
          )}

          <div className="gift-ideas-actions">
            {hasShared && (
              <SharedWhen label={giftIdeas.holdUntilAsked ? 'Saved' : 'Last shared'} at={giftIdeas.yourIdeasSharedAt} />
            )}
            <button type="submit" className="primary-button" disabled={isSubmitting || !ideas.trim() || unchanged}>
              {isSubmitting ? 'Sharing...' : holdUntilAsked ? 'Save' : 'Share'}
            </button>
          </div>
        </form>
      ) : hasShared ? (
        <Ideas heading="What you shared" ideas={giftIdeas.yourIdeas} sharedAt={giftIdeas.yourIdeasSharedAt} />
      ) : (
        <p className="text-muted gift-ideas-empty">You didn't share any gift ideas.</p>
      )}
    </section>
  )
}

interface AnswerFormProps {
  userEmail: string
  hatId: string
  ask: GiftIdeaAskForYou
  canShare: boolean
  onChanged: () => Promise<void>
}

/** One ask put to the caller, and their answer to it: the button in the ask's email, in place. */
function AnswerForm({ userEmail, hatId, ask, canShare, onChanged }: AnswerFormProps) {
  const [ideas, setIdeas] = useState(ask.yourAnswer)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [confirmation, setConfirmation] = useState('')

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()

    setError('')
    setConfirmation('')
    setIsSubmitting(true)

    try {
      await answerGiftIdeaAsk(userEmail, hatId, ask.askId, ideas)
      await onChanged()
      setConfirmation(`Sent to the person shopping for ${ask.subjectName}.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to share your gift ideas')
    } finally {
      setIsSubmitting(false)
    }
  }

  const heading = `What would ${ask.subjectName} like?`

  if (!canShare) {
    return ask.yourAnswer ? (
      <Ideas heading={heading} ideas={ask.yourAnswer} sharedAt={ask.answeredAt} />
    ) : (
      <p className="text-muted gift-ideas-empty">{heading} You didn't answer.</p>
    )
  }

  return (
    <form className="gift-ideas-answer" onSubmit={handleSubmit}>
      <label htmlFor={`answer-${ask.askId}`} className="gift-ideas-answer-label">
        {heading} <SharedWhen label="Asked" at={ask.askedAt} />
      </label>
      <textarea
        id={`answer-${ask.askId}`}
        className="gift-ideas-textarea gift-ideas-textarea-short"
        value={ideas}
        onChange={(e) => {
          setIdeas(e.target.value)
          setConfirmation('')
        }}
        maxLength={GIFT_IDEAS_MAX_LENGTH}
        rows={3}
        disabled={isSubmitting}
      />
      {error && <div className="error-text gift-ideas-error">{error}</div>}
      {confirmation && (
        <p className="gift-ideas-confirmation" role="status">
          {confirmation}
        </p>
      )}
      <div className="gift-ideas-actions">
        {ask.yourAnswer && <SharedWhen label="You answered" at={ask.answeredAt} />}
        <button
          type="submit"
          className="primary-button"
          disabled={isSubmitting || !ideas.trim() || ideas.trim() === ask.yourAnswer}
        >
          {isSubmitting ? 'Sharing...' : 'Share'}
        </button>
      </div>
    </form>
  )
}

interface IdeasProps {
  heading: string
  ideas: string
  sharedAt: string
}

/** Somebody's words, shown as they wrote them, line breaks and all. */
function Ideas({ heading, ideas, sharedAt }: IdeasProps) {
  return (
    <div className="gift-ideas-entry">
      <div className="gift-ideas-entry-heading">
        <span>{heading}</span>
        <SharedWhen at={sharedAt} />
      </div>
      <blockquote className="gift-ideas-quote">{ideas}</blockquote>
    </div>
  )
}

/** "3 days ago", with the full date behind it. Nothing at all for the minimum date. */
function SharedWhen({ label, at }: { label?: string; at: string }) {
  const relative = formatRelativeTime(at)
  if (!relative) return null

  return (
    <span className="gift-ideas-when" title={formatAbsoluteTime(at)}>
      {label ? `${label} ${relative}` : relative}
    </span>
  )
}
