import { apiConfig } from './config'
import { getSession } from './auth'
import { DrawType } from './drawType'

export interface HatMetadata {
  hatId: string
  hatName: string
  status: string
  /** When the status last changed. The minimum date where it is not known. */
  statusUpdatedAt: string
}

export interface GetHatsResponse {
  organizerName: string
  /** One page of hats, newest first. Empty when the page asked for is past the end. */
  hats: HatMetadata[]
  /** 1-based. */
  page: number
  /** Chosen by the server. */
  pageSize: number
  /** Every hat the organizer has, across all pages. */
  totalCount: number
}

export interface CreateHatRequest {
  hatName: string
  organizerName: string
  organizerEmail: string
}

export interface CreateHatResponse {
  hatId: string
}

export interface EditHatRequest {
  hatId: string
  organizerEmail: string
  name: string
  additionalInformation: string
  priceRange: string
}

export interface AddParticipantRequest {
  organizerEmail: string
  hatId: string
  name: string
  email: string
}

export interface RemoveParticipantRequest {
  organizerEmail: string
  hatId: string
  email: string
}

export interface EditParticipantRequest {
  organizerEmail: string
  hatId: string
  email: string
  eligibleRecipients: string[]
}

export interface EditParticipantEmojiRequest {
  organizerEmail: string
  hatId: string
  email: string
  /** One of PERSON_EMOJI in personEmoji.ts. The server refuses anything else. */
  emoji: string
}

export interface EditParticipantNameRequest {
  organizerEmail: string
  hatId: string
  /** The address the participant is recorded at, which is how the server finds them. */
  email: string
  name: string
}

export interface EditParticipantAddressRequest {
  organizerEmail: string
  hatId: string
  currentEmail: string
  newEmail: string
}

export interface EditParticipantAddressResponse {
  /** False before invitations have gone out, when there is nothing to resend. */
  emailResent: boolean
  /** INVITATION, COMPLETION, or empty when nothing was sent. */
  messageType: string
}

export interface DeleteHatRequest {
  organizerEmail: string
  hatId: string
}

export interface ValidateHatRequest {
  organizerEmail: string
  hatId: string
}

export interface ValidateHatResponse {
  success: boolean
  errors: string[]
}

export interface AssignRecipientsRequest {
  organizerEmail: string
  hatId: string
  /** One of the DrawType values in drawType.ts. Required: the server has no default. */
  drawType: DrawType
}

export interface SendInvitationsRequest {
  organizerEmail: string
  hatId: string
}

export interface PreviewInvitationsRequest {
  organizerEmail: string
  hatId: string
}

export interface PreviewInvitationsResponse {
  subject: string
  htmlBody: string
  senderIpAddress: string
}

export interface UpdateProfileRequest {
  name: string
}

export interface DeleteMyDataRequest {
  /** Also remove the caller's own name, where no exchange somebody else runs still shows it. */
  forgetMe: boolean
  /** Never let anybody add the caller's address to a gift exchange again. */
  doNotAddAnywhere: boolean
}

/** The categories the contact form offers. Mirrors FeedbackCategories.All on the server. */
export type FeedbackCategory = 'QUESTION' | 'FEATURE_REQUEST' | 'OTHER_FEEDBACK'

export interface SubmitFeedbackRequest {
  category: FeedbackCategory
  message: string
}

export interface CopyHatRequest {
  organizerEmail: string
  hatId: string
  newHatName: string
  excludePreviousRecipients: boolean
}

export interface CopyHatResponse {
  hatId: string
  /**
   * How many of the previous exchange's participants were not carried over, because they had asked
   * not to be added. A count and not names: the API does not say who, deliberately.
   */
  participantsOmitted: number
}

export interface CloseHatRequest {
  organizerEmail: string
  hatId: string
}

export interface ExportHatRequest {
  organizerEmail: string
  hatId: string
}

/** One participant, pointed at from another. The all-zero uuid and an empty name mean nobody. */
export interface ExportedParticipantReference {
  participantId: string
  name: string
}

export interface ExportedPerson {
  personId: string
  name: string
  email: string
}

