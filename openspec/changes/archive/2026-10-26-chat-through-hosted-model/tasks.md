## 1. The OpenAI-compatible provider

- [x] 1.1 Add the `OpenAI` package (2.14.x) to Chat.Infrastructure, `AiSettings` (`DefaultModel`, `Models`, `SystemPrompt`, `MaxHistoryMessages` = 10, `MaxReplyTokens` = 800) and `ModelOption` (`Name`, `BaseUrl`, `ApiKey`, `Model`, `ReasoningEffort`). Verify the solution builds
- [x] 1.2 Write failing tests in Chat.Infrastructure.Tests for `OpenAiCompatibleProvider`, configured with two options and a fake `HttpMessageHandler` returning a recorded chat completions event stream (model-provider "Replies come from the chosen option's chat completions endpoint", "Each request is bounded in history and reply length"):
  - A reply for option `a` goes to `a`'s `{BaseUrl}/chat/completions` with `Authorization: Bearer {a's key}`, `a`'s model and `stream: true`. A reply for `b` goes to `b`'s.
  - The messages are the system prompt and then the history in order with `user` and `assistant` roles; there is no system message when the prompt is empty.
  - With a history of 25 messages and a window of 10, the request carries the system prompt and the last 10, in order, ending with the newest. With 4 messages, it carries all 4.
  - The request asks for at most `MaxReplyTokens` tokens (`max_completion_tokens` or `max_tokens`, whichever the SDK sends).
  - `reasoning_effort` is `low` for an option that has it, and absent for one that does not.
  - Each `content` delta becomes one token in order. A delta carrying only `reasoning` yields nothing.
  - With a handler that sends the first event and then waits, the first token is yielded before the rest of the stream is released.
  - A 429 response throws `ModelRateLimitedException` after exactly one request, so nothing is retried. A 500 throws something else, and neither message contains the key.
- [x] 1.3 Add `ModelRateLimitedException` to Chat.Application, add the option id to `IAiProvider.StreamCompletionAsync`, and implement `OpenAiCompatibleProvider` with one `ChatClient` per offered option, no retries, and a transport injectable for tests (design.md). Update the three `FakeAiProvider`s (`ChatServiceTests`, `ChatApiFactory`, `AuthApiFactory`) to take the id, and the factories' fakes to record it. Verify 1.2 passes

## 2. Model options and startup checks

- [x] 2.1 Write failing tests for `ModelCatalog` in Chat.Infrastructure.Tests (model-provider "Model options come from configuration"):
  - An option with a key is offered; an option without one is not.
  - The default id and the offered options' ids and names are exposed, and nothing else.
  - `IsOffered` is false for an unknown id and for an option without a key.
- [x] 2.2 Implement `IModelCatalog` (Chat.Application) and `ModelCatalog` (Chat.Infrastructure). Verify 2.1 passes
- [x] 2.3 Write failing startup tests in Chat.Identity.Tests, in the style of `JwtSecretStartupTests` (model-provider "Model options come from configuration", "The app refuses to start without a usable model configuration"):
  - Production with the shipped `appsettings.json` and no Groq key refuses to start, saying no model option is offered.
  - Production with `Ai:Models:groq:ApiKey` set starts and offers only `groq`, which is the default; there is no `local`.
  - Development with no Groq key offers only `local`, as the default. With a Groq key it offers `local` and `groq`, and `local` is still the default.
  - A `DefaultModel` naming an option that is missing, or has no key, refuses to start and names `Ai:DefaultModel`.
  - An offered option with `BaseUrl` `not a url` or `ftp://x`, or `ReasoningEffort` `fast`, refuses to start and names the option.
  - `Ai:MaxHistoryMessages` or `Ai:MaxReplyTokens` of 0 refuses to start.

  Add a Groq key wherever a test starts the app in Production (`JwtSecretStartupTests`). Both factories set `Ai:Models:groq:ApiKey` to empty by default, so a key in the developer's user secrets cannot change the results (design.md)
- [x] 2.4 Bind and validate `AiSettings` with `ValidateOnStart`. Register `ModelCatalog` and `OpenAiCompatibleProvider` as singletons. Move `appsettings.json` and `appsettings.Development.json` to the `Ai` layout in design.md, with `Models` created case-insensitive. Add a `UserSecretsId` to `Chat.Api.csproj`. Remove `OllamaAiProvider`, `OllamaSettings` and `OllamaSharp`, and update the factory comments that name Ollama. Verify 2.3 passes, the whole backend suite passes, and `git grep -i ollamasharp` finds nothing

