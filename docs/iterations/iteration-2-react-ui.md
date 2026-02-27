# Iteration 2: React UI with Vite + TypeScript

## Goal
Create a ChatGPT-like chat interface that sends messages to the echo endpoint and displays responses,
with component-level tests using Vitest + React Testing Library.

## Context
Backend echo endpoint from Iteration 1 is running at `http://localhost:5xxx`.
CORS is already configured for `http://localhost:5173` (Vite default).

## Prerequisites
- Iteration 1 complete — all backend tests passing, endpoint works
- Node.js 22+ and npm 10+ installed

---

## Phase 1: Project Setup

### Task 1.1: Scaffold Vite + React + TypeScript project
**Directory:** `frontend/chat-ui/`

```bash
cd frontend
npm create vite@latest chat-ui -- --template react-swc-ts
cd chat-ui
npm install
```

Using `react-swc-ts` template (SWC compiler for faster builds).

### Task 1.2: Install testing dependencies
```bash
npm install -D vitest @testing-library/react @testing-library/jest-dom @testing-library/user-event jsdom
```

Configure Vitest in `vite.config.ts`:
```typescript
/// <reference types="vitest" />
export default defineConfig({
  // ... existing config
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
  },
});
```

Create `src/test/setup.ts`:
```typescript
import '@testing-library/jest-dom';
```

### Task 1.3: Configure API proxy in `vite.config.ts`
```typescript
server: {
  proxy: {
    '/api': {
      target: 'http://localhost:5000',
      changeOrigin: true,
    },
  },
},
```
This avoids CORS issues during development — all `/api` calls proxy to the backend.

---

## Phase 2: TypeScript Types

### Task 2.1: Define shared types
**File:** `frontend/chat-ui/src/types/chat.ts`

```typescript
export interface ChatMessage {
  id: string;
  message: string;
  role: 'user' | 'assistant';
  timestamp: string;
}

export interface ChatRequest {
  message: string;
}

export interface ChatResponse {
  id: string;
  message: string;
  role: string;
  timestamp: string;
}
```

---

## Phase 3: API Client (TDD — vertical slices)

**File:** `frontend/chat-ui/src/services/chatApi.ts`
**Test file:** `frontend/chat-ui/src/services/__tests__/chatApi.test.ts`

Use `vi.fn()` to mock `fetch`. One test → minimal impl → next test.

### Cycle 3.1 — POST body
- RED: test `sendMessage sends POST request with correct body`
- GREEN: implement `sendMessage` — bare `fetch` POST with JSON body, no response handling yet

### Cycle 3.2 — Success response
- RED: test `sendMessage returns parsed ChatResponse on success`
- GREEN: add `response.json()` and return typed result

### Cycle 3.3 — HTTP error
- RED: test `sendMessage throws error on non-ok response`
- GREEN: add `if (!response.ok) throw new Error(...)`

### Cycle 3.4 — Network failure
- RED: test `sendMessage throws error on network failure`
- GREEN: network errors propagate naturally — confirm test passes as-is or add try/catch if needed

### Refactor
Review `sendMessage` — extract `API_BASE` constant, verify types align with `ChatResponse`.

---

## Phase 4: Components (TDD — vertical slices)

Each component: write ONE failing test → write minimal code to pass → next test → repeat.
Only write the next test after the current one is GREEN.

### Task 4.1: MessageBubble component
**File:** `frontend/chat-ui/src/components/MessageBubble.tsx`
**Test:** `frontend/chat-ui/src/components/__tests__/MessageBubble.test.tsx`

Props: `{ message: ChatMessage }`

- Cycle 1: RED test "renders message content" → GREEN render `message.message` text
- Cycle 2: RED test "applies user styling for user role" → GREEN add role-based className
- Cycle 3: RED test "applies assistant styling for assistant role" → GREEN same className logic covers it
- Refactor

### Task 4.2: MessageList component
**File:** `frontend/chat-ui/src/components/MessageList.tsx`
**Test:** `frontend/chat-ui/src/components/__tests__/MessageList.test.tsx`

Props: `{ messages: ChatMessage[] }`

- Cycle 1: RED test "renders empty state when no messages" → GREEN render empty container
- Cycle 2: RED test "renders all messages in order" → GREEN map messages to MessageBubble
- Cycle 3: RED test "scrolls to bottom when new messages added" → GREEN add `useEffect` + `scrollIntoView`
- Refactor

### Task 4.3: ChatInput component
**File:** `frontend/chat-ui/src/components/ChatInput.tsx`
**Test:** `frontend/chat-ui/src/components/__tests__/ChatInput.test.tsx`

Props: `{ onSend: (message: string) => void; isLoading: boolean }`

- Cycle 1: RED test "renders textarea and send button" → GREEN render basic markup
- Cycle 2: RED test "calls onSend with message content when button clicked" → GREEN add `onClick` handler
- Cycle 3: RED test "calls onSend when Enter pressed (without Shift)" → GREEN add `onKeyDown` handler
- Cycle 4: RED test "clears input after sending" → GREEN reset state after calling `onSend`
- Cycle 5: RED test "disables send button when input is empty" → GREEN `disabled={!value.trim()}`
- Cycle 6: RED test "disables input and button when isLoading is true" → GREEN `disabled={isLoading}`
- Refactor

### Task 4.4: ChatWindow component (main container)
**File:** `frontend/chat-ui/src/components/ChatWindow.tsx`
**Test:** `frontend/chat-ui/src/components/__tests__/ChatWindow.test.tsx`

State: `messages: ChatMessage[]`, `isLoading: boolean`, `error: string | null`

- Cycle 1: RED test "renders ChatInput and MessageList" → GREEN compose the two components
- Cycle 2: RED test "adds user message to list on send" → GREEN append user message to state
- Cycle 3: RED test "calls API and adds assistant response to list" → GREEN call `sendMessage`, append response
- Cycle 4: RED test "shows loading state while waiting for API response" → GREEN set `isLoading` around API call
- Cycle 5: RED test "shows error message on API failure" → GREEN catch error, set `error` state
- Refactor

---

## Phase 5: Styling

### Task 5.1: Chat UI styles
Minimal CSS that creates a recognizable chat interface:
- Full-height layout with message area + input bar at bottom
- User messages aligned right (blue/dark background)
- Assistant messages aligned left (gray/light background)
- Message bubbles with rounded corners, padding, timestamps
- Scrollable message area
- Fixed input bar at bottom with textarea + send button
- Loading indicator (three dots or spinner)

Use CSS Modules or plain CSS — keep it simple, no UI framework needed.

### Task 5.2: Replace default App.tsx
Remove all Vite boilerplate. App.tsx renders only `<ChatWindow />`.

---

## Acceptance criteria
1. `npm run build` — zero TypeScript errors
2. `npm test` — all component and service tests pass
3. User can type a message, press Enter or click Send
4. User message appears immediately in chat (right-aligned, styled)
5. Echo response from backend appears after API call (left-aligned, styled)
6. Loading state shown while waiting for response
7. Error displayed if API call fails
8. Chat area scrolls to newest message
9. Input clears after sending
10. Empty messages cannot be sent

## Verification commands
```bash
cd frontend/chat-ui

# Install dependencies
npm install

# Run tests
npm test

# Run tests in watch mode
npm test -- --watch

# Build for production
npm run build

# Start dev server
npm run dev
# Open http://localhost:5173 — verify chat works with backend running
```

## What you will learn
- Vite + React + TypeScript project setup
- Component-driven development with TDD
- Vitest + React Testing Library for component tests
- API integration with fetch and error handling
- State management with React hooks (useState, useEffect)
- CSS layout for a chat interface
