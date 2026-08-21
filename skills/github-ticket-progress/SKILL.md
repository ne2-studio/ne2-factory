---
name: github-ticket-progress
description: "How ticket progress and outcome are represented on GitHub Issues in this repo's backlog pipeline: labels, states, and comment shape."
model: haiku
---

## Goal

Make where a backlog ticket stands a durable trace on its GitHub issue — visible without
opening a Claude session — using this repo's fixed set of labels and comment
conventions.

## Labels and states

| State | Labels | Set by |
|---|---|---|
| Queued, not refined | `backlog` | `./scripts/backlog add`, or Pedro labeling manually |
| Queued, ready | `backlog` + `refined` | `refiner`, after resolving material ambiguity |
| Done | issue closed, `backlog` kept | `./scripts/backlog`'s worker |
| Blocked/failed | `backlog:failed` (`backlog` removed) | `./scripts/backlog`'s worker |

The `refined` transition is the only one an agent performs directly:

```bash
gh label create refined --color 0E8A16 \
  --description "Ticket refined, ready for ./scripts/backlog" >/dev/null 2>&1 || true
gh issue edit <n> --add-label refined
```

Every other transition (queued → done, queued → blocked/failed, closing the issue) is
`./scripts/backlog`'s worker's job, driven by the `status=done`/`status=blocked` signal
file — not something an agent sets by editing labels itself. Never add/remove `backlog`
or `backlog:failed`, and never close the issue from an agent.

## Posting progress

Post a comment on the issue itself — not just a session summary — whenever you reach a
state Pedro should be able to see without opening a session:

```bash
gh issue comment <n> --body "<message>"
```

What the comment should contain depends on what you're reporting — e.g. a refinement
outcome (questions/answers, findings, implementation-ready summary) versus an
implementation outcome (what changed, why, verification evidence, commit SHA(s) on
success). That structure belongs to the reporting agent's own handoff contract; this
skill only covers the mechanics of posting it. Keep it factual and complete enough to
stand alone — Pedro may read it without the session that produced it.

## Constraints

- Never close an issue, and never add/remove `backlog` or `backlog:failed` — those
  transitions belong to `./scripts/backlog`'s worker.
- If posting the comment fails (network, permissions), don't let that block your own
  completion signal; note the failure and continue.
