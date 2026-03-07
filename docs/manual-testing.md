# Manual Testing & Debugging Reference

## Starting all services

### Step 1 — Docker (MongoDB)

```bash
# From project root
docker compose up -d
```

Check containers are healthy:

```bash
docker compose ps
```

Expected output:
```
NAME                  STATUS
chatapp-mongodb       running
chatapp-mongo-ui      running
```

Stop containers when done:

```bash
docker compose down
```

---

### Step 2 — Backend API

```bash
cd backend/src/Chat.Api
dotnet run
```

API base URL: `http://localhost:5064`

---

### Step 3 — Frontend

```bash
cd frontend/chat-ui
npm run dev
```

App URL: `http://localhost:5173`

---

## Testing the API manually (curl)

### Start a new conversation

```bash
curl -X POST http://localhost:5064/api/conversations \
  -H "Content-Type: application/json"
```

Response:
```json
{ "id": "<conversationId>", "createdAt": "..." }
```

### Send a message

```bash
curl -X POST http://localhost:5064/api/conversations/<conversationId>/messages \
  -H "Content-Type: application/json" \
  -d '{"content": "Hello world"}'
```

Response:
```json
{ "id": "...", "content": "Echo: Hello world", "role": "Assistant", "timestamp": "..." }
```

### Get conversation history

```bash
curl http://localhost:5064/api/conversations/<conversationId>/messages
```

---

## Viewing MongoDB data

### Option A — Mongo Express (browser UI)

URL: `http://localhost:8081`

Navigate: **chatapp** database → **conversations** collection

This shows all stored conversations and their embedded messages. Best for quick inspection during debugging.

---

### Option B — mongosh (CLI)

Connect to the Docker MongoDB (not the local one on port 27017):

```bash
mongosh "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256"
```

Useful queries once connected:

```js
// Switch to app database
use chatapp

// List all conversations
db.conversations.find().pretty()

// Count conversations
db.conversations.countDocuments()

// Find a specific conversation by ID
db.conversations.findOne({ _id: "<conversationId>" })

// View all messages in all conversations
db.conversations.find({}, { messages: 1 }).pretty()

// Clear all data (useful between manual test runs)
db.conversations.deleteMany({})
```

---

### Option C — VS Code MongoDB extension

Install **MongoDB for VS Code** extension, then connect with:

```
mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256
```

Provides an explorer sidebar with clickable collections and a document viewer.

---

## Running automated tests

```bash
# All backend tests (Application + Infrastructure + API)
dotnet test backend/ChatApp.sln --verbosity normal

# Only MongoDB integration tests (requires Docker running)
dotnet test backend/ChatApp.sln --filter "FullyQualifiedName~MongoChatRepositoryTests"

# Only API integration tests (no Docker needed)
dotnet test backend/ChatApp.sln --filter "FullyQualifiedName~ChatControllerTests"

# Frontend unit tests
cd frontend/chat-ui && npm test

# E2E tests (requires backend + frontend both running)
cd e2e/playwright && npx playwright test
```

---

## Common debug scenarios

### Messages not persisting after restart

1. Check Docker is running: `docker compose ps`
2. Check API is connecting to Docker MongoDB (port 27018), not local (port 27017)
3. Query `db.conversations.find()` in mongosh — if empty, messages never reached DB
4. Check API logs for MongoDB connection errors on startup

### MongoDB auth failure

The connection string must include `authMechanism=SCRAM-SHA-256` — MongoDB 7 does not serve SCRAM-SHA-1 by default.

```
mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256
```

### Port conflict (27017 already in use)

The Docker container maps internal port 27017 to **host port 27018** to avoid conflicting with a local MongoDB installation. Always use port **27018** in connection strings.

### Mongo Express not loading

It depends on MongoDB being healthy first. Wait a few seconds after `docker compose up -d`, then refresh `http://localhost:8081`.
