import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { RevealHatModal } from './RevealHatModal'
import { Participant } from '../api'

function participant(name: string, deliveryStatus = 'DELIVERED', deliveryMessageType = 'INVITATION'): Participant {
  const person = { name, email: `${name.toLowerCase()}@example.com` }

  return {
    person,
    pickedRecipient: person,
    eligibleRecipients: [],
    emoji: '😀',
    deliveryStatus,
    deliveryDetail: '',
    deliveryMessageType,
    deliveryOccurredAt: '2026-08-28T10:00:00+00:00',
  }
}

function renderModal(participants: Participant[]) {
  const props = {
    hatName: 'Family Christmas 2026',
    participants,
    onClose: vi.fn(),
    onSubmit: vi.fn(async () => Promise.resolve()),
  }

  render(<RevealHatModal {...props} />)
  return props
}

describe('RevealHatModal', () => {
  it('asks plainly when every invitation arrived', () => {
    renderModal([participant('Alpha'), participant('Beta')])

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reveal Picked Names' })).toBeInTheDocument()
  })

  it('names somebody whose invitation bounced', () => {
    renderModal([participant('Alpha', 'BOUNCED'), participant('Beta')])

    expect(screen.getByRole('alert')).toHaveTextContent(/The invitation to Alpha couldn't be delivered/)
    expect(screen.getByRole('alert')).toHaveTextContent(/don't know whose name they were assigned/)
    expect(screen.getByRole('button', { name: 'Reveal Anyway' })).toBeInTheDocument()
  })

  it('names everybody whose invitation failed', () => {
    renderModal([participant('Alpha', 'BOUNCED'), participant('Beta', 'REJECTED'), participant('Charlie')])

    expect(screen.getByRole('alert')).toHaveTextContent(/The invitations to Alpha and Beta couldn't be delivered/)
  })

  // Nothing heard is not "did not arrive", and a complaint means it did.
  it('does not warn about an invitation nothing has been heard about, or one marked as spam', () => {
    renderModal([participant('Alpha', ''), participant('Beta', 'COMPLAINED')])

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  // The warning is that somebody does not know who they drew, which only a failed invitation means.
  it('does not warn about a failed message that was not the invitation', () => {
    renderModal([participant('Alpha', 'BOUNCED', 'PARTICIPANT_LEFT')])

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('reveals when confirmed', async () => {
    const user = userEvent.setup()
    const props = renderModal([participant('Alpha', 'BOUNCED')])

    await user.click(screen.getByRole('button', { name: 'Reveal Anyway' }))

    expect(props.onSubmit).toHaveBeenCalled()
    expect(props.onClose).toHaveBeenCalled()
  })

  it('does nothing when cancelled', async () => {
    const user = userEvent.setup()
    const props = renderModal([participant('Alpha', 'BOUNCED')])

    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(props.onSubmit).not.toHaveBeenCalled()
    expect(props.onClose).toHaveBeenCalled()
  })

  it('surfaces a failure without closing', async () => {
    const user = userEvent.setup()
    const props = renderModal([participant('Alpha')])
    props.onSubmit.mockRejectedValueOnce(new Error('Hat status CLOSED is not valid for this operation'))

    await user.click(screen.getByRole('button', { name: 'Reveal Picked Names' }))

    expect(screen.getByText(/not valid for this operation/)).toBeInTheDocument()
    expect(props.onClose).not.toHaveBeenCalled()
  })
})