## 3. The daily message limit

- [x] 3.1 Write failing `ChatServiceTests` with a fake `IDailyMessageQuota` (daily-message-limit "Each user may send a limited number of messages per day"):
  - When the quota refuses, `DailyMessageLimitReachedException` carries the limit; no message is saved, no conversation is created, and the provider is not called.
  - A message naming someone else's conversation throws not found without consuming the quota.
  - An accepted message consumes the quota once, including when the provider then fails.
  - The service passes the option id it was given to the provider.
- [x] 3.2 Add `IDailyMessageQuota` and `DailyMessageLimitReachedException` to Chat.Application. Add the option id to `StreamResponseAsync`, and reorder it as in design.md: look up, consume, create, save, stream. Verify 3.1 passes and the existing `ChatServiceTests` still pass
- [x] 3.3 Write failing MongoDB tests in Chat.Infrastructure.Tests for `MongoDailyMessageQuota`, with a test `TimeProvider`:
  - with a limit of 3, three calls succeed and the fourth fails;
  - another user is unaffected;
  - moving the clock to the next UTC day succeeds again;
  - ten parallel calls with one left let exactly one through;
  - the collection has a TTL index on `expiresAt`.
- [x] 3.4 Implement `MongoDailyMessageQuota` with the single `FindOneAndUpdate` from design.md, and an index setup that startup runs next to `RoleSeeder`. Verify 3.3 passes
- [x] 3.5 Bind `Chat:DailyMessageLimit` (default 50, validated as positive with `ValidateOnStart`; 1,000 in `appsettings.Development.json`) and register the quota and `TimeProvider.System`. Write a failing startup test showing a limit of 0 refuses to start (daily-message-limit "The limit is configurable and must be positive"), then verify it passes. In the API test factories, replace the quota with an in-memory one that allows everything unless a test sets a limit

## 4. The API

- [x] 4.1 Write failing tests in Chat.Api.Tests (model-provider "The offered options can be listed", "A message is answered by the option it names"):
  - `GET /api/chat/models` returns the offered options' ids and names and the default id, and its body contains no base URL and no key.
  - Unauthenticated, it returns 401. That test lives in Chat.Identity.Tests, where authentication is real.
  - `POST /api/chat/messages` with `model` naming an offered option reaches the provider with that id; without `model` it reaches the provider with the default.
  - With `model` unknown, or naming an option without a key, it answers 400 "That model is not available.", is not an event stream, saves nothing, and does not consume the quota.
- [x] 4.2 Add the endpoint and the optional `model` to `SendMessageRequest`, checked after the content checks and before the plan check (design.md). Verify 4.1 passes
- [x] 4.3 Write failing `ChatStreamingTests` (chat-streaming "Refusals are answered before the stream starts", "A failure before the first token is an ordinary error"):
  - With the quota exhausted, `POST /api/chat/messages` answers 429 with "You have reached today's limit of 50 messages. It resets at midnight UTC.", is not an event stream, and the conversation count is unchanged.
  - With the fake provider throwing `ModelRateLimitedException` before its first token, the answer is 503 with "The demo has reached its model limit for now. Please try again later." and is not an event stream.
  - A 3-message limit configured on the factory refuses the fourth message.
- [x] 4.4 Map both exceptions in `ChatController` before the stream starts, and declare the 400, 429 and 503 responses. Verify 4.3 passes and the existing streaming tests still pass

## 5. The Settings dialog

- [x] 5.1 Write failing frontend tests (chat-settings):
  - `chatApi.getModels` calls `/api/chat/models` through `authorizedFetch` and returns the list and default, or an error for a failed response.
  - `streamMessage` sends `model` when given one, and omits it otherwise.
  - `modelChoice`:
    - with nothing saved, the default is in use;
    - a saved id that is offered is in use;
    - a saved id that is not offered falls back to the default and is removed;
    - storage that throws falls back to the default.
  - `ChatWindow`:
    - the Settings button opens a dialog whose Model dropdown lists the offered names with the one in use selected, including when only one is offered;
    - Save applies a new choice, so the next message is sent with its id;
    - Cancel keeps the old one;
    - the choice survives a remount;
    - a failed list shows "The models could not be loaded." and messages are sent without `model`.
  Add `getModels` to the `chatApi` mocks in `ChatWindow.test.tsx` and `App.test.tsx`, so existing tests do not reach the network.
