import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useState, useEffect, useRef, useCallback, KeyboardEvent } from 'react'
import { getHats, createHat, HatMetadata } from '../api'
import { formatHatStatus } from '../hatStatus'
import { formatRelativeTime, formatAbsoluteTime } from '../relativeTime'
import { Header } from '../components/Header'
import { Footer } from '../components/Footer'
import { CreateHatModal } from '../components/CreateHatModal'
import { Pagination } from '../components/Pagination'
import { ParticipatingGiftExchanges } from '../components/ParticipatingGiftExchanges'

/** The page in the URL, or the first page for anything that is not a positive whole number. */
function parsePage(value: string | null): number {
  const page = Number(value)
  return Number.isInteger(page) && page >= 1 ? page : 1
}

type HomeTab = 'organized' | 'joined'

const HOME_TABS: { tab: HomeTab; label: string }[] = [
  { tab: 'organized', label: 'Gift Exchanges you organized' },
  { tab: 'joined', label: "Gift Exchanges you're part of" },
]

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
  // Its own parameter, so that paging one list leaves the other where it was.
  const participatingPage = parsePage(searchParams.get('joinedPage'))
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
  // null until the list of exchanges they are part of has answered.
  const [participatingCount, setParticipatingCount] = useState<number | null>(null)
  const [participatingFailed, setParticipatingFailed] = useState(false)
  const handleParticipatingFailed = useCallback(() => setParticipatingFailed(true), [])
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
  // The same, for the list of exchanges they are part of, which renders from state rather than
  // reading the ref. Unlike the notice above it, dismissing nothing brings it back.
  const [hideParticipating, setHideParticipating] = useState(dataDeletionRequested)
  // The name of an exchange they have just left, from the page they left it on, which cannot say so
  // itself: they are no longer in the exchange it shows.
  const [leftGiftExchange, setLeftGiftExchange] = useState(
    () => (location.state as { leftGiftExchange?: string } | null)?.leftGiftExchange ?? ''
  )

  // Read once, above, and then removed from the history entry, so a refresh a day later does not
  // announce a deletion that finished long ago.
  useEffect(() => {
    const state = location.state as { dataDeletionRequested?: boolean; leftGiftExchange?: string } | null
    if (state?.dataDeletionRequested || state?.leftGiftExchange) {
      navigate(location.pathname, { replace: true, state: null })
    }
  }, [location.pathname, location.state, navigate])

  // Page 1 has no parameter at all, so the plain address is the first page of both lists.
  const goToPageOf = useCallback(
    (key: string, target: number, replace: boolean) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current)
          if (target <= 1) next.delete(key)
          else next.set(key, String(target))
          return next
        },
        { replace }
      )
    },
    [setSearchParams]
  )

  const goToPage = useCallback(
    (target: number, replace = false) => goToPageOf('page', target, replace),
    [goToPageOf]
  )

  const goToParticipatingPage = useCallback(
    (target: number, replace = false) => goToPageOf('joinedPage', target, replace),
    [goToPageOf]
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

  // Somebody with nothing to look at is here to create something, so the dialog opens for them --
  // but only once both lists have answered, because somebody who has only ever been invited is
  // here to look at that, not to be asked to organize.
  useEffect(() => {
    if (loading || error || hideHats.current || openedCreateForEmptyList.current) return

    if (totalCount === 0 && participatingCount === 0) {
      openedCreateForEmptyList.current = true
      setShowCreateModal(true)
    }
  }, [loading, error, totalCount, participatingCount])

  const totalPages = pageSize > 0 ? Math.ceil(totalCount / pageSize) : 1

  // Tabs only for somebody who has something under the second one. Most people who sign in are
  // organizers and nothing else, and a tab bar with one live tab would be one more thing for them
  // to read past.
  const showTabs = !hideParticipating && (participatingFailed || (participatingCount ?? 0) > 0)

  // Kept in the URL, like the pages, so coming back from an exchange lands on the tab it was
  // opened from. Without one, the tab with something in it: somebody who has only ever been
  // invited is here to look at that.
  const requestedTab = searchParams.get('tab')
  const activeTab: HomeTab = !showTabs
    ? 'organized'
    : requestedTab === 'joined' || requestedTab === 'organized'
      ? requestedTab
      : !loading && !error && totalCount === 0
        ? 'joined'
        : 'organized'

  // Always written out, rather than left off for the default, because which tab is the default
  // depends on what is in them.
  const selectTab = (tab: HomeTab) => {
    setSearchParams((current) => {
      const next = new URLSearchParams(current)
      next.set('tab', tab)
      return next
    })
  }

  // Arrow keys move between tabs, as they do in any other tab list.
  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
    event.preventDefault()
    const target: HomeTab = activeTab === 'organized' ? 'joined' : 'organized'
    selectTab(target)
    document.getElementById(`home-tab-${target}`)?.focus()
  }

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
    setHideParticipating(true)
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

          {leftGiftExchange && (
            <div className="home-notice" role="status">
              <span>You've left {leftGiftExchange}. You can't be added back to it.</span>
              <button
                type="button"
                className="home-notice-dismiss"
                onClick={() => setLeftGiftExchange('')}
                aria-label="Dismiss"
              >
                ×
              </button>
            </div>
          )}

          {showTabs && (
            <div className="home-tabs" role="tablist" aria-label="Gift exchanges">
              {HOME_TABS.map(({ tab, label }) => (
                <button
                  key={tab}
                  type="button"
                  role="tab"
                  id={`home-tab-${tab}`}
                  className={`home-tab${activeTab === tab ? ' active' : ''}`}
                  aria-selected={activeTab === tab}
                  aria-controls={`home-panel-${tab}`}
                  tabIndex={activeTab === tab ? 0 : -1}
                  onClick={() => selectTab(tab)}
                  onKeyDown={handleTabKeyDown}
                >
                  {label}
                </button>
              ))}
            </div>
          )}

          <div
            id="home-panel-organized"
            role={showTabs ? 'tabpanel' : undefined}
            aria-labelledby={showTabs ? 'home-tab-organized' : undefined}
            hidden={activeTab !== 'organized'}
          >
            {loading ? (
              <p>Loading your gift exchanges...</p>
            ) : error ? (
              <p className="error-message">{error}</p>
            ) : (
              <>
                {totalCount > 0 ? (
                  <div className="gift-exchanges-section">
                    <div className={`section-header${showTabs ? ' section-header-actions-only' : ''}`}>
                      {/* The tab says it when there are tabs. */}
                      {!showTabs && <h3>Gift Exchanges you organized</h3>}
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
                    <Pagination
                      page={page}
                      totalPages={totalPages}
                      busy={pageLoading}
                      label="Gift exchange pages"
                      onPageChange={goToPage}
                    />
                  </div>
                ) : (
                  <div className="empty-state">
                    <p>
                      {participatingCount
                        ? "You haven't organized any Gift Exchanges"
                        : "You don't have any Gift Exchanges"}
                    </p>
                    <button className="primary-button" onClick={handleCreateNew}>
                      Create a Gift Exchange
                    </button>
                  </div>
                )}
              </>
            )}
          </div>

          {/* Mounted whether or not its tab is showing: its count decides whether there are tabs. */}
          <div
            id="home-panel-joined"
            role={showTabs ? 'tabpanel' : undefined}
            aria-labelledby={showTabs ? 'home-tab-joined' : undefined}
            hidden={activeTab !== 'joined'}
          >
            <ParticipatingGiftExchanges
              userEmail={userEmail}
              page={participatingPage}
              onPageChange={goToParticipatingPage}
              hidden={hideParticipating}
              onLoaded={setParticipatingCount}
              onFailed={handleParticipatingFailed}
            />
          </div>
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
