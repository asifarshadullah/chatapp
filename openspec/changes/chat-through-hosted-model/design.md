## Context

- `IAiProvider.StreamCompletionAsync(history, ct)` in Chat.Application is the only contract the
  rest of the app sees. `OllamaAiProvider` implements it with `OllamaSharp`, configured from the
  `Ollama` section (`BaseUrl`, `Model`, `SystemPrompt`), which exists only in
  `appsettings.Development.json`. The test factories replace `IAiProvider` with a fake.
- `ChatService.StreamResponseAsync` looks up the named conversation (or creates one), saves the
  user's message, then streams from the provider. `ChatController` turns
  `ConversationNotFoundException` into 404 and any other failure before the first token into 502,
  all before the stream starts. The plan check runs in the controller before the service.
- The UI shows the `detail` of a refused message's problem response, so new refusals need no
  extra handling there.
- `ChatWindow` draws its header buttons (New chat, Manage Plan, Logout) itself, and the
  frontend uses MUI throughout. It has no settings of any kind yet.
- `appsettings.json` ships in the image and is loaded in every environment.
  `appsettings.Development.json` is loaded only in Development and is not in the image.
- Groq's free tier for `openai/gpt-oss-120b` allows, per account, 30 requests and **8,000 tokens
  a minute**, and 1,000 requests and **200,000 tokens a day**. Reasoning tokens count. The token
  limits bind first: at 1.5–2k tokens per message, prompt, reasoning and reply included, that is
  roughly 100 messages a day for everyone together. A single request over 8,000 tokens is
  rejected outright. Groq, OpenRouter, Gemini, Cloudflare Workers AI and Ollama
  (`/v1`) all serve `POST {base}/chat/completions` with `stream: true` and bearer authentication.
- Render runs one instance, so per-user counting could live in memory. But a restart, which
  happens on every deploy and after every sleep, would reset it, and #20 plans several instances.

## Goals / Non-Goals

**Goals:**
- One provider for every OpenAI-compatible service, fully covered by tests that need no network.
- Development runs on the local model by default and never needs a cloud key. The deployed app
  runs on Groq and cannot offer the local model.
- A limit no user can get past, even with parallel requests, and that survives restarts.
- Local development and the end-to-end tests keep running against Ollama.

