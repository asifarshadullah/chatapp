# ChatApp Progress Tracker

## Current iteration: 2 — React UI with Vite + TypeScript ✅

### Phase 1: Project Setup ✅
- [x] Scaffold `frontend/chat-ui` with `react-swc-ts` template
- [x] Install Vitest + React Testing Library + jsdom
- [x] Configure Vitest in `vite.config.ts` (globals, jsdom, setupFiles)
- [x] Create `src/test/setup.ts` (jest-dom + scrollIntoView mock)
- [x] Configure API proxy → `http://localhost:5064`
- [x] Add `"test": "vitest"` script to package.json
- [x] Verify: `npm run build` passes with zero TypeScript errors

### Phase 2: TypeScript Types ✅
- [x] `src/types/chat.ts` — ChatMessage, ChatRequest, ChatResponse

### Phase 3: API Client TDD ✅ (vertical slices)
- [x] Cycle 3.1: RED test POST body → GREEN minimal sendMessage
- [x] Cycle 3.2: RED test success response → GREEN response.json()
- [x] Cycle 3.3: RED test HTTP error → GREEN if (!response.ok) throw
- [x] Cycle 3.4: RED test network failure → GREEN propagates naturally
- [x] Refactor: API_BASE const, types verified

### Phase 4: Components TDD ✅ (vertical slices)
- [x] MessageBubble — 3 cycles (renders content, user styling, assistant styling)
- [x] MessageList — 3 cycles (empty state, renders in order, scrolls to bottom)
- [x] ChatInput — 7 cycles (renders UI, send on click, send on Enter, no send on Shift+Enter, clears after send, disabled when empty, disabled when loading)
- [x] ChatWindow — 5 cycles (renders both, adds user msg, API + response, loading state, error)
- [x] Verify: 22 tests passing

### Phase 5: Styling ✅
- [x] `src/components/Chat.css` — full-height layout, bubbles, input bar, loading dots, error banner
- [x] Replace `App.tsx` boilerplate — renders only `<ChatWindow />`
- [x] Replace `index.css` with minimal reset
- [x] Verify: `npm run build` zero errors, `npm test` 22/22 passing

---

## Completed iterations
- **Iteration 1:** Backend skeleton + echo endpoint — 16 tests, tagged `iteration-1` (commit 73145d1)
- **Iteration 2:** React UI — 22 component/service tests, `npm run build` clean

---

## Upcoming iterations
- **Iteration 3:** Playwright E2E tests
- **Iteration 4:** In-memory conversation history
- **Iteration 5:** MongoDB persistence (Docker Compose)
- **Iteration 6:** SignalR streaming + UX polish

---

## Decisions log
| Date | Decision | Reason |
|------|----------|--------|
| 2025-02-07 | Target .NET 8 (SDK 8.0.417) | User preference; pinned via global.json |
| 2025-02-07 | Echo-only, no AI/LLM integration | Focus on architecture and patterns first |
| 2025-02-07 | Controllers, not Minimal API | User preference for learning controller patterns |
| 2025-02-07 | Vitest + RTL for frontend tests | Component tests in Iteration 2 alongside UI |
| 2026-02-27 | Import defineConfig from vitest/config | Fixes TS error when tsconfig.node.json restricts types to ["node"] |
| 2026-02-27 | Add vitest/globals to tsconfig.app.json types | Allows vi global to be used in test files without explicit import |
| 2026-02-27 | Mock scrollIntoView in setup.ts | jsdom doesn't implement scrollIntoView; needed for MessageList tests |
