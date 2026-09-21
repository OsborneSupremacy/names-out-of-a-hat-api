import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useState, useEffect, useRef, useCallback } from 'react'
import { getHats, createHat, HatMetadata } from '../api'
import { formatHatStatus } from '../hatStatus'
import { formatRelativeTime, formatAbsoluteTime } from '../relativeTime'
import { Header } from '../components/Header'
import { Footer } from '../components/Footer'
import { CreateHatModal } from '../components/CreateHatModal'

/** The page in the URL, or the first page for anything that is not a positive whole number. */
function parsePage(value: string | null): number {
  const page = Number(value)
  return Number.isInteger(page) && page >= 1 ? page : 1
}

interface HomeProps {
  userEmail: string
  onSignOut: () => void
}

export function Home({ userEmail, onSignOut }: HomeProps) {
  const navigate = useNavigate()
  const location = useLocation()
  // Kept in the URL so that coming back from an exchange, or refreshing, lands on the same page.
  const [searchParams, setSearchParams] = useSearchParams()
  const page = parsePage(searchParams.get('page'))
  const [hats, setHats] = useState<HatMetadata[]>([])
  // Across every page, which is what decides between the list and the empty state: an empty page
  // is not the same as having no exchanges.
  const [totalCount, setTotalCount] = useState(0)
  const [pageSize, setPageSize] = useState(0)
  // The first load only. Changing page keeps the current list on screen, dimmed, instead.
  const [loading, setLoading] = useState(true)
  const [pageLoading, setPageLoading] = useState(false)
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

  // Page 1 has no parameter at all, so the plain address is the first page.
  const goToPage = useCallback(
    (target: number, replace = false) => {
      setSearchParams(target <= 1 ? {} : { page: String(target) }, { replace })
    },
    [setSearchParams]
  )

  useEffect(() => {
    // A page answered after the organizer has already moved on to another is dropped.
    let cancelled = false

    async function loadHats() {
      setPageLoading(true)

      try {
        const response = await getHats(userEmail, page)
        if (cancelled) return

        setOrganizerName(response.organizerName)

        // Nor is the create dialog opened for somebody who has just emptied the list on purpose.
        if (hideHats.current) {
          setLoading(false)
          setPageLoading(false)
          return
        }

        // Past the end: a bookmarked page that no longer exists, or the last exchange on it
        // deleted. The last page that does exist is shown instead, still under the loading state.
        if (response.totalCount > 0 && response.hats.length === 0) {
          goToPage(Math.ceil(response.totalCount / response.pageSize), true)
          return
        }

        setHats(response.hats)
        setTotalCount(response.totalCount)
        setPageSize(response.pageSize)
        setLoading(false)
        setPageLoading(false)

        if (response.totalCount === 0 && !openedCreateForEmptyList.current) {
          openedCreateForEmptyList.current = true
          setShowCreateModal(true)
        }
      } catch (err) {
        if (cancelled) return
        console.error('Error loading gift exchanges:', err)
        setError(err instanceof Error ? err.message : 'Failed to load your gift exchanges')
        setLoading(false)
        setPageLoading(false)
      }
    }

    if (userEmail) {
      loadHats()
    }

    return () => {
      cancelled = true
    }
  }, [userEmail, page, goToPage])

  const totalPages = pageSize > 0 ? Math.ceil(totalCount / pageSize) : 1

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
    setTotalCount(0)
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
              {totalCount > 0 ? (
                <div className="gift-exchanges-section">
                  <div className="section-header">
                    <h3>Your Gift Exchanges</h3>
                    <button className="primary-button" onClick={handleCreateNew}>
                      Create New Gift Exchange
                    </button>
                  </div>
                  <ul className={`gift-exchanges-list${pageLoading ? ' page-loading' : ''}`} aria-busy={pageLoading}>
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
                  {totalPages > 1 && (
                    <nav className="pagination" aria-label="Gift exchange pages">
                      <button
                        type="button"
                        className="pagination-button"
                        onClick={() => goToPage(page - 1)}
                        disabled={page <= 1 || pageLoading}
                      >
                        ‹ Previous
                      </button>
                      <span className="pagination-status">
                        Page {page} of {totalPages}
                      </span>
                      <button
                        type="button"
                        className="pagination-button"
                        onClick={() => goToPage(page + 1)}
                        disabled={page >= totalPages || pageLoading}
                      >
                        Next ›
                      </button>
                    </nav>
                  )}
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
