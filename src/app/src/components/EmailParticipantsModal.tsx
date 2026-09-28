import { useEffect, useMemo, useState } from 'react'
import { Hat } from '../api'
import { isUndeliverable } from '../deliveryStatus'
import { displayName, formatNames } from '../participantNaming'
import { buildMailtoUrl, MAILTO_LENGTH_WARNING, topicsFor } from '../participantEmail'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './CopyHatModal.css'
import './EmailParticipantsModal.css'

interface EmailParticipantsModalProps {
  hat: Hat
  onClose: () => void
}

/**
 * Hands the organizer a ready-written email to send from their own address.
 *
 * Our mail comes from an address nobody has written to before, which is exactly what spam filters
 * and Gmail's tabs are suspicious of. The organizer's address is one their participants know, so a
 * message from it is the one most likely to be read — and the most useful thing it can say is where
 * to find ours.
 *
 * Nothing is sent from here. The dialog builds a mailto: link, and copies of the addresses and the
 * message for anybody whose computer has no mail app for that link to open.
 */
export function EmailParticipantsModal({ hat, onClose }: EmailParticipantsModalProps) {
  const topics = useMemo(() => topicsFor(hat), [hat])
  const [topicId, setTopicId] = useState(topics[0].id)
  const [copied, setCopied] = useState<'' | 'addresses' | 'message'>('')

  const people = hat.participants.map((participant) => participant.person)

  // The organizer is not written to, even when they are also a participant: they are the sender,
  // and the To line already gives them a copy.
  const others = hat.participants.filter(
    (participant) => !sameAddress(participant.person.email, hat.organizer.email)
  )

  // An address that has already bounced will bounce this too. The fix is Edit Address, not a
  // second email to the same place.
  const reachable = others.filter((participant) => !isUndeliverable(participant.deliveryStatus))
  const unreachableNames = others
    .filter((participant) => isUndeliverable(participant.deliveryStatus))
    .map((participant) => displayName(participant.person, people))

  const [selected, setSelected] = useState<Set<string>>(
    () => new Set(reachable.map((participant) => participant.person.email))
  )

  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [onClose])

  const topic = topics.find((candidate) => candidate.id === topicId) ?? topics[0]
  const { subject, body } = topic.compose(hat)

  const bcc = reachable
    .map((participant) => participant.person.email)
    .filter((email) => selected.has(email))

  const mailto = buildMailtoUrl({ to: [hat.organizer.email], bcc, subject, body })

  const toggle = (email: string) => {
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(email)) {
        next.delete(email)
      } else {
        next.add(email)
      }
      return next
    })
  }

  const copy = async (what: 'addresses' | 'message', text: string) => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(what)
    } catch {
      setCopied('')
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-content email-participants-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="email-participants-title"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h2 id="email-participants-title">Email Participants</h2>
          <button className="close-button" onClick={onClose} aria-label="Close">
            ×
          </button>
        </div>

        <div className="email-participants-body">
          <p className="modal-note">
            Send a message from your own email address. People are more likely to see mail from
            somebody they know than from us.
          </p>

          <fieldset className="email-participants-fieldset">
            <legend>What do you want to email your participants about?</legend>
            {topics.map((candidate) => (
              <label key={candidate.id} className="copy-option email-participants-option">
                <input
                  type="radio"
                  name="email-topic"
                  value={candidate.id}
                  checked={candidate.id === topic.id}
                  onChange={() => setTopicId(candidate.id)}
                />
                <span>{candidate.label}</span>
              </label>
            ))}
          </fieldset>

          <fieldset className="email-participants-fieldset">
            <legend>Who to send it to</legend>
            {reachable.map((participant) => (
              <label key={participant.person.email} className="copy-option email-participants-option">
                <input
                  type="checkbox"
                  checked={selected.has(participant.person.email)}
                  onChange={() => toggle(participant.person.email)}
                />
                <span>
                  {displayName(participant.person, people)}
                  <span className="copy-option-hint">{participant.person.email}</span>
                </span>
              </label>
            ))}
          </fieldset>

          {unreachableNames.length > 0 && (
            <p className="copy-warning">
              {formatNames(unreachableNames)}{' '}
              {unreachableNames.length === 1 ? "isn't" : "aren't"} on this list because email to{' '}
              {unreachableNames.length === 1 ? 'their address' : 'their addresses'} bounced. Use{' '}
              <strong>Edit Address</strong> on the participant list to fix{' '}
              {unreachableNames.length === 1 ? 'it' : 'them'}.
            </p>
          )}

          <div className="email-participants-preview" aria-label="Preview">
            <div className="email-participants-preview-subject">
              <span className="email-participants-preview-label">Subject:</span> {subject}
            </div>
            {body ? (
              <div className="email-participants-preview-text">{body}</div>
            ) : (
              <div className="email-participants-preview-text text-muted">
                You write the message.
              </div>
            )}
          </div>

          <p className="modal-note">
            Everyone goes in BCC, so nobody sees anybody else's address. You'll be in the To line,
            so you get a copy. You can change anything before you send it.
          </p>

          {mailto.length > MAILTO_LENGTH_WARNING && (
            <p className="copy-warning">
              This is a long list, and some email apps cut long links short. If people or part of the
              message are missing when your email app opens, use the copy buttons instead.
            </p>
          )}

          <div className="email-participants-copy">
            <button
              type="button"
              className="secondary-button"
              onClick={() => copy('addresses', bcc.join(', '))}
              disabled={bcc.length === 0}
            >
              {copied === 'addresses' ? 'Addresses copied' : 'Copy addresses'}
            </button>
            <button
              type="button"
              className="secondary-button"
              onClick={() => copy('message', body ? `${subject}\n\n${body}` : subject)}
            >
              {copied === 'message' ? 'Message copied' : 'Copy message'}
            </button>
          </div>

          <div className="modal-actions">
            <button type="button" className="secondary-button" onClick={onClose}>
              Cancel
            </button>
            {bcc.length > 0 ? (
              <a className="primary-button email-participants-open" href={mailto}>
                Open in my email app
              </a>
            ) : (
              <button type="button" className="primary-button" disabled>
                Open in my email app
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

function sameAddress(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase()
}
