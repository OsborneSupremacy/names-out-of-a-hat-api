import { useLocation, useNavigate } from 'react-router-dom'
import { useState, useEffect, useRef } from 'react'
import { getHats, createHat, HatMetadata } from '../api'
import { formatHatStatus } from '../hatStatus'
import { formatRelativeTime, formatAbsoluteTime } from '../relativeTime'
import { Header } from '../components/Header'
import { Footer } from '../components/Footer'
import { CreateHatModal } from '../components/CreateHatModal'

interface HomeProps {
  userEmail: string
  onSignOut: () => void
}

export function Home({ userEmail, onSignOut }: HomeProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const [hats, setHats] = useState<HatMetadata[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')
  const [showCreateModal, setShowCreateModal] = useState(false)
  // null until the hats response arrives, so the greeting never guesses.
  const [organizerName, setOrganizerName] = useState<string | null>(null)
  // An organizer with nothing to look at is here to create something, so the dialog opens for them.
  // Guarded so that dismissing it leaves them on the empty state rather than reopening it.
  const openedCreateForEmptyList = useRef(false)
  // Set once somebody has asked for their data to be deleted, here or on the page they came from.
  // The deleting happens on a queue, so a list fetched now can still hold exchanges that are about
  // to go; they are hidden rather than shown and then taken away.
  const [dataDeletionRequested, setDataDeletionRequested] = useState(
    () => (location.state as { dataDeletionRequested?: boolean } | null)?.dataDeletionRequested === true
  )
  const hideHats = useRef(dataDeletionRequested)

  // Read once, above, and then removed from the history entry, so a refresh a day later does not
  // announce a deletion that finished long ago.
  useEffect(() => {
    if ((location.state as { dataDeletionRequested?: boolean } | null)?.dataDeletionRequested) {
      navigate(location.pathname, { replace: true, state: null })
    }
  }, [location.pathname, location.state, navigate])

  useEffect(() => {
    async function loadHats() {
      try {
        const response = await getHats(userEmail)
        setOrganizerName(response.organizerName)

        // Nor is the create dialog opened for somebody who has just emptied the list on purpose.
        if (hideHats.current) return

        setHats(response.hats)

        if (response.hats.length === 0 && !openedCreateForEmptyList.current) {
          openedCreateForEmptyList.current = true
          setShowCreateModal(true)
        }
      } catch (err) {
        console.error('Error loading gift exchanges:', err)
        setError(err instanceof Error ? err.message : 'Failed to load your gift exchanges')
      } finally {
        setLoading(false)
      }
    }

    if (userEmail) {
      loadHats()
    }
  }, [userEmail])

  const handleCreateNew = () => {
    setShowCreateModal(true)
  }

  const handleCreateSubmit = async (hatName: string, name: string) => {
    const { hatId } = await createHat({
      hatName,
      organizerName: name,
      organizerEmail: userEmail,
    })

    // A new exchange has nothing in it yet, so send the organizer straight to where they fill it in
    // rather than back to a list they have just left.
    navigate(`/gift-exchange/${hatId}`)
  }

  const handleDataDeleted = () => {
    hideHats.current = true
    setHats([])
    setDataDeletionRequested(true)
  }

  const handleHatClick = (hatId: string) => {
    navigate(`/gift-exchange/${hatId}`)
  }

  return (
    <div className="app-container">
      <Header
        userEmail={userEmail}
        givenName={organizerName}
        onSignOut={onSignOut}
        onNameUpdated={setOrganizerName}
        onDataDeleted={handleDataDeleted}
      />

      <main className="main-content">
        <div className="content-wrapper">
          {/* A non-breaking space holds the line's height so the greeting does not shift the
              page when it resolves. */}
          <h2>{organizerName === null ? '\u00A0' : `Hello ${organizerName || 'there'}!`}</h2>
          <p>Welcome to Names Out of a Hat!</p>

          {dataDeletionRequested && (
            <div className="home-notice" role="status">
              <span>Your gift exchanges are being deleted. This can take a minute.</span>
              <button
                type="button"
                className="home-notice-dismiss"
                onClick={() => setDataDeletionRequested(false)}
                aria-label="Dismiss"
              >
                ×
              </button>
            </div>
          )}

          {loading ? (
            <p>Loading your gift exchanges...</p>
          ) : error ? (
            <p className="error-message">{error}</p>
          ) : (
            <>
              {hats.length > 0 ? (
                <div className="gift-exchanges-section">
                  <div className="section-header">
                    <h3>Your Gift Exchanges</h3>
                    <button className="primary-button" onClick={handleCreateNew}>
                      Create New Gift Exchange
                    </button>
                  </div>
                  <ul className="gift-exchanges-list">
                    {hats.map((hat) => {
                      // Empty for a timestamp that cannot be phrased — the minimum date the API
                      // uses for "not known" among them — and the line is left out entirely rather
                      // than rendered blank, so the pill keeps its own height.
                      const statusAge = formatRelativeTime(hat.statusUpdatedAt)

                      return (
                        <li
                          key={hat.hatId}
                          className="gift-exchange-item"
                          onClick={() => handleHatClick(hat.hatId)}
                        >
                          <div className="gift-exchange-info">
                            <strong>{hat.hatName}</strong>
                          </div>
                          <div className="gift-exchange-status">
                            <span className={`status-pill ${hat.status.toLowerCase().replace(/_/g, '-')}`}>
                              {formatHatStatus(hat.status)}
                            </span>
                            {statusAge && (
                              // Under the pill rather than beside the name: it is how long the hat
                              // has been at that status, not when the hat was last touched.
                              <span className="status-age" title={formatAbsoluteTime(hat.statusUpdatedAt)}>
                                {statusAge}
                              </span>
                            )}
                          </div>
                        </li>
                      )
                    })}
                  </ul>
                </div>
              ) : (
                <div className="empty-state">
                  <p>You don't have any Gift Exchanges</p>
                  <button className="primary-button" onClick={handleCreateNew}>
                    Create a Gift Exchange
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      </main>

      <Footer />

      {showCreateModal && (
        <CreateHatModal
          organizerName={organizerName ?? ''}
          organizerEmail={userEmail}
          onClose={() => setShowCreateModal(false)}
          onSubmit={handleCreateSubmit}
        />
      )}
    </div>
  )
}
