import { RefObject, useEffect } from 'react'

/**
 * Closes an open menu on a click outside it or on Escape.
 *
 * The same dismissal the profile menu has, and for the same reason: a menu that only closes by
 * choosing something from it is a menu somebody is stuck in. Shared by the organizer's and the
 * participant's options menus so that the two cannot come to disagree about how to get out.
 */
export function useMenuDismissal(isOpen: boolean, close: () => void, menuRef: RefObject<HTMLElement | null>) {
  useEffect(() => {
    if (!isOpen) return

    function handleClickOutside(event: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        close()
      }
    }

    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        close()
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleEscape)

    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleEscape)
    }
  }, [isOpen, close, menuRef])
}
