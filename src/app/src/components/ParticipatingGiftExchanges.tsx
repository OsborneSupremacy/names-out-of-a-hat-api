import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getParticipatingHats, ParticipatingHatMetadata } from '../api'
import { formatExchangeDate } from '../exchangeDate'
import { Pagination } from './Pagination'

interface ParticipatingGiftExchangesProps {
  userEmail: string
  page: number
  onPageChange: (page: number, replace?: boolean) => void
  /** After a request to delete their data, when what is listed here is about to go. */
  hidden: boolean
  /** How many there are across every page, once that is known. */
  onLoaded: (totalCount: number) => void
}

/**
 * The exchanges somebody has been invited to, under the ones they organized.
 *
 * Renders nothing at all for somebody in none of them. Most people who sign in are organizers, and
 * a heading over an empty list would be one more thing on the page for them to read past.
 */
export function ParticipatingGiftExchanges({
  userEmail,
  page,
  onPageChange,
  hidden,
  onLoaded,
}: ParticipatingGiftExchangesProps) {
  const navigate = useNavigate()
  const [hats, setHats] = useState<ParticipatingHatMetadata[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [pageSize, setPageSize] = useState(0)
  const [pageLoading, setPageLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    // A page answered after they have already moved on to another is dropped.
    let cancelled = false

    async function load() {
      setPageLoading(true)

      try {
        const response = await getParticipatingHats(userEmail, page)
        if (cancelled) return

        // Past the end, for the same reasons the organizer's list can be: shown the last page
        // that does exist instead.
        if (response.totalCount > 0 && response.hats.length === 0) {
          onPageChange(Math.ceil(response.totalCount / response.pageSize), true)
          return
        }

        setHats(response.hats)
        setTotalCount(response.totalCount)
        setPageSize(response.pageSize)
        setError('')
        setPageLoading(false)
        onLoaded(response.totalCount)
      } catch (err) {
        if (cancelled) return
        console.error('Error loading the gift exchanges you are part of:', err)
        setError(err instanceof Error ? err.message : 'Failed to load the gift exchanges you are part of')
        setPageLoading(false)
        onLoaded(0)
      }
    }

    if (userEmail) {
      load()
    }

    return () => {
      cancelled = true
    }
  }, [userEmail, page, onPageChange, onLoaded])

  if (hidden) return null

  if (error) {
    return (
      <div className="gift-exchanges-section participating-section">
        <h3>Gift Exchanges you're part of</h3>
        <p className="error-message">{error}</p>
      </div>
    )
  }

  if (totalCount === 0) return null

  const totalPages = pageSize > 0 ? Math.ceil(totalCount / pageSize) : 1

  return (
    <div className="gift-exchanges-section participating-section">
      <div className="section-header">
        <h3>Gift Exchanges you're part of</h3>
      </div>
      <ul className={`gift-exchanges-list${pageLoading ? ' page-loading' : ''}`} aria-busy={pageLoading}>
        {hats.map((hat) => {
          const revealed = hat.status === 'CLOSED'
          const exchangeDate = formatExchangeDate(hat.exchangeDate)

          return (
            <li
              key={hat.hatId}
              className="gift-exchange-item"
              onClick={() => navigate(`/participating/${hat.hatId}`)}
            >
              <div className="gift-exchange-info">
                <strong>{hat.hatName}</strong>
                <div className="gift-exchange-organizer">Organized by {hat.organizerName}</div>
              </div>
              <div className="gift-exchange-status">
                {/*
                  * Two states rather than the organizer's six: somebody taking part only ever
                  * sees an exchange after it has been drawn, and the one thing that changes for
                  * them afterwards is whether everybody's pick is out.
                  */}
                <span className={`status-pill${revealed ? ' sent' : ''}`}>
                  {revealed ? 'Revealed' : 'Invited'}
                </span>
                {exchangeDate && <span className="status-age">{exchangeDate}</span>}
              </div>
            </li>
          )
        })}
      </ul>
      <Pagination
        page={page}
        totalPages={totalPages}
        busy={pageLoading}
        label="Gift exchanges you're part of pages"
        onPageChange={onPageChange}
      />
    </div>
  )
}
