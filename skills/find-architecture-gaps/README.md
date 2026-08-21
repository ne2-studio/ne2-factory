# Architecture Gap Scout

Architecture Gap Scout inspects a repository and returns a prioritized list of evidence-based refactoring initiatives.

It is deliberately read-only:

* it does not modify code;
* it does not implement refactors;
* it does not propose product features;
* it does not reward architectural fashion without repository evidence.

## Structure

* `SKILL.md`: runtime instructions for the agent.
* `attributes.md`: the quality attribute catalogue — the single source of truth for attribute names, referenced by id from every smell.
* `smells/`: recurring architectural pressures, grouped by category, extensible independently of `SKILL.md`.
* `smells/README.md`: the smell/evidence/tension/movement/decision model, and instructions for authoring smells.
* `prioritisation.md`: the Architectural importance scoring model (Consequence, Reach, Frequency, Structural leverage), plus Confidence, Effort, and Regression risk as separate decision-support fields.
* `output-template.md`: the exact structure every reported initiative must follow.
* `templates/smell-entry.md`: starting point for adding a new smell.

## Extending the skill

New recurring architectural pressures should normally be added as a row in the relevant `smells/<category>.md`, rather than by modifying the central skill. New quality attributes go in `attributes.md`. Changes to how initiatives are scored go in `prioritisation.md`. Changes to the report's shape go in `output-template.md`.

Add or change `SKILL.md` only when the inspection workflow, validity gate, or output contract changes.

## Design philosophy

The skill separates detection of architectural pressure (a **smell**) from its concrete manifestation in this repository (**evidence**), from what's being traded off (**tension**), from a candidate fix (a **movement**), from the contextual choice to act on it (a **decision**). A smell should help the scout notice and name pressure; it should not force a pattern, framework, naming convention, or preferred implementation style — see `smells/README.md` for the full model.

`smells/` is vocabulary for diagnosis, not a checklist. The scout is expected to observe a difficult change or a degraded attribute first, and only then reach for the catalogue to name what it found — not work through every row looking for a match.

Smells should remain independent where possible. When two signals point to the same underlying pressure, prefer improving one smell over creating overlapping smells that produce duplicate initiatives.
