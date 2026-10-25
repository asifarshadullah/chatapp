const CONVERSATION_PARAM = 'conversation'
const CONVERSATION_ID_FORMAT = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function conversationInUrl(): string | null {
  return new URLSearchParams(window.location.search).get(CONVERSATION_PARAM) || null
}

/** The id is put into a request path, so anything else could steer that request elsewhere. */
export function isConversationId(value: string): boolean {
  return CONVERSATION_ID_FORMAT.test(value)
}

export function showConversationInUrl(conversationId: string) {
  if (conversationInUrl() === conversationId) return
  replaceConversationParam((params) => params.set(CONVERSATION_PARAM, conversationId))
}

export function removeConversationFromUrl() {
  if (conversationInUrl() === null) return
  replaceConversationParam((params) => params.delete(CONVERSATION_PARAM))
}

function replaceConversationParam(change: (params: URLSearchParams) => void) {
  const url = new URL(window.location.href)
  change(url.searchParams)
  window.history.replaceState(window.history.state, '', url)
}
