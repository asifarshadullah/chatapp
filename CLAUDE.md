# ChatApp — ChatGPT-style Chat Application

## Project overview
A ChatGPT-like chat interface where users submit prompts and receive echo responses.
Built with Clean Architecture to learn patterns, TDD, and full-stack development.

## Tech stack
- **Backend:** .NET 8, ASP.NET Core Web API (Controllers), xUnit, FluentAssertions
- **Frontend:** React 19, TypeScript, Vite (added in Iteration 2)
- **E2E:** Playwright (added in Iteration 3)
- **Database:** MongoDB via Docker Compose (added in Iteration 5)
- **Real-time:** SignalR (added in Iteration 6)

## Architecture — Clean Architecture

```
Chat.Domain         → Entities, value objects. ZERO external dependencies.
Chat.Application    → Services, DTOs, interfaces. Depends only on Domain.
Chat.Infrastructure → Implementations (repos, external). Depends on Application + Domain.
Chat.Api            → Controllers, DI root, middleware. References Application + Infrastructure.
```

**Dependency rule:** Domain ← Application ← Infrastructure. Api references Application + Infrastructure for DI only.

## Coding standards
- Controllers are thin — delegate all logic to Application layer services
- All public async methods accept `CancellationToken`
- Use DTOs in API responses; never expose domain entities directly
- File-scoped namespaces (`namespace X;`)
- Nullable reference types enabled everywhere
- XML comments on all public methods
- No static mutable state — use dependency injection

## Testing standards
- **TDD — vertical slices only:** One test → one implementation → repeat. Never write all tests first then all code (horizontal slicing produces tests against imagined behavior).
- **Framework:** xUnit + FluentAssertions
- **Pattern:** Arrange-Act-Assert
- **Naming:** `MethodName_Scenario_ExpectedResult`
- **Integration tests:** Use `WebApplicationFactory<Program>`
- Tests verify behavior through public interfaces only — never implementation details, private methods, or internal collaborators
- Run ALL tests after every change — never leave tests broken

## Verification commands
```bash
dotnet build backend/ChatApp.sln                    # Build backend
dotnet test backend/ChatApp.sln                     # Run all backend tests
dotnet test backend/ChatApp.sln --verbosity normal  # Verbose test output
cd frontend/chat-ui && npm run dev                  # Frontend dev server
cd frontend/chat-ui && npm run build                # Frontend production build
cd frontend/chat-ui && npm test                     # Frontend component tests
cd e2e/playwright && npx playwright test            # E2E tests
```

## Workflow rules
1. Read the **current iteration brief** from `docs/iterations/` before starting work
2. Check `PROGRESS.md` for current status — pick up where we left off
3. Follow TDD strictly: RED (failing test) → GREEN (implement) → REFACTOR
4. Make small, focused commits — one logical change per commit
5. Run `dotnet test backend/ChatApp.sln` after every backend change
6. Update `PROGRESS.md` when completing tasks
7. Do NOT add libraries without explaining why
8. Do NOT skip ahead to future iterations

## Plan mode
- Plans must be extremely concise — sacrifice grammar for concision
- End every plan with a list of unresolved questions (if any)

## Current iteration
**Iteration 1: Backend Skeleton + Echo Endpoint**
Brief: `docs/iterations/iteration-1-backend-skeleton.md`
