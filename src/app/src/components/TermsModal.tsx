import { useEffect } from 'react'
// Shares the modal chrome with the other dialogs; see the note in EditNameModal.
import './CreateHatModal.css'
import './TermsModal.css'

const GOVERNING_JURISDICTION = 'The United States of America'

/**
 * The date the text below last changed. Bumped by hand when it does — an automatic date would say
 * the terms changed every time the site was deployed, which is exactly the signal it should not
 * give.
 */
const LAST_UPDATED = 'August 31, 2026'

interface TermsModalProps {
  onClose: () => void
}

/**
 * The terms of service, as a modal rather than a page.
 *
 * A modal because the footer is the only way in and there is nothing to link to from outside — no
 * signup flow gates on accepting these, and nobody arrives at the application by searching for
 * them. If either of those changes this wants to become a route, so that it has a URL somebody can
 * be sent to.
 */
export function TermsModal({ onClose }: TermsModalProps) {
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
        className="modal-content terms-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="terms-title"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header">
          <h2 id="terms-title">Terms &amp; Conditions</h2>
          <button className="close-button" onClick={onClose} aria-label="Close">
            ×
          </button>
        </div>

        <div className="terms-body">
          <p className="terms-updated">Last updated {LAST_UPDATED}</p>

          <p>
            These terms cover your use of Names Out of a Hat (the &ldquo;Service&rdquo;). By using
            it you agree to them. If you don&rsquo;t, please don&rsquo;t use the Service.
          </p>

          <h3>1. What the Service does</h3>
          <p>
            The Service organizes gift exchanges. You create a gift exchange, add participants by
            name and email address, and the Service draws names and emails each participant the name
            they drew. It also sends related mail, such as sign-in links and announcements when an
            exchange finishes.
          </p>

          <h3>2. Your account</h3>
          <p>
            You sign in with a link emailed to your address; there is no password. Anyone with
            access to your inbox can therefore sign in as you, so keep it secure. You are
            responsible for what happens under your account. Tell us if you believe it has been
            used without your permission.
          </p>
          <p>
            You must be old enough to agree to these terms in the place you live, and you must give
            an email address you actually control.
          </p>

          <h3>3. The people you add</h3>
          <p>
            When you add participants you are giving us their names and email addresses, and asking
            us to email them on your behalf. You confirm that you have a reasonable basis for doing
            so — that these are people you know and who would expect to hear from you about a gift
            exchange. Do not use the Service to send mail to people who have not agreed to take
            part.
          </p>
          <p>
            Participant addresses are used to run the exchange and for nothing else. They are not
            sold, rented, or used to market anything.
          </p>

          <h3>4. Acceptable use</h3>
          <p>You agree not to:</p>
          <ul>
            <li>use the Service to send spam, bulk mail, or anything unlawful, harassing, or deceptive;</li>
            <li>impersonate anybody, or enter names or content intended to abuse or mislead;</li>
            <li>attempt to access accounts, exchanges, or data that are not yours;</li>
            <li>probe, scan, overload, or otherwise interfere with the Service or its infrastructure;</li>
            <li>scrape, automate, or resell access to the Service.</li>
          </ul>

          <h3>5. Content you provide</h3>
          <p>
            You keep ownership of the names, descriptions, gift ideas, and other content you enter.
            You give us permission to store it, and to include it in the emails the Service sends,
            for as long as it takes to run your gift exchanges. Content may be screened
            automatically for abusive material and rejected.
          </p>

          <h3>6. Availability</h3>
          <p>
            The Service is free and provided as-is. There is no uptime commitment, no guarantee that
            email is delivered — mail providers make their own decisions about what reaches an inbox
            — and no guarantee that any feature will keep working or continue to exist. Deliveries
            may be delayed, filtered, or dropped by mail systems outside our control.
          </p>

          <h3>7. Ending it</h3>
          <p>
            You can stop using the Service at any time and delete your gift exchanges from within
            the application. A gift exchange you give a date for is deleted automatically 18
            months after that date. We may suspend or remove an account that breaks these terms, and we
            may discontinue the Service entirely. If we discontinue it, we will try to give
            reasonable notice, but we may not always be able to.
          </p>

          <h3>8. Disclaimer and liability</h3>
          <p>
            The Service is provided &ldquo;as is&rdquo; and &ldquo;as available&rdquo;, without
            warranties of any kind, express or implied, including any warranty of merchantability,
            fitness for a particular purpose, or non-infringement.
          </p>
          <p>
            To the fullest extent permitted by law, we are not liable for any indirect, incidental,
            special, consequential, or punitive damages, or for any loss of data, gifts, goodwill,
            or spoiled surprises arising out of your use of the Service. Some jurisdictions do not
            allow these limits, in which case they apply to you only as far as the law allows.
          </p>

          <h3>9. Changes to these terms</h3>
          <p>
            These terms may change. The date at the top says when they last did. Continuing to use
            the Service after a change means you accept the revised terms.
          </p>

          <h3>10. Governing law</h3>
          <p>
            These terms are governed by the laws of {GOVERNING_JURISDICTION}, without regard to its
            conflict of law rules.
          </p>

          <h3>11. Contact</h3>
          <p>
            Questions about these terms can be sent through the Contact link in the footer.
          </p>
        </div>

        <div className="terms-actions">
          <button type="button" className="primary-button" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
