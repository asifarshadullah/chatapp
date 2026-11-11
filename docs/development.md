# Development

How to run ChatApp on your machine, run its tests, and turn on the optional parts: a hosted
model, Google sign-in and Stripe billing. Deploying is covered in [deployment.md](deployment.md).

## Prerequisites

- [Docker](https://www.docker.com/) (for MongoDB)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- [Ollama](https://ollama.com/) with a pulled model (default: `gemma3:4b`, about 3 GB). For a lighter setup, pull `gemma3:1b` (about 0.8 GB) and change both `gemma3:4b` entries in `backend/src/Chat.Api/appsettings.Development.json` to it

## Run it (chat works, no OAuth or billing setup needed)

```bash
# 1. Start MongoDB
docker compose up -d

# 2. Pull the LLM model
ollama pull gemma3:4b

# 3. Start the backend (terminal 1)
cd backend
dotnet run --project src/Chat.Api

# 4. Start the frontend (terminal 2)
cd frontend/chat-ui
npm install
npm run dev
```

Open http://localhost:5173. Register with email + password, then chat. The local model is the default; the gear button opens Settings, where the Model dropdown shows what is offered.

## Use Groq locally

Keep the Groq key in the macOS Keychain and start the backend through `scripts/with-groq.sh`, which hands the key to that one command only. Groq then appears in the dropdown, while the local model stays the default:

```bash
# once: store the key (you are prompted for it; nothing is echoed or saved in your shell history)
security add-generic-password -a "$USER" -s chatapp-groq-key -w

# each time: run the backend, or the production image, with the key
scripts/with-groq.sh dotnet run --project backend/src/Chat.Api
scripts/with-groq.sh docker compose --profile app up --build
```

The first time, macOS may ask whether `security` may read the item; choose Always Allow. `dotnet user-secrets set "Ai:Models:groq:ApiKey" …` works too, but keeps the key in a plain JSON file in your home folder.

## Run the production image locally

The image that is deployed also runs on your machine, against the local MongoDB and your Ollama:

```bash
ollama pull gemma3:4b
docker compose --profile app up --build
```

Open http://localhost:8080. It offers the local model, and Groq too when compose runs through `scripts/with-groq.sh`. Use Chrome or Firefox: the app runs in Production mode, so its refresh cookie is `Secure`, and Safari may not keep a `Secure` cookie on plain `http://localhost`. On Linux, start Ollama with `OLLAMA_HOST=0.0.0.0` so the container can reach it.

## Optional: Google sign-in

1. Create OAuth credentials at [Google Cloud Console](https://console.cloud.google.com/apis/credentials)
2. Authorized redirect URI: `http://localhost:5064/auth/callback/google`
3. Set values in `backend/src/Chat.Api/appsettings.Development.json`:
   ```json
   "Google": {
     "ClientId": "your-client-id.apps.googleusercontent.com",
     "ClientSecret": "your-client-secret"
   }
   ```
4. Restart the backend. A "Sign in with Google" button appears on the login page.

## Optional: Stripe billing

1. Get test-mode keys from [Stripe Dashboard](https://dashboard.stripe.com/test/apikeys)
2. Create a test-mode product, Pro, with a monthly price of USD 19, and copy the price ID
3. Run the Stripe CLI to forward webhooks during local dev. It prints the webhook signing secret (`whsec_...`):
   ```bash
   stripe listen --events checkout.session.completed,customer.subscription.created,customer.subscription.updated,customer.subscription.deleted,invoice.paid,invoice.payment_succeeded,invoice.payment_failed --forward-to localhost:5064/webhooks/stripe
   ```
4. Keep the three values in user secrets, out of the repository:
   ```bash
   cd backend/src/Chat.Api
   dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
   dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."
   dotnet user-secrets set "Stripe:PriceIds:Pro" "price_..."
   ```
5. Restart the backend. The "Manage Plan" button in the chat header opens the billing page, which now says it is in test mode.

Development allows 1000 messages a day on both plans, so the end-to-end tests never run into the limit. To see the real limits locally, start the backend with `Billing__Plans__Free__DailyMessages=10 Billing__Plans__Pro__DailyMessages=100`. The app refuses a live key (`sk_live_...`) unless `Stripe:AllowLiveKey` is `true`.

## Run the tests

```bash
# Backend (xUnit + FluentAssertions)
dotnet test backend/ChatApp.sln

# Frontend (Vitest + React Testing Library), with the type check and lint CI also runs
cd frontend/chat-ui
npm run typecheck && npm run lint && npm run test:run

# E2E (Playwright, Chromium), against the running app and the local model
cd e2e/playwright
npx playwright test
```

The MongoDB integration tests need Docker Compose running. The other backend tests need nothing else: API tests use an in-memory `IChatRepository`, a fake `IAiProvider` and a test auth handler (see `ChatApiFactory` and `AuthApiFactory`). CI runs the backend and frontend suites on every pull request; the E2E suite runs locally, because it needs a real model.

## Project structure

```
backend/
├── src/
│   ├── Chat.Api/                     # Composition root: controllers, DI
│   ├── Chat.Domain/                  # Conversation, ChatMessage entities
│   ├── Chat.Application/             # ChatService, IAiProvider, DTOs
│   ├── Chat.Infrastructure/          # MongoChatRepository, OpenAiCompatibleProvider, daily message counts
│   ├── Chat.Identity.Domain/         # AppUser, ExternalLogin
│   ├── Chat.Identity.Application/    # IdentityService, IPermissionService
│   ├── Chat.Identity.Infrastructure/ # JwtTokenGenerator, MongoUserStore
│   ├── Chat.Billing.Domain/          # Plan, Subscription, Feature enum
│   ├── Chat.Billing.Application/     # SubscriptionService, IPlanFeatureService
│   └── Chat.Billing.Infrastructure/  # StripePaymentGateway, StripeWebhookHandler
└── tests/                            # xUnit test projects per layer
frontend/chat-ui/                     # React 19 + Vite + TypeScript + MUI
e2e/playwright/                       # Playwright E2E tests
docs/                                 # Development, deployment and architecture notes
openspec/                             # Specs of current behaviour, and change proposals
docker-compose.yml                    # MongoDB 7 + Mongo Express, and the app under the `app` profile
Dockerfile                            # Production image: built UI + API on one origin
```
