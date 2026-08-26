---
name: work-ticket
description: "Coordinates a single backlog ticket end-to-end, delegating implementation to bug-fixer/implementer, verifying, committing, and signaling completion for the backlog orchestrator."
disable-model-invocation: true
---

## Goal

Coordinate a single backlog ticket end-to-end: understand it, delegate implementation to
the agent that owns it, confirm it's verified, get it committed and pushed to `main`, and
hand control back to `./scripts/backlog`'s orchestrator. This skill runs unattended (via
`./scripts/backlog run`) inside a fresh session with no prior context — everything needed
must come from the ticket text and the repository itself. It does not implement changes
or run verification itself — that's `bug-fixer`/`implementer`'s and `verifier`'s job.

## Workflow

### 1. Understand the ticket

The prompt gives you the ticket's title, body, and comments already fetched and
formatted. Read them per the `read-github-ticket` skill — including resolving any
embedded images before proceeding. Inspect the codebase, the relevant `docs/` files
referenced by root/service `CLAUDE.md`, and existing code before assuming intent.

This ticket already went through the `refine-backlog` session (it's only queued here once
labeled `refined`), so it should be implementation-ready. This session is unattended —
there is no live Pedro to interrupt, so never ask a question here. If it still turns out
to be genuinely ambiguous or contradictory in a way that materially affects product
behavior, scope, or architecture, that's a gap in refinement, not something to resolve by
guessing: treat it as blocked (step 6) and say so, so it can go back through refinement.

### 2. Delegate implementation

This skill coordinates the ticket's lifecycle; it does not implement the change itself.
Spawn the agent that owns the outcome:

- The ticket reports a defect in existing behavior → spawn `bug-fixer`.
- The ticket asks for a new or changed behavior → spawn `implementer`.

Give it the ticket text (and any comments/images already resolved in step 1). That agent
owns reproducing/implementing, testing, and getting its own diff verified through a
`verifier` agent — do not duplicate that work here.

If it returns `BLOCKED: REQUIREMENT_AMBIGUITY`, do not attempt to resolve it yourself —
proceed to step 6 as blocked, including its question in the reason, so the ticket can go
back through `refine-backlog`.

### 3. Confirm the handoff

Read the agent's handoff. Proceed to commit only once it reports its diff as verified
(via `verifier`) — never on red or partial evidence, and never by re-running verification
yourself on its behalf.

### 4. Commit and push

This session runs fully unattended and headless (`claude --print`) — there is no one to
review or approve anything, so commit and push straight to `main` without pausing for
confirmation at any point. Write the commit message with what changed, why, and the
verification evidence (mirroring the `verifier` agent's handoff format); you'll reuse
that same summary for the issue comment in step 5.

Never leave `main` in a state where the working tree has verified-but-uncommitted
changes when you finish the turn — either it's committed and pushed, or you've signaled
`blocked` (step 6) explaining why.

### 5. Report back to the issue

The ticket's GitHub issue number is given in the prompt (`GitHub issue: #<n> (<url>)`).
Use the `github-ticket-progress` skill to post your final feedback there as a comment, so
the outcome is visible on the issue itself, not just in a session no one will attend:

- On success: the same summary from the commit message (what changed, why, verification
  evidence), plus the commit SHA(s).
- If blocked: the explanation of why, exactly as given in your final message.

Post this before signaling completion (step 6).

### 6. Signal completion

This is the last thing you do, right before your final message. The orchestrator has no
other way to know the outcome — it only reads `.backlog/.signal` after this headless
session exits on its own.

- On success (committed and pushed):
  ```bash
  mkdir -p .backlog && printf 'status=done\n' > .backlog/.signal
  ```
- If genuinely blocked (an agent reported `BLOCKED: REQUIREMENT_AMBIGUITY`, verification
  cannot be made to pass, ticket describes something out of scope for this repo, etc.) —
  explain why in your final message, then:
  ```bash
  mkdir -p .backlog && printf 'status=blocked\nreason=%s\n' "<short reason>" > .backlog/.signal
  ```

Do not write the signal file until the work is actually committed+pushed or you have
truly given up.

## Constraints

- Never fabricate verification results; if a risk is unverified, that's `blocked`, not
  `done`.
- One ticket, one session, one commit (or a small number of logically-related commits)
  — don't carry work into a follow-up turn expecting more context; there won't be any.
