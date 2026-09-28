import { useEffect } from 'react'
import { SENDER_ADDRESS } from '../participantEmail'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './DeliveryHelpModal.css'

interface DeliveryHelpModalProps {
  /** Whose name opens the subject line of every invitation this exchange sent. */
  organizerName: string
  onClose: () => void
}

/**
 * What the email status column is actually claiming, and what to do when it disagrees with a
 * participant.
 *
 * The dialog exists for one conversation in particular: the table says "Delivered" and the person
 * says they never got anything. Both are usually true. "Delivered" is a statement about a mail
 * server accepting a message, and every step after that — the junk folder, Gmail's Promotions tab,
 * a filter somebody's employer wrote — happens on the far side of the last thing we can see.
 *
 * So the answer is not reassurance. It is the two facts the organizer can pass on that actually
 * find the message: the address it came from, and when it arrived.
 */
export function DeliveryHelpModal({ organizerName, onClose }: DeliveryHelpModalProps) {
  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [onClose])

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div
        className="modal-content delivery-help-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="delivery-help-title"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h2 id="delivery-help-title">About these email statuses</h2>
          <button className="close-button" onClick={onClose} aria-label="Close">
            ×
          </button>
        </div>

        <div className="delivery-help-body">
          <h3>What &ldquo;Delivered&rdquo; means</h3>
          <p>
            The mail server on the other end accepted the message and said so. That is the furthest
            anything here can see, and it is genuinely good news &mdash; the address works and the
            message is on that server.
          </p>
          <p>
            It does not mean the message reached the inbox, and it does not mean anybody has read
            it. Where a server files a message it has accepted is entirely that server&rsquo;s
            business.
          </p>

          <h3>So &ldquo;Delivered&rdquo; and &ldquo;I never got an email&rdquo; can both be true</h3>
          <p>Usually one of these happened:</p>
          <ul>
            <li>It went to Spam or Junk.</li>
            <li>
              Gmail sorted it into the Promotions, Social or Updates tab. Mail in those tabs never
              appears in the inbox, and plenty of people have never opened them.
            </li>
            <li>
              A filter moved it to a folder. On a work address this is often a rule the person did
              not write and does not know about.
            </li>
            <li>They have it, and have not looked since the day it arrived.</li>
          </ul>

          <h3>What to ask them to do</h3>
          <ul>
            <li>
              <strong>Search their whole mailbox for {SENDER_ADDRESS}</strong> &mdash; searching,
              rather than looking in the inbox. A search covers Spam, the Gmail tabs, and any folder
              a rule moved it into.
            </li>
            <li>
              The subject line starts with &ldquo;{organizerName || 'your name'} has added you
              to&rdquo;, so that works as a search too.
            </li>
            <li>
              Give them the date and time from this table. That is when their mail server took the
              message, so it tells them which day to look in.
            </li>
            <li>
              If they find it in Spam, ask them to mark it <em>not spam</em>. The exchange has more
              to send them later.
            </li>
          </ul>

          <h3>If it really is not there</h3>
          <p>
            Then the address we have is probably not the one they read. Use <strong>Edit Address</strong>{' '}
            on their row: saving a corrected address sends their invitation again, to the new one.
          </p>
          <p>
            There is no button for sending the same message to the same address a second time. If
            the first one was delivered, a second copy lands in the same folder the first one did.
          </p>

          <h3>Why we cannot tell you more than this</h3>
          <p>
            We could measure whether an email was opened, and we have chosen not to. The measurement
            is done with a hidden image, and it is wrong in the direction that matters: Apple loads
            that image for every recipient whether or not they looked, other mail apps block it for
            people who did look, and the corporate mail filters most likely to bury an invitation
            fetch it automatically. A confident &ldquo;opened&rdquo; that is not true is worse than
            no answer, because it stops you chasing the one person who never saw their name.
          </p>

          <h3>The other statuses</h3>
          <dl className="delivery-help-glossary">
            <dt>No confirmation yet</dt>
            <dd>
              Nothing has been heard about this message. Normal for the first minutes after sending,
              and it never means the message failed.
            </dd>

            <dt>Sent</dt>
            <dd>It left here. Nothing has come back about the far end yet.</dd>

            <dt>Delayed, still trying</dt>
            <dd>
              Temporarily undeliverable &mdash; a full mailbox, or a server having a bad day. Still
              being retried, so this is neither a success nor a failure.
            </dd>

            <dt>Bounced</dt>
            <dd>
              It came back, and the reason from the receiving server is shown underneath. This is
              the one worth acting on: usually the address has a typo in it.
            </dd>

            <dt>Marked as spam</dt>
            <dd>
              It arrived, and the recipient reported it as junk. There is nothing to fix at this
              end, but their mail provider is likely to file later messages from us the same way.
            </dd>

            <dt>Rejected, Failed to send</dt>
            <dd>It never left. Nothing reached the recipient at all.</dd>
          </dl>
        </div>

        <div className="delivery-help-actions">
          <button type="button" className="primary-button" onClick={onClose}>
            Got it
          </button>
        </div>
      </div>
    </div>
  )
}
