# Project conventions for Claude Code

## Git

- **Never push.** Pushing to any remote requires explicit permission from the
  user, every time. Prior approval for one push does not carry over to the next.
- Committing is fine when asked, but propose the commit message and wait for
  approval before running `git commit` unless told otherwise.
- One logical change per commit. Unrelated cleanups — lockfile churn,
  formatting, stray renames — get their own commit or get reverted.

## Commit messages

Separate the subject from the body with a blank line. Limit the subject to 50
characters; 72 is a hard ceiling when a type prefix eats into the budget.
Capitalize the subject and do not end it with a period. Write the subject in
the imperative mood, as an instruction to the codebase: "Fix the reconnect
race", not "Fixed" or "Fixes".

Keep the type prefix this repo already uses — `feat`, `fix`, `enhance`,
`chore`, `docs` — lowercase, followed by a capitalized summary. Name the
behaviour that changed, not the plan it came from: "Fix: Make role seeding safe
for concurrent startup" survives, "Implement iteration 10" stops meaning
anything once the plan file moves on.

Wrap the body at 72 characters. Use it to explain what changed and why, never
how — the diff already says how. Reach for a body only when the why is not
recoverable from the code: a non-obvious root cause, an alternative that was
rejected, a constraint that forced the shape. Routine changes need no body at
all.

Keep the body to one or two short paragraphs of plain prose. No headings, no
markdown, no bullet lists, and no restating the diff as a summary of edits.
Test counts, pass rates and verification notes belong in the pull request or
the conversation, not in permanent history.

No trailers. Do not append `Co-Authored-By:`, `Generated with ...`, or any
similar attribution footer to commit messages or pull request bodies.

## Issues

An issue is not done when its fix merges — it is done when the behaviour has
been verified. Merging a pull request that references an issue moves that issue
into the `qa` label rather than closing it, and the QA handoff workflow does
this automatically: write `Closes #12` as normal and the merge will reopen and
label it, or write `QA: #12` for work that advances an issue without claiming
to finish it.

Closing a `qa` issue is deliberate and manual. It is the moment someone
confirms the fix actually works, and nothing should do it on your behalf.

## Specs

Use OpenSpec for any change beyond a trivial fix. Run `/opsx:propose` and get the
proposal reviewed before writing code; implement with `/opsx:apply`, and
`/opsx:archive` once the work is merged so `openspec/specs/` keeps describing
real behaviour.

Before writing any artifact, run `openspec instructions <artifact-id> --change
<name> --json` and follow what it returns. The schema's own instruction is only
half of it: the project rules in `openspec/config.yaml` are layered on top by
that command and are invisible if you work from `schema.yaml` alone. Those rules
are what require the grilling interview before a proposal, C4 diagrams in a
design, and an ADR round that the user decides rather than you.

`scripts/check-change-conventions.py` enforces the mechanical half of those
rules and runs in CI. It is a backstop, not the rule: passing it does not mean
the interview happened.

Write specs only for what is changing. Do not backfill specs for code you are
not touching — nothing forces them to stay true, so they go stale and mislead.

Each task in a change's `tasks.md` starts as a failing test, per the tdd skill.

Reserve GitHub Spec Kit for a genuinely new bounded context, where its
constitution and phase gates have something to constrain. Do not run both
systems over the same change.

## Testing

- Run the suites the way CI does before declaring work done: `npm run typecheck`,
  `npm run lint`, `npm run test:run` in `frontend/chat-ui`, and
  `dotnet test backend/ChatApp.sln`. E2E (`npx playwright test` in
  `e2e/playwright`) is not in CI, so run it locally when touching the chat path.
- When fixing a bug, verify the new test actually fails against the old code
  before keeping it.
