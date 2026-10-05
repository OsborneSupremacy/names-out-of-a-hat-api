import { useCallback, useRef, useState } from 'react'
import { useMenuDismissal } from './useMenuDismissal'
import './AdvancedOptionsMenu.css'

interface AdvancedOptionsMenuProps {
  /** False once invitations have gone out, which is as far as a reset can reach back. */
  canReset: boolean
  /** Why not, shown under the disabled item. Only read when {@link canReset} is false. */
  resetUnavailableReason: string
  /** False once invitations have gone out: there are people expecting something by then. */
  canDelete: boolean
  /** Why not, shown under the disabled item. Only read when {@link canDelete} is false. */
  deleteUnavailableReason: string
  isExporting: boolean
  /** False when there is nobody but the organizer to write to. */
  canEmailParticipants: boolean
  /** Why not, shown under the disabled item. Only read when {@link canEmailParticipants} is false. */
  emailParticipantsUnavailableReason: string
  onExport: () => void
  onEmailParticipants: () => void
  onReset: () => void
  onDelete: () => void
}

/**
 * The things an organizer needs rarely and would not want to meet by accident.
 *
 * Behind a menu rather than beside the buttons that move an exchange forward, because none of these
 * do: one takes a copy away, one writes to the participants from outside the app, and the other two
 * throw the setup out. Putting them in the same row as
 * "Shake the Hat" would make the row a place to be careful, which is the opposite of what the rest
 * of that page is for.
 *
 * The two destructive ones sit last and in the order they get worse — reset keeps the people,
 * delete does not.
 */
export function AdvancedOptionsMenu({
  canReset,
  resetUnavailableReason,
  canDelete,
  deleteUnavailableReason,
  isExporting,
  canEmailParticipants,
  emailParticipantsUnavailableReason,
  onExport,
  onEmailParticipants,
  onReset,
  onDelete,
}: AdvancedOptionsMenuProps) {
  const [isOpen, setIsOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  const close = useCallback(() => setIsOpen(false), [])
  useMenuDismissal(isOpen, close, menuRef)

  const choose = (action: () => void) => {
    setIsOpen(false)
    action()
  }

  return (
    <div className="advanced-options" ref={menuRef}>
      <button
        type="button"
        className="advanced-options-button"
        onClick={() => setIsOpen(!isOpen)}
        aria-label="Advanced options"
        aria-haspopup="menu"
        aria-expanded={isOpen}
      >
        {/* Drawn rather than typed, so it is the same three bars at every font size. */}
        <span className="advanced-options-bar" aria-hidden="true"></span>
        <span className="advanced-options-bar" aria-hidden="true"></span>
        <span className="advanced-options-bar" aria-hidden="true"></span>
      </button>

      {isOpen && (
        <div className="advanced-options-menu" role="menu">
          <button
            type="button"
            role="menuitem"
            className="advanced-options-item"
            onClick={() => choose(onExport)}
            disabled={isExporting}
          >
            <span className="advanced-options-item-label">
              {isExporting ? 'Exporting...' : 'Export Gift Exchange'}
            </span>
            <span className="advanced-options-item-hint">
              Download everything in this gift exchange as a JSON file.
            </span>
          </button>

          <button
            type="button"
            role="menuitem"
            className="advanced-options-item"
            onClick={() => choose(onEmailParticipants)}
            disabled={!canEmailParticipants}
          >
            <span className="advanced-options-item-label">Email Participants From Your Address</span>
            <span className="advanced-options-item-hint">
              {canEmailParticipants
                ? 'Write to everyone from your own email, which is less likely to land in spam.'
                : emailParticipantsUnavailableReason}
            </span>
          </button>

          {/*
            Shown disabled rather than removed once invitations are out. An option that vanishes
            reads as one that was never there, and the organizer looking for it deserves to be told
            why it has gone rather than left to wonder whether they imagined it.
          */}
          <button
            type="button"
            role="menuitem"
            className="advanced-options-item advanced-options-item-danger"
            onClick={() => choose(onReset)}
            disabled={!canReset}
          >
            <span className="advanced-options-item-label">Reset</span>
            <span className="advanced-options-item-hint">
              {canReset
                ? 'Keep everybody, and start the setup again from scratch.'
                : resetUnavailableReason}
            </span>
          </button>

          <button
            type="button"
            role="menuitem"
            className="advanced-options-item advanced-options-item-danger"
            onClick={() => choose(onDelete)}
            disabled={!canDelete}
          >
            <span className="advanced-options-item-label">Delete Gift Exchange</span>
            <span className="advanced-options-item-hint">
              {canDelete
                ? 'Throw the whole thing away, participants and all. This cannot be undone.'
                : deleteUnavailableReason}
            </span>
          </button>
        </div>
      )}
    </div>
  )
}
