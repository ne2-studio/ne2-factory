Take a single backlog ticket end-to-end: understand it, implement and verify it per your
own role, get it committed and pushed to the default branch, and report the outcome. This
session runs unattended inside a fresh session with no prior context — everything needed
must come from the ticket below and the repository itself.

Read `.ne2-factory/project.md` first for project context: the default branch to push to,
and the documentation paths to consult.

## Ticket

Ticket: {{TICKET_REFERENCE}}

{{TITLE}}

{{BODY}}

{{COMMENTS}}

## Workflow

### 1. Understand the ticket

The ticket's title, body, and comments are given above, already fetched and formatted;
later comments can correct or override earlier ones, so read them in order. Resolve any
embedded images (e.g. download and view screenshots linked in the body or comments) before
proceeding. Inspect the codebase, the documentation paths listed in
`.ne2-factory/project.md`, and existing code before assuming intent.

This ticket already went through refinement (it's only queued here once labeled
`refined`), so it should be implementation-ready. This session is unattended — there is no
live reviewer to interrupt, so never ask a question here. If it still turns out to be
genuinely ambiguous or contradictory in a way that materially affects product behavior,
scope, or architecture, that's a gap in refinement, not something to resolve by guessing:
treat it as blocked (step 4) and say so, so it can go back through refinement.

### 2. Implement and verify

Implement the change and get it verified, per your own role's workflow. If that workflow
concludes `BLOCKED: REQUIREMENT_AMBIGUITY`, do not attempt to resolve it yourself — proceed
to step 4 as blocked, including the question in the reason, so the ticket can go back
through refinement.

### 3. Commit and push

This session runs fully unattended and headless (`claude --print`) — there is no one to
review or approve anything, so commit and push straight to the default branch without
pausing for confirmation at any point. Write the commit message with what changed, why, and
the verification evidence; you'll reuse that same summary in your final report (step 4) —
posting it on the issue is the factory's job, not yours.

Never leave the default branch in a state where the working tree has verified-but-
uncommitted changes when you finish the turn — either it's committed and pushed, or you've
signaled `blocked` (step 4) explaining why.

### 4. Report the outcome

This is the last thing you do. Your outcome is one of:

- **done** — the work is committed and pushed.
- **blocked** — you hit `BLOCKED: REQUIREMENT_AMBIGUITY`, verification cannot be made to
  pass, the ticket describes something out of scope for this repo, etc. Include a short
  reason.

Do not report `done` until the work is actually committed and pushed, and don't report
`blocked` until you have truly given up.

Your final report is parsed programmatically — the factory posts it on the issue and
closes/labels it for you, so include everything the reviewer would need to see there:
- `status`: `done` or `blocked`.
- `summary`: on `done`, what changed, why, the verification evidence, and the commit
  SHA(s); on `blocked`, leave it empty.
- `reason`: on `done`, empty; on `blocked`, a short explanation of why.

## Constraints

- Never fabricate verification results; if a risk is unverified, that's `blocked`, not
  `done`.
- One ticket, one session, one commit (or a small number of logically-related commits) —
  don't carry work into a follow-up turn expecting more context; there won't be any.
