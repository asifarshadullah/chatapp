## Context

See proposal.md for why. Today:

- `ChatHub` carries `[Authorize(Policy = "CanChat")]` (`conversation:create`) and asks
  `IPlanFeatureService` for `chat` before streaming.
- `ChatController` carries a bare `[Authorize]`. `POST /api/chat` calls
  `IChatService.SendMessageAsync`, which nothing else calls. `GET .../history` calls
  `GetHistoryAsync` with no permission check.
- Policies are registered in `Program.cs` as `PermissionRequirement`s, handled by
  `PermissionRequirementHandler` through `IPermissionService`. A role holding `*` passes
  every permission.
- In `Chat.Api.Tests`, `ChatApiFactory` swaps in `AlwaysAllowPermissionService`, so no
  test there can see a permission refusal. No test anywhere shows the hub refusing a
  caller without `conversation:create`.

## Goals / Non-Goals

**Goals:**

- One way to send a message (the hub), with its existing permission and plan gates.
- The history endpoint checks `conversation:read` through the same policy mechanism the
  hub uses.

**Non-Goals:**

- Changing the hub, its policy or its plan check. Their behaviour gains spec scenarios
  and a missing test, not new code.
- A plan check on reading history.
- Changing which roles hold which permissions.

## Decisions

### Remove the HTTP send path rather than guard it

Delete `ChatController.SendMessage`, `IChatService.SendMessageAsync` and
`ChatService.SendMessageAsync`, `ChatRequestDto` and `ChatResponseDto`. On the frontend,
delete `sendMessage` from `chatApi.ts`, the `ChatResponse` type and `chatApi.test.ts`,
which tests only `sendMessage`.

*Alternative:* add `CanChat` and the plan check to the action. Rejected (user decision):
nothing calls it, and two send paths would need their gates kept in step forever.

With the action gone, `POST /api/chat` matches no route and returns 404 before
authorization runs. That is the "not found" in the spec.

### A `CanReadConversation` policy on the history action

Register `CanReadConversation` → `PermissionRequirement("conversation:read")` next to
`CanChat` in `Program.cs`, and put `[Authorize(Policy = "CanReadConversation")]` on
`GetHistory`. The class keeps its bare `[Authorize]`.

Authorization runs before the action, so a caller without the permission gets 403 before
the conversation is looked up, whatever id they name. A caller with it falls through to
the existing owner filter, which still gives 404.

*Alternative:* call `IPermissionService` inside the action. Rejected: the hub already uses
a policy, and an attribute can't be forgotten on the path the way an inline call can.

### Permission tests run against the real permission service

The permission scenarios are tested in `Chat.Identity.Tests` on `AuthApiFactory`, which
runs the real `PermissionService` against a `FakeRoleStore`, with real signed tokens. It
already seeds `User`, `OrgAdmin` and `Admin` (`*`). Add two test-only roles:

- `NoRead`: `conversation:create` only, for the history 403s;
- `NoCreate`: `conversation:read` only, for the refused real-time connection.

Because the real service decides, the tests can fail in every way that matters:

- removing the policy from the action lets `NoRead` through;
- pointing the policy at `conversation:create` lets `NoRead` through too;
- breaking the `*` wildcard shuts `Admin` out.

*Alternative:* a `ChatApiFactory` variant with a stub that denies `conversation:read`.
Rejected: that factory's default stub allows everything, so the "every permission"
scenario run there could never fail.

Conversations for these tests are seeded through the factory's `IChatRepository`.

### Seed history tests without the HTTP send path

The controller tests start conversations with `POST /api/chat`. They will seed through
`factory.Repository` instead: `CreateConversationAsync` for the owner, then
`AddMessageAsync`.

`Chat.Identity.Tests`' `Unauthenticated_SendMessage_Returns401` posts to `/api/chat`, which
will be 404. It will target `GET /api/chat/{id}/history` instead, which keeps the
"unauthenticated is 401" check on a chat endpoint that still exists.

### Keep the coverage the deleted service tests carried

Some `SendMessageAsync` service tests cover behaviour that `StreamResponseAsync` shares
but no streaming test pins down:

- null, empty and whitespace content are rejected;
- a message into an existing conversation is appended to it.

Add those as `StreamResponseAsync` tests first, then delete the `SendMessageAsync` tests.
The controller's HTTP ownership tests go: their real-time twins already exist in
`ChatHubTests`.

## Risks / Trade-offs

- **An outside client might use `POST /api/chat`.** → None is known: the UI and the e2e
  suite send through the hub. The proposal marks the removal BREAKING.
- **403 vs 404 shows whether you hold the permission.** → That is about the caller's own
  role, not about any conversation, so it reveals nothing about another user's data. The
  spec requires the same 403 for every id.
- **Permission results are cached per role for five minutes.** → Not changed here. A role
  edit can take up to five minutes to apply to history, as it already does for the hub.

## Migration Plan

No data migration. Every seeded role (`User`, `OrgAdmin`, `Admin` via `*`) holds
`conversation:read`, so behaviour is unchanged for existing users. To roll back, revert
the commit.
