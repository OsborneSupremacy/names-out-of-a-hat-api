import { describe, expect, it } from 'vitest'
import {
  NO_EXCHANGE_DATE,
  formatExchangeDate,
  fromDateInputValue,
  hasExchangeDate,
  toDateInputValue,
  todayAsDateInputValue,
} from './exchangeDate'

describe('exchangeDate', () => {
  it('treats the minimum date as no date', () => {
    expect(hasExchangeDate(NO_EXCHANGE_DATE)).toBe(false)
    expect(hasExchangeDate('')).toBe(false)
    expect(hasExchangeDate(undefined)).toBe(false)
    expect(hasExchangeDate('2026-12-25')).toBe(true)
  })

  it('shows no date as an empty input, and sends an empty input as no date', () => {
    expect(toDateInputValue(NO_EXCHANGE_DATE)).toBe('')
    expect(toDateInputValue('2026-12-25')).toBe('2026-12-25')
    expect(fromDateInputValue('')).toBe(NO_EXCHANGE_DATE)
    expect(fromDateInputValue('2026-12-25')).toBe('2026-12-25')
  })

  it('formats the calendar day it names, whatever the local time zone', () => {
    // Christmas 2026 is a Friday. Parsed as local midnight west of UTC, it would come out as
    // Thursday the 24th.
    expect(formatExchangeDate('2026-12-25', 'en-US')).toBe('Friday, December 25, 2026')
  })

  it('formats no date as nothing', () => {
    expect(formatExchangeDate(NO_EXCHANGE_DATE, 'en-US')).toBe('')
    expect(formatExchangeDate('not a date', 'en-US')).toBe('')
  })

  it('gives today in local time as a date input value', () => {
    expect(todayAsDateInputValue(new Date(2026, 0, 5, 23, 30))).toBe('2026-01-05')
  })
})
