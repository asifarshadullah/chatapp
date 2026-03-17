# Scenario N: Design Decision

## Analysis

### What is the invariant (if any)?

[Describe the business rule that must be impossible to violate.]

### New entity or modify existing?

[Answer the question. State the reason.]

### Domain method or Application method?

[For each significant operation, explain where it goes and why.]

---

## Layer split

### Domain changes

```csharp
// Entity or value object changes
```

### Application changes

Interface additions:
```csharp
// IChatService or IChatRepository additions
```

Implementation:
```csharp
// Service method
```

### Infrastructure changes

```csharp
// Document schema changes
// Mapping changes
// New repository methods
```

### API changes

```csharp
// New controller endpoint
```

---

## Non-obvious problems encountered during design

[What surprised you? What did you initially get wrong? What friction appeared between
layers that forced a design revision?]
