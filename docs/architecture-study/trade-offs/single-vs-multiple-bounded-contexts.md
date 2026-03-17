# Trade-off: Single vs. Multiple Bounded Contexts

## What is a bounded context?

A bounded context is a boundary within which a domain model is consistent and all terms
mean exactly one thing. Inside a single context, "User" means the same thing everywhere.
"Conversation" means the same thing everywhere.

When the same word starts meaning different things in different parts of the system, you have
discovered a boundary between two contexts.

---

## ChatApp's current single context

Everything in ChatApp belongs to one bounded context: the messaging context.

```
Conversation
ChatMessage
MessageRole (User, Assistant)
```

These concepts all relate to each other in a consistent way. "User" here means a message
sender — there is no separate User entity with login credentials, billing info, or profile.

This is appropriate. The system is focused, small, and all the concepts are about the same
thing.

---

## When a second bounded context appears

Imagine you add features:

**Feature: User accounts (registration, login)**

Now you have a `User` entity. In the messaging context, "User" is just a role on a message.
In the identity context, "User" is a registered account with a password hash, email, created
date, and login history. These are two different things that happen to share a name.

**Feature: Billing / subscriptions**

"User" in billing means "subscriber" — with a plan, payment method, invoice history. This
has no meaningful relationship to the conversation model.

When this happens, you have two bounded contexts:
- **Messaging context**: Conversation, ChatMessage, MessageRole
- **Identity context**: User, Credential, Session
- **Billing context**: Subscriber, Plan, Invoice, PaymentMethod

---

## How bounded contexts communicate

They should NOT share domain entities. A `User` from the identity context should not be
passed directly into the messaging context. Instead, they share only identifiers:

```csharp
// In messaging context — does NOT import identity domain entities
public class Conversation
{
    public Guid OwnerId { get; }  // just an ID — doesn't know what a User is
}
```

The messaging context knows that a conversation belongs to *someone* identified by a Guid.
It does not know that someone is a `User` with a password hash. That is identity's problem.

Communication between contexts happens via:
- **Shared identifiers** (Guid keys passed between contexts)
- **Integration events** (UserRegistered → messaging context creates an initial conversation)
- **API calls** (messaging asks identity "is this user valid?" via an interface)

---

## Practical signals that you need a split

| Signal | Example |
|---|---|
| Same word, different meaning | "User" as a message role vs. "User" as a registered account |
| One area changes without affecting the other | Billing can be overhauled without touching Conversation |
| Different database or team ownership | Billing uses SQL, Messaging uses MongoDB |
| One side can be deployed independently | You can release a billing update without redeploying messaging |
| Concepts from one area make no sense in the other | `InvoicePdfUrl` has no meaning in a conversation |

---

## Cost of splitting too early vs. too late

**Splitting too early:**
- Over-engineering. You build communication infrastructure (events, APIs) for two contexts that
  don't actually need to be separate.
- More code, more coordination overhead, harder to refactor across the boundary.

**Splitting too late:**
- One bloated domain where concepts start bleeding into each other.
- `Conversation` starts getting `SubscriptionPlanId` fields. `User` starts holding
  `LastConversationId`. Everything is coupled.
- Very expensive to untangle once entrenched.

**Rule of thumb:** Start with a single context. Split when you feel actual friction — when
adding a feature requires you to make concepts from different areas aware of each other in
ways that feel wrong. Do not split preemptively.

---

## In code — what a split looks like

**Single context (current ChatApp):**
```
backend/
  src/
    Chat.Domain/          ← one domain for everything
    Chat.Application/
    Chat.Infrastructure/
    Chat.Api/
```

**Split contexts:**
```
backend/
  src/
    Messaging/
      Messaging.Domain/
      Messaging.Application/
      Messaging.Infrastructure/
    Identity/
      Identity.Domain/
      Identity.Application/
      Identity.Infrastructure/
    Billing/
      Billing.Domain/
      Billing.Application/
      Billing.Infrastructure/
    Chat.Api/             ← one API that composes all contexts via DI
```

Each context is internally consistent and does not import domain types from other contexts.
They share only identifiers (Guids) and communicate via defined integration points.

---

## The most important takeaway

You do not need multiple bounded contexts for a small, focused system. ChatApp should stay
as one context until it genuinely needs to split.

The value of knowing about bounded contexts is recognizing the moment when splitting is
right — when you start seeing the friction, the word overloading, the unwanted coupling.
That is when you act, not before.
