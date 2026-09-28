# Backlog tooling

ne2-factory's orchestration layer: dev-workflow automation over GitHub Issues, separate
from any product code in the repo it runs on. Both commands live in the `ne2-factory`
.NET console app (`src/Ne2Factory.Cli`); run them from the root of the repo being worked
on.

* `ne2-factory run` — runs each queued GitHub issue (label `backlog`) that's also
  labeled `refined` unattended through `claude --agent implementer`, fed the `work-ticket`
  prompt (a versioned resource in this project, not a Claude Code skill — see
  `PromptTemplates` in `src/Ne2Factory.Cli`): the session opens as the Implementer Agent
  and coordinates the ticket end to end — delegate implementation, verify, commit, push —
  then reports what happened in its final JSON message. The factory itself, not the
  session, posts that as a comment on the issue and closes it (or marks it failed).
  Unrefined tickets get the same treatment via `claude --agent refiner`. See
  `ne2-factory run --help`. Tickets are queued by filing a GitHub issue with the `backlog`
  label directly — this tool no longer queues tickets itself.
* `ne2-factory backlog` — inspect/manage the queue (`list`, `refine`, `requeue`). See
  `ne2-factory backlog --help`.
* `ne2-factory backlog refine` — walks every queued ticket that isn't yet `refined`, one at
  a time, each in its own fresh headless `claude --agent refiner` session (handed just the
  ticket's number, title, body, and comments — the refiner has no idea GitHub is the
  backend behind it) that investigates and reports a refinement summary plus an outcome:
  `ready` or `missing_data`, with open questions when it's the latter. The factory itself —
  not the agent — then posts that summary (and any questions) as a comment on the ticket
  and labels it `refined` or `missing-data` accordingly. A `missing-data` ticket is
  excluded from the queue (neither `refine` nor `ne2-factory run`'s worker will pick it up)
  until a human adds the missing information and requeues it. Without `--all` this stops
  after the first ticket; Ctrl-C is always safe — whatever wasn't processed yet stays
  queued for next time.
* `ne2-factory gap-scout` — runs the `find-architecture-gaps` skill periodically over a
  configured scope (`GAP_SCOUT_SCOPES` in `.ne2-factory.env`) via the
  `architecture-gap-scout` agent, dedupes against previously filed initiatives, and files
  new ones as GitHub issues (label `gap-scout`) pending approval. See
  [`gap-scout.md`](gap-scout.md) for how to schedule it and how approval works.

"The reviewer" is the person named in the consuming repo's `.ne2-factory/project.md`.

## Approval flow

`gap-scout` never queues its own findings for execution — it only proposes. Approving an
initiative means adding the `backlog` label to its issue by hand; that queues it, same as any
manually-filed ticket — it still needs a `backlog refine` pass (label `refined`) before
`ne2-factory run`'s worker will pick it up. Leaving an issue unlabeled (or closing it)
means "not now" — it won't be re-filed verbatim on the next scan.
