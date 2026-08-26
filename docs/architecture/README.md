# Architecture Notes

The reasoning behind ChatApp's structural decisions. The [main README](../../README.md)
describes what the architecture *is*; this folder records how it was arrived at, what was
traded away, and where the up-front design turned out to be wrong.

---

## Contents

```
trade-offs/
  single-vs-multiple-bounded-contexts.md   When to split a domain into separate contexts,
                                           and what the split actually cost here
  embedded-vs-separate-collection.md       MongoDB document design: embedding messages in a
                                           conversation versus a separate collection

scenarios/
  01-archive-conversation/                 One feature, followed end to end
    requirement.md                         The feature in plain English
    design.md                              The layer split decided BEFORE any code
    retrospective.md                       What the implementation taught, and where the
                                           design was wrong
```

---

## The core mental model

Every layer answers one question. Domain: *what is this system about?* Application: *what can
this system do?* Infrastructure: *how does it talk to the outside world?* API: *how does the
outside world reach it?* If a piece of code does not clearly answer the question for its
layer, it is in the wrong place.

Dependencies point inward. The Domain depends on nothing. The Application layer defines the
interfaces it needs — `IAiProvider`, `IPermissionService`, `IPlanFeatureService` — and
Infrastructure supplies the implementations. That is what makes the LLM provider swappable
and the contexts independently testable.

---

## Why the retrospectives exist

A design written before coding is a prediction. The gap between it and what the code turned
out to require is the part worth writing down, and it is the part usually lost.

`scenarios/01-archive-conversation/retrospective.md` is the worked example: a domain
invariant written to guard mutation also blocked the reconstruction path when loading an
archived conversation from MongoDB — something the design had not anticipated. The fix was a
reconstruction constructor that trusts stored data, and the lesson generalises to every
entity that gains a guard.

New scenarios are no longer added here by hand. Non-trivial changes now go through
[OpenSpec](../../openspec/) as reviewed proposals, which serve the same purpose with less
ceremony. This folder keeps the decisions that predate that workflow.

---

## Further reading

- *Domain-Driven Design* — Eric Evans
- *Implementing Domain-Driven Design* — Vaughn Vernon
- *Clean Architecture* — Robert C. Martin
