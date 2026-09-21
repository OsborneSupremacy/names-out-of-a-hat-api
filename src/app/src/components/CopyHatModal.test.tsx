import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { CopyHatModal, suggestCopyName } from './CopyHatModal'
import { Participant } from '../api'

function person(name: string) {
  return { name, email: `${name.toLowerCase()}@example.com` }
}

function participant(
  name: string,
  pickedRecipient: string,
  eligibleRecipients: string[]
): Participant {
  return {
    person: person(name),
    pickedRecipient: person(pickedRecipient),
    eligibleRecipients: eligibleRecipients.map(person),
    // A face, because every participant has one. Nothing this dialog does looks at it.
    emoji: '😀',
    // Nothing heard. Copying a hat has nothing to do with what was delivered from the one it came
    // from, so these stay empty here.
    deliveryStatus: '',
    deliveryDetail: '',
    deliveryMessageType: '',
    // The minimum date, which is how the API spells a timestamp it does not have.
    deliveryOccurredAt: '0001-01-01T00:00:00+00:00',
  }
}

// Alpha drew Beta and could have drawn either of the others; Beta drew Charlie and had nobody
// else to draw, so excluding last year's pick strands them.
const participants = [
  participant('Alpha', 'Beta', ['Beta', 'Charlie']),
  participant('Beta', 'Charlie', ['Charlie']),
  participant('Charlie', 'Alpha', ['Alpha', 'Beta']),
]

function renderModal(overrides: Partial<Parameters<typeof CopyHatModal>[0]> = {}) {
  const props = {
    sourceHatName: 'Family Christmas 2025',
    participants,
    onClose: vi.fn(),
    onSubmit: vi.fn(async () => Promise.resolve()),
    ...overrides,
  }

  render(<CopyHatModal {...props} />)
  return props
}

describe('suggestCopyName', () => {
  it('moves a year in the name on by one', () => {
    expect(suggestCopyName('Family Christmas 2025')).toBe('Family Christmas 2026')
  })

  it('uses the last year in the name', () => {
    expect(suggestCopyName('2025 Reunion 2026')).toBe('2025 Reunion 2027')
  })

  it('falls back to (Copy) when there is no year to move on', () => {
    expect(suggestCopyName('The Office Draw')).toBe('The Office Draw (Copy)')
  })

  // Digits that are not a year should be left alone rather than quietly rewritten.
  it('ignores numbers that are not years', () => {
    expect(suggestCopyName('Draw 12345')).toBe('Draw 12345 (Copy)')
  })

  // The name is capped at 50 characters server-side.
  it('keeps the suggestion within the length the server accepts', () => {
    expect(suggestCopyName('x'.repeat(50)).length).toBeLessThanOrEqual(50)
  })
})

describe('CopyHatModal', () => {
  it('suggests a name for the copy', () => {
    renderModal()

    expect(screen.getByLabelText(/new gift exchange name/i)).toHaveValue('Family Christmas 2026')
  })

  it('excludes previous recipients by default', () => {
    renderModal()

    expect(screen.getByRole('checkbox')).toBeChecked()
  })

  it('submits the name and the exclusion choice', async () => {
    const user = userEvent.setup()
    const props = renderModal()

    await user.click(screen.getByRole('button', { name: /create copy/i }))

    expect(props.onSubmit).toHaveBeenCalledWith('Family Christmas 2026', true)
  })

  it('submits without the exclusion when it is unticked', async () => {
    const user = userEvent.setup()
    const props = renderModal()

    await user.click(screen.getByRole('checkbox'))
    await user.click(screen.getByRole('button', { name: /create copy/i }))

    expect(props.onSubmit).toHaveBeenCalledWith('Family Christmas 2026', false)
  })

  it('does not submit an empty name', async () => {
    const user = userEvent.setup()
    const props = renderModal()

    await user.clear(screen.getByLabelText(/new gift exchange name/i))
    await user.click(screen.getByRole('button', { name: /create copy/i }))

    expect(props.onSubmit).not.toHaveBeenCalled()
    expect(screen.getByText(/cannot be empty/i)).toBeInTheDocument()
  })

  // Better to find out here than at the shake, but it is a warning and not a block.
  it('warns about anyone left with nobody to draw', () => {
    renderModal()

    expect(screen.getByText(/Beta/)).toBeInTheDocument()
    expect(screen.getByText(/nobody left to draw/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /create copy/i })).toBeEnabled()
  })

  it('drops the warning when previous recipients are allowed again', async () => {
    const user = userEvent.setup()
    renderModal()

    await user.click(screen.getByRole('checkbox'))

    expect(screen.queryByText(/nobody left to draw/i)).not.toBeInTheDocument()
  })

  it('surfaces a failure without closing', async () => {
    const user = userEvent.setup()
    const props = renderModal({
      onSubmit: vi.fn(async () => {
        throw new Error('A gift exchange with this name already exists.')
      }),
    })

    await user.click(screen.getByRole('button', { name: /create copy/i }))

    expect(screen.getByText(/already exists/i)).toBeInTheDocument()
    expect(props.onClose).not.toHaveBeenCalled()
  })
})
