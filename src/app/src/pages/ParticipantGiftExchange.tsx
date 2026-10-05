import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { getParticipantView, leaveGiftExchange, ParticipantView } from '../api'
import { formatExchangeDate, hasExchangeDate } from '../exchangeDate'
import { Header } from '../components/Header'
import { Footer } from '../components/Footer'
import { LeaveGiftExchangeModal } from '../components/LeaveGiftExchangeModal'
import { ParticipantGiftIdeas } from '../components/ParticipantGiftIdeas'
import { ParticipantOptionsMenu } from '../components/ParticipantOptionsMenu'
import './GiftExchangeDetail.css'
import './ParticipantGiftExchange.css'

interface ParticipantGiftExchangeProps {
  userEmail: string
  onSignOut: () => void
}

/**
 * A gift exchange as somebody taking part in it sees it: what the organizer said about it, who is
 * in it, who they are giving to, and everything the buttons in their emails do — sharing gift
 * ideas, asking for them, answering asks, and, behind the menu, leaving. The exchange itself is the
 * organizer's to change.
 *
 * The server decides which picks and which gift ideas are here. Before the exchange is revealed
 * only the caller's own pick arrives, and only the ideas that have already reached them, so this
 * page shows whatever it is given rather than hiding anything itself.
 */
export function ParticipantGiftExchange({ userEmail, onSignOut }: ParticipantGiftExchangeProps) {
  const { hatId } = useParams<{ hatId: string }>()
  const navigate = useNavigate()
  const [view, setView] = useState<ParticipantView | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  // Theirs, for the header. null until the exchange arrives, as the header expects.
  const [givenName, setGivenName] = useState<string | null>(null)
  const [showLeaveModal, setShowLeaveModal] = useState(false)

  // After something is shared or asked, so it shows up where it belongs. Quietly, without the
  // loading state: the panels stay where they are, and so does whatever they just said.
  const refresh = useCallback(async () => {
    if (!hatId) return
    setView(await getParticipantView(userEmail, hatId))
  }, [userEmail, hatId])

  const handleLeave = async (blockOrganizer: boolean, blockAnywhere: boolean) => {
    if (!view) return
    await leaveGiftExchange(userEmail, view.hatId, blockOrganizer, blockAnywhere)
    // Home says so, because this page cannot: they are no longer in the exchange it would show.
    navigate('/', { state: { leftGiftExchange: view.name } })
  }

  useEffect(() => {
    let cancelled = false

    async function load() {
      if (!hatId) return

      try {
        const response = await getParticipantView(userEmail, hatId)
        if (cancelled) return

        setView(response)
        setGivenName(response.participants.find((participant) => participant.isYou)?.name ?? '')
      } catch (err) {
        if (cancelled) return
        console.error('Error loading gift exchange:', err)
        setError(err instanceof Error ? err.message : 'Failed to load gift exchange')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()

    return () => {
      cancelled = true
    }
  }, [userEmail, hatId])

  const you = view?.participants.find((participant) => participant.isYou)
  const revealed = view?.status === 'CLOSED'
  // Names arrive already told apart where two people share one, so a name finds its face.
  const emojiFor = (name: string) => view?.participants.find((participant) => participant.name === name)?.emoji ?? ''

  return (
    <div className="app-container">
      <Header
        userEmail={userEmail}
        givenName={givenName}
        onSignOut={onSignOut}
        onNameUpdated={setGivenName}
        // They are leaving this exchange along with everything else.
        onDataDeleted={() => navigate('/', { state: { dataDeletionRequested: true } })}
      />

      <main className="main-content">
        <div className="content-wrapper">
          <button className="back-button" onClick={() => navigate('/')}>
            ← Back to Gift Exchanges
          </button>

          {loading ? (
            <p>Loading gift exchange...</p>
          ) : error ? (
            <p className="error-message">{error}</p>
          ) : view ? (
            <div className="hat-detail">
              <div className="hat-header">
                <h2>{view.name}</h2>
                <div className="hat-actions">
                  <ParticipantOptionsMenu canLeave={view.canLeave} onLeave={() => setShowLeaveModal(true)} />
                </div>
              </div>
              <p className="participant-view-organizer">Organized by {view.organizerName}</p>

              {you?.pickedRecipient && (
                <div className="participant-view-pick">
                  <span className="participant-view-pick-label">You're giving a gift to</span>
                  <strong className="participant-view-pick-name">
                    {emojiFor(you.pickedRecipient) && (
                      <span className="participant-emoji">{emojiFor(you.pickedRecipient)} </span>
                    )}
                    {you.pickedRecipient}
                  </strong>
                </div>
              )}

              <div className="hat-info-grid">
                <div className="info-card full-width">
                  <h3>Additional Information</h3>
                  <p>{view.additionalInformation || <span className="text-muted">None</span>}</p>
                </div>

                <div className="info-card">
                  <h3>Price Range</h3>
                  <p>{view.priceRange || <span className="text-muted">Not set</span>}</p>
                </div>

                <div className="info-card">
                  <h3>Exchange Date</h3>
                  <p>
                    {hasExchangeDate(view.exchangeDate)
                      ? `Around ${formatExchangeDate(view.exchangeDate)}`
                      : <span className="text-muted">Not set</span>}
                  </p>
                </div>
              </div>

              <ParticipantGiftIdeas
                userEmail={userEmail}
                hatId={view.hatId}
                pickName={you?.pickedRecipient ?? ''}
                giftIdeas={view.giftIdeas}
                onChanged={refresh}
              />

              <div className="participants-section">
                <div className="section-header">
                  <div>
                    <h3>Participants ({view.participants.length})</h3>
                    <p className="participant-view-hint">
                      {revealed
                        ? 'The gift exchange is over, so everybody can see who picked whose name.'
                        : "Everybody's picks stay secret until the organizer reveals them."}
                    </p>
                  </div>
                </div>

                <ul className="participant-view-list">
                  {view.participants.map((participant) => (
                    <li key={participant.name} className="participant-view-row">
                      <span className="participant-view-name">
                        {participant.emoji && <span className="participant-emoji">{participant.emoji} </span>}
                        {participant.name}
                        {participant.isYou && <span className="participant-view-you"> (you)</span>}
                      </span>
                      {/* Only revealed picks are drawn as a pairing; the caller's own is above. */}
                      {revealed && participant.pickedRecipient && (
                        <span className="participant-view-recipient">
                          <span className="participant-view-arrow" aria-hidden="true">→</span>
                          {emojiFor(participant.pickedRecipient) && (
                            <span className="participant-emoji">{emojiFor(participant.pickedRecipient)} </span>
                          )}
                          {participant.pickedRecipient}
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          ) : null}
        </div>
      </main>

      {showLeaveModal && view && (
        <LeaveGiftExchangeModal
          hatName={view.name}
          organizerName={view.organizerName}
          // Mirrors GiftExchangeLeaving.ShowsConsequences: once the exchange has cooled off or
          // closed, nobody is sent back to the hat.
          showsConsequences={view.status !== 'READY_TO_CLOSE' && view.status !== 'CLOSED'}
          onClose={() => setShowLeaveModal(false)}
          onSubmit={handleLeave}
        />
      )}

      <Footer />
    </div>
  )
}
