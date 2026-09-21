import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { InvitationsPreviewModal } from './InvitationsPreviewModal'

describe('InvitationsPreviewModal', () => {
  it('renders subject and html body content', () => {
    render(
      <InvitationsPreviewModal
        subject="Sample Subject"
        htmlBody="<p>Hello preview body</p>"
        isSending={false}
        onBack={vi.fn()}
        onSend={vi.fn(async () => Promise.resolve())}
      />
    )

    expect(screen.getByText('Invitation Preview')).toBeInTheDocument()
    expect(screen.getByText('Sample Subject')).toBeInTheDocument()
    expect(screen.getByText('Hello preview body')).toBeInTheDocument()
  })

  it('calls onBack when Back to Edit is clicked', async () => {
    const user = userEvent.setup()
    const onBack = vi.fn()

    render(
      <InvitationsPreviewModal
        subject="Subject"
        htmlBody="<p>Body</p>"
        isSending={false}
        onBack={onBack}
        onSend={vi.fn(async () => Promise.resolve())}
      />
    )

    await user.click(screen.getByRole('button', { name: 'Back to Edit' }))

    expect(onBack).toHaveBeenCalledOnce()
  })

  it('calls onSend when Send Invitations is clicked', async () => {
    const user = userEvent.setup()
    const onSend = vi.fn(async () => Promise.resolve())

    render(
      <InvitationsPreviewModal
        subject="Subject"
        htmlBody="<p>Body</p>"
        isSending={false}
        onBack={vi.fn()}
        onSend={onSend}
      />
    )

    await user.click(screen.getByRole('button', { name: 'Send Invitations' }))

    expect(onSend).toHaveBeenCalledOnce()
  })

  it('does not follow links in the email preview', () => {
    render(
      <InvitationsPreviewModal
        subject="Subject"
        htmlBody='<p><a href="https://example.com/leave">leave this gift exchange</a></p>'
        isSending={false}
        onBack={vi.fn()}
        onSend={vi.fn(async () => Promise.resolve())}
      />
    )

    const link = screen.getByRole('link', { name: 'leave this gift exchange' })
    const followed = fireEvent.click(link)

    expect(followed).toBe(false)
  })

  it('disables actions while sending', () => {
    render(
      <InvitationsPreviewModal
        subject="Subject"
        htmlBody="<p>Body</p>"
        isSending={true}
        onBack={vi.fn()}
        onSend={vi.fn(async () => Promise.resolve())}
      />
    )

    expect(screen.getByRole('button', { name: 'Back to Edit' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Sending Invitations...' })).toBeDisabled()
  })
})
