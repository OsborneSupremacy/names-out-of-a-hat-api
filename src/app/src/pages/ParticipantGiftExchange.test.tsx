import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { ParticipantGiftExchange } from './ParticipantGiftExchange'
import { ParticipantGiftIdeas, ParticipantView } from '../api'

const getParticipantView = vi.fn()
const shareGiftIdeas = vi.fn()
const answerGiftIdeaAsk = vi.fn()
const askForGiftIdeas = vi.fn()
const offerGiftIdeas = vi.fn()
const leaveGiftExchange = vi.fn()

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  getParticipantView: (email: string, hatId: string) => getParticipantView(email, hatId),
  shareGiftIdeas: (...args: unknown[]) => shareGiftIdeas(...args),
  answerGiftIdeaAsk: (...args: unknown[]) => answerGiftIdeaAsk(...args),
  askForGiftIdeas: (...args: unknown[]) => askForGiftIdeas(...args),
  offerGiftIdeas: (...args: unknown[]) => offerGiftIdeas(...args),
  leaveGiftExchange: (...args: unknown[]) => leaveGiftExchange(...args)
}))

const NEVER = '0001-01-01T00:00:00+00:00'

const jo = '22222222-2222-2222-2222-222222222222'
const taylor = '33333333-3333-3333-3333-333333333333'

/** Nothing shared by anybody yet, with everything still open. */
const quiet: ParticipantGiftIdeas = {
  canShare: true,
  canAsk: true,
  yourIdeas: '',
  yourIdeasSharedAt: NEVER,
  holdUntilAsked: false,
  hasSharedOutrightBefore: false,
  fromYourPick: '',
  fromYourPickSharedAt: NEVER,
  aboutYourPick: [],
  askedAboutYourPick: [],
  asksForYou: [],
  yourOffers: [],
  askCandidates: [
    { participantId: jo, name: 'Jo', isTheirPick: true },
    { participantId: taylor, name: 'Taylor', isTheirPick: false }
  ],
  offerCandidates: [{ participantId: taylor, name: 'Taylor' }]
}

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
  ],
  canLeave: true,
  giftIdeas: quiet
}

