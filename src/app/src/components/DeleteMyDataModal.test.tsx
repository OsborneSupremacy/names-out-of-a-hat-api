import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { DeleteMyDataModal } from './DeleteMyDataModal'

function renderModal(overrides: Partial<Parameters<typeof DeleteMyDataModal>[0]> = {}) {
  const props = {
    onClose: vi.fn(),
    onSubmit: vi.fn(async () => Promise.resolve()),
    ...overrides,
  }

  render(<DeleteMyDataModal {...props} />)
  return props
}

const submit = (user: ReturnType<typeof userEvent.setup>) =>
  user.click(screen.getByRole('button', { name: 'Delete My Data' }))

describe('DeleteMyDataModal', () => {
  it('says what is deleted, and that other organizers’ exchanges are not left by it', () => {
    renderModal()

    expect(screen.getByText(/Every gift exchange you have organized/)).toBeInTheDocument()
    expect(screen.getByText(/does not remove you from gift exchanges organized by other people/)).toBeInTheDocument()
    expect(screen.getByText(/not even the administrators/)).toBeInTheDocument()
  })

  // Forgetting is what somebody reaching for this means; refusing everybody else's future exchanges
  // is a decision nobody should make by not noticing a checkbox.
  it('forgets by default and does not refuse to be added by default', () => {
    renderModal()

    expect(screen.getByRole('checkbox', { name: /Forget me/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /add me to a gift exchange again/ })).not.toBeChecked()
  })

  it('sends the defaults and closes', async () => {
    const user = userEvent.setup()
    const { onSubmit, onClose } = renderModal()

    await submit(user)

    expect(onSubmit).toHaveBeenCalledWith({ forgetMe: true, doNotAddAnywhere: false })
    expect(onClose).toHaveBeenCalled()
  })

  it('sends what the checkboxes say', async () => {
    const user = userEvent.setup()
    const { onSubmit } = renderModal()

    await user.click(screen.getByRole('checkbox', { name: /Forget me/ }))
    await user.click(screen.getByRole('checkbox', { name: /add me to a gift exchange again/ }))
    await submit(user)

    expect(onSubmit).toHaveBeenCalledWith({ forgetMe: false, doNotAddAnywhere: true })
  })

  it('keeps a refusal in the dialog', async () => {
    const user = userEvent.setup()
    const { onClose } = renderModal({
      onSubmit: vi.fn(async () => {
        throw new Error('Something went wrong on our end')
      }),
    })

    await submit(user)

    expect(screen.getByText(/Something went wrong on our end/)).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('closes without deleting when cancelled', async () => {
    const user = userEvent.setup()
    const { onSubmit, onClose } = renderModal()

    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(onSubmit).not.toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })
})