- [x] 5.2 Implement `getModels`, the `model` parameter of `streamMessage`, `services/modelChoice.ts`, and the gear button and Settings dialog in `ChatWindow` (design.md). Verify 5.1 passes, and that typecheck and lint are clean
- [x] 5.3 Add Playwright tests in `chat.spec.ts`:
  - The Settings dialog shows "Local · gemma3:4b" selected.
  - After Save and a reload, it is still selected.
  - A message sent afterwards gets a reply.

## 6. Local runs, compose and docs

- [x] 6.1 Add the `local` option to the compose `app` service as environment variables (`Ai__Models__local__*` with `http://host.docker.internal:11434/v1`, `gemma3:4b` and key `ollama`), set `Ai__DefaultModel=local`, and pass `Ai__Models__groq__ApiKey` from an optional `GROQ_API_KEY`. Remove the `Ollama__*` variables. Verify `docker compose --profile app up --build` offers Local in the dialog, signs up and streams a reply
- [x] 6.2 Update `docs/deployment.md`:
  - Render's environment table: `Ai__Models__groq__ApiKey` replaces `Ollama__*`, with notes on `Ai__Models__groq__Model`, `Ai__MaxHistoryMessages`, `Ai__MaxReplyTokens` and `Chat__DailyMessageLimit`;
  - where to get a Groq key, and how to check that a model is still offered;
  - sending one message as the last setup step;
  - the migration steps from design.md.

  Update the README:
  - the AI provider wording and features: any OpenAI-compatible service, Ollama locally, Groq when deployed;
  - the Settings dialog;
  - trying Groq locally: `dotnet user-secrets set "Ai:Models:groq:ApiKey" "<key>" --project backend/src/Chat.Api`.

  Verify every setting named matches `AiSettings`

## 7. Secrets from files and the Keychain

- [x] 7.1 Write failing startup tests in Chat.Identity.Tests (hosting "Secrets can be supplied as files"), using a temporary folder as `SecretsDirectory`:
  - Production with no Groq key in configuration and a file `Ai__Models__groq__ApiKey` holding a key starts and offers `groq`.
  - A file `Jwt__Secret` overrides an environment-style setting of the same name: the setting holds the placeholder, which would refuse to start, and the file holds a proper secret, and the app starts.
  - A `SecretsDirectory` that does not exist starts normally.
- [x] 7.2 Add `AddKeyPerFile` for `SecretsDirectory` (default `/etc/secrets`, optional) in `Program.cs`, after the default sources. Verify 7.1 passes and the whole backend suite passes
- [x] 7.3 Add `scripts/with-groq.sh`: it reads the key from the Keychain service `chatapp-groq-key`, exports it as `Ai__Models__groq__ApiKey` and `GROQ_API_KEY`, and runs the given command. Without the item it exits non-zero with the command that stores one. Verify both paths by hand against a throwaway Keychain file (`CHATAPP_KEYCHAIN`), deleted afterwards, so the owner's login Keychain is not touched
- [x] 7.4 Update `docs/deployment.md` (Secret Files for the three secrets, how to create them, and the migration) and the README (the Keychain and the script as the way to try Groq locally, and user secrets as the alternative). Verify every name matches the code

## 8. Verification

- [x] 8.1 Run the suites as CI does: `npm run typecheck`, `npm run lint` and `npm run test:run` in `frontend/chat-ui`, and `dotnet test backend/ChatApp.sln`. Run `npx playwright test`, which now reaches Ollama through the new provider. Run `openspec validate --all --strict`. Verify all pass
- [x] 8.2 Mutation checks, restoring the code after each:
  - consume the quota after saving the message: the "not saved" test fails;
  - compare `count < limit` instead of `<=`: the limit-of-3 test fails;
  - allow the SDK's default retries: the single-request 429 test fails;
  - send the whole history: the 25-message window test fails;
  - always send `reasoning_effort`: the "absent" test fails;
  - offer an option without a key: the `ModelCatalog` "not offered" test and the Production "no Groq key refuses to start" test fail;
  - drop the secret files source: the secret-file startup tests fail.
- [x] 8.3 With a Groq key from the owner, stored in the Keychain and passed in through `scripts/with-groq.sh`:
  - run the app in Development and check the dialog offers Local and Groq;
  - send one message with each, and check a streamed reply arrives from each;
  - check the logs contain no key.
- [x] 8.4 Found during 8.3: with Groq, a reply showed twice, once cut short and once in full, because `ChatWindow` read the reply's id from a ref only when React applied each token's update, after `onComplete` had cleared it (#36). Write a failing `ChatWindow` test where the last token arrives together with the end of the stream, capture the id when the token arrives, and verify the test passes and the reply shows once against Groq
