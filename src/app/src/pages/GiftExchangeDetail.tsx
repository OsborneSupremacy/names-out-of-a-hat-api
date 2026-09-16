import { Fragment, useEffect, useRef, useState } from 'react'
import { useParams, useNavigate, useLocation } from 'react-router-dom'
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
  copyHat,
  editParticipantAddress,
  editParticipantEmoji,
  editParticipantName,
  exportHat,
  resetHat,
  Hat,
  Participant,
  PreviewInvitationsResponse,
} from '../api'
import { HAT_STATUS_STEPS, formatHatStatus } from '../hatStatus'
import { DrawType } from '../drawType'
import {
  deliveryTone,
  formatDeliveryMessageType,
  formatDeliveryStatus,
  showsDeliveryDetail,
} from '../deliveryStatus'
import { formatRelativeTime, formatDateAndTime } from '../relativeTime'
import { MAX_PARTICIPANTS } from '../participantLimit'
import { EditAddressModal, ResendKind } from '../components/EditAddressModal'
import { EditEmojiModal } from '../components/EditEmojiModal'
import { EditParticipantNameModal } from '../components/EditParticipantNameModal'
import { Header } from '../components/Header'
import { Footer } from '../components/Footer'
import { AddParticipantModal } from '../components/AddParticipantModal'
import { InvitationsPreviewModal } from '../components/InvitationsPreviewModal'
import { SendConfirmationModal } from '../components/SendConfirmationModal'
import { CopyHatModal } from '../components/CopyHatModal'
import { ShakeHatModal } from '../components/ShakeHatModal'
import { AdvancedOptionsMenu } from '../components/AdvancedOptionsMenu'
import { ResetHatModal } from '../components/ResetHatModal'
import { DeleteHatModal } from '../components/DeleteHatModal'
import { DeliveryHelpModal } from '../components/DeliveryHelpModal'
import { downloadExport } from '../hatExport'
import './GiftExchangeDetail.css'

interface GiftExchangeDetailProps {
  userEmail: string
  onSignOut: () => void
}

