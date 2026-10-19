/**
 * The exchange date as the API carries it: a yyyy-MM-dd string, with 0001-01-01 standing for "no
 * date", the same way the minimum timestamp stands for "never" everywhere else in the contract.
 *
 * A date input holds the same yyyy-MM-dd string, or the empty string when it is cleared, so the
 * only translation needed is between those two spellings of "none".
 */
export const NO_EXCHANGE_DATE = '0001-01-01'

export function hasExchangeDate(exchangeDate: string | undefined): boolean {
  return !!exchangeDate && exchangeDate !== NO_EXCHANGE_DATE
}

/** What a date input should show for a date from the API. */
export function toDateInputValue(exchangeDate: string | undefined): string {
  return hasExchangeDate(exchangeDate) ? exchangeDate! : ''
}

/** What to send the API for what a date input holds. */
export function fromDateInputValue(value: string): string {
  return value === '' ? NO_EXCHANGE_DATE : value
}

/**
 * Written out with the weekday, in the reader's locale, or the empty string for no date.
 *
 * Formatted in UTC because the string names a calendar day, not an instant. Parsing it as local
 * midnight and formatting in a zone west of UTC would show the day before.
 */
export function formatExchangeDate(exchangeDate: string | undefined, locale?: string): string {
  if (!hasExchangeDate(exchangeDate)) return ''

  const parsed = new Date(`${exchangeDate}T00:00:00Z`)
  if (Number.isNaN(parsed.getTime())) return ''

  return parsed.toLocaleDateString(locale, {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    timeZone: 'UTC',
  })
}

/** Today in the reader's own time zone, as a date input's min. */
export function todayAsDateInputValue(now: Date = new Date()): string {
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

/**
 * How close the exchange date has to be before the organizer is offered the reveal outright. A few
 * days of slack covers an exchange that happens a little early, or a date that was a guess.
 */
export const REVEAL_LEAD_DAYS = 3

/**
 * Whether the exchange date is still more than {@link REVEAL_LEAD_DAYS} days off. No date means no
 * way to tell, so it is never too early.
 *
 * Counted in whole calendar days from the reader's own today, so the answer does not change partway
 * through a day.
 */
export function isTooEarlyToReveal(exchangeDate: string | undefined, now: Date = new Date()): boolean {
  if (!hasExchangeDate(exchangeDate)) return false

  const exchangeDay = Date.parse(`${exchangeDate}T00:00:00Z`)
  if (Number.isNaN(exchangeDay)) return false

  const today = Date.parse(`${todayAsDateInputValue(now)}T00:00:00Z`)
  const daysUntil = Math.round((exchangeDay - today) / 86_400_000)

  return daysUntil > REVEAL_LEAD_DAYS
}
