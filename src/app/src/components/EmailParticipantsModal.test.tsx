import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { EmailParticipantsModal } from './EmailParticipantsModal'
import { Hat, Participant } from '../api'

function participant(name: string, deliveryStatus = ''): Participant {
  const person = { name, email: `${name.toLowerCase()}@example.com` }

  return {
    person,
    pickedRecipient: { name: '', email: '' },
    eligibleRecipients: [],
    emoji: '😀',
    deliveryStatus,
    deliveryDetail: '',
    deliveryMessageType: deliveryStatus ? 'INVITATION' : '',
    deliveryOccurredAt: '0001-01-01T00:00:00+00:00',
  }
}

function hat(participants: Participant[]): Hat {
  return {
    id: 'hat-1',
    name: 'Family Christmas',
    additionalInformation: '',
    priceRange: '',
    organizer: { name: 'Jane', email: 'jane@example.com' },
    participants,
    status: 'INVITATIONS_SENT',
    exchangeDate: '0001-01-01',
  }
}

const openLink = () => screen.getByRole('link', { name: 'Open in my email app' })
const bccOf = (href: string) => decodeURIComponent(/bcc=([^&]*)/.exec(href)?.[1] ?? '')

describe('EmailParticipantsModal', () => {
  it('writes to everyone but the organizer, and leaves out addresses that bounced', () => {
    render(
      <EmailParticipantsModal
        hat={hat([participant('Jane'), participant('Sam'), participant('Alex'), participant('Jo', 'BOUNCED')])}
        onClose={vi.fn()}
      />
    )

    const href = openLink().getAttribute('href')!

    expect(href.startsWith('mailto:jane%40example.com?')).toBe(true)
    expect(bccOf(href)).toBe('sam@example.com,alex@example.com')
    expect(screen.getByText(/Jo isn't on this list/)).toBeInTheDocument()
  })

  it('leaves out anybody unticked', async () => {
    const user = userEvent.setup()
    render(<EmailParticipantsModal hat={hat([participant('Sam'), participant('Alex')])} onClose={vi.fn()} />)

    await user.click(screen.getByRole('checkbox', { name: /Sam/ }))

    expect(bccOf(openLink().getAttribute('href')!)).toBe('alex@example.com')
  })

  it('will not open an email with nobody on it', async () => {
    const user = userEvent.setup()
    render(<EmailParticipantsModal hat={hat([participant('Sam')])} onClose={vi.fn()} />)

    await user.click(screen.getByRole('checkbox', { name: /Sam/ }))

    expect(screen.queryByRole('link', { name: 'Open in my email app' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open in my email app' })).toBeDisabled()
  })

  it('changes the message with the topic', async () => {
    const user = userEvent.setup()
    render(<EmailParticipantsModal hat={hat([participant('Sam')])} onClose={vi.fn()} />)

    expect(openLink().getAttribute('href')).toContain('body=')

    await user.click(screen.getByRole('radio', { name: 'Something Else' }))

    expect(screen.getByText('You write the message.')).toBeInTheDocument()
    expect(openLink().getAttribute('href')).not.toContain('body=')
  })

  it('copies the addresses for a computer with no mail app', async () => {
    const user = userEvent.setup()
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue()
    render(<EmailParticipantsModal hat={hat([participant('Sam'), participant('Alex')])} onClose={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: 'Copy addresses' }))

    expect(writeText).toHaveBeenCalledWith('sam@example.com, alex@example.com')
    expect(screen.getByRole('button', { name: 'Addresses copied' })).toBeInTheDocument()
  })

  it('closes on Escape', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(<EmailParticipantsModal hat={hat([participant('Sam')])} onClose={onClose} />)

    await user.keyboard('{Escape}')

    expect(onClose).toHaveBeenCalled()
  })
})