export function GiftExchangeDetail({ userEmail, onSignOut }: GiftExchangeDetailProps) {

  const { hatId } = useParams<{ hatId: string }>()
  const navigate = useNavigate()
  const location = useLocation()
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
  const [showSendConfirmation, setShowSendConfirmation] = useState(false)
  const [isClosing, setIsClosing] = useState(false)
  const [showCopyModal, setShowCopyModal] = useState(false)
  const [showShakeModal, setShowShakeModal] = useState(false)
  const [showResetModal, setShowResetModal] = useState(false)
  const [showDeleteModal, setShowDeleteModal] = useState(false)
  const [showDeliveryHelp, setShowDeliveryHelp] = useState(false)
  const [isExporting, setIsExporting] = useState(false)
  const [editingAddressFor, setEditingAddressFor] = useState<Participant | null>(null)
  const [savingAddress, setSavingAddress] = useState(false)
  const [editingEmojiFor, setEditingEmojiFor] = useState<Participant | null>(null)
  const [savingEmoji, setSavingEmoji] = useState(false)
  const [editingNameFor, setEditingNameFor] = useState<Participant | null>(null)
  const [savingName, setSavingName] = useState(false)
  // Kept apart from the page-level `error` like the two above, and carrying more than they usually
  // do: this is where the server's refusal lands when the rename was not this organizer's to make,
  // and that sentence is the entire explanation the organizer gets.
  const [nameError, setNameError] = useState('')
  // Kept apart from the page-level `error` for the reason addressError is: a failed change should
  // leave the organizer looking at their exchange with the dialog still open.
  const [emojiError, setEmojiError] = useState('')
  // Kept apart from the page-level `error`, which replaces the whole view when it is set. A failed
  // address change should leave the organizer looking at their exchange with the dialog still open.
  const [addressError, setAddressError] = useState('')
  // Anything worth telling the organizer about the participant list, shown just above it. Set by
  // an address correction and by a copy that had to leave somebody out.
  const [participantsNotice, setParticipantsNotice] = useState('')
  // An exchange holding only its organizer cannot do anything yet, so the dialog opens for them.
  // Guarded so that closing it does not bring it straight back when the hat reloads.
  const openedAddParticipantForEmptyHat = useRef(false)

  /**
   * Picks up a notice handed over by whatever navigated here — today only a copy that had to leave
   * somebody out.
   *
   * The history entry is rewritten as it is read, so a refresh does not bring the notice back for
   * an exchange the organizer has since fixed. Keyed on the exchange as well, so that navigating
   * between two of them does not carry one's notice onto the other.
   */
  useEffect(() => {
    const carried = (location.state as { notice?: string } | null)?.notice

    if (!carried) return

    setParticipantsNotice(carried)
    navigate(location.pathname, { replace: true, state: null })
  }, [location.pathname, location.state, navigate])

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

        if (
          hatData.participants.length <= 1 &&
          hatData.status === 'IN_PROGRESS' &&
          !openedAddParticipantForEmptyHat.current
        ) {
          openedAddParticipantForEmptyHat.current = true
          setShowAddParticipantModal(true)
        }
      } catch (err) {
        console.error('Error loading gift exchange:', err)
        setError(err instanceof Error ? err.message : 'Failed to load gift exchange details')
      } finally {
        setLoading(false)
      }
    }

    loadHat()
  }, [hatId, userEmail])

  // The organizer's name is displayed from the loaded hat, so reload it rather than patching
  // state that the server has just changed underneath us.
  const handleNameUpdated = async () => {
    if (!hatId) return

    try {
      setHat(await getHat(userEmail, hatId))
    } catch (err) {
      console.error('Error reloading gift exchange after rename:', err)
    }
  }

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

  /**
   * Which email a correction will resend, given where the exchange has got to.
   *
   * Mirrors EditParticipantAddressService.MessageTypeFor on the server. The server is what decides;
   * this is only so the dialog can say what is about to happen before it happens.
   */
  const resendKindFor = (status: string): ResendKind => {
    if (status === 'INVITATIONS_SENT' || status === 'READY_TO_CLOSE') return 'invitation'
    if (status === 'CLOSED') return 'announcement'
    return 'none'
  }

  const handleOpenAddressModal = (participant: Participant) => {
    setAddressError('')
    setParticipantsNotice('')
    setEditingAddressFor(participant)
  }

  const handleSaveAddress = async (newEmail: string) => {
    if (!hatId || !editingAddressFor) return

    const { name } = editingAddressFor.person

    setSavingAddress(true)
    setAddressError('')

    try {
      const result = await editParticipantAddress({
        organizerEmail: userEmail,
        hatId,
        currentEmail: editingAddressFor.person.email,
        newEmail,
      })

      setEditingAddressFor(null)

      // Says what actually happened rather than a generic "saved". Mail going out on the
      // organizer's behalf should never be something they have to infer.
      setParticipantsNotice(
        result.emailResent
          ? `${name} is now at ${newEmail}, and ${
              result.messageType === 'COMPLETION' ? 'the announcement has' : 'their invitation has'
            } been resent there.`
          : `${name} is now at ${newEmail}. Nothing has been sent for this gift exchange yet, so nobody was emailed.`
      )

      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
    } catch (err) {
      console.error('Error updating participant address:', err)
      setAddressError(err instanceof Error ? err.message : 'Failed to update the address')
    } finally {
      setSavingAddress(false)
    }
  }

  const handleOpenEmojiModal = (participant: Participant) => {
    setEmojiError('')
    setEditingEmojiFor(participant)
  }

  const handleSaveEmoji = async (emoji: string) => {
    if (!hatId || !editingEmojiFor) return

    setSavingEmoji(true)
    setEmojiError('')

    try {
      await editParticipantEmoji({
        organizerEmail: userEmail,
        hatId,
        email: editingEmojiFor.person.email,
        emoji,
      })

      setEditingEmojiFor(null)

      // No notice, unlike an address change: nothing was sent, and the new face is visible in the
      // row the moment this reload lands, which says everything a sentence would.
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
    } catch (err) {
      console.error('Error updating participant emoji:', err)
      setEmojiError(err instanceof Error ? err.message : 'Failed to update the emoji')
    } finally {
      setSavingEmoji(false)
    }
  }

  const handleOpenNameModal = (participant: Participant) => {
    setNameError('')
    setEditingNameFor(participant)
  }

  /**
   * Renaming one participant.
   *
   * The dialog stays open on a failure, which matters more here than for the other two edits: the
   * refusals this can return are explanations rather than glitches — somebody else already goes by
   * that name, or the name is not this organizer's to change — and both are read while looking at
   * the box that caused them.
   *
   * A notice afterwards, unlike the emoji edit, because the change is not confined to what the
   * organizer can see. The row will show the new name when the reload lands; the sentence is there
   * to say the rest of it went further than this exchange.
   */
  const handleSaveName = async (newName: string) => {
    if (!hatId || !editingNameFor) return

    const previousName = editingNameFor.person.name

    setSavingName(true)
    setNameError('')

    try {
      await editParticipantName({
        organizerEmail: userEmail,
        hatId,
        email: editingNameFor.person.email,
        name: newName,
      })

      setEditingNameFor(null)

      setParticipantsNotice(
        `${previousName} is now ${newName}, here and in every other gift exchange they take part in.`
      )

      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
    } catch (err) {
      console.error('Error updating participant name:', err)
      setNameError(err instanceof Error ? err.message : 'Failed to update the name')
    } finally {
      setSavingName(false)
    }
  }

  /**
   * The face worn by whoever this participant drew, or nothing.
   *
   * Nothing is the ordinary case before the exchange is closed: the API replaces every pick with
   * "Hidden" until then, which matches nobody here — so the column shows a name with no face
   * against it rather than a face belonging to somebody else.
   */
  const emojiForName = (name: string) =>
    hat?.participants.find(participant => participant.person.name === name)?.emoji ?? ''

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

  /**
   * Throws the exchange away, and leaves for the list once it is gone.
   *
   * Errors are rethrown rather than set on the page, as the reset does: the dialog is still open and
   * is where the organizer is looking. The page behind it is about to be navigated away from in the
   * success case anyway, so a message left there would be a message nobody reads.
   */
  const handleDeleteHat = async () => {
    if (!hatId || !hat) return

    try {
      await deleteHat({
        organizerEmail: userEmail,
        hatId,
      })

      // Nothing left here to look at, so back to the list.
      navigate('/')
    } catch (err) {
      console.error('Error deleting gift exchange:', err)
      throw err
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

  // The preview's Send button no longer sends; it opens the acknowledgment step.
  const handleProceedToSendConfirmation = async () => {
    setShowInvitationsPreview(false)
    setShowSendConfirmation(true)
  }

  const handleCancelSendConfirmation = () => {
    if (isSendingInvitations) return

    // Back to the preview rather than out of the flow entirely, so cancelling is not punishing.
    setShowSendConfirmation(false)
    setShowInvitationsPreview(true)
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
      setShowSendConfirmation(false)
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
      'Reveal who everybody drew? Every participant will be emailed to say the gift exchange has finished, along with who picked whose name. This cannot be undone, so only do it once the gift exchange has actually happened.'
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
      console.error('Error revealing picked names:', err)
      setError(err instanceof Error ? err.message : 'Failed to reveal the picked names')
    } finally {
      setIsClosing(false)
    }
  }

  /**
   * The copy is a separate gift exchange, so the organizer is taken to it. Staying on a revealed
   * exchange after asking for a new one would leave them wondering whether anything happened.
   */
  const handleCopyHat = async (newHatName: string, excludePreviousRecipients: boolean) => {
    if (!hatId) return

    const { hatId: newHatId, participantsOmitted } = await copyHat({
      organizerEmail: userEmail,
      hatId,
      newHatName,
      excludePreviousRecipients,
    })

    // Carried across the navigation rather than shown before it, because the exchange it is about
    // is the one we are going to. A count and not names: the API does not say who, deliberately,
    // and an organizer holding both lists could subtract one from the other.
    navigate(`/gift-exchange/${newHatId}`, {
      state:
        participantsOmitted > 0
          ? {
              notice:
                participantsOmitted === 1
                  ? 'One person from the previous gift exchange was left out, because they have asked not to be added to gift exchanges. Add anybody else you need before you shake the hat.'
                  : `${participantsOmitted} people from the previous gift exchange were left out, because they have asked not to be added to gift exchanges. Add anybody else you need before you shake the hat.`,
            }
          : undefined,
    })
  }

  /**
   * Draws the names the way the dialog was told to.
   *
   * Errors are rethrown rather than set on the page. A shake that could not be satisfied is the
   * one failure the organizer can do something about immediately — by picking a looser rule — so
   * it belongs in the dialog beside the options, not behind it. The confirmation for re-shaking
   * lives in the dialog too, which is why there is no confirm() here any more.
   */
  const handleShakeHat = async (drawType: DrawType) => {
    if (!hatId || !hat) return

    setIsAssigning(true)
    setValidationErrors([])
    setError('')

    try {
      // Assign recipients (validation not needed - hat is already validated if status is NAMES_ASSIGNED)
      await assignRecipients({
        organizerEmail: userEmail,
        hatId,
        drawType,
      })

      // Reload the hat data
      const updatedHat = await getHat(userEmail, hatId)
      setHat(updatedHat)
    } catch (err) {
      console.error('Error shaking hat:', err)
      throw err
    } finally {
      setIsAssigning(false)
    }
  }

  /**
   * Downloads the whole exchange as a file.
   *
   * The notice rather than the page-level `error` on the way out: an export that failed has changed
   * nothing, and replacing the exchange with an error message would be a far bigger reaction than
   * the failure deserves.
   */
  const handleExportHat = async () => {
    if (!hatId || !hat) return

    setIsExporting(true)
    setParticipantsNotice('')

    try {
      downloadExport(await exportHat({ organizerEmail: userEmail, hatId }))
    } catch (err) {
      console.error('Error exporting gift exchange:', err)
      setParticipantsNotice(
        err instanceof Error ? err.message : 'Failed to export the gift exchange'
      )
    } finally {
      setIsExporting(false)
    }
  }

  /**
   * Puts the exchange back to the beginning, keeping everybody in it.
   *
   * Errors are rethrown rather than set on the page, as the shake does: the dialog is still open,
   * it is where the organizer is looking, and a reset refused because invitations went out in
   * another tab is something they need to read beside the button they just pressed.
   */
  const handleResetHat = async () => {
    if (!hatId || !hat) return

    try {
      await resetHat({ organizerEmail: userEmail, hatId })

      setValidationErrors([])
      setEditingEligibleFor(null)
      setHat(await getHat(userEmail, hatId))
      setParticipantsNotice(
        'The gift exchange has been reset. Everybody is in it still, and everybody can draw everybody else again.'
      )
    } catch (err) {
      console.error('Error resetting gift exchange:', err)
      throw err
    }
  }

  const isEditableStatus = hat
    ? ['IN_PROGRESS', 'READY_FOR_ASSIGNMENT', 'NAMES_ASSIGNED'].includes(hat.status)
    : false

  // Eligibility is about who somebody may draw, so with only the organizer in the hat there is
  // nothing to edit. Offering the row anyway opened an editor with nothing in it.
  const canEditEligibility = isEditableStatus && (hat?.participants.length ?? 0) > 1

  // Mirrors HatStatuses.BeforeInvitationsSent on the server, which is what actually decides. Once
  // invitations are out, people have been told who they drew, and undoing the draw would make what
  // they were told wrong with no way to tell them so.
  const canReset = hat
    ? ['IN_PROGRESS', 'READY_FOR_ASSIGNMENT', 'NAMES_ASSIGNED'].includes(hat.status)
    : false

  // The same line, for the same reason, one step harsher: once invitations are out there are people
  // waiting on this exchange, and deleting it would leave them waiting on nothing.
  const canDelete = hat
    ? ['IN_PROGRESS', 'READY_FOR_ASSIGNMENT', 'NAMES_ASSIGNED'].includes(hat.status)
    : false

  return (
    <div className="app-container">
      <Header
        userEmail={userEmail}
        givenName={hat ? hat.organizer.name : null}
        onSignOut={onSignOut}
        onNameUpdated={handleNameUpdated}
        // The exchange on this page is one of the ones being deleted.
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
                <div className="hat-actions">
                  {hat.status !== 'INVITATIONS_SENT' && hat.status !== 'READY_TO_CLOSE' && hat.status !== 'CLOSED' && (
                    isEditing ? (
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
                    )
                  )}

                  {/*
                    Here at every status, unlike Edit. Exporting is a read and always makes sense;
                    resetting and deleting stop making sense once invitations are out, and the menu
                    says so rather than quietly dropping the options.
                  */}
                  <AdvancedOptionsMenu
                    canReset={canReset}
                    resetUnavailableReason="Invitations have gone out, so this can no longer be reset."
                    canDelete={canDelete}
                    deleteUnavailableReason="Invitations have gone out, so this can no longer be deleted."
                    isExporting={isExporting}
                    onExport={handleExportHat}
                    onReset={() => setShowResetModal(true)}
                    onDelete={() => setShowDeleteModal(true)}
                  />
                </div>
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

              <div className="status-progression">
                <div className="status-steps">
                  {HAT_STATUS_STEPS.map((status, index) => (
                    <Fragment key={status}>
                      {index > 0 && <div className="status-step-connector"></div>}
                      <div className={`status-step ${hat.status === status ? 'active' : ''}`}>
                        <div className="status-step-indicator"></div>
                        <div className="status-step-label">{formatHatStatus(status)}</div>
                      </div>
                    </Fragment>
                  ))}
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
                      onClick={() => setShowShakeModal(true)}
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
                      onClick={() => setShowShakeModal(true)}
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
                    <p className="action-hint">The picks stay hidden for a short while so nobody's surprise is spoiled early.</p>
                  </div>
                )}

                {hat.status === 'READY_TO_CLOSE' && (
                  <div className="action-container">
                    <button
                      className="action-button action-close-button"
                      onClick={handleCloseHat}
                      disabled={isClosing}
                    >
                      {isClosing ? 'Revealing...' : 'Reveal Picked Names'}
                    </button>
                    <p className="action-hint">
                      Show who everybody drew. Every participant is emailed to say the gift exchange has finished, with
                      the full list of who picked whose name. Do this once the gift exchange has actually happened — it
                      cannot be undone.
                    </p>
                  </div>
                )}

                {hat.status === 'CLOSED' && (
                  <div className="action-container">
                    <p className="action-complete">The picks are revealed</p>
                    <p className="action-hint">Everyone has been emailed the full list. See below for the names everyone was assigned.</p>
                    <button
                      className="action-button copy-hat-button"
                      onClick={() => setShowCopyModal(true)}
                    >
                      Copy to a New Exchange
                    </button>
                    <p className="action-hint">Start the next one with the same people and the same rules. This exchange stays as it is.</p>
                  </div>
                )}
              </div>

              <div className="participants-section">
                <div className="section-header">
                  <div>
                    <h3>Participants ({hat.participants.length})</h3>
                    {canEditEligibility && (
                      <p className="participants-edit-hint">Click a participant row to edit eligible recipients.</p>
                    )}
                  </div>
                  {hat.status !== 'CLOSED' && hat.status !== 'INVITATIONS_SENT' && hat.status !== 'READY_TO_CLOSE' && (
                    /*
                     * Disabled at the limit rather than hidden, so the exchange still says what it
                     * is that can no longer be done, and the hint below says why.
                     */
                    <button
                      className="primary-button"
                      onClick={() => setShowAddParticipantModal(true)}
                      disabled={hat.participants.length >= MAX_PARTICIPANTS}
                    >
                      Add Participant
                    </button>
                  )}
                </div>
                {hat.participants.length >= MAX_PARTICIPANTS && (
                  <p className="participants-edit-hint">
                    This exchange holds the most participants allowed ({MAX_PARTICIPANTS}). Remove
                    somebody to make room, or run a second exchange alongside it.
                  </p>
                )}
                {participantsNotice && (
                  <div className="participants-notice">
                    <span>{participantsNotice}</span>
                    <button
                      type="button"
                      className="participants-notice-dismiss"
                      onClick={() => setParticipantsNotice('')}
                      aria-label="Dismiss"
                    >
                      ×
                    </button>
                  </div>
                )}
                {hat.participants.length > 0 ? (
                  (hat.status === 'CLOSED' || hat.status === 'INVITATIONS_SENT' || hat.status === 'READY_TO_CLOSE') ? (
                    <table className="participants-table">
                      <thead>
                        <tr>
                          <th>Name</th>
                          <th>Picked Recipient</th>
                          {/*
                            * "Email Status" rather than "Email": the addresses are in the first
                            * column, and this one says what became of the mail sent to them.
                            *
                            * The explanation hangs off the header rather than off each row. What
                            * needs explaining is the column — and in particular that "Delivered"
                            * is a claim about a mail server rather than about a person — which is
                            * the same explanation for every participant in the table.
                            */}
                          <th>
                            Email Status
                            <button
                              type="button"
                              className="delivery-help-trigger"
                              onClick={() => setShowDeliveryHelp(true)}
                              aria-label="What do these email statuses mean?"
                              title="What do these email statuses mean?"
                            >
                              ?
                            </button>
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {hat.participants.map((participant, index) => {
                          // Empty when nothing has been heard, since the API spells a missing
                          // timestamp with the minimum date. Both are left out rather than
                          // rendered blank, so a row nothing is known about says only that.
                          const deliveredWhen = formatDateAndTime(participant.deliveryOccurredAt)
                          const deliveredAge = formatRelativeTime(participant.deliveryOccurredAt)
                          const messageLabel = formatDeliveryMessageType(participant.deliveryMessageType)

                          // What the status is about and when it happened, which together are what
                          // an organizer hands to somebody who cannot find their invitation. The
                          // date is the line and the elapsed time is the tooltip, which is the
                          // reverse of the exchange list: this one exists to be read out to a
                          // participant, and a mailbox is searched by date rather than by how long
                          // ago something was.
                          const deliveryMeta = [messageLabel, deliveredWhen].filter(Boolean).join(' · ')

                          return (
                            <tr key={index}>
                              <td>
                                <div className="participant-name-cell">
                                  <div className="participant-name-line">
                                    <button
                                      type="button"
                                      className="participant-emoji-button"
                                      onClick={() => handleOpenEmojiModal(participant)}
                                      aria-label={`Change ${participant.person.name}'s emoji`}
                                      title={`Change ${participant.person.name}'s emoji`}
                                    >
                                      {participant.emoji}
                                    </button>
                                    <strong>{participant.person.name}</strong>
                                  </div>
                                  <span className="participant-email">{participant.person.email}</span>
                                  {/*
                                    * Only here, on the table shown once invitations have gone out.
                                    * This is where a wrong address is discovered — the delivery
                                    * column is right beside it — and where removing and re-adding
                                    * somebody would break the draw instead of fixing anything.
                                    */}
                                  <div className="participant-edit-actions">
                                    <button
                                      type="button"
                                      className="edit-address-button"
                                      onClick={() => handleOpenAddressModal(participant)}
                                    >
                                      Edit Address
                                    </button>
                                    {/*
                                      * Beside the address for the same reason the address is here:
                                      * this is the table an organizer is looking at when somebody
                                      * writes back to say their name is spelled wrong, and neither
                                      * repair should cost them the draw.
                                      */}
                                    <button
                                      type="button"
                                      className="edit-name-button"
                                      onClick={() => handleOpenNameModal(participant)}
                                    >
                                      Edit Name
                                    </button>
                                  </div>
                                </div>
                              </td>
                              <td>
                                {/*
                                  * The face belongs to the person named here, so it only appears
                                  * once that name is a real one: before the exchange is closed the
                                  * pick reads "Hidden", which matches nobody and carries no face.
                                  */}
                                <strong>
                                  {emojiForName(participant.pickedRecipient) && (
                                    <span className="participant-emoji">
                                      {emojiForName(participant.pickedRecipient)}{' '}
                                    </span>
                                  )}
                                  {participant.pickedRecipient || 'Not assigned'}
                                </strong>
                              </td>
                              <td>
                                <div className="delivery-cell">
                                  <span
                                    className={`delivery-status delivery-status-${deliveryTone(participant.deliveryStatus)}`}
                                  >
                                    {formatDeliveryStatus(participant.deliveryStatus)}
                                  </span>
                                  {showsDeliveryDetail(participant.deliveryStatus, participant.deliveryDetail) && (
                                    // Rendered as text, never as markup. This sentence was written by
                                    // whichever mail server refused the message and has passed through
                                    // no moderation of any kind.
                                    <span className="delivery-detail">{participant.deliveryDetail}</span>
                                  )}
                                  {deliveryMeta && (
                                    <span className="delivery-meta" title={deliveredAge || undefined}>
                                      {deliveryMeta}
                                    </span>
                                  )}
                                </div>
                              </td>
                            </tr>
                          )
                        })}
                      </tbody>
                    </table>
                  ) : (
                    <table className="participants-table">
                      <thead>
                        <tr>
                          <th>Name</th>
                          <th>Recipients</th>
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
                              className={[
                                isEditingThis ? 'editing-row' : '',
                                !isEditingThis && canEditEligibility ? 'clickable-row' : '',
                                canEditEligibility ? 'editable-status-clickable' : '',
                              ].filter(Boolean).join(' ')}
                              onClick={() => canEditEligibility && !isEditingThis && handleEditEligibleRecipients(
                                participant.person.email,
                                participant.eligibleRecipients
                              )}
                            >
                              <td>
                                <div className="participant-name-cell">
                                  <div className="participant-name-line">
                                    {/*
                                      * The row itself opens the eligibility editor, so the click
                                      * that opens the emoji picker has to stop there.
                                      */}
                                    <button
                                      type="button"
                                      className="participant-emoji-button"
                                      onClick={(e) => {
                                        e.stopPropagation()
                                        handleOpenEmojiModal(participant)
                                      }}
                                      aria-label={`Change ${participant.person.name}'s emoji`}
                                      title={`Change ${participant.person.name}'s emoji`}
                                    >
                                      {participant.emoji}
                                    </button>
                                    <strong>
                                      {participant.person.name}
                                      {isOrganizer && <span className="organizer-badge">Organizer</span>}
                                    </strong>
                                  </div>
                                  <span className="participant-email">{participant.person.email}</span>
                                  {canEditEligibility && !isEditingThis && (
                                    <span className="row-edit-indicator">Click row to edit</span>
                                  )}
                                  {isEditingThis && (
                                    <div className="participant-edit-actions">
                                      <button
                                        type="button"
                                        className="edit-address-button"
                                        onClick={() => handleOpenAddressModal(participant)}
                                      >
                                        Edit Address
                                      </button>
                                      <button
                                        type="button"
                                        className="edit-name-button"
                                        onClick={() => handleOpenNameModal(participant)}
                                      >
                                        Edit Name
                                      </button>
                                    </div>
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
                                        <div className="recipient-summary">
                                          <div className="recipient-group recipient-group-eligible">
                                            <span className="recipient-group-label">Eligible</span>
                                            {eligibleRecipients.length > 0 ? (
                                              <div className="recipient-chips">
                                                {eligibleRecipients.map((name, nameIndex) => (
                                                  <span key={`${name}-${nameIndex}`} className="recipient-chip">{name}</span>
                                                ))}
                                              </div>
                                            ) : (
                                              <span className="recipient-group-empty">None</span>
                                            )}
                                          </div>
                                          {ineligibleRecipients.length > 0 && (
                                            <div className="recipient-group recipient-group-ineligible">
                                              <span className="recipient-group-label">Ineligible</span>
                                              <div className="recipient-chips">
                                                {ineligibleRecipients.map((name, nameIndex) => (
                                                  <span key={`${name}-${nameIndex}`} className="recipient-chip">{name}</span>
                                                ))}
                                              </div>
                                            </div>
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
            </div>
          ) : (
            <p className="error-message">Gift exchange not found</p>
          )}
        </div>
      </main>

      <Footer />

      {editingAddressFor && hat && (
        <EditAddressModal
          participantName={editingAddressFor.person.name}
          currentEmail={editingAddressFor.person.email}
          resendKind={resendKindFor(hat.status)}
          isSaving={savingAddress}
          error={addressError}
          onCancel={() => setEditingAddressFor(null)}
          onConfirm={handleSaveAddress}
        />
      )}

      {editingNameFor && (
        <EditParticipantNameModal
          currentName={editingNameFor.person.name}
          currentEmail={editingNameFor.person.email}
          isSaving={savingName}
          error={nameError}
          onCancel={() => setEditingNameFor(null)}
          onConfirm={handleSaveName}
        />
      )}

      {editingEmojiFor && (
        <EditEmojiModal
          participantName={editingEmojiFor.person.name}
          currentEmoji={editingEmojiFor.emoji}
          isSaving={savingEmoji}
          error={emojiError}
          onCancel={() => setEditingEmojiFor(null)}
          onConfirm={handleSaveEmoji}
        />
      )}

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
          onSend={handleProceedToSendConfirmation}
        />
      )}

      {showShakeModal && hat && (
        <ShakeHatModal
          isReshake={hat.status === 'NAMES_ASSIGNED'}
          participantCount={hat.participants.length}
          onClose={() => setShowShakeModal(false)}
          onSubmit={handleShakeHat}
        />
      )}

      {showResetModal && hat && (
        <ResetHatModal
          hatName={hat.name}
          participantCount={hat.participants.length}
          hasBeenShaken={hat.status === 'NAMES_ASSIGNED'}
          onClose={() => setShowResetModal(false)}
          onSubmit={handleResetHat}
        />
      )}

      {showDeleteModal && hat && (
        <DeleteHatModal
          hatName={hat.name}
          participantCount={hat.participants.length}
          hasBeenShaken={hat.status === 'NAMES_ASSIGNED'}
          onClose={() => setShowDeleteModal(false)}
          onSubmit={handleDeleteHat}
        />
      )}

      {showCopyModal && hat && (
        <CopyHatModal
          sourceHatName={hat.name}
          participants={hat.participants}
          onClose={() => setShowCopyModal(false)}
          onSubmit={handleCopyHat}
        />
      )}

      {showDeliveryHelp && hat && (
        <DeliveryHelpModal
          organizerName={hat.organizer.name}
          onClose={() => setShowDeliveryHelp(false)}
        />
      )}

      {showSendConfirmation && invitationsPreview && hat && (
        <SendConfirmationModal
          organizerEmail={userEmail}
          senderIpAddress={invitationsPreview.senderIpAddress}
          recipientCount={hat.participants.length}
          isSending={isSendingInvitations}
          onCancel={handleCancelSendConfirmation}
          onConfirm={handleConfirmSendInvitations}
        />
      )}
    </div>
  )
}
