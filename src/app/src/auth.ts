import { apiConfig } from './config'

const TOKEN_KEY = 'noah.accessToken'

export interface Session {
  token: string
  email: string
}

interface SessionClaims {
  email: string
  exp: number
}

/**
 * Reads the claims without verifying anything. The signature and expiry that matter are checked by
 * the Lambda authorizer on every request; this only lets the UI avoid sending a token it already
 * knows is stale, and decide whether to show the sign-in screen.
 */
function readClaims(token: string): SessionClaims | null {
  try {
    const payload = token.split('.')[1]
    if (!payload) return null

    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/')
    const json = decodeURIComponent(
      atob(base64)
        .split('')
        .map((character) => `%${character.charCodeAt(0).toString(16).padStart(2, '0')}`)
        .join(''),
    )

    const claims = JSON.parse(json)

    return typeof claims.email === 'string' && typeof claims.exp === 'number'
      ? { email: claims.email, exp: claims.exp }
      : null
  } catch {
    return null
  }
}

export function getSession(): Session | null {
  const token = localStorage.getItem(TOKEN_KEY)
  if (!token) return null

  const claims = readClaims(token)

  if (!claims || claims.exp * 1000 <= Date.now()) {
    localStorage.removeItem(TOKEN_KEY)
    return null
  }

  return { token, email: claims.email }
}

export function signOut(): void {
  localStorage.removeItem(TOKEN_KEY)
}

export async function requestMagicLink(email: string): Promise<void> {
  const response = await fetch(`${apiConfig.endpoint}/auth/requestlink`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  })

  // A 400 is about the address itself, such as one from a temporary email service, and trying
  // again won't fix it, so show the reason the server gave.
  if (response.status === 400) {
    const body = await response.json().catch(() => ({}))
    throw new Error(body.message || 'Please check your email address and try again.')
  }

  if (!response.ok) {
    throw new Error('Could not send the sign-in link. Please try again in a moment.')
  }
}

export async function redeemMagicLink(token: string): Promise<Session> {
  const response = await fetch(`${apiConfig.endpoint}/auth/redeem`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ token }),
  })

  if (!response.ok) {
    throw new Error('That sign-in link has expired or was already used. Please request a new one.')
  }

  const result = await response.json()
  localStorage.setItem(TOKEN_KEY, result.accessToken)

  return { token: result.accessToken, email: result.email }
}
