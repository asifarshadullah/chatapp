import { authService } from './authService'

/**
 * Performs a request with a bearer token that is good at the moment of sending, renewing
 * first when the stored one is stale.
 *
 * If the server still refuses, the request is retried exactly once against a token known to
 * be a different one: the stored token can be rejected even when it looks unexpired, for
 * instance after a clock skew or a server restart, so re-reading storage is not enough. One
 * retry, never a loop — a second refusal means the session really is over.
 *
 * Raises SessionExpiredError when no token can be obtained, so callers can send the user to
 * sign in rather than reporting a transport failure.
 */
export async function authorizedFetch(
  input: string,
  init: RequestInit = {},
): Promise<Response> {
  const token = await authService.getValidToken()
  const response = await fetch(input, withBearer(init, token))

  if (response.status !== 401) return response

  // Naming the rejected token matters twice over: the renewal must not hand back the token
  // the server just refused, and a token another tab obtained meanwhile is a fresh answer
  // rather than the same wrong one.
  const renewed = await authService.refresh(token)
  return fetch(input, withBearer(init, renewed))
}

function withBearer(init: RequestInit, token: string): RequestInit {
  return {
    ...init,
    headers: { ...(init.headers as Record<string, string>), Authorization: `Bearer ${token}` },
  }
}
