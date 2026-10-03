import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { ParticipantGiftExchange } from './ParticipantGiftExchange'
import { ParticipantView } from '../api'

const getParticipantView = vi.fn()

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  getParticipantView: (email: string, hatId: string) => getParticipantView(email, hatId)
}))

const hatId = '11111111-1111-1111-1111-111111111111'

/** As the server sends it before the reveal: the caller's pick, and nobody else's. */
const drawn: ParticipantView = {
  hatId,
  name: 'Family Christmas',
  status: 'INVITATIONS_SENT',
  organizerName: 'Alex',
  additionalInformation: 'Bring a mince pie.',
  priceRange: '$20-$30',
  exchangeDate: '0001-01-01',
  participants: [
    { name: 'Jo', emoji: '🤠', isYou: false, pickedRecipient: '' },
    { name: 'Sam', emoji: '😀', isYou: true, pickedRecipient: 'Jo' },
    { name: 'Taylor', emoji: '👻', isYou: false, pickedRecipient: '' }
  ]
}

function renderPage(view: ParticipantView) {
  getParticipantView.mockResolvedValue(view)

  render(
    <MemoryRouter initialEntries={[`/participating/${hatId}`]}>
      <Routes>
        <Route
          path="/participating/:hatId"
          element={<ParticipantGiftExchange userEmail="sam@example.com" onSignOut={vi.fn()} />}
        />
      </Routes>
    </MemoryRouter>
  )
}

describe('ParticipantGiftExchange', () => {
  beforeEach(() => {
    getParticipantView.mockReset()
  })

  it('shows what the organizer said about the exchange', async () => {
    renderPage(drawn)

    expect(await screen.findByRole('heading', { name: 'Family Christmas' })).toBeInTheDocument()
    expect(getParticipantView).toHaveBeenCalledWith('sam@example.com', hatId)
    expect(screen.getByText('Organized by Alex')).toBeInTheDocument()
    expect(screen.getByText('Bring a mince pie.')).toBeInTheDocument()
    expect(screen.getByText('$20-$30')).toBeInTheDocument()
  })

  it('names who they are giving to, and nobody else\'s pick', async () => {
    renderPage(drawn)

    expect(await screen.findByText("You're giving a gift to")).toBeInTheDocument()
    expect(screen.getByText('Participants (3)')).toBeInTheDocument()
    expect(screen.getByText('(you)')).toBeInTheDocument()
    expect(screen.queryByText('→')).not.toBeInTheDocument()
  })

  it('offers nothing to change', async () => {
    renderPage(drawn)

    await screen.findByRole('heading', { name: 'Family Christmas' })

    expect(screen.queryByRole('button', { name: /Add Participant|Edit|Delete|Reveal/ })).not.toBeInTheDocument()
  })

  it('shows every pairing once the picks are revealed', async () => {
    renderPage({
      ...drawn,
      status: 'CLOSED',
      participants: [
        { name: 'Jo', emoji: '🤠', isYou: false, pickedRecipient: 'Taylor' },
        { name: 'Sam', emoji: '😀', isYou: true, pickedRecipient: 'Jo' },
        { name: 'Taylor', emoji: '👻', isYou: false, pickedRecipient: 'Sam' }
      ]
    })

    expect(await screen.findByText(/everybody can see who picked whose name/)).toBeInTheDocument()
    expect(screen.getAllByText('→')).toHaveLength(3)
  })

  it('says so when the exchange cannot be found', async () => {
    getParticipantView.mockRejectedValue(new Error('Gift exchange not found'))

    render(
      <MemoryRouter initialEntries={[`/participating/${hatId}`]}>
        <Routes>
          <Route
            path="/participating/:hatId"
            element={<ParticipantGiftExchange userEmail="sam@example.com" onSignOut={vi.fn()} />}
          />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByText('Gift exchange not found')).toBeInTheDocument()
  })
})
