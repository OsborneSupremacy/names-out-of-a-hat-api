import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import {
  getHat,
  editHat,
  addParticipant,
  removeParticipant,
  editParticipant,
  deleteHat,
  validateHat,
  assignRecipients,
  previewInvitations,
  sendInvitations,
  closeHat,
  Hat,
  PreviewInvitationsResponse,
} from '../api'
import { Header } from '../components/Header'
import { Footer } from '../components/Footer'
import { AddParticipantModal } from '../components/AddParticipantModal'
import { InvitationsPreviewModal } from '../components/InvitationsPreviewModal'
import './GiftExchangeDetail.css'

interface GiftExchangeDetailProps {
  userEmail: string
  givenName: string
  onSignOut: () => void
}

export function GiftExchangeDetail({ userEmail, givenName, onSignOut }: GiftExchangeDetailProps) {
  const { hatId } = useParams<{ hatId: string }>()
  const navigate = useNavigate()
  const [hat, setHat] = useState<Hat | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>('')
  const [isEditing, setIsEditing] = useState(false)
  const [editedName, setEditedName] = useState('')
  const [editedAdditionalInfo, setEditedAdditionalInfo] = useState('')
  const [editedPriceRange, setEditedPriceRange] = useState('')
  const [saving, setSaving] = useState(false)
  const [showAddParticipantModal, setShowAddParticipantModal] = useState(false)
  const [removingParticipant, setRemovingParticipant] = useState<string | null>(null)
  const [editingEligibleFor, setEditingEligibleFor] = useState<string | null>(null)
  const [tempEligibleRecipients, setTempEligibleRecipients] = useState<string[]>([])
  const [isAssigning, setIsAssigning] = useState(false)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [isPreviewLoading, setIsPreviewLoading] = useState(false)
  const [showInvitationsPreview, setShowInvitationsPreview] = useState(false)
  const [invitationsPreview, setInvitationsPreview] = useState<PreviewInvitationsResponse | null>(null)
  const [isSendingInvitations, setIsSendingInvitations] = useState(false)
  const [isClosing, setIsClosing] = useState(false)

  useEffect(() => {
    async function loadHat() {
      if (!hatId) {
        setError('No gift exchange ID provided')
        setLoading(false)
        return
      }

      try {
        const hatData = await getHat(userEmail, hatId)
        setHat(hatData)
        setEditedName(hatData.name)
        setEditedAdditionalInfo(hatData.additionalInformation)
        setEditedPriceRange(hatData.priceRange)
      } catch (err) {
        console.error('Error loading gift exchange:', err)
        setError(err instanceof Error ? err.message : 'Failed to load gift exchange details')
      } finally {
        setLoading(false)
      }
    }

    loadHat()
  }, [hatId, userEmail])

  const handleEdit = () => {
    setIsEditing(true)
  }

  const handleCancel = () => {
    if (hat) {
      setEditedName(hat.name)
      setEditedAdditionalInfo(hat.additionalInformation)
      setEditedPriceRange(hat.priceRange)
    }
    setIsEditing(false)
  }

  const handleSave = async () => {
    if (!hat || !hatId) return

    setSaving(true)
    try {
      await editHat({
        hatId,
        organizerEmail: userEmail,
        name: editedName,
        additionalInformation: editedAdditionalInfo,
        priceRange: editedPriceRange,
      })

      // Reload the hat data
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
      setIsEditing(false)
    } catch (err) {
      console.error('Error saving changes:', err)
      setError(err instanceof Error ? err.message : 'Failed to save changes')
    } finally {
      setSaving(false)
    }
  }

  const handleAddParticipant = async (name: string, email: string) => {
    if (!hatId) return

    await addParticipant({
      organizerEmail: userEmail,
      hatId,
      name,
      email,
    })

    // Reload the hat data
    const updatedHat = await getHat(userEmail, hatId)
    setHat(updatedHat)
  }

  const handleRemoveParticipant = async (participantEmail: string) => {
    if (!hatId) return

    const confirmed = window.confirm(
      `Are you sure you want to remove this participant?\n\nEmail: ${participantEmail}`
    )

    if (!confirmed) return

    setRemovingParticipant(participantEmail)
    try {
      await removeParticipant({
        organizerEmail: userEmail,
        hatId,
        email: participantEmail,
      })

      // Reload the hat data
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
    } catch (err) {
      console.error('Error removing participant:', err)
      setError(err instanceof Error ? err.message : 'Failed to remove participant')
    } finally {
      setRemovingParticipant(null)
    }
  }

  const handleEditEligibleRecipients = (participantEmail: string, currentEligible: string[]) => {
    setEditingEligibleFor(participantEmail)
    setTempEligibleRecipients(currentEligible)
  }

  const handleToggleEligible = (recipientName: string) => {
    setTempEligibleRecipients(prev =>
      prev.includes(recipientName)
        ? prev.filter(e => e !== recipientName)
        : [...prev, recipientName]
    )
  }

  const handleSaveEligibleRecipients = async (participantEmail: string) => {
    if (!hatId) return

    try {
      await editParticipant({
        organizerEmail: userEmail,
        hatId,
        email: participantEmail,
        eligibleRecipients: tempEligibleRecipients,
      })

      // Reload the hat data
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
      setEditingEligibleFor(null)
    } catch (err) {
      console.error('Error updating eligible recipients:', err)
      setError(err instanceof Error ? err.message : 'Failed to update eligible recipients')
    }
  }

  const handleCancelEditEligible = () => {
    setEditingEligibleFor(null)
    setTempEligibleRecipients([])
  }

  const handleDeleteHat = async () => {
    if (!hatId || !hat) return

    const confirmed = window.confirm(
      `Are you sure you want to delete "${hat.name}"?\n\nThis action cannot be undone.`
    )

    if (!confirmed) return

    try {
      await deleteHat({
        organizerEmail: userEmail,
        hatId,
      })

      // Navigate back to home after successful deletion
      navigate('/')
    } catch (err) {
      console.error('Error deleting gift exchange:', err)
      setError(err instanceof Error ? err.message : 'Failed to delete gift exchange')
    }
  }

  const handleSendInvitations = async () => {
    if (!hatId || !hat) return

    setIsPreviewLoading(true)
    setError('')

    try {
      const preview = await previewInvitations({
        organizerEmail: userEmail,
        hatId,
      })

      setInvitationsPreview(preview)
      setShowInvitationsPreview(true)
    } catch (err) {
      console.error('Error loading invitation preview:', err)
      setError(err instanceof Error ? err.message : 'Failed to load invitation preview')
    } finally {
      setIsPreviewLoading(false)
    }
  }

  const handleBackFromInvitationsPreview = () => {
    if (isSendingInvitations) return
    setShowInvitationsPreview(false)
  }

  const handleConfirmSendInvitations = async () => {
    if (!hatId || !hat) return

    setIsSendingInvitations(true)
    setError('')

    try {
      await sendInvitations({
        organizerEmail: userEmail,
        hatId,
      })

      // Reload the hat data to reflect the updated status
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
      setShowInvitationsPreview(false)
      setInvitationsPreview(null)
    } catch (err) {
      console.error('Error sending invitations:', err)
      setError(err instanceof Error ? err.message : 'Failed to send invitations')
    } finally {
      setIsSendingInvitations(false)
    }
  }

  const handleValidate = async () => {
    if (!hatId || !hat) return

    setIsAssigning(true)
    setValidationErrors([])
    setError('')

    try {
      const validationResult = await validateHat({
        organizerEmail: userEmail,
        hatId,
      })

      if (!validationResult.success) {
        setValidationErrors(validationResult.errors)
      } else {
        // Validation successful - reload to get updated status
        const updatedHat = await getHat(userEmail, hatId)
        setHat(updatedHat)
        setValidationErrors([])
      }
    } catch (err) {
      console.error('Error validating gift exchange:', err)
      setError(err instanceof Error ? err.message : 'Failed to validate gift exchange')
    } finally {
      setIsAssigning(false)
    }
  }

  const handleCloseHat = async () => {
    if (!hatId || !hat) return

    const confirmed = window.confirm(
      'Are you sure you want to close the gift exchange? The picked names for all recipients will be revealed. You should only do this once the exchange has actually happened.'
    )
    if (!confirmed) return

    setIsClosing(true)
    setError('')

    try {
      await closeHat({
        organizerEmail: userEmail,
        hatId,
      })

      // Reload the hat data
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
    } catch (err) {
      console.error('Error closing gift exchange:', err)
      setError(err instanceof Error ? err.message : 'Failed to close gift exchange')
    } finally {
      setIsClosing(false)
    }
  }

  const handleShakeHat = async () => {
    if (!hatId || !hat) return

    // If recipients are already assigned, confirm before re-shaking
    if (hat.status === 'NAMES_ASSIGNED') {
      const confirmed = window.confirm(
        'The hat has already been shaken and all participants have a name picked. Are you sure you want to shake the hat again?'
      )
      if (!confirmed) return
    }

    setIsAssigning(true)
    setValidationErrors([])
    setError('')

    try {
      // Assign recipients (validation not needed - hat is already validated if status is NAMES_ASSIGNED)
      await assignRecipients({
        organizerEmail: userEmail,
        hatId,
      })

      // Reload the hat data
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
    } catch (err) {
      console.error('Error shaking hat:', err)
      setError(err instanceof Error ? err.message : 'Failed to shake the hat')
    } finally {
      setIsAssigning(false)
    }
  }

  const showRowEditAffordance = hat
    ? ['IN_PROGRESS', 'READY_FOR_ASSIGNMENT', 'NAMES_ASSIGNED'].includes(hat.status)
    : false

  return (
    <div className="app-container">
      <Header
        userEmail={userEmail}
        givenName={givenName}
        onSignOut={onSignOut}
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
          ) : hat ? (
            <div className="hat-detail">
              <div className="hat-header">
                {isEditing ? (
                  <input
                    type="text"
                    className="edit-name-input"
                    value={editedName}
                    onChange={(e) => setEditedName(e.target.value)}
                    disabled={saving}
                  />
                ) : (
                  <h2>{hat.name}</h2>
                )}
                {hat.status !== 'INVITATIONS_SENT' && hat.status !== 'COOLED_OFF' && hat.status !== 'CLOSED' && (
                  <div className="hat-actions">
                    {isEditing ? (
                      <>
                        <button
                          className="secondary-button"
                          onClick={handleCancel}
                          disabled={saving}
                        >
                          Cancel
                        </button>
                        <button
                          className="primary-button"
                          onClick={handleSave}
                          disabled={saving}
                        >
                          {saving ? 'Saving...' : 'Save Changes'}
                        </button>
                      </>
                    ) : (
                      <button className="primary-button" onClick={handleEdit}>
                        Edit
                      </button>
                    )}
                  </div>
                )}
              </div>

              <div className="status-progression">
                <div className="status-steps">
                  <div className={`status-step ${hat.status === 'IN_PROGRESS' ? 'active' : ''}`}>
                    <div className="status-step-indicator"></div>
                    <div className="status-step-label">In Progress</div>
                  </div>
                  <div className="status-step-connector"></div>
                  <div className={`status-step ${hat.status === 'READY_FOR_ASSIGNMENT' ? 'active' : ''}`}>
                    <div className="status-step-indicator"></div>
                    <div className="status-step-label">Ready For Assignment</div>
                  </div>
                  <div className="status-step-connector"></div>
                  <div className={`status-step ${hat.status === 'NAMES_ASSIGNED' ? 'active' : ''}`}>
                    <div className="status-step-indicator"></div>
                    <div className="status-step-label">Names Assigned</div>
                  </div>
                  <div className="status-step-connector"></div>
                  <div className={`status-step ${hat.status === 'INVITATIONS_SENT' ? 'active' : ''}`}>
                    <div className="status-step-indicator"></div>
                    <div className="status-step-label">Invitations Sent</div>
                  </div>
                  <div className="status-step-connector"></div>
                  <div className={`status-step ${hat.status === 'COOLED_OFF' ? 'active' : ''}`}>
                    <div className="status-step-indicator"></div>
                    <div className="status-step-label">Cooling Off</div>
                  </div>
                  <div className="status-step-connector"></div>
                  <div className={`status-step ${hat.status === 'CLOSED' ? 'active' : ''}`}>
                    <div className="status-step-indicator"></div>
                    <div className="status-step-label">Closed</div>
                  </div>
                </div>
              </div>

              <div className="status-action-section">
                {hat.status === 'IN_PROGRESS' && (
                  <div className="action-container">
                    <button
                      className="action-button validate-button"
                      onClick={handleValidate}
                      disabled={hat.participants.length < 3 || isAssigning}
                    >
                      {isAssigning ? 'Validating...' : 'Validate Gift Exchange'}
                    </button>
                    {hat.participants.length < 3 && (
                      <p className="action-hint">Add at least 3 participants to validate</p>
                    )}
                    {validationErrors.length > 0 && (
                      <div className="validation-errors">
                        <h4>Validation failed:</h4>
                        <ul>
                          {validationErrors.map((error, index) => (
                            <li key={index}>{error}</li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                )}

                {hat.status === 'READY_FOR_ASSIGNMENT' && (
                  <div className="action-container">
                    <button
                      className="action-button shake-button"
                      onClick={handleShakeHat}
                      disabled={isAssigning}
                    >
                      {isAssigning ? 'Shaking...' : 'Shake the Hat!'}
                    </button>
                    <p className="action-hint">Assign gift recipients to participants</p>
                    {validationErrors.length > 0 && (
                      <div className="validation-errors">
                        <h4>Cannot shake the hat:</h4>
                        <ul>
                          {validationErrors.map((error, index) => (
                            <li key={index}>{error}</li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                )}

                {hat.status === 'NAMES_ASSIGNED' && (
                  <div className="action-container">
                    <button
                      className="action-button send-button"
                      onClick={handleSendInvitations}
                      disabled={isSendingInvitations || isPreviewLoading}
                    >
                      {isPreviewLoading
                        ? 'Loading Preview...'
                        : isSendingInvitations
                          ? 'Sending Invitations...'
                          : 'Send Invitations'}
                    </button>
                    <button
                      className="action-button-secondary shake-again-button"
                      onClick={handleShakeHat}
                      disabled={isAssigning}
                    >
                      {isAssigning ? 'Shaking...' : 'Shake the Hat Again'}
                    </button>
                    <p className="action-hint">Send invitations to all participants, or re-shuffle assignments</p>
                  </div>
                )}

                {hat.status === 'INVITATIONS_SENT' && (
                  <div className="action-container">
                    <p className="action-complete">Invitations have been sent</p>
                    <p className="action-hint">This gift exchange is in a waiting period and cannot be closed yet.</p>
                  </div>
                )}

                {hat.status === 'COOLED_OFF' && (
                  <div className="action-container">
                    <button
                      className="action-button action-close-button"
                      onClick={handleCloseHat}
                      disabled={isClosing}
                    >
                      {isClosing ? 'Closing...' : 'Close Gift Exchange'}
                    </button>
                    <p className="action-hint">Mark this gift exchange as completed. Once you do this, you will be able to view the picked recipients of all participants.</p>
                  </div>
                )}

                {hat.status === 'CLOSED' && (
                  <div className="action-container">
                    <p className="action-complete">This gift exchange is closed</p>
                    <p className="action-hint">See below for the names everyone was assigned. See you next time!</p>
                  </div>
                )}
              </div>

              <div className="hat-info-grid">
                <div className="info-card full-width">
                  <h3>Additional Information</h3>
                  {isEditing ? (
                    <textarea
                      className="edit-textarea"
                      value={editedAdditionalInfo}
                      onChange={(e) => setEditedAdditionalInfo(e.target.value)}
                      rows={4}
                      disabled={saving}
                    />
                  ) : (
                    <p>{hat.additionalInformation || <span className="text-muted">None</span>}</p>
                  )}
                </div>

                <div className="info-card">
                  <h3>Price Range</h3>
                  {isEditing ? (
                    <input
                      type="text"
                      className="edit-input"
                      value={editedPriceRange}
                      onChange={(e) => setEditedPriceRange(e.target.value)}
                      placeholder="e.g., $20-$50"
                      disabled={saving}
                    />
                  ) : (
                    <p>{hat.priceRange || <span className="text-muted">Not set</span>}</p>
                  )}
                </div>
              </div>

              <div className="participants-section">
                <div className="section-header">
                  <div>
                    <h3>Participants ({hat.participants.length})</h3>
                    {showRowEditAffordance && (
                      <p className="participants-edit-hint">Click a participant row to edit eligible recipients.</p>
                    )}
                  </div>
                  {hat.status !== 'CLOSED' && hat.status !== 'INVITATIONS_SENT' && hat.status !== 'COOLED_OFF' && (
                    <button
                      className="primary-button"
                      onClick={() => setShowAddParticipantModal(true)}
                    >
                      Add Participant
                    </button>
                  )}
                </div>
                {hat.participants.length > 0 ? (
                  (hat.status === 'CLOSED' || hat.status === 'INVITATIONS_SENT' || hat.status === 'COOLED_OFF') ? (
                    <table className="participants-table">
                      <thead>
                        <tr>
                          <th>Name</th>
                          <th>Picked Recipient</th>
                        </tr>
                      </thead>
                      <tbody>
                        {hat.participants.map((participant, index) => (
                          <tr key={index}>
                            <td>
                              <div className="participant-name-cell">
                                <strong>{participant.person.name}</strong>
                                <span className="participant-email">{participant.person.email}</span>
                              </div>
                            </td>
                            <td>
                              <strong>{participant.pickedRecipient || 'Not assigned'}</strong>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  ) : (
                    <table className="participants-table">
                      <thead>
                        <tr>
                          <th>Name</th>
                          <th>Eligible Recipients</th>
                        </tr>
                      </thead>
                      <tbody>
                        {hat.participants.map((participant, index) => {
                          const isOrganizer = participant.person.email === hat.organizer.email
                          const isEditingThis = editingEligibleFor === participant.person.email
                          const otherParticipants = hat.participants.filter(p => p.person.email !== participant.person.email)
                          const eligibleRecipients = otherParticipants
                            .filter(otherParticipant => participant.eligibleRecipients.includes(otherParticipant.person.name))
                            .map(otherParticipant => otherParticipant.person.name)
                          const ineligibleRecipients = otherParticipants
                            .filter(otherParticipant => !participant.eligibleRecipients.includes(otherParticipant.person.name))
                            .map(otherParticipant => otherParticipant.person.name)

                          return (
                            <tr
                              key={index}
                              className={`${isEditingThis ? 'editing-row' : 'clickable-row'} ${showRowEditAffordance ? 'editable-status-clickable' : ''}`.trim()}
                              onClick={() => !isEditingThis && handleEditEligibleRecipients(
                                participant.person.email,
                                participant.eligibleRecipients
                              )}
                            >
                              <td>
                                <div className="participant-name-cell">
                                  <strong>
                                    {participant.person.name}
                                    {isOrganizer && <span className="organizer-badge">Organizer</span>}
                                  </strong>
                                  <span className="participant-email">{participant.person.email}</span>
                                  {showRowEditAffordance && !isEditingThis && (
                                    <span className="row-edit-indicator">Click row to edit</span>
                                  )}
                                  {isEditingThis && !isOrganizer && (
                                    <button
                                      className="danger-button remove-participant-button"
                                      onClick={() => handleRemoveParticipant(participant.person.email)}
                                      disabled={removingParticipant === participant.person.email}
                                    >
                                      {removingParticipant === participant.person.email ? 'Removing...' : 'Remove Participant'}
                                    </button>
                                  )}
                                </div>
                              </td>
                              <td>
                                <div className="eligible-recipients-section">
                                  {otherParticipants.length > 0 ? (
                                    <>
                                      {isEditingThis ? (
                                        <>
                                          <div className="recipients-list">
                                            {otherParticipants.map((otherParticipant) => {
                                              const eligible = tempEligibleRecipients.includes(otherParticipant.person.name)

                                              return (
                                                <label key={otherParticipant.person.email} className="recipient-checkbox">
                                                  <input
                                                    type="checkbox"
                                                    checked={eligible}
                                                    onChange={() => handleToggleEligible(otherParticipant.person.name)}
                                                  />
                                                  <span>{otherParticipant.person.name}</span>
                                                </label>
                                              )
                                            })}
                                          </div>

                                          <div className="eligible-actions">
                                            <button
                                              className="primary-button"
                                              onClick={() => handleSaveEligibleRecipients(participant.person.email)}
                                              disabled={tempEligibleRecipients.length === 0}
                                              title={tempEligibleRecipients.length === 0 ? 'At least one recipient must be selected' : ''}
                                            >
                                              Save
                                            </button>
                                            <button
                                              className="secondary-button"
                                              onClick={handleCancelEditEligible}
                                            >
                                              Cancel
                                            </button>
                                          </div>
                                        </>
                                      ) : (
                                        <div className="recipient-summary-list">
                                          <p className="recipient-summary-item">
                                            <span className="recipient-summary-label">Eligible:</span>{' '}
                                            {eligibleRecipients.length > 0 ? eligibleRecipients.join(', ') : 'None'}
                                          </p>
                                          {ineligibleRecipients.length > 0 && (
                                            <p className="recipient-summary-item">
                                              <span className="recipient-summary-label">Not Eligible:</span>{' '}
                                              {ineligibleRecipients.join(', ')}
                                            </p>
                                          )}
                                        </div>
                                      )}
                                    </>
                                  ) : (
                                    <p className="text-muted">No other participants to assign</p>
                                  )}
                                </div>
                              </td>
                            </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  )
                ) : (
                  <p className="text-muted">No participants yet</p>
                )}
              </div>

              {hat.status !== 'INVITATIONS_SENT' && hat.status !== 'COOLED_OFF' && hat.status !== 'CLOSED' && (
                <div className="delete-section">
                  <button
                    className="danger-button"
                    onClick={handleDeleteHat}
                  >
                    Delete Gift Exchange
                  </button>
                </div>
              )}
            </div>
          ) : (
            <p className="error-message">Gift exchange not found</p>
          )}
        </div>
      </main>

      <Footer />

      {showAddParticipantModal && (
        <AddParticipantModal
          onClose={() => setShowAddParticipantModal(false)}
          onSubmit={handleAddParticipant}
        />
      )}

      {showInvitationsPreview && invitationsPreview && (
        <InvitationsPreviewModal
          subject={invitationsPreview.subject}
          htmlBody={invitationsPreview.htmlBody}
          isSending={isSendingInvitations}
          onBack={handleBackFromInvitationsPreview}
          onSend={handleConfirmSendInvitations}
        />
      )}
    </div>
  )
}
