import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { Header } from './Header'

const deleteMyData = vi.fn()

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  deleteMyData: (request: unknown) => deleteMyData(request),
}))

/**
 * Wrapped in a router because the wordmark is a Link, which throws outside one. MemoryRouter rather
 * than BrowserRouter so the tests do not touch window.history.
 */
function renderHeader(givenName: string | null, initialPath = '/') {
  const callbacks = { onSignOut: vi.fn(), onDataDeleted: vi.fn() }

  render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Header
        userEmail="osborne.ben@gmail.com"
        givenName={givenName}
        onSignOut={callbacks.onSignOut}
        onNameUpdated={vi.fn()}
        onDataDeleted={callbacks.onDataDeleted}
      />
    </MemoryRouter>
  )

  return callbacks
}

async function deleteFromMenu(user: ReturnType<typeof userEvent.setup>, forgetMe: boolean) {
  await user.click(screen.getByLabelText('Profile menu'))
  await user.click(screen.getByRole('button', { name: 'Delete My Data' }))

  if (!forgetMe) {
    await user.click(screen.getByRole('checkbox', { name: /Forget me/ }))
  }

  await user.click(screen.getByRole('button', { name: 'Delete My Data' }))
}

describe('Header', () => {
  // Falling back to the email initial while the name loads is not a placeholder, it is a second
  // answer — which is what made the avatar flick from "O" to "B" on every page load.
  it('shows no initial while the name is still unknown', () => {
    renderHeader(null)

    expect(screen.getByLabelText('Profile menu')).toHaveTextContent(/^\s*$/)
  })

  it('shows the name initial once the name is known', () => {
    renderHeader('Ben')

    expect(screen.getByLabelText('Profile menu')).toHaveTextContent('B')
  })

  it('falls back to the email initial for a user who genuinely has no name', () => {
    renderHeader('')

    expect(screen.getByLabelText('Profile menu')).toHaveTextContent('O')
  })

  it('points the wordmark at the home page', () => {
    renderHeader('Ben')

    expect(screen.getByRole('link', { name: 'Names Out of a Hat' })).toHaveAttribute('href', '/')
  })

  // Deliberate: a masthead that is a link on every page except one is a masthead that moves out
  // from under the keyboard.
  it('leaves the wordmark a link on the home page itself', () => {
    renderHeader('Ben', '/')

    expect(screen.getByRole('link', { name: 'Names Out of a Hat' })).toBeInTheDocument()
  })

  describe('Delete My Data', () => {
    beforeEach(() => {
      deleteMyData.mockReset()
      deleteMyData.mockResolvedValue(undefined)
    })

    it('signs out somebody who asked to be forgotten', async () => {
      const user = userEvent.setup()
      const { onSignOut, onDataDeleted } = renderHeader('Ben')

      await deleteFromMenu(user, true)

      expect(deleteMyData).toHaveBeenCalledWith({ forgetMe: true, doNotAddAnywhere: false })
      expect(onSignOut).toHaveBeenCalled()
      expect(onDataDeleted).not.toHaveBeenCalled()
    })

    it('keeps somebody signed in who did not ask to be forgotten, and tells the page', async () => {
      const user = userEvent.setup()
      const { onSignOut, onDataDeleted } = renderHeader('Ben')

      await deleteFromMenu(user, false)

      expect(deleteMyData).toHaveBeenCalledWith({ forgetMe: false, doNotAddAnywhere: false })
      expect(onDataDeleted).toHaveBeenCalled()
      expect(onSignOut).not.toHaveBeenCalled()
    })
  })
})
