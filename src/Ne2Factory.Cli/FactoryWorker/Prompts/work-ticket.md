Take a single backlog ticket end-to-end: understand it, implement and verify it per your
own role, commit it on the branch you're on, and report the outcome. This session runs
unattended inside a fresh session with no prior context — everything needed must come from
the ticket below and the repository itself.

Read `.ne2-factory/project.md` first for project context and the documentation paths to
consult.

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

### 3. Commit

The session starts already checked out on a branch the factory prepared for this ticket,
cut from an up-to-date default branch. Commit your work there, without pausing for
confirmation at any point — this session runs fully unattended and headless
(`claude --print`), there is no one to approve anything. Write the commit message with what
changed, why, and the verification evidence.

Do not push, switch branches, rebase onto anything else, or open a pull request: the
factory publishes the branch for review once you report `done`. Your final report's
`summary` (step 4) becomes the review request's description, so write it for the reviewer.

Never finish the turn with verified-but-uncommitted changes in the working tree — either
it's committed, or you've signaled `blocked` (step 4) explaining why.

### 4. Report the outcome

This is the last thing you do. Your outcome is one of:

- **done** — the work is committed on the current branch.
- **blocked** — you hit `BLOCKED: REQUIREMENT_AMBIGUITY`, verification cannot be made to
  pass, the ticket describes something out of scope for this repo, etc. Include a short
  reason.

Do not report `done` until the work is actually committed, and don't report
`blocked` until you have truly given up.

Your final report is parsed programmatically — the factory posts it on the issue and
labels it for you, so include everything the reviewer would need to see there:
- `status`: `done` or `blocked`.
- `summary`: on `done`, what changed, why, and the verification evidence; on `blocked`,
  leave it empty.
- `reason`: on `done`, empty; on `blocked`, a short explanation of why.

## Constraints

- Never fabricate verification results; if a risk is unverified, that's `blocked`, not
  `done`.
- One ticket, one session, one commit (or a small number of logically-related commits) —
  don't carry work into a follow-up turn expecting more context; there won't be any.
