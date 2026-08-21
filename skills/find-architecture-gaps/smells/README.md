# Smell catalogue

Each file in this directory is a table of recurring architectural pressures for one category, grouped by the part of the system they typically affect (structure, domain model, dependencies, data, integration, testability, operations) plus two catch-alls (`hygiene.md` for code-level findings that aren't really architectural, `meta.md` for drift between code and documented decisions).

The scout reads every `*.md` file in this directory except this README when it needs vocabulary for a category. It does not need to read all of them for every inspection — see "How the scout should use this" below.

## The model

* **Smell** — a general pattern of architectural symptom. Lives in this directory. Codebase-independent.
* **Evidence** — the concrete manifestation of a smell in *this* repository: which files, which change, which cost. Never belongs here — it belongs in the report.
* **Tension** — the quality attributes (from `../attributes.md`) that are competing, or that the current design is balancing badly. Every smell row names at least one.
* **Movement** — a candidate architectural transformation that could resolve the tension. Listed per smell as a name, not a tutorial.
* **Decision** — the contextual choice to apply, defer, or reject a movement. Made by the scout when writing the report, not by this catalogue.

A smell does not prescribe a refactor. It opens an investigation. Movements are *candidates to weigh against the evidence and the trade-off*, not a lookup table from smell name to fix — matching a signal is never sufficient justification on its own; see `SKILL.md`'s core principles for what evidence is required before a movement gets recommended.

## Table schema

Every category file is a markdown table with these columns:

| Column | Content |
|---|---|
| `id` | kebab-case, stable — used as the report's `Type:` field |
| Smell | human-readable name |
| Description | the general pattern, in one or two sentences — never a this-codebase specific — the "smell" definition, not the "evidence" |
| Attributes in tension | `id`s from `../attributes.md`, comma-separated |
| Candidate movements | short names, semicolon-separated — directions to investigate, not a prescription |
| Common false positive | the one situation most likely to look like this smell but not be worth acting on |

When a smell needs more than the false-positive cell can hold — a subtlety about applicability, a distinction from a neighbouring smell, a worked boundary case — add a `## Notes` section below the table, referencing the smell by its `id`. Most smells will never need one. Reach for it only when a one-line false positive genuinely isn't enough; a `## Notes` section per smell as a default habit re-inflates the catalogue this structure exists to avoid.

## Adding a smell

1. Pick the category file it belongs to (or propose a new category if none fit — this should be rare).
2. Add one table row. Add a `## Notes` section only if the false-positive cell can't carry the nuance.
3. Describe an observable pressure, not a preferred solution.
4. Make sure "Attributes in tension" references real `id`s from `../attributes.md` — add the attribute there first if it's genuinely missing.
5. Keep candidate movements as names, not instructions.

## Design rules

A smell should be:

* recurring across codebases;
* detectable from repository evidence;
* independent from a particular pattern or implementation;
* capable of producing a measurable improvement;
* distinct from existing smells — if two rows would always fire together in this codebase, merge them.

Good smell row:

> Repeated permission rules are interpreted differently across endpoints.

Bad smell row:

> Use the specification pattern.

The first describes pressure. The second prescribes a solution — that belongs, if anywhere, in "Candidate movements", and even there only as a name to weigh, not a mandate.

## How the scout should use this

This is a **vocabulary for diagnosis, not a checklist**. Do not work through every row looking for a match. The order is:

1. Observe a difficult change or a degraded attribute directly from the codebase (Step 1 of `SKILL.md`).
2. Come here, during Step 2, to *name* the pattern and see what movements and tensions other codebases with this pattern have needed to weigh.
3. Confirm with concrete evidence from this repository that this smell, and not a neighbouring one, is what's actually present — still Step 2.

An agent that tries to find one instance of every smell in this directory has misunderstood what it's for.

## Separation rule

Use this test when deciding where content belongs:

> Does this sentence change how the agent should behave during this execution?

If yes, it belongs in `../SKILL.md`.

If it explains how to maintain, extend, or understand this directory, it belongs in this file.

If it is operational smell content — the pattern, its tension, its candidate movements — it belongs in `<category>.md`.
