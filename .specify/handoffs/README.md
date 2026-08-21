# Session handoffs

A **handoff** is a markdown record of work left unfinished at a session boundary —
deferred review items, follow-ups, tasks blocked on something outside the session
(a real deploy, an external credential). It lets that work survive the session and
**re-enter the SpecKit flow** instead of being lost in a chat transcript.

Handoffs live here, one file per handoff:

```
.specify/handoffs/<yyyy-mm-dd>-<short-slug>.md
```

## Lifecycle

```
open  ──(items folded into plan/tasks/implement, or explicitly dropped)──>  consumed
```

- **open** — not yet picked up. The load-time check (below) surfaces these.
- **consumed** — its items have been incorporated somewhere durable (tasks.md, the
  plan, code) or consciously dropped, recorded in the file's Consumption log.

Consumed handoffs are kept (not deleted) as an audit trail.

## File format

Front matter, then the body:

```markdown
---
id: 2026-08-21-org-tenant-committee-followups   # stable, unique
created: 2026-08-21                              # absolute date
feature: 001-org-tenant-context                  # feature dir, or "-"
branch: 001-org-tenant-context                   # git branch it targets
status: open                                     # open | consumed
source: code-review committee (5 rounds)          # what produced it
consume_in: [plan, tasks, implement]              # which SpecKit phases should pick items up
---

## Context
Short: what this is and the state it was left in.

## Verified baseline
What is known-good right now (tests, guards) so the next session doesn't re-verify from zero.

## Decisions already made (do not re-litigate)
Settled choices, so a new session doesn't reopen them.

## Outstanding items
| # | Priority | Area | Item | Suggested phase | Notes |
|---|----------|------|------|-----------------|-------|

## Consumption log
(Appended when consumed: date, what happened to each item, where it went.)
```

## The load-time check

At the start of a session, and **before running any `/speckit-*` command**, check
for open handoffs:

```
pwsh .specify/scripts/powershell/check-handoffs.ps1
# or
sh .specify/scripts/bash/check-handoffs.sh
```

The behavior Claude follows on load is documented in the repo-root **`CLAUDE.md`**
(`## Session handoffs — check on load`), which is loaded into every session — that
is what makes new SpecKit commands pick these up. In short: read any open handoff,
surface its items to the user, fold the relevant ones into the current phase, then
mark the handoff `consumed` with a Consumption log entry.

## Creating a handoff

At the end of a session with unfinished work, copy the format above into a new
`.specify/handoffs/<yyyy-mm-dd>-<slug>.md`, list the outstanding items, and commit
it. That is the whole mechanism.
