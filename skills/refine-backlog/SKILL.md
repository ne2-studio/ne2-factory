---
name: refine-backlog
description: "Interactive session that refines every unrefined backlog ticket (GitHub issues labeled `backlog` without `refined`) one at a time via the refiner agent, so ./scripts/backlog only ever picks up implementation-ready tickets."
disable-model-invocation: true
model: haiku
---

## Goal

Turn every currently-queued, not-yet-refined backlog ticket into an implementation-ready
one before `./scripts/backlog run` picks it up. This is a deliberate, interactive session
Pedro runs himself — it's the only place in the pipeline where clarifying questions are
asked live, keeping interactive refinement and unattended implementation separate.

## Workflow

### 1. Gather unrefined tickets

```bash
gh issue list --label backlog --state open --json number,title,url,labels \
  --jq '[.[] | select((.labels|map(.name)|index("refined"))|not)] | .[] | "\(.number)\t\(.title)\t\(.url)"'
```

If none, report that and stop — nothing to refine.

### 2. Refine sequentially

For each ticket, in order:

1. Fetch its full title, body, and comments per the `read-github-ticket` skill.
2. Spawn a `refiner` agent with the ticket's number, title, body, formatted comments, and
   URL. It asks you questions directly as needed — answer them as they come.
3. Wait for it to finish before moving to the next ticket. This is sequential by design:
   one ticket's questions shouldn't overlap with another's.

### 3. Report

Summarize what was refined: ticket numbers/titles, and briefly what was clarified or
decided for each. Refined tickets are now labeled `refined` and will be picked up by
`./scripts/backlog run` on its next pass.

## Constraints

- Never implement anything here — this session only clarifies and records decisions on
  the ticket.
- Never skip a ticket's questions to save time; unresolved ambiguity is exactly what this
  session exists to remove before unattended implementation.
