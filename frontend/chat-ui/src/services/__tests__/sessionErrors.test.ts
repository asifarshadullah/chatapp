import { describe, it, expect } from 'vitest'
import { RenewalFailedError, SessionExpiredError } from '../sessionErrors'

/**
 * Two failures that look alike and mean opposite things. An ended session is acted on — the
 * handler for it signs out and revokes the credential family server-side — so a renewal that
 * merely could not be completed must not be reported with it, or one tab's bad luck would
 * end the session for every other tab of it.
 */

describe('renewal failure is not session expiry', () => {
  it('is a distinct type, not a subclass', () => {
    const failed = new RenewalFailedError()

    expect(failed).toBeInstanceOf(RenewalFailedError)
    // A subclass would be caught by every existing `instanceof SessionExpiredError` branch,
    // which is exactly the sign-out this type exists to avoid.
    expect(failed).not.toBeInstanceOf(SessionExpiredError)
  })

  it('is named for what it is', () => {
    expect(new RenewalFailedError().name).toBe('RenewalFailedError')
  })
})
