import { useState, useRef, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { updateProfile, deleteMyData, DeleteMyDataRequest } from '../api'
import { EditNameModal } from './EditNameModal'
import { DeleteMyDataModal } from './DeleteMyDataModal'
import './Header.css'

interface HeaderProps {
  userEmail: string
  /**
   * null while the name is still being loaded, '' when the user genuinely has no name yet.
   *
   * The distinction matters: falling back to the email initial while loading is not a placeholder,
   * it is a different answer, and the UI visibly corrects itself once the real name arrives.
   */
  givenName: string | null
  onSignOut: () => void
  onNameUpdated: (name: string) => void
  /**
   * Called when the user asked for their data to be deleted but not to be forgotten, so they stay
   * signed in. The page decides what that means for what it is showing.
   */
  onDataDeleted: () => void
}

export function Header({ userEmail, givenName, onSignOut, onNameUpdated, onDataDeleted }: HeaderProps) {
  const [isMenuOpen, setIsMenuOpen] = useState(false)
  const [showEditName, setShowEditName] = useState(false)
  const [showDeleteMyData, setShowDeleteMyData] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)

  const handleNameSubmit = async (name: string) => {
    await updateProfile({ name })
    onNameUpdated(name)
  }

  // Somebody who asked to be forgotten has no name left to be signed in as, so they are signed out
  // rather than left looking at a page that still greets them.
  const handleDeleteMyData = async (request: DeleteMyDataRequest) => {
    await deleteMyData(request)

    if (request.forgetMe) {
      onSignOut()
    } else {
      onDataDeleted()
    }
  }

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setIsMenuOpen(false)
      }
    }

    if (isMenuOpen) {
      document.addEventListener('mousedown', handleClickOutside)
    }

    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [isMenuOpen])

  return (
    <header className="app-header">
      <div className="header-content">
        <div className="app-title">
          <h1>
            {/*
              A router Link rather than an <a href="/">: a plain anchor reloads the whole bundle
              and throws away the session state held in App, which is the difference between
              returning home and starting the app again.

              It stays a link on the home page itself, where it goes nowhere. Making it conditional
              would mean the one element every page shares is sometimes focusable and sometimes not,
              and somebody tabbing through would find it missing without being told why.
            */}
            <Link to="/" className="app-logo-link">
              <img
                className="app-logo"
                src="/logo-horizontal.png"
                alt="Names Out of a Hat"
                width={960}
                height={323}
              />
            </Link>
          </h1>
        </div>

        <div className="profile-section" ref={menuRef}>
          <button
            className="profile-button"
            onClick={() => setIsMenuOpen(!isMenuOpen)}
            aria-label="Profile menu"
          >
            <div className="profile-icon">
              {givenName === null
                ? '\u00A0'
                : (givenName.charAt(0) || userEmail.charAt(0)).toUpperCase()}
            </div>
          </button>

          {isMenuOpen && (
            <div className="profile-menu">
              <div className="profile-menu-header">
                {givenName !== null && <div className="profile-name">{givenName || 'User'}</div>}
                <div className="profile-email">{userEmail}</div>
              </div>
              <div className="profile-menu-divider"></div>
              <button
                className="profile-menu-item"
                onClick={() => {
                  setIsMenuOpen(false)
                  setShowEditName(true)
                }}
              >
                Edit Name
              </button>
              <button
                className="profile-menu-item"
                onClick={() => {
                  setIsMenuOpen(false)
                  setShowDeleteMyData(true)
                }}
              >
                Delete My Data
              </button>
              <button
                className="profile-menu-item"
                onClick={() => {
                  setIsMenuOpen(false)
                  onSignOut()
                }}
              >
                Sign Out
              </button>
            </div>
          )}
        </div>
      </div>

      {showEditName && (
        <EditNameModal
          currentName={givenName ?? ''}
          onClose={() => setShowEditName(false)}
          onSubmit={handleNameSubmit}
        />
      )}

      {showDeleteMyData && (
        <DeleteMyDataModal
          onClose={() => setShowDeleteMyData(false)}
          onSubmit={handleDeleteMyData}
        />
      )}
    </header>
  )
}