export interface ExportedParticipant {
  participantId: string
  person: ExportedPerson
  /**
   * Who this participant drew. The empty reference until the picks are revealed — the exchange
   * keeps the draw from its own organizer until they close it, and the export answers that
   * question the same way the detail view does.
   */
  pickedRecipient: ExportedParticipantReference
  /** The face this participant is marked with. One of a closed list the server owns. */
  emoji: string
  eligibleRecipients: ExportedParticipantReference[]
  deliveryStatus: string
  deliveryDetail: string
  deliveryMessageType: string
  deliveryOccurredAt: string
}

export interface ExportedHat {
  hatId: string
  name: string
  status: string
  additionalInformation: string
  priceRange: string
  createdAt: string
  invitationsQueuedAt: string
  /** The exchange this one was copied from, or the all-zero uuid when it was not a copy. */
  copiedFromHatId: string
  organizer: ExportedPerson
  participants: ExportedParticipant[]
}

export interface ExportHatResponse {
  formatVersion: string
  exportedAt: string
  hat: ExportedHat
}

export interface ResetHatRequest {
  organizerEmail: string
  hatId: string
}

export interface Participant {
  person: {
    name: string
    email: string
  }
  pickedRecipient: string
  eligibleRecipients: string[]
  /**
   * The face this participant is marked with wherever they are named, here and in the email that
   * tells somebody they drew them. One of a closed list the server owns, so it is rendered as it
   * stands rather than treated as something a person typed.
   */
  emoji: string
  /**
   * How far the last email sent to this participant is known to have got. Empty means nothing has
   * been heard, which is not the same as not delivered — see deliveryStatus.ts.
   */
  deliveryStatus: string
  /** Why, for the statuses that have a why. Written by a remote mail server, so never trusted as markup. */
  deliveryDetail: string
  /**
   * Which of our emails the status above is about — INVITATION, COMPLETION, PARTICIPANT_LEFT or
   * ORGANIZER_LEFT_NOTE. Empty when nothing has been heard.
   *
   * An exchange sends more than one thing to the same person and the status is the newest of them,
   * so without this "Delivered" cannot be read as being about the invitation.
   */
  deliveryMessageType: string
  /**
   * When that happened, as SES reported it. The minimum date when nothing has been heard, which is
   * what relativeTime.ts renders as nothing at all.
   */
  deliveryOccurredAt: string
}

export interface Hat {
  id: string
  name: string
  additionalInformation: string
  priceRange: string
  organizer: {
    name: string
    email: string
  }
  participants: Participant[]
  status: string
}

async function getAuthHeaders() {
  const session = getSession()

  if (!session) {
    throw new Error('Your session has expired. Please sign in again.')
  }

  return {
    'Authorization': `Bearer ${session.token}`,
    'Content-Type': 'application/json',
  }
}

async function handleApiError(response: Response, defaultMessage: string): Promise<never> {
  // Reading the body is best effort: some endpoints answer with a status and no body at all, and
  // API Gateway's own error responses are not always JSON. A parse failure must not become the
  // message the user sees, which is what the previous `throw e` in the catch produced.
  let message = ''

  try {
    const errorData = await response.json()

    if (typeof errorData?.message === 'string') {
      message = errorData.message
    }
  } catch {
    // Leave message empty and fall through to the default.
  }

  // The status is included because it is often the only clue when the body is empty.
  throw new Error(message || `${defaultMessage} (${response.status} ${response.statusText})`)
}

export async function getHats(email: string, page = 1): Promise<GetHatsResponse> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hats/${encodeURIComponent(email)}?page=${page}`, {
    method: 'GET',
    headers,
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to fetch hats')
  }

  return response.json()
}

export async function createHat(request: CreateHatRequest): Promise<CreateHatResponse> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to create gift exchange')
  }

  return response.json()
}

export async function getHat(email: string, hatId: string): Promise<Hat> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat/${encodeURIComponent(email)}/${hatId}`, {
    method: 'GET',
    headers,
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to fetch gift exchange')
  }

  return response.json()
}

export async function editHat(request: EditHatRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to edit gift exchange')
  }
}

export async function addParticipant(request: AddParticipantRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/participant`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to add participant')
  }
}

export async function removeParticipant(request: RemoveParticipantRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/participant`, {
    method: 'DELETE',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to remove participant')
  }
}

