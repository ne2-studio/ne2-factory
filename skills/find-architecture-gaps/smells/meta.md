# Meta smells: code vs. documented decisions

Unlike every other category, this one is not code-vs-code — it's code vs. this repository's own recorded architectural decisions. Read `SKILL.md` Step 1 before evaluating it.

| id | Smell | Description | Attributes in tension | Candidate movements | Common false positive |
|---|---|---|---|---|---|
| `architecture-documentation-drift` | Architecture documentation drift | Code crosses a boundary, contradicts an invariant, or no longer matches a component or flow that a root architecture document or an ADR states as binding. | `consistency`, `comprehensibility`, `robustness` vs. the cost of keeping docs current | Bring the code back in line with the documented rule; or, if the code is the side that legitimately evolved, update the document instead | The documentation is silent on this case rather than actually contradicted, or reads as aspirational prose with no real enforcement behind it |

## Notes

### `architecture-documentation-drift`

Read the repository's architecture documentation before evaluating this smell: the root-level architecture document (e.g. `ARCHITECTURE.md`), plus every ADR under `docs/adr/` and under `<app>/docs/adr/` for each deployable application in the repo (one such directory per app, e.g. `api/docs/adr/`, `app/docs/adr/`, `admin/docs/adr/`). Treat a stated rule, invariant, or diagram in these documents as ground truth to check the code against — not as background reading. If none of these locations exist yet, or exist but contain nothing binding, skip this smell; there is nothing documented to diverge from.

Only report a violation when the documentation asserts something with actual force — a "never"/"always"/"must" statement, an explicit boundary ("X depends only on Y"), a layering diagram, a stated invariant — not from silence on a case the docs never addressed, and not from language that reads as aspirational rather than descriptive of what's actually enforced.

When code and documentation disagree, determine which side is stale before recommending a fix. If the code changed for a good reason and the documentation simply wasn't updated, the correct initiative is to update the documentation, not to bend the code back into shape — report that as a valid initiative under this smell, since a stale architecture doc or ADR misleads every future contributor and quietly erodes `consistency` and `comprehensibility` just as much as code drift does. Cite the exact document section alongside the violating (or superseding) code location so both sides of the evidence are visible.

Do not flag a gap because the documentation is silent on a case; do not force code back in line with a document without checking whether the document is the stale side; do not propose a new ADR for a decision that involved no real trade-off; do not treat stylistic or naming differences as drift when the underlying rule still holds.
