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
