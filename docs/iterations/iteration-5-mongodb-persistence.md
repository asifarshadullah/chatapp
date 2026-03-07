# Iteration 5: MongoDB Persistence

## Goal
Replace in-memory conversation storage with MongoDB so conversations persist across server restarts.
Uses Docker Compose for local MongoDB and the Repository pattern for a clean swap.

## Context
Iteration 4 introduced `IChatRepository` with `InMemoryChatRepository`. This iteration adds
`MongoChatRepository` implementing the same interface — the Application layer doesn't change at all.
This demonstrates the power of Clean Architecture and the Dependency Inversion Principle.

## Prerequisites
- Iterations 1–4 complete — conversation history working with in-memory storage
- Docker Desktop installed and running
- Basic familiarity with Docker Compose

---

## Phase 1: Docker Compose Setup

### Task 1.1: Create `docker-compose.yml` at repo root
**File:** `docker-compose.yml`

```yaml
services:
  mongodb:
    image: mongo:7
    container_name: chatapp-mongodb
    ports:
      - "27017:27017"
    environment:
      MONGO_INITDB_ROOT_USERNAME: chatapp
      MONGO_INITDB_ROOT_PASSWORD: chatapp_dev
      MONGO_INITDB_DATABASE: chatapp
    volumes:
      - mongodb_data:/data/db

  mongo-express:
    image: mongo-express:1
    container_name: chatapp-mongo-ui
    ports:
      - "8081:8081"
    environment:
      ME_CONFIG_MONGODB_ADMINUSERNAME: chatapp
      ME_CONFIG_MONGODB_ADMINPASSWORD: chatapp_dev
      ME_CONFIG_MONGODB_URL: mongodb://chatapp:chatapp_dev@mongodb:27017/
    depends_on:
      - mongodb

volumes:
  mongodb_data:
```

Includes Mongo Express for visual database inspection at `http://localhost:8081`.

### Task 1.2: Add connection string to appsettings
**File:** `backend/src/Chat.Api/appsettings.Development.json`
```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://chatapp:chatapp_dev@localhost:27017",
    "DatabaseName": "chatapp"
  }
}
```

### Task 1.3: Create MongoDB settings class
**File:** `backend/src/Chat.Infrastructure/Configuration/MongoDbSettings.cs`
```csharp
public class MongoDbSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}
```

---

## Phase 2: MongoDB Repository (TDD)

### Task 2.1: Add MongoDB.Driver NuGet package
Add to `Chat.Infrastructure.csproj`:
```xml
<PackageReference Include="MongoDB.Driver" Version="2.*" />
```

### MongoDB repository cycles (TDD — vertical slices)
**File:** `backend/tests/Chat.Infrastructure.Tests/Repositories/MongoChatRepositoryTests.cs`

Tests run against real MongoDB (Docker Compose). Use `IClassFixture` with a unique db name per run (`chatapp_test_{Guid}`); drop it in `Dispose()`.

One test → minimal impl → next test. Never write the full test list before implementing.

- Cycle 2.2: RED `CreateConversationAsync_StoresConversationInDatabase` → GREEN scaffold `MongoChatRepository` + document models + insert
- Cycle 2.3: RED `GetConversationAsync_WithValidId_ReturnsStoredConversation` → GREEN find by id, map to domain
- Cycle 2.4: RED `GetConversationAsync_WithInvalidId_ReturnsNull` → GREEN return null on miss
- Cycle 2.5: RED `AddMessageAsync_PersistsMessageToDatabase` → GREEN push message to document
- Cycle 2.6: RED `GetMessagesAsync_ReturnsAllStoredMessages` → GREEN return mapped messages
- Cycle 2.7: RED `GetMessagesAsync_ReturnsMessagesInChronologicalOrder` → GREEN sort by timestamp
- Bonus verification: `Conversation_PersistsAcrossRepositoryInstances` — no new impl needed; passes once the above cycles are complete (proves real DB, not just in-memory state)
- Refactor

---

## Phase 3: Swap DI Registration

### Cycle 3.1: RED `PostMessage_WithMongoDI_PersistsConversationAcrossRequests` → GREEN swap DI
**File:** `backend/tests/Chat.Api.Tests/Controllers/ChatControllerTests.cs`

Add one integration test that uses a `WebApplicationFactory` wired to real MongoDB (separate from the in-memory factory used by other tests). The test sends two requests with the same `conversationId` and asserts both messages appear in history. This test is RED until the DI swap below is made.

### Task 3.2: Update Program.cs dependency registration
**File:** `backend/src/Chat.Api/Program.cs`

```csharp
// MongoDB setup
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDB"));

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddScoped<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var settings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return client.GetDatabase(settings.DatabaseName);
});

// Swap: InMemoryChatRepository → MongoChatRepository
builder.Services.AddScoped<IChatRepository, MongoChatRepository>();
```

### Task 3.3: Keep InMemoryChatRepository for testing
Do NOT delete `InMemoryChatRepository`. It's still useful for:
- Integration tests (fast, no Docker dependency)
- Fallback if MongoDB is unavailable

Update `Chat.Api.Tests` to use `InMemoryChatRepository` in the test `WebApplicationFactory`
so integration tests don't require Docker.

---

## Phase 4: Verify Everything Still Works

### Task 4.1: Run all backend tests
```bash
dotnet test backend/ChatApp.sln --verbosity normal
```
Existing tests should pass without changes (they use InMemory via WebApplicationFactory).

### Task 4.2: Run MongoDB-specific integration tests
Start Docker Compose first, then run Infrastructure tests:
```bash
docker compose up -d
dotnet test backend/tests/Chat.Infrastructure.Tests --verbosity normal
```

### Task 4.3: Manual end-to-end verification
1. Start Docker Compose: `docker compose up -d`
2. Start backend: `cd backend/src/Chat.Api && dotnet run`
3. Start frontend: `cd frontend/chat-ui && npm run dev`
4. Send messages in the UI
5. Restart the backend (`Ctrl+C`, then `dotnet run` again)
6. Verify: previous conversations are still accessible via the history endpoint

### Task 4.4: Run E2E tests
```bash
cd e2e/playwright && npx playwright test
```
E2E tests should pass (they don't care about storage backend).

---

## Acceptance criteria
1. `docker compose up -d` starts MongoDB and Mongo Express successfully
2. All existing tests pass (unit, integration, E2E) — zero regressions
3. MongoDB-specific repository tests pass against real MongoDB
4. Conversations persist across backend restarts
5. Mongo Express at `http://localhost:8081` shows stored conversations
6. `IChatRepository` interface unchanged — only the DI registration switched
7. Integration tests still use InMemoryChatRepository (no Docker dependency for CI)

## Verification commands
```bash
# Start infrastructure
docker compose up -d

# Verify MongoDB is running
docker compose ps

# Run all backend tests
dotnet test backend/ChatApp.sln --verbosity normal

# Run only MongoDB tests
dotnet test backend/tests/Chat.Infrastructure.Tests --verbosity normal

# Run E2E tests (start backend + frontend first or use webServer config)
cd e2e/playwright && npx playwright test

# Check data in MongoDB
# Open http://localhost:8081 (Mongo Express)

# Stop infrastructure
docker compose down
# To also remove data volumes:
docker compose down -v
```

## What you will learn
- Docker Compose for local development infrastructure
- MongoDB.Driver for .NET — CRUD operations with BSON documents
- Repository pattern payoff — swapping implementations without changing business logic
- Dependency Inversion Principle in practice
- Document-to-entity mapping (keeping infrastructure concerns separate from domain)
- Test isolation — different repositories for different test contexts
