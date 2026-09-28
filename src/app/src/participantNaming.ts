import { Person } from './api'

/**
 * How one participant is named next to the others: by name alone, unless somebody else in the
 * exchange shares it, in which case the address follows in brackets — "Sam (sam@example.com)".
 *
 * The same rule the server applies to the emails it sends. A person is one record shared by every
 * exchange they are in, so two people in one exchange may answer to the same name, and the address
 * is what tells them apart.
 */
export function displayName(person: Person, people: Person[]): string {
  if (!person.name.trim()) {
    return person.name
  }

  const shared = people.some(
    (other) => !sameText(other.email, person.email) && sameText(other.name, person.name)
  )

  return shared ? `${person.name} (${person.email})` : person.name
}

/** "Sam", "Sam and Alex", "Sam, Alex, and Jo" — for a list of names inside a sentence. */
export function formatNames(names: string[]): string {
  if (names.length === 1) return names[0]
  if (names.length === 2) return `${names[0]} and ${names[1]}`
  return `${names.slice(0, -1).join(', ')}, and ${names[names.length - 1]}`
}

function sameText(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase()
}
