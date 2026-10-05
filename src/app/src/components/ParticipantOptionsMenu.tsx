import { useCallback, useRef, useState } from 'react'
import { useMenuDismissal } from './useMenuDismissal'
import './AdvancedOptionsMenu.css'

interface ParticipantOptionsMenuProps {
  /** False for the organizer, who is never offered a way out of their own exchange. */
  canLeave: boolean
  onLeave: () => void
}

/**
 * The participant's counterpart of the organizer's advanced options: the things somebody taking part
 * would need rarely and would not want to meet by accident.
 *
 * Leaving lives here rather than beside the gift ideas, for the reason the organizer's reset and
 * delete live behind their menu. It is the one thing on this page that cannot be taken back — the
 * draw is redone without them and they cannot be added again — and a button in the same row as
 * "Share" would make the page a place to be careful.
 *
 * The same look as the organizer's menu, so the three bars mean the same thing on both pages.
 */
export function ParticipantOptionsMenu({ canLeave, onLeave }: ParticipantOptionsMenuProps) {
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
        <span className="advanced-options-bar" aria-hidden="true"></span>
        <span className="advanced-options-bar" aria-hidden="true"></span>
        <span className="advanced-options-bar" aria-hidden="true"></span>
      </button>

      {isOpen && (
        <div className="advanced-options-menu" role="menu">
          {/* Shown disabled rather than removed for the organizer, as the organizer's own menu
              does: an option that vanishes reads as one that was never there. */}
          <button
            type="button"
            role="menuitem"
            className="advanced-options-item advanced-options-item-danger"
            onClick={() => choose(onLeave)}
            disabled={!canLeave}
          >
            <span className="advanced-options-item-label">Leave Gift Exchange</span>
            <span className="advanced-options-item-hint">
              {canLeave
                ? "Take yourself out of this gift exchange. You can't be added back."
                : "You organized this gift exchange, so you can't leave it."}
            </span>
          </button>
        </div>
      )}
    </div>
  )
}
