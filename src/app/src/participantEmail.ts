import { Hat } from './api'
import { formatExchangeDate, hasExchangeDate } from './exchangeDate'

/**
 * The address every message this application sends to a participant comes from.
 *
 * Written out here rather than fetched, because it is the one thing an organizer needs to hand
 * somebody who cannot find their invitation: searching a mailbox for it finds the message wherever
 * it was filed, which looking in the inbox does not. It is a literal in the sending services too
 * (InvitationQueueHandlerService and AutomaticEmailSender) — it is a public fact, printed in the
 * header of every email this exchange has already sent, rather than configuration.
 */
export const SENDER_ADDRESS = 'donotreply@mail.namesoutofahat.com'

/**
 * The exchange as it should be named in a sentence. Mirrors GiftExchangeNaming.Describe on the
 * server, so the organizer's email names the exchange the way ours did.
 */
export function describeExchange(name: string): string {
  return name.trim() ? `the gift exchange, ${name.trim()}` : 'the gift exchange'
}

/** The same, with the aside closed so the sentence can carry on. GiftExchangeNaming.DescribeMidSentence. */
function describeMidSentence(name: string): string {
  return name.trim() ? `${describeExchange(name)},` : describeExchange(name)
}

function capitalise(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/**
 * The invitation's subject line. Duplicates EmailCompositionService.GetSubject, and has to stay in
 * step with it: an organizer passes this on as something to search for.
 */
export function invitationSubject(organizerName: string, hatName: string): string {
  return `${organizerName} has added you to ${describeExchange(hatName)}!`
}

/** The reveal's subject line. Duplicates CompletionEmailCompositionService.GetSubject. */
export function resultsSubject(hatName: string): string {
  return `${capitalise(describeMidSentence(hatName))} has finished`
}

export interface ComposedEmail {
  subject: string
  body: string
}

export interface EmailTopic {
  id: string
  label: string
  compose: (hat: Hat) => ComposedEmail
}

const BEFORE_INVITATIONS = ['IN_PROGRESS', 'READY_FOR_ASSIGNMENT', 'NAMES_ASSIGNED']
const INVITATIONS_OUT = ['INVITATIONS_SENT', 'READY_TO_CLOSE']

function signOff(hat: Hat): string {
  return hat.organizer.name.trim() ? `Thanks,\n${hat.organizer.name.trim()}` : 'Thanks!'
}

/**
 * How to find a message of ours that has gone missing. The same advice DeliveryHelpModal gives the
 * organizer, written to the participant: search rather than look, because a search covers Spam, the
 * Gmail tabs, and whatever folder a rule moved it into.
 */
function findingAdvice(subject: string): string {
  return [
    `If you haven't seen it, it may have gone to Spam or Junk, to the Promotions or Updates tab in Gmail, or into a folder a filter moved it to (common on work addresses).`,
    `The quickest way to find it is to search your whole mailbox for:\n\n${SENDER_ADDRESS}\n\nor for the subject line:\n\n${subject}`,
    `If you find it in Spam, please mark it "not spam", so the next email from the gift exchange arrives in your inbox.`,
  ].join('\n\n')
}

const lookForInvitations: EmailTopic = {
  id: 'look-for-invitations',
  label: 'Look for Gift Exchange Invitations',
  compose: (hat) => ({
    subject: `Did you get your invitation to ${describeExchange(hat.name)}?`,
    body: [
      'Hi everyone,',
      `I'm organizing ${describeExchange(hat.name)} using Names Out Of A Hat, and it has emailed each of you an invitation telling you whose name you drew.`,
      findingAdvice(invitationSubject(hat.organizer.name, hat.name)),
      `If it really isn't anywhere, reply to me with the email address you'd like me to use instead.`,
      signOff(hat),
    ].join('\n\n'),
  }),
}

const invitationsComing: EmailTopic = {
  id: 'invitations-coming',
  label: 'Invitations Are Coming',
  compose: (hat) => ({
    subject: `Watch for your invitation to ${describeExchange(hat.name)}`,
    body: [
      'Hi everyone,',
      `I'm organizing ${describeExchange(hat.name)} using Names Out Of A Hat. Soon you'll get an email from ${SENDER_ADDRESS} telling you whose name you drew. Its subject line will be:`,
      invitationSubject(hat.organizer.name, hat.name),
      `Please keep an eye out for it, and check your Spam or Junk folder (or Gmail's Promotions tab) if it doesn't show up. If you find it there, please mark it "not spam".`,
      `If this isn't the email address you check, reply and tell me which one to use.`,
      signOff(hat),
    ].join('\n\n'),
  }),
}

const dateAndBudget: EmailTopic = {
  id: 'date-and-budget',
  label: 'Reminder: Date and Budget',
  compose: (hat) => {
    const facts = [
      hasExchangeDate(hat.exchangeDate) && `We're exchanging gifts on ${formatExchangeDate(hat.exchangeDate)}.`,
      hat.priceRange.trim() && `The spending limit is ${hat.priceRange.trim()}.`,
    ].filter(Boolean)

    return {
      subject: `Reminder: ${describeExchange(hat.name)}`,
      body: [
        'Hi everyone,',
        `A quick reminder about ${describeExchange(hat.name)}.`,
        facts.join(' '),
        `The name you drew is in the invitation email from ${SENDER_ADDRESS}. If you can't find it, search your whole mailbox for that address, including Spam.`,
        signOff(hat),
      ].join('\n\n'),
    }
  },
}

const lookForResults: EmailTopic = {
  id: 'look-for-results',
  label: 'Look for the Results Email',
  compose: (hat) => ({
    subject: `Who drew whom in ${describeExchange(hat.name)}`,
    body: [
      'Hi everyone,',
      `Names Out Of A Hat has emailed each of you the results of ${describeExchange(hat.name)}, showing who drew whom.`,
      findingAdvice(resultsSubject(hat.name)),
      signOff(hat),
    ].join('\n\n'),
  }),
}

const somethingElse: EmailTopic = {
  id: 'something-else',
  label: 'Something Else',
  compose: (hat) => ({
    subject: `About ${describeExchange(hat.name)}`,
    body: '',
  }),
}

/**
 * What an organizer might want to write about at this point in the exchange. Only what fits the
 * status: telling people to look for an invitation that hasn't been sent would send them looking
 * for nothing. "Something Else" is always there, and always last.
 */
export function topicsFor(hat: Hat): EmailTopic[] {
  const topics: EmailTopic[] = []

  if (BEFORE_INVITATIONS.includes(hat.status)) {
    topics.push(invitationsComing)
  }

  if (INVITATIONS_OUT.includes(hat.status)) {
    topics.push(lookForInvitations)

    if (hasExchangeDate(hat.exchangeDate) || hat.priceRange.trim()) {
      topics.push(dateAndBudget)
    }
  }

  if (hat.status === 'CLOSED') {
    topics.push(lookForResults)
  }

  topics.push(somethingElse)
  return topics
}

/**
 * A mailto: link, per RFC 6068. Line breaks become CRLF because that is what the RFC asks for, and
 * some mail apps drop a bare LF. Parameters that are empty are left out, since a few apps treat an
 * empty body= as a body to replace the signature with.
 */
export function buildMailtoUrl({
  to,
  bcc,
  subject,
  body,
}: {
  to: string[]
  bcc: string[]
  subject: string
  body: string
}): string {
  const addresses = (list: string[]) => list.map(encodeURIComponent).join(',')
  const text = (value: string) => encodeURIComponent(value.replace(/\r?\n/g, '\r\n'))

  const params = [
    bcc.length > 0 && `bcc=${addresses(bcc)}`,
    subject && `subject=${text(subject)}`,
    body && `body=${text(body)}`,
  ].filter(Boolean)

  return `mailto:${addresses(to)}${params.length > 0 ? `?${params.join('&')}` : ''}`
}

/**
 * Past roughly this length some mail apps — Outlook on the desktop especially — cut the link off,
 * losing recipients or the end of the message without saying so.
 */
export const MAILTO_LENGTH_WARNING = 1800
