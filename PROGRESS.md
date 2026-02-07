# ChatApp Progress Tracker

## Current iteration: 1 — Backend Skeleton + Echo Endpoint

### Phase 1: Scaffolding
- [ ] Create `backend/Directory.Build.props` (shared project settings)
- [ ] Create `backend/ChatApp.sln` with 7 projects (4 src + 3 test)
- [ ] Set up project references (Clean Architecture dependency flow)
- [ ] Add `InternalsVisibleTo` in Chat.Api for integration tests
- [ ] Verify: `dotnet build backend/ChatApp.sln` passes with zero warnings

### Phase 2: Echo Endpoint (TDD)
- [ ] RED: Write integration test — POST /api/chat returns echo response
- [ ] RED: Write unit test — ChatService.SendMessageAsync returns echo
- [ ] RED: Write domain test — ChatMessage entity validation
- [ ] GREEN: Implement ChatMessage domain entity
- [ ] GREEN: Implement ChatService in Application layer
- [ ] GREEN: Implement ChatController
- [ ] Wire up DI in Program.cs + CORS for localhost:5173
- [ ] REFACTOR: Review and clean up
- [ ] Verify: All tests pass, endpoint works via Swagger/curl
- [ ] Commit and tag: `git tag iteration-1`

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
