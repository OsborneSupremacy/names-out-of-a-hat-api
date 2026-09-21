import { displayName } from './participantNaming'

const ana = { name: 'Ana', email: 'ana@example.com' }
const firstSam = { name: 'Sam', email: 'sam.one@example.com' }
const secondSam = { name: 'sam ', email: 'sam.two@example.com' }

describe('displayName', () => {
  it('is the name alone when nobody else has it', () => {
    expect(displayName(ana, [ana, firstSam])).toBe('Ana')
  })

  it('adds the address when somebody else has the same name, ignoring case and spacing', () => {
    expect(displayName(firstSam, [ana, firstSam, secondSam])).toBe('Sam (sam.one@example.com)')
  })

  it('does not count the person themselves', () => {
    expect(displayName(firstSam, [firstSam])).toBe('Sam')
  })

  it('leaves an empty name empty', () => {
    expect(displayName({ name: '', email: '' }, [firstSam, secondSam])).toBe('')
  })
})
