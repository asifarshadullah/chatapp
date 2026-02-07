# Iteration 1: Backend Skeleton + Echo Endpoint

## Goal
Create the .NET 8 solution with Clean Architecture project structure and a working
POST `/api/chat` echo endpoint, fully tested with TDD.

## Context
This is the foundation. Every subsequent iteration builds on this skeleton.
The echo endpoint accepts a chat message and returns it prefixed with "Echo: " — no AI integration.

---

## Phase 1: Scaffolding (infrastructure setup — not TDD)

### Task 1.1: Create `backend/Directory.Build.props`
Shared MSBuild properties for all 7 projects:
- `<TargetFramework>net8.0</TargetFramework>`
- `<Nullable>enable</Nullable>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (src projects only)
- Conditional `<ItemGroup>` for test projects (`*.Tests`): add xUnit, FluentAssertions, Microsoft.NET.Test.Sdk, coverlet.collector

### Task 1.2: Create solution and projects

```
backend/
├── ChatApp.sln
├── Directory.Build.props
├── src/
│   ├── Chat.Api/              (Microsoft.NET.Sdk.Web — ASP.NET Core Web API)
│   ├── Chat.Application/      (Microsoft.NET.Sdk — class library)
│   ├── Chat.Domain/           (Microsoft.NET.Sdk — class library)
│   └── Chat.Infrastructure/   (Microsoft.NET.Sdk — class library)
└── tests/
    ├── Chat.Api.Tests/        (xUnit test project)
    ├── Chat.Application.Tests/ (xUnit test project)
    └── Chat.Infrastructure.Tests/ (xUnit test project)
```

### Task 1.3: Set up project references (enforce Clean Architecture)

**Source projects:**
- `Chat.Api` → references `Chat.Application`, `Chat.Infrastructure`
- `Chat.Application` → references `Chat.Domain`
- `Chat.Infrastructure` → references `Chat.Application`, `Chat.Domain`
- `Chat.Domain` → references **nothing** (zero dependencies)

**Test projects:**
- `Chat.Api.Tests` → references `Chat.Api`
- `Chat.Application.Tests` → references `Chat.Application`
- `Chat.Infrastructure.Tests` → references `Chat.Infrastructure`

### Task 1.4: Enable test access to Program.cs
In `Chat.Api/Program.cs`, add at the bottom:
```csharp
// Make Program accessible to integration tests
public partial class Program { }
```

Or add `<InternalsVisibleTo Include="Chat.Api.Tests" />` to Chat.Api.csproj.

### Task 1.5: Add Swagger/OpenAPI
Add `Swashbuckle.AspNetCore` to Chat.Api for API documentation during development.

### Task 1.6: Verify scaffolding
Run: `dotnet build backend/ChatApp.sln`
**Expected:** Clean build, zero warnings, zero errors.

---

## Phase 2: Echo Endpoint (TDD — Red/Green/Refactor)

### Task 2.1: RED — Write integration test for POST /api/chat
**File:** `backend/tests/Chat.Api.Tests/Controllers/ChatControllerTests.cs`

```
Test: PostMessage_WithValidContent_ReturnsEchoResponse
- Use WebApplicationFactory<Program>
- POST to /api/chat with JSON: { "message": "Hello" }
- Assert: 200 OK
- Assert: response body is { "id": "...", "message": "Echo: Hello", "role": "assistant", "timestamp": "..." }

Test: PostMessage_WithEmptyContent_ReturnsBadRequest
- POST with { "message": "" }
- Assert: 400 Bad Request

Test: PostMessage_WithNullContent_ReturnsBadRequest
- POST with { "message": null }
- Assert: 400 Bad Request

Test: PostMessage_WithContentOver5000Chars_ReturnsBadRequest
- POST with message exceeding 5000 characters
- Assert: 400 Bad Request
```

Run tests → **EXPECT FAILURE** (controller doesn't exist yet).

### Task 2.2: RED — Write unit tests for ChatService
**File:** `backend/tests/Chat.Application.Tests/Services/ChatServiceTests.cs`

```
Test: SendMessageAsync_WithValidContent_ReturnsEchoResponse
- Input: "Hello"
- Assert: response message is "Echo: Hello"
- Assert: response role is "assistant"
- Assert: response has valid ID and timestamp

Test: SendMessageAsync_WithNullContent_ThrowsArgumentException
Test: SendMessageAsync_WithEmptyContent_ThrowsArgumentException
```

Run tests → **EXPECT FAILURE**.

### Task 2.3: RED — Write domain entity tests
**File:** `backend/tests/Chat.Application.Tests/Domain/ChatMessageTests.cs`

```
Test: Create_WithValidContent_Succeeds
- Assert: Id is not empty Guid
- Assert: Content matches input
- Assert: Timestamp is recent
- Assert: Role matches input

