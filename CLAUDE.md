# Sprint Sync — guide for Claude

Multi-tenant work-item tracker built with Spec-Driven Development (SpecKit). The
project constitution is `.specify/memory/constitution.md` and is authoritative —
read it before design or implementation work.

## Session handoffs — check on load

Unfinished work is carried across sessions as markdown under `.specify/handoffs/`
(protocol: `.specify/handoffs/README.md`). **This file is loaded into every
session, so treat the following as a standing instruction — including inside every
`/speckit-*` command.**

**On load, and before running any `/speckit-*` command (specify, clarify, plan,
tasks, analyze, implement), check for open handoffs:**

```
pwsh .specify/scripts/powershell/check-handoffs.ps1
# or:  sh .specify/scripts/bash/check-handoffs.sh
```

If the check reports any OPEN handoff:

1. **Read it in full** (`.specify/handoffs/<file>.md`).
2. **Surface its outstanding items to the user** before proceeding with the
   command — do not silently ignore them.
3. **Fold the relevant items into the current phase:**
   - `/speckit-plan` → account for open design/deploy items in the plan and its
     Constitution Check.
   - `/speckit-tasks` → append open, task-shaped items to `tasks.md`.
   - `/speckit-implement` → work the open implementation items.
   - Other commands → at minimum, remind the user the open items exist.
4. **When a handoff's items have been incorporated** (into tasks/plan/code) **or
   consciously dropped**, set its front-matter `status: consumed`, append a
   `## Consumption log` entry saying where each item went, and commit.

A handoff whose items are still pending stays `open` across sessions until acted
on. The check is advisory (exit 0) — it never blocks a command; it ensures the
work is seen.

## Creating a handoff — end of session with unfinished work

When a session ends with outstanding work (deferred review findings, follow-ups,
tasks blocked on a real deploy or an external credential), **create a new
handoff** `.specify/handoffs/<yyyy-mm-dd>-<slug>.md` per the README, list the
items, and commit it. That is the entire mechanism: a handoff in, a handoff out.
