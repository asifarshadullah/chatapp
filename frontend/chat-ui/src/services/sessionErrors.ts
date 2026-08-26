/**
 * Raised when the session has ended — the access token has lapsed and cannot be renewed —
 * rather than the server being unreachable. Callers must tell those two apart: reporting an
 * ended session as "server down" sends users to debug their infrastructure when all they
 * need to do is sign in again.
 *
 * Defined in its own module because both authService and signalRService raise it, and
 * signalRService already depends on authService.
 */
export class SessionExpiredError extends Error {
  constructor() {
    super('Session expired')
    this.name = 'SessionExpiredError'
  }
}

export const SESSION_EXPIRED_MESSAGE = 'Your session has expired. Please sign in again.'
export const CONNECT_FAILED_MESSAGE = 'Failed to connect to chat server.'
