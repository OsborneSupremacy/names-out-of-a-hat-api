import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { vi } from 'vitest'
import { Home } from './Home'
import { HatMetadata } from '../api'

const getHats = vi.fn()

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  getHats: (email: string, page?: number) => getHats(email, page),
  createHat: vi.fn()
}))

const hoursAgo = (hours: number) => new Date(Date.now() - hours * 60 * 60 * 1000).toISOString()

function renderHome(hats: HatMetadata[]) {
  getHats.mockResolvedValue({ organizerName: 'Ben', hats, page: 1, pageSize: 5, totalCount: hats.length })

  render(
    <MemoryRouter>
      <Home userEmail="organizer@example.com" onSignOut={vi.fn()} />
    </MemoryRouter>
  )
}

describe('Home', () => {
  beforeEach(() => {
    getHats.mockReset()
  })

  it('says how long each exchange has been at its status', async () => {
    renderHome([
      {
        hatId: '11111111-1111-1111-1111-111111111111',
        hatName: 'Family Christmas',
        status: 'INVITATIONS_SENT',
        statusUpdatedAt: hoursAgo(3)
      }
    ])

    expect(await screen.findByText('Family Christmas')).toBeInTheDocument()
    expect(screen.getByText('Invitations Sent')).toBeInTheDocument()
    expect(screen.getByText('3 hours ago')).toBeInTheDocument()
  })

  // The age is when the status last changed, so two exchanges at the same status can carry
  // different ones -- which is the whole reason it is shown next to the pill rather than inferred
  // from it.
  it('ages each exchange independently of its status', async () => {
    renderHome([
      {
        hatId: '11111111-1111-1111-1111-111111111111',
        hatName: 'Family Christmas',
        status: 'IN_PROGRESS',
        statusUpdatedAt: hoursAgo(2)
      },
      {
        hatId: '22222222-2222-2222-2222-222222222222',
        hatName: 'Office Draw',
        status: 'IN_PROGRESS',
        statusUpdatedAt: hoursAgo(30 * 24)
      }
    ])

    expect(await screen.findByText('2 hours ago')).toBeInTheDocument()
    expect(screen.getByText('1 month ago')).toBeInTheDocument()
  })

  // The API spells "not known" with the minimum date rather than with null. Nothing should reach
  // the list carrying it, but a row written outside the application could, and "2025 years ago" is
  // not the thing to show an organizer.
  it('shows no age for an exchange whose status has no timestamp', async () => {
    renderHome([
      {
        hatId: '11111111-1111-1111-1111-111111111111',
        hatName: 'Family Christmas',
        status: 'IN_PROGRESS',
        statusUpdatedAt: '0001-01-01T00:00:00+00:00'
      }
    ])

    await waitFor(() => expect(screen.getByText('Family Christmas')).toBeInTheDocument())

    expect(screen.getByText('In Progress')).toBeInTheDocument()
    expect(screen.queryByText(/ago$/)).not.toBeInTheDocument()
  })

  /**
   * Deleting happens on a queue, so the list fetched on arrival can still hold exchanges that are
   * about to go. Showing them, or offering to create one as if the list were empty by accident,
   * would both contradict what the person was just told.
   */
  it('hides the list and says deletion is under way when arriving from a deletion', async () => {
    getHats.mockResolvedValue({
      organizerName: 'Ben',
      page: 1,
      pageSize: 5,
      totalCount: 1,
      hats: [
        {
          hatId: '11111111-1111-1111-1111-111111111111',
          hatName: 'Family Christmas',
          status: 'IN_PROGRESS',
          statusUpdatedAt: hoursAgo(3)
        }
      ]
    })

    render(
      <MemoryRouter initialEntries={[{ pathname: '/', state: { dataDeletionRequested: true } }]}>
        <Home userEmail="organizer@example.com" onSignOut={vi.fn()} />
      </MemoryRouter>
    )

    expect(await screen.findByText('Hello Ben!')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/being deleted/)
    expect(screen.queryByText('Family Christmas')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Create New Gift Exchange' })).not.toBeInTheDocument()
  })

  describe('paging', () => {
    const hat = (n: number): HatMetadata => ({
      hatId: `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`,
      hatName: `Exchange ${n}`,
      status: 'IN_PROGRESS',
      statusUpdatedAt: hoursAgo(n)
    })

    /** Answers like the API: 7 hats in pages of 5, and nothing past the end. */
    function serveSevenHats() {
      const all = [1, 2, 3, 4, 5, 6, 7].map(hat)
      getHats.mockImplementation(async (_email: string, page = 1) => ({
        organizerName: 'Ben',
        hats: all.slice((page - 1) * 5, page * 5),
        page,
        pageSize: 5,
        totalCount: all.length
      }))
    }

    function LocationProbe() {
      return <span data-testid="location">{useLocation().search}</span>
    }

    function renderAt(url: string) {
      render(
        <MemoryRouter initialEntries={[url]}>
          <Home userEmail="organizer@example.com" onSignOut={vi.fn()} />
          <LocationProbe />
        </MemoryRouter>
      )
    }

    it('hides the pager when everything fits on one page', async () => {
      renderHome([hat(1), hat(2)])

      expect(await screen.findByText('Exchange 1')).toBeInTheDocument()
      expect(screen.queryByRole('navigation', { name: 'Gift exchange pages' })).not.toBeInTheDocument()
    })

    it('asks the server for the next page', async () => {
      serveSevenHats()
      renderAt('/')

      expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /Previous/ })).toBeDisabled()

      fireEvent.click(screen.getByRole('button', { name: /Next/ }))

      expect(await screen.findByText('Exchange 6')).toBeInTheDocument()
      expect(screen.queryByText('Exchange 1')).not.toBeInTheDocument()
      expect(getHats).toHaveBeenLastCalledWith('organizer@example.com', 2)
      expect(screen.getByText('Page 2 of 2')).toBeInTheDocument()
      expect(screen.getByTestId('location')).toHaveTextContent('?page=2')
      expect(screen.getByRole('button', { name: /Next/ })).toBeDisabled()
    })

    it('moves a page past the end to the last page, without offering to create one', async () => {
      serveSevenHats()
      renderAt('/?page=9')

      expect(await screen.findByText('Exchange 7')).toBeInTheDocument()
      expect(getHats).toHaveBeenCalledWith('organizer@example.com', 9)
      expect(screen.getByTestId('location')).toHaveTextContent('?page=2')
      expect(screen.queryByText("You don't have any Gift Exchanges")).not.toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: 'Create New Gift Exchange' })).not.toBeInTheDocument()
    })
  })
})
