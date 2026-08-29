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

/**
 * Raised when a renewal could not be completed but the session has not been shown to be over
 * — another client of it renewed successfully moments ago, so the credential is live and this
 * client simply lost the race.
 *
 * Deliberately not a subclass of SessionExpiredError: that error is acted on rather than
 * merely reported, and the action revokes the refresh credential for every client of the
 * session. A client that cannot renew right now must be able to say so without ending a
 * session that other tabs are still using.
 */
export class RenewalFailedError extends Error {
  constructor() {
    super('Renewal failed')
    this.name = 'RenewalFailedError'
  }
}

export const SESSION_EXPIRED_MESSAGE = 'Your session has expired. Please sign in again.'
export const CONNECT_FAILED_MESSAGE = 'Failed to connect to chat server.'
