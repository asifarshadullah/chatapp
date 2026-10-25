## Why

The live demo signs users in but cannot answer them: the only model the app can reach is Ollama on the same machine, and Render's free instance has no room to run one (#29). Groq serves capable models free behind the OpenAI chat completions API, and so do several other providers, so one provider for that API makes the model a configuration choice. Groq's free allowance belongs to one account and is shared by every visitor, so one user must not be able to use it up.

## What Changes

- **One provider for the OpenAI chat completions API** replaces the Ollama provider. Groq, OpenRouter, Gemini, Cloudflare and local Ollama (through its `/v1` endpoint) all become configuration. **BREAKING** for configuration: the `Ollama` settings section becomes `Ai`.
- **Model options per environment.** Configuration defines named options, each a base URL, model, API key and optional reasoning effort. An option is offered only when its key is set:
  - **Deployed app:** offers only **Groq `openai/gpt-oss-120b`** (reasoning effort `low`) as the default, and has no local option at all. Groq retired `llama-3.3-70b-versatile` and `llama-3.1-8b-instant` for free accounts on 16 August 2026, and `gpt-oss-120b` is its recommended replacement.
  - **Development:** offers the **local Ollama model** as the default, and Groq too, only when a key is set locally outside the repository.
- **A Settings dialog** opens from a gear button in the chat header. Its first field is a **Model** dropdown of the offered options, shown even when there is only one. The choice is remembered in the browser; remembering it per user on the server is #34. A new endpoint lists the offered options by id and name, and each message names the option in use. Naming an option that is not offered is refused with 400.
- **Each request is bounded**: only the last 10 messages of a conversation are sent, and replies are capped at 800 tokens, both configurable. Groq's free tier allows 8,000 tokens a minute and 200,000 a day for this model, so without a bound a long conversation would eventually be rejected on every message. The conversation itself still keeps and shows everything. Smarter context handling is #32, and telling the user when a reply hit the cap is #33.
- **The app refuses to start without a usable model configuration**: at least one offered option, a default that is offered, valid URLs, positive bounds and known reasoning efforts.
- **A per-user daily message limit**, 50 by default and configurable, counted across all models. The next message is refused before the stream starts with 429 and a message saying so, and other users are unaffected.
- **A provider that refuses for its own rate limit** before the first token gets 503 and a message saying the demo has reached its model limit for now, instead of the generic "The reply could not be completed.".
- **Secrets as files on Render.** The app also reads settings from files in `/etc/secrets`, where Render mounts its Secret Files. The database connection string, the signing secret and the Groq key can then live there instead of in the process environment, which child processes inherit and process inspection can read.
- **The Groq key in the macOS Keychain for local runs.** `scripts/with-groq.sh` reads it from the Keychain and passes it to one command, such as `dotnet run` or `docker compose`, so it is never stored in a plain file. GitHub never holds the key: nothing there uses it.
- The compose run and the deployment guide follow suit: compose offers the local model, and Render needs only the Groq key. The `OllamaSharp` package is removed.

## Capabilities

### New Capabilities
- `chat/model-provider`: which model options exist in each environment, how a message picks one, and that adding or switching providers needs only configuration.
- `chat/daily-message-limit`: how many messages a user may send in a day, and what happens past the limit.
- `chat/chat-settings`: the Settings dialog, its Model dropdown, and how the choice is remembered.

### Modified Capabilities
- `platform/hosting`: the app reads secrets from files the host mounts.
- `chat/chat-streaming`: the daily limit becomes a refusal answered before the stream; a provider rate limit before the first token gets its own status and message.

## Impact

- **Backend:**
  - Chat.Infrastructure: a new OpenAI-compatible provider replaces `OllamaAiProvider` and `OllamaSettings`, plus a MongoDB-backed daily message counter.
  - Chat.Application: the counter's interface, and `ChatService` consumes a message from it.
  - Chat.Application: a model catalog interface, and `IAiProvider` takes the option id.
  - Chat.Api: settings, startup validation, `GET /api/chat/models`, the optional `model` on `SendMessageRequest`, and the new refusals in `ChatController`.
- **Dependencies:** adds the official `OpenAI` .NET package and removes `OllamaSharp`.
- **Configuration:** `appsettings.Development.json`, `docker-compose.yml`, `docs/deployment.md` and the README move from `Ollama__*` to `Ai__*`. Development raises the daily limit to 1,000, so local use and the end-to-end tests are not capped at 50. On Render, the `Ollama__*` variables are replaced by one, `Ai__Models__groq__ApiKey`; the rest of the Groq option ships in `appsettings.json`.
- **Data:** a new `daily_message_counts` collection whose documents expire on their own.
- **Frontend:** the gear button and Settings dialog in `ChatWindow`, a `modelChoice` service backed by `localStorage`, `getModels` in `chatApi`, and `streamMessage` sending the model id. Refusals need no change, because the UI already shows the message a refusal carries.
