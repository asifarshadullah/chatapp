# ChatApp Progress Tracker

## Current iteration: 1 — Backend Skeleton + Echo Endpoint

### Phase 1: Scaffolding ✅
- [x] Create `backend/Directory.Build.props` (shared project settings)
- [x] Create `backend/ChatApp.sln` with 7 projects (4 src + 3 test)
- [x] Set up project references (Clean Architecture dependency flow)
- [x] Add `public partial class Program` in Chat.Api for integration tests
- [x] Verify: `dotnet build backend/ChatApp.sln` passes with zero warnings

### Phase 2: Echo Endpoint (TDD) ✅
- [x] RED: Write integration tests — POST /api/chat (4 tests)
- [x] RED: Write unit tests — ChatService.SendMessageAsync (4 tests)
- [x] RED: Write domain tests — ChatMessage entity validation (8 tests)
- [x] GREEN: Implement ChatMessage domain entity + MessageRole enum
- [x] GREEN: Implement Application layer (DTOs, IChatService, ChatService)
- [x] GREEN: Implement ChatController
- [x] Wire up DI in Program.cs + CORS for localhost:5173
- [x] REFACTOR: Review and clean up
- [x] Verify: 16 tests passing, zero warnings, zero errors
- [x] Commit and tag: `git tag iteration-1` (commit 73145d1)

---

## Completed iterations
_(none yet)_

---

## Upcoming iterations
- **Iteration 2:** React UI with Vite + TypeScript + component tests
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
