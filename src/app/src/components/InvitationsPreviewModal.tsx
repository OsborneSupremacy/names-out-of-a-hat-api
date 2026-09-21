import { MouseEvent } from 'react'
import './InvitationsPreviewModal.css'

interface InvitationsPreviewModalProps {
  subject: string
  htmlBody: string
  isSending: boolean
  onBack: () => void
  onSend: () => Promise<void>
}

// The preview's links point at sample URLs that only exist once real invitations go out,
// so following one lands on an error page. CSS stops mouse clicks; this also covers keyboard activation.
function disablePreviewLinks(event: MouseEvent<HTMLDivElement>) {
  if (event.target instanceof Element && event.target.closest('a')) {
    event.preventDefault()
  }
}

export function InvitationsPreviewModal({
  subject,
  htmlBody,
  isSending,
  onBack,
  onSend,
}: InvitationsPreviewModalProps) {
  return (
    <div className="preview-modal-overlay" role="dialog" aria-modal="true" aria-labelledby="preview-modal-title">
      <div className="preview-modal-content">
        <div className="preview-modal-header">
          <h2 id="preview-modal-title">Invitation Preview</h2>
        </div>

        <div className="preview-modal-body">
          <p className="preview-note">
            This is the invitation template your participants will receive. Use Back to Edit if you want to change gift exchange details before sending.
          </p>

          <div className="preview-card">
            <div className="preview-subject">
              <span className="preview-label">Subject:</span> {subject}
            </div>
            <div
              className="preview-email-body"
              onClickCapture={disablePreviewLinks}
              dangerouslySetInnerHTML={{ __html: htmlBody }}
            />
          </div>
        </div>

        <div className="preview-modal-actions">
          <button
            type="button"
            className="preview-back-button"
            onClick={onBack}
            disabled={isSending}
          >
            Back to Edit
          </button>
          <button
            type="button"
            className="preview-send-button"
            onClick={onSend}
            disabled={isSending}
          >
            {isSending ? 'Sending Invitations...' : 'Send Invitations'}
          </button>
        </div>
      </div>
    </div>
  )
}
