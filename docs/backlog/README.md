# Backlog tooling

ne2-factory's orchestration layer: dev-workflow automation over GitHub Issues, separate
from any product code in the repo it runs on. Both commands live in the `ne2-factory`
.NET console app (`src/Ne2Factory.Cli`); run them from the root of the repo being worked
on.

* `ne2-factory run` — runs each queued GitHub issue (label `backlog`) that's also
  labeled `refined` unattended through the `work-ticket` skill: delegate implementation,
  verify, commit, push, comment the result back on the issue. See
  `ne2-factory run --help`. Tickets are queued by filing a GitHub issue with the
  `backlog` label directly — this tool no longer queues tickets itself.
* `ne2-factory backlog` — interactively inspect/manage the queue (`list`, `requeue`). See
  `ne2-factory backlog --help`.
* `refine-backlog` (Claude Code skill, run interactively as `/refine-backlog`) — walks every
  queued ticket that isn't yet `refined`, one at a time, spawning a `refiner` agent that asks
  the reviewer directly to resolve ambiguity before the ticket is safe to hand to unattended
  implementation. This is the only place in the pipeline where clarifying questions are asked
  live — `work-ticket` never asks anything; a ticket that turns out ambiguous during
  implementation is reported `blocked` instead, to go back through refinement.
* `ne2-factory gap-scout` — runs the `find-architecture-gaps` skill periodically over a
  configured scope (`GAP_SCOUT_SCOPES` in `.ne2-factory.env`) via the
  `architecture-gap-scout` agent, dedupes against previously filed initiatives, and files
  new ones as GitHub issues (label `gap-scout`) pending approval. See
  [`gap-scout.md`](gap-scout.md) for how to schedule it and how approval works.

"The reviewer" is the person named in the consuming repo's `.ne2-factory/project.md`.

## Approval flow

`gap-scout` never queues its own findings for execution — it only proposes. Approving an
initiative means adding the `backlog` label to its issue by hand; that queues it, same as any
manually-filed ticket — it still needs a `refine-backlog` pass (label `refined`) before
`ne2-factory run`'s worker will pick it up. Leaving an issue unlabeled (or closing it)
means "not now" — it won't be re-filed verbatim on the next scan.