Test: Create_WithNullContent_ThrowsArgumentException
Test: Create_WithEmptyContent_ThrowsArgumentException
Test: Create_WithContentOver5000Chars_ThrowsArgumentException
```

Run tests → **EXPECT FAILURE**.

### Task 2.4: GREEN — Implement ChatMessage domain entity
**File:** `backend/src/Chat.Domain/Entities/ChatMessage.cs`

Properties:
- `Id` — `Guid`, auto-generated
- `Content` — `string`, required, max 5000 chars
- `Role` — `MessageRole` enum (User, Assistant)
- `Timestamp` — `DateTime`, set to UtcNow on creation

Validation in constructor:
- Throw `ArgumentException` for null, empty, or whitespace content
- Throw `ArgumentException` for content exceeding 5000 characters

**File:** `backend/src/Chat.Domain/Enums/MessageRole.cs`
```csharp
public enum MessageRole { User, Assistant }
```

Run tests → domain tests should **PASS**.

### Task 2.5: GREEN — Implement Application layer

**DTOs:**
- `backend/src/Chat.Application/DTOs/ChatRequestDto.cs`
  ```csharp
  public record ChatRequestDto(string Message);
  ```
- `backend/src/Chat.Application/DTOs/ChatResponseDto.cs`
  ```csharp
  public record ChatResponseDto(Guid Id, string Message, string Role, DateTime Timestamp);
  ```

**Interface:**
- `backend/src/Chat.Application/Interfaces/IChatService.cs`
  ```csharp
  Task<ChatResponseDto> SendMessageAsync(string content, CancellationToken cancellationToken = default);
  ```

**Implementation:**
- `backend/src/Chat.Application/Services/ChatService.cs`
  - Creates a `ChatMessage` with Role = Assistant and Content = `$"Echo: {content}"`
  - Returns `ChatResponseDto` mapped from the domain entity

Run tests → application tests should **PASS**.

### Task 2.6: GREEN — Implement ChatController
**File:** `backend/src/Chat.Api/Controllers/ChatController.cs`

```csharp
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ChatResponseDto>> SendMessage(
        [FromBody] ChatRequestDto request,
        CancellationToken cancellationToken)
    {
        // Validate, call IChatService, return Ok(response)
    }
}
```

Validation:
- Return `BadRequest` for null/empty/whitespace message
- Return `BadRequest` for message over 5000 characters

Run tests → integration tests should **PASS**.

### Task 2.7: Wire up DI + CORS in Program.cs
**File:** `backend/src/Chat.Api/Program.cs`

Register services:
```csharp
builder.Services.AddScoped<IChatService, ChatService>();
```

Configure CORS (for Iteration 2 frontend):
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
// ...
app.UseCors("AllowFrontend");
```

Add Swagger:
```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// ...
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

### Task 2.8: REFACTOR — Review and clean up
- Ensure all tests pass: `dotnet test backend/ChatApp.sln`
- Ensure build is clean: `dotnet build backend/ChatApp.sln`
- Remove any unused `using` statements
- Verify XML comments on all public methods
- Check that no domain entities leak into API responses

---

## Acceptance criteria
1. `dotnet build backend/ChatApp.sln` — zero errors, zero warnings
2. `dotnet test backend/ChatApp.sln` — ALL tests pass (expect ~10+ tests)
3. POST `http://localhost:5xxx/api/chat` with `{"message":"Hello"}` returns `{"id":"...","message":"Echo: Hello","role":"assistant","timestamp":"..."}`
4. POST with empty/null/too-long message returns 400
5. Swagger UI accessible at `/swagger`
6. Solution follows Clean Architecture — Domain has zero external references
7. No domain entities exposed in API responses (only DTOs)

## Verification commands
```bash
# Build
dotnet build backend/ChatApp.sln

# Run all tests
dotnet test backend/ChatApp.sln --verbosity normal

# Start the API
cd backend/src/Chat.Api && dotnet run

# Smoke test (in a separate terminal)
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d "{\"message\":\"Hello, World!\"}"

# Expected response:
# {"id":"<guid>","message":"Echo: Hello, World!","role":"assistant","timestamp":"<datetime>"}
```

## What you will learn
- Clean Architecture project structure and the dependency rule
- `Directory.Build.props` for shared MSBuild configuration
- `WebApplicationFactory<Program>` for integration testing
- TDD Red-Green-Refactor cycle in practice
- .NET dependency injection and service registration
- DTOs vs domain entities — why they're separate