**Non-Goals:**
- Saving settings with the user on the server (#34). The choice lives in the browser for now.
- Models per plan. Plan-based limits belong with billing (#30).
- Retrying or falling back to a second provider when the first refuses.
- Tool calling, structured output or any model feature beyond streamed text.
- Showing the remaining allowance in the UI.

## Decisions

**The official `OpenAI` .NET package, pointed at the configured endpoint.**
`OpenAIClientOptions.Endpoint` takes the base URL, and `ChatClient.CompleteChatStreamingAsync`
streams `StreamingChatCompletionUpdate`s, whose `ContentUpdate` parts become tokens.
The package is maintained by OpenAI with Microsoft, handles server-sent events and errors, and
accepts a custom transport (`HttpClientPipelineTransport` around an `HttpClient`). Tests can
therefore run it against a fake `HttpMessageHandler` and assert on the exact request it sends.
Alternatives:
- A hand-written `HttpClient` with SSE parsing: no dependency, but we would own the parsing and
  its error cases.
- `Microsoft.Extensions.AI`: another layer over the same package, with nothing here that needs it.

The provider is `OpenAiCompatibleProvider`, and `OllamaAiProvider`, `OllamaSettings` and
`OllamaSharp` are removed. Ollama keeps working through its `/v1` endpoint.
It is registered as a singleton holding one `ChatClient` per offered option, all sharing one
`HttpClient`. The old provider was scoped, which here would mean new clients and a new connection
pool per request.
A test with a handler that releases the event stream in two parts checks that the first token is
yielded before the second part arrives, so the SDK cannot buffer the stream unnoticed.

**Model options are a keyed map in configuration; having a key is what makes one offered.**
```json
"Ai": {
  "DefaultModel": "groq",
  "Models": {
    "groq": { "Name": "Groq · gpt-oss-120b", "BaseUrl": "https://api.groq.com/openai/v1",
              "Model": "openai/gpt-oss-120b", "ReasoningEffort": "low", "ApiKey": "" }
  }
}
```
- **`appsettings.json`**, which ships in the image, defines only `groq` and makes it the default.
  Its key is empty, and Render supplies it as `Ai__Models__groq__ApiKey`.
- **`appsettings.Development.json`** adds `local` (`http://localhost:11434/v1`, `gemma3:4b`,
  key `ollama`) and sets `DefaultModel` to `local`.
- **Groq in development:** its entry also loads there from `appsettings.json`. It appears in the
  dropdown only when you set `Ai:Models:groq:ApiKey` with `dotnet user-secrets` or an environment
  variable.

So the rule "an option is offered when its key is set" covers every case without asking which
environment the app is in. The deployed image cannot offer `local`, because nothing it loads
defines it. A loopback guard, which refuses `localhost` outside Development, was rejected
because the compose run of the production image legitimately uses `host.docker.internal`.

A keyed map rather than a list means environment variables address one option by name
(`Ai__Models__groq__ApiKey`) instead of by position. The map is created with
`StringComparer.OrdinalIgnoreCase`, as configuration keys are, so `groq` and `GROQ` are the same
option.

**User secrets, and keeping them out of the tests.**
`Chat.Api.csproj` has no `UserSecretsId` yet, so `dotnet user-secrets set` would fail. The project
gets one (`dotnet user-secrets init`), and ASP.NET then loads the secrets in Development.
`WebApplicationFactory` also runs in Development, so a Groq key in the developer's secrets would
make the tests offer `groq` on that machine but not in CI. Both test factories therefore set
`Ai:Models:groq:ApiKey` to empty, and only tests that want Groq set a key.

**`IModelCatalog` decides which options exist; the provider holds a client per option.**
`IModelCatalog` in Chat.Application exposes the offered options (id and name), the default id,
and whether an id is offered. It is built once from `AiSettings`. `IAiProvider` gains the option
id: `StreamCompletionAsync(modelId, history, ct)`.
`OpenAiCompatibleProvider` builds one `ChatClient` per offered option at startup, each with its
own endpoint, key and options, and picks the client by id. Startup has already refused an
unknown default, and the controller has already refused an unknown id, so an unknown id
reaching the provider is a bug and throws.

**Two API changes.**
- `GET /api/chat/models`, behind `[Authorize]`, returns `{ "defaultModel": "local", "models":
  [{ "id": "local", "name": "Local · gemma3:4b" }] }`. It returns names only: no URLs, models or
  keys.
- `SendMessageRequest` gains an optional `model`. The controller checks it right after the content
  checks: absent means the default, and unknown or not offered means 400 "That model is not
  available.". It then runs the plan check and passes the id to `ChatService`. Since the quota is
  consumed later, inside the service, a refused model never counts.

**The Settings dialog, in the frontend.**
- A gear `IconButton` (`aria-label="Settings"`) joins the header buttons and opens an MUI
  `Dialog` titled Settings, whose first field is a Model `Select`. Cancel and Save close it, and
  only Save applies the choice.
- The options load from `GET /api/chat/models` through `authorizedFetch` when the chat mounts.
  The dropdown is shown even with one option.
- `services/modelChoice.ts` keeps the saved id in `localStorage` (`chatapp.model`). Reads and
  writes are wrapped in try/catch, so blocked storage falls back to the default.
- The id in use is the saved one if the server offers it, and otherwise the default; an
  unavailable saved id is removed. It is sent as `model` with each message.
- If the list fails to load, the dialog says "The models could not be loaded." and messages go
  without `model`, so the server answers with its default.
- Until the list arrives, the option in use is the saved id, if there is one, unchecked. A message
  sent in that moment with a stale id gets the 400 "That model is not available.". The list then
  arrives and drops the stale id. Holding the input until the list loads was rejected: it would
  slow every page load for a case that needs a stale id and a very quick first message.
- Keeping the choice in the browser needs no server storage, and it ends up where a browser
  setting belongs until #34 moves settings to the user.

**Secrets come from files on Render, and from the Keychain locally; GitHub never holds them.**
- `Program.cs` adds `AddKeyPerFile(<secrets directory>, optional: true)` after the default
  sources. Key-per-file maps a file name's `__` to `:`, as environment variables do, and the
  last source added wins, so a secret file overrides an environment variable of the same name.
  The directory defaults to `/etc/secrets`, where Render mounts Secret Files, and the setting
  `SecretsDirectory` overrides it, so tests can point it at a temporary folder.
- On Render, `MongoDB__ConnectionString`, `Jwt__Secret` and `Ai__Models__groq__ApiKey` move to
  Secret Files. Render already encrypts environment variables at rest. A file additionally keeps
  the value out of the process environment, which any child process inherits and which reading
  the process can reveal. `PORT`, `MongoDB__DatabaseName` and the tunables are not secret and
  stay environment variables.
- Locally, `scripts/with-groq.sh` reads the key from the macOS Keychain (`security
  find-generic-password -s chatapp-groq-key -w`). It exports it as `Ai__Models__groq__ApiKey`,
  and as `GROQ_API_KEY` for compose, to the one command it runs. User secrets stay supported,
  since the project has a `UserSecretsId`, but they are a plain JSON file, so the docs recommend
  the Keychain.
- GitHub gets no Groq key. CI uses a fake model, and Deploy only calls Render's hook. Pushing
  the key from GitHub to Render would need a Render API key in GitHub, which can change the whole
  Render account.

**Each request sends a window of history and caps the reply.**
The provider takes the last `MaxHistoryMessages` (default 10) of the history it is given, after
the system prompt, and sets `MaxOutputTokenCount` to `MaxReplyTokens` (default 800).
`ChatService` still saves and the UI still shows the whole conversation; only the request is
trimmed. At 10 messages a request stays well below 8,000 tokens, so a long conversation keeps
working. The model forgets what lies outside the window: improving that is #32, and saying when a
reply hit the cap is #33.
Trimming lives in the provider because the limits belong to the model service; another service
might allow more.

**The `Ai` section is validated at startup.**
`AiSettings` has `DefaultModel`, `Models`, `SystemPrompt`, `MaxHistoryMessages` and
`MaxReplyTokens`. Each `ModelOption` has `Name`, `BaseUrl`, `ApiKey`, `Model` and
`ReasoningEffort`. They are bound with `ValidateOnStart`, like the JWT and refresh-token settings:
- at least one option must have a key;
- `DefaultModel` must name an option that has one;
- every offered option needs an absolute `http` or `https` `BaseUrl` and a `Model`, and its
  `ReasoningEffort` must be empty, `low`, `medium` or `high`;
- both bounds must be positive.

So a deploy that forgot the Groq key, or a typo, fails at startup instead of on every message.
Ollama ignores the key, so the local option uses the placeholder `ollama`. Making the key the
offered switch means a cloud option can never be offered without one.
When an option has a `ReasoningEffort`, its requests carry `reasoning_effort`, which `gpt-oss`
models use to trade thinking time for speed. Otherwise nothing is sent, because Ollama's models
do not take it.

**The SDK does not retry.**
The package retries 429 and 5xx responses by default, with backoff. For a reply the user is
waiting on, that turns a clear "try again later" into a long silence, and it spends more of the
per-minute allowance. So `RetryPolicy` is set to no retries, and a refusal is reported at once.

**A provider rate limit is its own exception.**
The provider catches `ClientResultException` with status 429 and throws
`ModelRateLimitedException`, defined in Chat.Application next to `IAiProvider`, so the Api layer
does not depend on the SDK. The controller maps it, before the first token, to 503 with "The demo
has reached its model limit for now. Please try again later.".
503 rather than 429: our 429 means this caller has used their own allowance, while this is the
shared service being unavailable. Part-way through a reply it stays the generic error event,
since a 429 arrives before any output.
The provider never puts the key in an exception message. The SDK's exceptions carry the status
and the service's error body, which the controller already logs and never returns.

**The daily limit is a MongoDB counter per user and day, consumed inside `ChatService`.**
`IDailyMessageQuota.TryConsumeAsync(userId, ct)` lives in Chat.Application, and
`MongoDailyMessageQuota` in Chat.Infrastructure. It runs a single `FindOneAndUpdate`:
- the filter is `_id = "<userId>:<yyyy-MM-dd UTC>"`;
- the update is `$inc: { count: 1 }`, plus `$setOnInsert` for `userId`, `day` and `expiresAt`
  (the day after tomorrow);
- it upserts and returns the document after the update, and the message is allowed when `count`
  is at most the limit.

One atomic increment per message means parallel requests cannot both see "one left". Two first
messages of the day racing to insert the same `_id` cannot both fail: since 4.2 the server retries
an upsert that hits a duplicate key on an equality match of `_id`, and Atlas runs 8.0. The parallel
test covers it.
A TTL index on `expiresAt` removes old days. It is created at startup next to `RoleSeeder`, since
creating an existing index is a no-op.
Refused attempts still increment, which is harmless: once over the limit, the count only needs to
stay over it.

`ChatService` calls the quota after it has found the named conversation and before it creates a
conversation or saves the message:
1. look up the existing conversation, or throw not found;
2. consume one message, or throw `DailyMessageLimitReachedException(limit)`;
3. create the conversation if there is none;
4. save the message;
5. stream.

That makes "counts once accepted" hold. A message to someone else's conversation is refused
without counting, and a refused first message leaves no empty conversation behind.
Alternatives:
- Counting the user's saved messages for the day: no new collection, but it needs a query across
  conversations and is not atomic.
- An in-memory counter: resets on every deploy and sleep, and does not work across instances.

The controller maps the exception to 429 with "You have reached today's limit of {limit} messages.
It resets at midnight UTC.".
`TimeProvider` (built into .NET 8) gives the current UTC date, so tests can move the clock to the
next day. `Chat:DailyMessageLimit` defaults to 50 and is validated as positive at startup.
`appsettings.Development.json` sets it to 1,000, so local use and the Playwright suite, which
reuses one user per run, are not capped. The API test factories replace the quota altogether.

**Configuration per environment.**

| Where | Options offered | Default | What sets it |
|---|---|---|---|
| `dotnet run` (Development) | `local`, plus `groq` if a key is in user secrets | `local` | `appsettings.Development.json` |
| Compose `app` profile (Production) | `local` (`host.docker.internal:11434/v1`), plus `groq` if `GROQ_API_KEY` is set | `local` | compose environment variables |
| Render (Production) | `groq` | `groq` | `appsettings.json`, plus the `Ai__Models__groq__ApiKey` Secret File |

`Ai__MaxHistoryMessages` (10) and `Ai__MaxReplyTokens` (800) keep their defaults everywhere unless
a service needs otherwise.

## Risks / Trade-offs

- [Groq retires models, as it did `llama-3.3-70b-versatile`] → The model is configuration. A
  Render variable (`Ai__Models__groq__Model`) overrides it without a code change, and the
  deployment guide says where to check.
- [A choice saved in one environment is meaningless in another, for example `local` remembered by
  a browser that then opens the live app] → The frontend checks the saved id against the offered
  list and falls back to the default; the server refuses an unknown id anyway.
- [The header's buttons are positioned over the message list, so at about 1280 px the first message
  already overlaps "New chat", and a gear button makes the header wider] → This bug predates this
  change and is #35. The gear is an icon button, the narrowest addition possible.
- [`gpt-oss-120b` reasons before answering, which delays the first token] → `low` effort keeps it
  short. The reasoning text arrives in a field that the SDK does not expose as content, so it never
  reaches users. A test asserts that only `content` deltas become tokens.
- [The shared allowance is about 100 messages a day, so at 50 per user two heavy users can use it
  up] → The owner chose 50. When the allowance runs out, everyone gets the clear 503 message until
  Groq's window resets. The limit is a Render variable and can be lowered without a deploy of new
  code.
- [A conversation whose last 10 messages are themselves very long could still exceed 8,000
  tokens] → Messages are capped at 5,000 characters, so the window is at most about 15k tokens in
  theory but far less in practice. Groq answers such a request with 413, which gets the generic
  error. A token budget is part of #32.
- [A message that fails at the model still uses up one of the user's messages] → Accepted: it keeps
  the counter to a single atomic step. At 50 a day, a lost message rarely matters.
- [The SDK marks `ReasoningEffortLevel` as experimental (`OPENAI001`), so a later version may change
  it] → The warning is suppressed only around that one assignment. The provider tests assert the
  exact `reasoning_effort` the request carries, so a breaking SDK update fails a test instead of
  silently dropping the setting. The package also needs `Microsoft.Extensions.Options` 10.0.9,
  which Chat.Infrastructure now references.
- [Ollama's OpenAI compatibility differs slightly from OpenAI's] → The Playwright suite runs
  against local Ollama through the new provider, which shows streaming works end to end.
- [A misconfigured key on Render only shows up on the first message] → The app checks that a key
  is set, but cannot know it is valid without calling the service. The deployment guide's last step
  sends one message.

## Migration Plan

1. Merge. The deploy is manual, so nothing changes on Render yet.
2. On Render:
   - add the Secret Files `Ai__Models__groq__ApiKey` (the Groq key), `Jwt__Secret` and
     `MongoDB__ConnectionString`, each holding only the value;
   - delete those as environment variables, along with `Ollama__BaseUrl` and `Ollama__Model`.

   Everything else about the Groq option ships in `appsettings.json`. Without the key the new
   image refuses to start, and Render keeps the previous deploy running.
   Secret Files are read only by the new image, so step 2 and the deploy belong together.
3. Run Deploy on `main`, then sign in on the live app and send a message.

Rollback: deploy the previous commit. It ignores the `Ai__*` variables and reads `Ollama__*`,
which would have to be restored, and chat would stop working again.
