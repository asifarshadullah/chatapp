# Architecture Study

A personal knowledge base for learning and practising Clean Architecture, Domain-Driven Design,
and architectural decision-making. Built alongside the ChatApp project.

---

## How to use this folder

**When learning a new concept** — read the relevant file in `concepts/`.

**When facing an architectural decision** — read `concepts/04-decision-heuristics.md` first.
Run through the questions in order. If still unsure, find the closest scenario in `scenarios/`
and compare.

**When practising** — pick a feature requirement, create a new folder under `scenarios/`,
write `requirement.md` and `design.md` before touching any code, implement it, then write
`retrospective.md` after. The gap between your design and what the code required is the lesson.

**When coming back after time away** — read this README, then `concepts/04-decision-heuristics.md`.
That is enough to re-orient before working.

---

## Folder structure

```
concepts/
  01-domain-layer.md              What the domain is, what belongs, how to design it
  02-application-layer.md         Orchestration, use cases, DTOs, interfaces
  03-infrastructure-layer.md      Pluggability, persistence, mapping, the two-constructor pattern
  04-decision-heuristics.md       Questions to ask when you are stuck on where something goes

scenarios/
  01-archive-conversation/        Fully worked example from ChatApp
    requirement.md                Feature in plain English (as from a product manager)
    design.md                     Layer split decision written BEFORE coding
    retrospective.md              What the implementation taught, where the plan was wrong

  TEMPLATE/                       Copy this for each new scenario
    requirement.md
    design.md
    retrospective.md

trade-offs/
  embedded-vs-separate-collection.md     MongoDB document design choices
  single-vs-multiple-bounded-contexts.md When to split the domain into separate contexts
```

---

## The core mental model (one paragraph)

Every layer answers one question. Domain: *what is this system about?* Application: *what can
this system do?* Infrastructure: *how does it talk to the outside world?* API: *how does the
outside world reach it?* If a piece of code does not clearly answer the question for its layer,
it is in the wrong place.

---

## Reference codebase

ChatApp — `d:\Programming files\MassStorage\WebApplication\ChatApp`

All concrete examples in this folder trace back to that codebase. When an example references
a file, you can open it there and read the real implementation.

---

## Book references

- *Domain-Driven Design* — Eric Evans (canonical; dense but worth it)
- *Implementing Domain-Driven Design* — Vaughn Vernon (more practical, better for beginners)
- *Clean Architecture* — Robert C. Martin (layer concepts, dependency rule)

## Code references

- github.com/jasontaylordev/CleanArchitecture — .NET Clean Architecture template
- github.com/ardalis/CleanArchitecture — Steve Smith's opinionated .NET version
