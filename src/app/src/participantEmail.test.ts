import { Hat } from './api'
import {
  buildMailtoUrl,
  describeExchange,
  invitationSubject,
  resultsSubject,
  SENDER_ADDRESS,
  topicsFor,
} from './participantEmail'

function hat(overrides: Partial<Hat> = {}): Hat {
  return {
    id: 'hat-1',
    name: 'Family Christmas',
    additionalInformation: '',
    priceRange: '',
    organizer: { name: 'Jane', email: 'jane@example.com' },
    participants: [],
    status: 'INVITATIONS_SENT',
    exchangeDate: '0001-01-01',
    ...overrides,
  }
}

const topicIds = (h: Hat) => topicsFor(h).map((topic) => topic.id)

describe('topicsFor', () => {
  it.each(['IN_PROGRESS', 'READY_FOR_ASSIGNMENT', 'NAMES_ASSIGNED'])(
    'offers the heads-up before invitations go out (%s)',
    (status) => {
      expect(topicIds(hat({ status }))).toEqual(['invitations-coming', 'something-else'])
    }
  )

  it.each(['INVITATIONS_SENT', 'READY_TO_CLOSE'])(
    'offers the invitation search once they are out (%s)',
    (status) => {
      expect(topicIds(hat({ status }))).toEqual(['look-for-invitations', 'something-else'])
    }
  )

  it('offers the date and budget reminder only when there is one to give', () => {
    expect(topicIds(hat({ priceRange: '$25' }))).toContain('date-and-budget')
    expect(topicIds(hat({ exchangeDate: '2026-12-25' }))).toContain('date-and-budget')
    expect(topicIds(hat({ priceRange: '  ' }))).not.toContain('date-and-budget')
  })

  it('offers the results search once revealed', () => {
    expect(topicIds(hat({ status: 'CLOSED' }))).toEqual(['look-for-results', 'something-else'])
  })

  it('puts the invitation search first, as the default, once invitations are out', () => {
    const [first] = topicsFor(hat())
    expect(first.label).toBe('Look for Gift Exchange Invitations')
  })
})

describe('the topics', () => {
  const compose = (id: string, h: Hat) => topicsFor(h).find((topic) => topic.id === id)!.compose(h)

  it('tells people what to search for to find the invitation', () => {
    const { subject, body } = compose('look-for-invitations', hat())

    expect(subject).toBe('Did you get your invitation to the gift exchange, Family Christmas?')
    expect(body).toContain(SENDER_ADDRESS)
    expect(body).toContain('Jane has added you to the gift exchange, Family Christmas!')
    expect(body).toContain('Promotions')
    expect(body).toMatch(/Jane$/)
  })

  it('leaves the body of "Something Else" to the organizer', () => {
    const { subject, body } = compose('something-else', hat())

    expect(subject).toBe('About the gift exchange, Family Christmas')
    expect(body).toBe('')
  })

  it('gives only the date or budget it has', () => {
    const { body } = compose('date-and-budget', hat({ priceRange: '$25' }))

    expect(body).toContain('The spending limit is $25.')
    expect(body).not.toContain('exchanging gifts on')
  })

  it('points to the results by their subject', () => {
    const { body } = compose('look-for-results', hat({ status: 'CLOSED' }))

    expect(body).toContain('The gift exchange, Family Christmas, has finished')
  })
})

describe('naming', () => {
  it('names an unnamed exchange plainly', () => {
    expect(describeExchange('  ')).toBe('the gift exchange')
    expect(invitationSubject('Jane', '')).toBe('Jane has added you to the gift exchange!')
    expect(resultsSubject('')).toBe('The gift exchange has finished')
  })
})

describe('buildMailtoUrl', () => {
  it('puts recipients in BCC and encodes what would break the link', () => {
    const url = buildMailtoUrl({
      to: ['jane@example.com'],
      bcc: ['a@example.com', 'b+x@example.com'],
      subject: 'Q&A #1?',
      body: 'Line one\nLine two',
    })

    expect(url).toBe(
      'mailto:jane%40example.com?bcc=a%40example.com,b%2Bx%40example.com' +
        '&subject=Q%26A%20%231%3F&body=Line%20one%0D%0ALine%20two'
    )
  })

  it('leaves out an empty body', () => {
    const url = buildMailtoUrl({ to: ['jane@example.com'], bcc: [], subject: 'Hi', body: '' })

    expect(url).toBe('mailto:jane%40example.com?subject=Hi')
  })
})
