import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { AdvancedOptionsMenu } from './AdvancedOptionsMenu'

function renderMenu(overrides: Partial<Parameters<typeof AdvancedOptionsMenu>[0]> = {}) {
  const props = {
    canReset: true,
    resetUnavailableReason: 'Invitations have gone out, so this can no longer be reset.',
    canDelete: true,
    deleteUnavailableReason: 'Invitations have gone out, so this can no longer be deleted.',
    isExporting: false,
    canEmailParticipants: true,
    emailParticipantsUnavailableReason: 'Add some participants first.',
    onExport: vi.fn(),
    onEmailParticipants: vi.fn(),
    onReset: vi.fn(),
    onDelete: vi.fn(),
    ...overrides,
  }

  render(<AdvancedOptionsMenu {...props} />)
  return props
}

const openMenu = async (user: ReturnType<typeof userEvent.setup>) =>
  user.click(screen.getByRole('button', { name: 'Advanced options' }))

describe('AdvancedOptionsMenu', () => {
  it('keeps its options out of the way until it is opened', () => {
    renderMenu()

    expect(screen.queryByRole('menuitem', { name: /Export Gift Exchange/ })).not.toBeInTheDocument()
  })

  it('offers every option once opened', async () => {
    const user = userEvent.setup()
    renderMenu()

    await openMenu(user)

    expect(screen.getByRole('menuitem', { name: /Export Gift Exchange/ })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: /^Reset/ })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: /Delete Gift Exchange/ })).toBeInTheDocument()
  })

  // Worst last: somebody scanning down the menu should not meet delete before reset.
  it('puts the delete under the reset', async () => {
    const user = userEvent.setup()
    renderMenu()

    await openMenu(user)

    const labels = screen.getAllByRole('menuitem').map((item) => item.textContent)

    expect(labels).toHaveLength(4)
    expect(labels[2]).toMatch(/^Reset/)
    expect(labels[3]).toMatch(/^Delete Gift Exchange/)
  })

  it('exports and closes', async () => {
    const user = userEvent.setup()
    const { onExport } = renderMenu()

    await openMenu(user)
    await user.click(screen.getByRole('menuitem', { name: /Export Gift Exchange/ }))

    expect(onExport).toHaveBeenCalled()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('opens the participant email and closes', async () => {
    const user = userEvent.setup()
    const { onEmailParticipants } = renderMenu()

    await openMenu(user)
    await user.click(screen.getByRole('menuitem', { name: /Email Participants From Your Address/ }))

    expect(onEmailParticipants).toHaveBeenCalled()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('shows the participant email disabled, with the reason, when there is nobody to write to', async () => {
    const user = userEvent.setup()
    const { onEmailParticipants, emailParticipantsUnavailableReason } = renderMenu({
      canEmailParticipants: false,
    })

    await openMenu(user)

    const item = screen.getByRole('menuitem', { name: /Email Participants From Your Address/ })

    expect(item).toBeDisabled()
    expect(screen.getByText(emailParticipantsUnavailableReason)).toBeInTheDocument()

    await user.click(item)
    expect(onEmailParticipants).not.toHaveBeenCalled()
  })

  it('resets and closes', async () => {
    const user = userEvent.setup()
    const { onReset } = renderMenu()

    await openMenu(user)
    await user.click(screen.getByRole('menuitem', { name: /^Reset/ }))

    expect(onReset).toHaveBeenCalled()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('deletes and closes', async () => {
    const user = userEvent.setup()
    const { onDelete } = renderMenu()

    await openMenu(user)
    await user.click(screen.getByRole('menuitem', { name: /Delete Gift Exchange/ }))

    expect(onDelete).toHaveBeenCalled()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  // Exporting always makes sense; it is a read. Only the two destructive ones have a point past
  // which they do not.
  it('still offers the export once nothing else can be done', async () => {
    const user = userEvent.setup()
    renderMenu({ canReset: false, canDelete: false })

    await openMenu(user)

    expect(screen.getByRole('menuitem', { name: /Export Gift Exchange/ })).toBeEnabled()
  })

  /**
   * Disabled rather than removed. An option that vanishes reads as one that was never there, and
   * the organizer looking for it deserves to be told why it has gone.
   */
  it('shows the reset disabled, with the reason, once invitations have gone out', async () => {
    const user = userEvent.setup()
    const { onReset, resetUnavailableReason } = renderMenu({ canReset: false })

    await openMenu(user)

    const reset = screen.getByRole('menuitem', { name: /^Reset/ })

    expect(reset).toBeDisabled()
    expect(screen.getByText(resetUnavailableReason)).toBeInTheDocument()

    await user.click(reset)
    expect(onReset).not.toHaveBeenCalled()
  })

  it('shows the delete disabled, with the reason, once invitations have gone out', async () => {
    const user = userEvent.setup()
    const { onDelete, deleteUnavailableReason } = renderMenu({ canDelete: false })

    await openMenu(user)

    const deleteItem = screen.getByRole('menuitem', { name: /Delete Gift Exchange/ })

    expect(deleteItem).toBeDisabled()
    expect(screen.getByText(deleteUnavailableReason)).toBeInTheDocument()

    await user.click(deleteItem)
    expect(onDelete).not.toHaveBeenCalled()
  })

  it('says it is exporting while it is', async () => {
    const user = userEvent.setup()
    renderMenu({ isExporting: true })

    await openMenu(user)

    expect(screen.getByRole('menuitem', { name: /Exporting/ })).toBeDisabled()
  })

  // A menu that only closes by choosing something from it is a menu somebody is stuck in.
  it('closes on Escape without choosing anything', async () => {
    const user = userEvent.setup()
    const { onExport, onReset, onDelete } = renderMenu()

    await openMenu(user)
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(onExport).not.toHaveBeenCalled()
    expect(onReset).not.toHaveBeenCalled()
    expect(onDelete).not.toHaveBeenCalled()
  })

  it('closes when something else is clicked', async () => {
    const user = userEvent.setup()
    renderMenu()

    await openMenu(user)
    await user.click(document.body)

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })
})
