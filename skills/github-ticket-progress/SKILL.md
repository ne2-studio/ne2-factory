---
name: github-ticket-progress
description: "How ticket progress and outcome are represented on GitHub Issues in this repo's backlog pipeline: labels, states, and comment shape."
model: haiku
---

## Goal

Make where a backlog ticket stands a durable trace on its GitHub issue — visible without
opening a Claude session — using this repo's fixed set of labels and comment
conventions. "The reviewer" below is the person named in `.ne2-factory/project.md`.

## Labels and states

| State | Labels | Set by |
|---|---|---|
| Queued, not refined | `backlog` | filed directly on GitHub, or the reviewer labeling manually |
| Queued, ready | `backlog` + `refined` | `ne2-factory backlog`'s worker, from the refiner's reported outcome |
| Queued, missing data | `backlog` + `missing-data` | `ne2-factory backlog`'s worker, from the refiner's reported outcome |
| Done | issue closed, `backlog` kept | `ne2-factory backlog`'s worker |
| Blocked/failed | `backlog:failed` (`backlog` removed) | `ne2-factory backlog`'s worker |

Every transition around refinement (queued → refined, queued → missing-data, and the
comment recording why) is `ne2-factory backlog`'s worker's job, driven by the refinement
outcome the `refiner` agent reports in its final message — the refiner itself never
touches the issue. The transitions below (done, blocked/failed) are also the worker's job,
driven by the `status=done`/`status=blocked` signal an implementation session reports.

## Posting progress

Post a comment on the issue itself — not just a session summary — whenever you reach a
state the reviewer should be able to see without opening a session:

```bash
gh issue comment <n> --body "<message>"
```

This applies to implementation outcomes (what changed, why, verification evidence, commit
SHA(s) on success) — the `work-ticket` session's job, per its own handoff contract. It does
not apply to refinement outcomes, which the worker posts programmatically from the
refiner's reported JSON instead. Keep it factual and complete enough to stand alone — the
reviewer may read it without the session that produced it.

## Constraints

- Never close an issue, and never add/remove `backlog` or `backlog:failed` — those
  transitions belong to `ne2-factory backlog`'s worker.
- If posting the comment fails (network, permissions), don't let that block your own
  completion signal; note the failure and continue.