function renderPage(view: ParticipantView) {
  getParticipantView.mockResolvedValue(view)

  render(
    <MemoryRouter initialEntries={[`/participating/${hatId}`]}>
      <Routes>
        <Route path="/" element={<p>Home page</p>} />
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
    for (const mock of [getParticipantView, shareGiftIdeas, answerGiftIdeaAsk, askForGiftIdeas, offerGiftIdeas, leaveGiftExchange]) {
      mock.mockReset()
    }
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

  it('offers nothing to change about the exchange itself', async () => {
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

  describe('gift ideas', () => {
    it('shares their own ideas and reads the exchange again', async () => {
      const user = userEvent.setup()
      shareGiftIdeas.mockResolvedValue(undefined)
      renderPage(drawn)

      await user.type(await screen.findByLabelText('Your gift ideas'), 'A scarf')
      await user.click(screen.getByRole('button', { name: 'Share' }))

      expect(shareGiftIdeas).toHaveBeenCalledWith('sam@example.com', hatId, 'A scarf', false)
      expect(await screen.findByText('Shared with the person who picked your name.')).toBeInTheDocument()
      expect(getParticipantView).toHaveBeenCalledTimes(2)
    })

    it('saves rather than shares when they ask for their ideas to be held', async () => {
      const user = userEvent.setup()
      shareGiftIdeas.mockResolvedValue(undefined)
      renderPage(drawn)

      await user.type(await screen.findByLabelText('Your gift ideas'), 'A scarf')
      await user.click(screen.getByRole('checkbox', { name: /Only share if the person who has my name asks/ }))
      await user.click(screen.getByRole('button', { name: 'Save' }))

      expect(shareGiftIdeas).toHaveBeenCalledWith('sam@example.com', hatId, 'A scarf', true)
      expect(await screen.findByText(/only if they ask for gift ideas/)).toBeInTheDocument()
    })

    it('keeps what they wrote and says why when it is refused', async () => {
      const user = userEvent.setup()
      shareGiftIdeas.mockRejectedValue(new Error('What you wrote mentions the name of the person you picked.'))
      renderPage(drawn)

      const box = await screen.findByLabelText('Your gift ideas')
      await user.type(box, 'Same as Jo')
      await user.click(screen.getByRole('button', { name: 'Share' }))

      expect(await screen.findByText(/mentions the name of the person you picked/)).toBeInTheDocument()
      expect(box).toHaveValue('Same as Jo')
    })

    it('shows what has been shared about their pick, and who suggested it', async () => {
      renderPage({
        ...drawn,
        giftIdeas: {
          ...quiet,
          fromYourPick: 'Anything with owls',
          fromYourPickSharedAt: '2026-10-01T09:00:00+00:00',
          aboutYourPick: [
            { from: 'Taylor', ideas: 'Owl socks', sharedAt: '2026-10-02T09:00:00+00:00', wasAskedFor: true }
          ],
          askedAboutYourPick: [{ name: 'Taylor', askedAt: '2026-10-01T08:00:00+00:00', hasAnswered: true }]
        }
      })

      expect(await screen.findByText("In Jo's own words")).toBeInTheDocument()
      expect(screen.getByText('Anything with owls')).toBeInTheDocument()
      expect(screen.getByText('From Taylor, who you asked')).toBeInTheDocument()
      expect(screen.getByText('Owl socks')).toBeInTheDocument()
      expect(screen.queryByText(/Still waiting to hear from/)).not.toBeInTheDocument()
    })

    it('asks their pick by default, and reports who was asked', async () => {
      const user = userEvent.setup()
      askForGiftIdeas.mockResolvedValue({
        attempts: [{ name: 'Jo', sent: true, previouslyAskedAt: NEVER }],
        releasedHeldIdeas: false
      })
      renderPage(drawn)

      await user.click(await screen.findByRole('button', { name: 'Ask for Gift Ideas' }))
      const dialog = screen.getByRole('heading', { name: 'Gift Ideas for Jo' }).closest('.modal-content') as HTMLElement

      expect(within(dialog).getByText(/may well work out that it was you/)).toBeInTheDocument()
      await user.click(within(dialog).getByRole('button', { name: 'Ask' }))

      expect(askForGiftIdeas).toHaveBeenCalledWith('sam@example.com', hatId, [jo], '')
      expect(await within(dialog).findByText(/We've asked Jo what they'd like/)).toBeInTheDocument()
    })

    it('answers an ask put to them, which never says who asked', async () => {
      const user = userEvent.setup()
      answerGiftIdeaAsk.mockResolvedValue(undefined)
      const askId = '44444444-4444-4444-4444-444444444444'
      renderPage({
        ...drawn,
        giftIdeas: {
          ...quiet,
          asksForYou: [
            { askId, subjectName: 'Taylor', askedAt: '2026-10-01T09:00:00+00:00', yourAnswer: '', answeredAt: NEVER }
          ]
        }
      })

      await user.type(await screen.findByLabelText(/What would Taylor like\?/), 'Gardening gloves')
      const panel = screen.getByRole('heading', { name: 'Asked of You' }).closest('section') as HTMLElement
      await user.click(within(panel).getByRole('button', { name: 'Share' }))

      expect(answerGiftIdeaAsk).toHaveBeenCalledWith('sam@example.com', hatId, askId, 'Gardening gloves')
      expect(await screen.findByText('Sent to the person shopping for Taylor.')).toBeInTheDocument()
    })

    it('offers ideas about somebody other than their pick', async () => {
      const user = userEvent.setup()
      offerGiftIdeas.mockResolvedValue(undefined)
      renderPage(drawn)

      await user.click(await screen.findByRole('button', { name: 'Share Ideas' }))
      await user.click(screen.getByRole('radio', { name: 'Taylor' }))
      await user.type(screen.getByLabelText("What do you think they'd like?"), 'Seeds')
      const dialog = screen.getByRole('heading', { name: 'Share Ideas About Someone Else' }).closest('.modal-content') as HTMLElement
      await user.click(within(dialog).getByRole('button', { name: 'Share' }))

      expect(offerGiftIdeas).toHaveBeenCalledWith('sam@example.com', hatId, taylor, 'Seeds')
      expect(await within(dialog).findByText(/go to the person shopping for Taylor/)).toBeInTheDocument()
    })

    it('shows what was shared but offers nothing to do once the exchange is over', async () => {
      renderPage({
        ...drawn,
        status: 'CLOSED',
        giftIdeas: {
          ...quiet,
          canShare: false,
          canAsk: false,
          yourIdeas: 'A scarf',
          yourIdeasSharedAt: '2026-10-01T09:00:00+00:00',
          askCandidates: [],
          offerCandidates: []
        }
      })

      expect(await screen.findByText('A scarf')).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /Share|Ask for Gift Ideas/ })).not.toBeInTheDocument()
    })
  })

  describe('leaving', () => {
    it('is behind the advanced options, and goes home once they have left', async () => {
      const user = userEvent.setup()
      leaveGiftExchange.mockResolvedValue(undefined)
      renderPage(drawn)

      expect(await screen.findByRole('heading', { name: 'Family Christmas' })).toBeInTheDocument()
      expect(screen.queryByRole('menuitem', { name: /Leave Gift Exchange/ })).not.toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: 'Advanced options' }))
      await user.click(screen.getByRole('menuitem', { name: /Leave Gift Exchange/ }))
      await user.click(screen.getByRole('checkbox', { name: "Don't let Alex add me to gift exchanges again" }))
      await user.click(screen.getByRole('button', { name: 'Leave Gift Exchange' }))

      expect(leaveGiftExchange).toHaveBeenCalledWith('sam@example.com', hatId, true, false)
      expect(await screen.findByText('Home page')).toBeInTheDocument()
    })

    it('stays put when they change their mind', async () => {
      const user = userEvent.setup()
      renderPage(drawn)

      await user.click(await screen.findByRole('button', { name: 'Advanced options' }))
      await user.click(screen.getByRole('menuitem', { name: /Leave Gift Exchange/ }))
      await user.click(screen.getByRole('button', { name: 'Stay In' }))

      expect(leaveGiftExchange).not.toHaveBeenCalled()
      expect(screen.getByRole('heading', { name: 'Family Christmas' })).toBeInTheDocument()
    })

    it('is not available to the organizer', async () => {
      const user = userEvent.setup()
      renderPage({ ...drawn, canLeave: false })

      await user.click(await screen.findByRole('button', { name: 'Advanced options' }))

      expect(screen.getByRole('menuitem', { name: /Leave Gift Exchange/ })).toBeDisabled()
      expect(screen.getByText("You organized this gift exchange, so you can't leave it.")).toBeInTheDocument()
    })
  })
})
