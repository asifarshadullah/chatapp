# Iteration 6: SignalR Streaming + UX Polish

## Goal
Replace the request/response pattern with real-time streaming using SignalR, and add UX polish:
loading indicators, error handling, typing animation, and overall visual improvements.

## Context
The app is fully functional with MongoDB persistence (Iteration 5). This final iteration adds
real-time communication so responses stream word-by-word (simulating how ChatGPT shows responses),
plus production-ready UX improvements.

## Prerequisites
- Iterations 1–5 complete — full stack working with MongoDB persistence
- All tests passing

---

## Phase 1: SignalR Backend Setup

### Task 1.1: Add SignalR to the backend
SignalR is included in `Microsoft.AspNetCore.App` shared framework — no extra NuGet package needed.

### Task 1.2: Register SignalR and map the hub in Program.cs
```csharp
builder.Services.AddSignalR();
// ...
app.MapHub<ChatHub>("/chatHub");
```

### Task 1.3: Keep REST endpoints
Do NOT remove the existing conversation/message endpoints. They remain for API testing,
debugging, and non-browser clients.

### ChatHub cycles (TDD — vertical slices)
**One test → minimal impl → next test. Never write the full test list before implementing.**

**File:** `backend/tests/Chat.Api.Tests/Hubs/ChatHubTests.cs`

Use `HubConnectionBuilder` in tests pointing to the test server's `/chatHub` endpoint.

- Cycle 1.4: RED `SendMessage_StreamsEchoResponseWordByWord` → GREEN scaffold `ChatHub` with `IAsyncEnumerable<string>`, split content into words, yield with delay
- Cycle 1.5: RED `SendMessage_StoresMessagesInConversation` → GREEN inject `IChatService`, store user + assistant messages
- Cycle 1.6: RED `SendMessage_WithNewConversation_ReturnsConversationId` → GREEN emit conversationId via client callback
- Cycle 1.7: RED `SendMessage_WithInvalidContent_SendsErrorMessage` → GREEN validate, send error to caller
- Refactor

---

## Phase 2: Frontend SignalR Integration

### Task 2.1: Install SignalR client
```bash
cd frontend/chat-ui
npm install @microsoft/signalr
```

### Frontend cycles (TDD — vertical slices)
**One test → minimal impl → next test. Never write the full test list before implementing.**

**File:** `frontend/chat-ui/src/components/ChatWindow.test.tsx`

Mock the SignalR connection (`vi.mock('../services/signalRService')`). Let the service file and
connection lifecycle emerge from what the tests require.

- Cycle 2.2: RED `renders streaming words as they arrive` → GREEN update ChatWindow to accept words via streaming callback and accumulate into assistant message
- Cycle 2.3: RED `shows typing indicator while streaming` → GREEN add `isStreaming` state, render indicator when true
- Cycle 2.4: RED `hides typing indicator when streaming completes` → GREEN clear `isStreaming` on stream end callback
- Cycle 2.5: RED `shows error message when connection fails` → GREEN handle error callback, display error banner
- Refactor

---

## Phase 3: UX Polish

> UX polish — not test-driven. Verify manually during development and via E2E in Phase 4.

### Task 3.1: Typing indicator
Show animated "..." or pulsing dots while the assistant is "typing" (streaming).
Hide when streaming completes.

### Task 3.2: Loading and connection states
- **Connecting:** Show "Connecting to chat..." on initial load
- **Connected:** Normal chat interface
- **Reconnecting:** Show subtle banner "Reconnecting..."
- **Disconnected:** Show error with retry button

### Task 3.3: Error handling
- Network errors → show inline error message with "Retry" button
- Invalid message → show validation feedback before sending
- Server errors → show user-friendly error, log details to console

### Task 3.4: Visual improvements
- Smooth scroll to bottom on new messages
- Auto-resize textarea as user types (up to max height)
- Timestamps on messages (relative: "2 min ago", or absolute)
- Empty state: "Send a message to start chatting"
- Subtle animations: message fade-in, typing indicator pulse

### Task 3.5: Keyboard shortcuts
- `Enter` to send message
- `Shift+Enter` for newline
- Focus input automatically on page load

---

## Phase 4: E2E Verification

### Cycle 4.1: RED before Phase 3 work
Write the streaming E2E test first — it will fail until Phase 3 makes it pass.

**File:** `e2e/playwright/tests/chat.spec.ts`

```
Test: "response appears word by word (streaming)"
1. Send a message
2. Assert: typing indicator appears
3. Assert: response text grows incrementally
4. Assert: typing indicator disappears when complete
5. Assert: full echo response is visible
```

### Task 4.2: Update existing E2E tests
Existing tests may need timeout adjustments since streaming adds delays.

### Task 4.3: Run full test suite
All tests — backend unit, backend integration, frontend component, and E2E — must pass.

---

## Acceptance criteria
1. Messages stream word-by-word (visible typing effect)
2. Typing indicator shows while response is streaming
3. Connection state is visible (connecting/connected/reconnecting)
4. Errors show user-friendly messages with retry options
5. REST endpoints still work alongside SignalR
6. All backend tests pass
7. All frontend component tests pass
8. All E2E tests pass
9. Chat feels responsive and polished

## Verification commands
```bash
# Backend tests
dotnet test backend/ChatApp.sln --verbosity normal

# Frontend tests
cd frontend/chat-ui && npm test

# E2E tests
cd e2e/playwright && npx playwright test

# Manual verification:
# 1. Start Docker: docker compose up -d
# 2. Start backend: cd backend/src/Chat.Api && dotnet run
# 3. Start frontend: cd frontend/chat-ui && npm run dev
# 4. Open http://localhost:5173
# 5. Send a message — watch it stream word by word
# 6. Check connection indicator
# 7. Kill backend — see reconnection UI
# 8. Restart backend — see auto-reconnect
```

## What you will learn
- SignalR real-time communication with server-streaming (`IAsyncEnumerable`)
- SignalR client in React — connection lifecycle management
- `withAutomaticReconnect` for resilient connections
- UX patterns for real-time applications (connection states, streaming indicators)
- Progressive enhancement — keeping REST alongside real-time
- User experience polish that makes a prototype feel professional