export async function editParticipant(request: EditParticipantRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/participant`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to edit participant')
  }
}

/**
 * Changes the face a participant is marked with.
 *
 * Its own endpoint rather than part of editParticipant, for the reason editParticipantAddress is
 * one: that call resets the exchange to IN_PROGRESS, which would throw away a completed draw over
 * a change of decoration.
 */
export async function editParticipantEmoji(request: EditParticipantEmojiRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/participant/emoji`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to update the emoji')
  }
}

/**
 * Changes the name a participant is known by.
 *
 * Its own endpoint rather than part of editParticipant, for the reason editParticipantEmoji is one:
 * that call resets the exchange to IN_PROGRESS, and a name has nothing to do with the draw —
 * eligibility and picks are held server-side as ids, so a rename cannot invalidate one.
 *
 * Two refusals are worth knowing about at the call site, both surfaced as the server's own message:
 * 409 when somebody in an exchange this person is in already goes by the new name, and 403 when the
 * caller neither is that person nor added them. Nothing in the participant payload says which
 * organizer introduced whom, so the 403 can only be found out by asking.
 */
export async function editParticipantName(request: EditParticipantNameRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/participant/name`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to update the name')
  }
}

/**
 * Corrects the address a participant was invited at.
 *
 * Its own endpoint rather than part of editParticipant, which edits eligibility and resets the
 * exchange to IN_PROGRESS as a side effect — harmless before the draw, and destructive after
 * invitations have gone out, which is exactly when an address needs correcting.
 */
export async function editParticipantAddress(
  request: EditParticipantAddressRequest
): Promise<EditParticipantAddressResponse> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/participant/address`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to update the address')
  }

  return await response.json()
}

export async function deleteHat(request: DeleteHatRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat`, {
    method: 'DELETE',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to delete gift exchange')
  }
}

export async function validateHat(request: ValidateHatRequest): Promise<ValidateHatResponse> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat/validate`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to validate gift exchange')
  }

  return response.json()
}

export async function assignRecipients(request: AssignRecipientsRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/recipients`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to assign recipients')
  }
}

export async function sendInvitations(request: SendInvitationsRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat/sendinvitations`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to send invitations')
  }
}

export async function previewInvitations(request: PreviewInvitationsRequest): Promise<PreviewInvitationsResponse> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat/${encodeURIComponent(request.organizerEmail)}/previewinvitations/${request.hatId}`, {
    method: 'GET',
    headers,
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to preview invitations')
  }

  return response.json()
}

export async function closeHat(request: CloseHatRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat/close`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to close gift exchange')
  }
}

export async function copyHat(request: CopyHatRequest): Promise<CopyHatResponse> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat/copy`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to copy gift exchange')
  }

  return response.json()
}

/**
 * The whole gift exchange as data, for the organizer to keep.
 *
 * A GET like the other reads, and the response is handed to the caller rather than saved here:
 * turning it into a file is the browser's business, not the API client's.
 */
export async function exportHat(request: ExportHatRequest): Promise<ExportHatResponse> {
  const headers = await getAuthHeaders()

  const response = await fetch(
    `${apiConfig.endpoint}/hat/${encodeURIComponent(request.organizerEmail)}/export/${request.hatId}`,
    {
      method: 'GET',
      headers,
    }
  )

  if (!response.ok) {
    await handleApiError(response, 'Failed to export gift exchange')
  }

  return response.json()
}

/**
 * Takes the gift exchange back to the beginning: the same people, everybody allowed to draw
 * everybody, nobody holding a name. Refused once invitations have gone out.
 */
export async function resetHat(request: ResetHatRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/hat/reset`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to reset gift exchange')
  }
}

export async function updateProfile(request: UpdateProfileRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/profile`, {
    method: 'PUT',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to update your name')
  }
}

/**
 * Asks for everything the caller has organized to be deleted.
 *
 * The server answers 202 once the request is accepted, not once the deleting is done: that happens
 * on a queue, and can take a minute for somebody with many exchanges. A list fetched straight
 * afterwards may still include some of them.
 */
export async function deleteMyData(request: DeleteMyDataRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/profile`, {
    method: 'DELETE',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to delete your data')
  }
}

export async function submitFeedback(request: SubmitFeedbackRequest): Promise<void> {
  const headers = await getAuthHeaders()

  const response = await fetch(`${apiConfig.endpoint}/feedback`, {
    method: 'POST',
    headers,
    body: JSON.stringify(request),
  })

  if (!response.ok) {
    await handleApiError(response, 'Failed to send your message')
  }
}
