---
name: find-architecture-gaps
description: "Inspect a codebase for architecture, design, testing, and maintainability gaps. Produce a prioritized list of evidence-based refactoring initiatives. Do not modify code, create plans for features, or implement refactors."
---

# Architecture Gap Scout

## Purpose

Inspect the current codebase and identify the highest-value opportunities to improve its architecture, design, testability, clarity, and long-term capacity for change.

The output is a prioritized list of refactoring initiatives supported by concrete evidence from the repository.

This skill does not implement changes, propose speculative architecture, or recommend patterns merely because they are considered good practice.

Every reported initiative must demonstrate that the proposed change would objectively improve at least one relevant attribute of the codebase.

## Scope

The caller may give a scope (e.g. a single deployable's directory). When given, Steps 1-4
apply to that directory only: evidence, tensions, and reported initiatives must all come
from inside it. Root-level docs (`ARCHITECTURE.md`, ADRs) stay fair game for context
regardless of scope — Step 1 reads them for any deployable. With no scope given, inspect
the whole repository.

---

# Core principles

## 0. The model

Every initiative is built from five distinct things. Keep them distinct — collapsing them is how a report turns into a checklist.

* **Smell** — a general pattern of architectural symptom, defined once in `smells/*.md`. Codebase-independent.
* **Evidence** — the concrete manifestation of a smell in *this* repository: which files, which change, which cost. Never in the catalogue; always in the report.
* **Tension** — which attributes from `attributes.md` are competing, or being balanced badly, right now. Naming the attribute an initiative improves without naming what it costs is incomplete.
* **Movement** — a candidate architectural transformation that could resolve the tension, drawn from the smell's candidate movements or reasoned independently when none fit. A candidate to weigh, not a mandate to apply.
* **Decision** — the contextual choice, made in Step 4, to recommend the movement now, note it as deferred (real tension, not worth it yet), or drop it. `prioritisation.md` is what this decision is based on.

A smell does not prescribe a refactor. It opens an investigation. See `smells/README.md` for the full model and how to use the catalogue as vocabulary rather than a checklist.

## 1. Evidence before recommendation

Do not report a gap without repository evidence — something located in *this* repository, not a general software-engineering opinion.

Evidence shapes that cut across many smells: files, methods, components, or modules that repeatedly change together; repeated production defects tied to the same area; a representative maintenance task trace (Step 2). For evidence specific to one pattern, use the matching row in `smells/*.md`.

Do not rely only on generic statements such as "this violates SOLID", "this should use a repository", "this class is too large", "this should follow hexagonal architecture", "this would be cleaner", or "this is not best practice". Explain the concrete cost currently caused by the observed structure.

## 2. Improve attributes, not aesthetics

Every initiative must name attributes it improves from `attributes.md`, by id, and the attributes it puts under tension. `attributes.md` is the single source of truth for attribute names — do not invent an attribute inline in a report.

A recommendation is invalid if its only justification is personal preference or architectural symmetry. A recommendation that claims a free improvement, with no tension named, has probably not been thought through — go back to the evidence.

## 3. Architecture must emerge from pressure

Do not recommend abstractions pre-emptively. An abstraction needs a stronger bar than ordinary evidence: the pressure must already be real and recurring — duplicated behaviour, growing combinatorial logic, multiple callers depending on the same concept, a responsibility already varying independently — not hypothetical future need. Check the matching row in `smells/*.md` (e.g. `wrong-abstraction`, `hub-dependency`, `missing-domain-concept`, `inappropriate-intimacy`, `context-scattering`) for the specific threshold that pattern requires.

Do not introduce an interface merely because a class has dependencies, or a layer merely to conform to an architectural template. Do not suggest repositories, services, factories, handlers, ports, adapters, or domain objects unless repository evidence supports their existence. The same restraint applies to a smell's candidate movements (`smells/*.md`): weigh them against the evidence, never look one up by smell name and apply it — matching a signal is not sufficient justification on its own.

Cognitive and context pressure are valid architectural pressure in their own right, not just a flavour of coupling — a module can warrant a boundary even when it rarely changes, if active code repeatedly has to inspect or coordinate its internals. Check `smells/structural.md`'s notes for `context-scattering` and `excessive-module-surface` for what that improvement should and shouldn't look like — deep modules, stable narrow facades, when extraction is and isn't justified. Throughout, distinguish abstraction from indirection, and large code from low-cohesion code — size alone is never the pressure this principle requires.

## 4. Preserve the outer safety net

When proposing a movement:

* identify which behavioural tests should remain at the public boundary;
* identify which combinatorial cases should move to a smaller component;
* explain what wiring remains protected;
* explain what feedback becomes faster or more precise;
* do not replace all black-box coverage with isolated tests.

The intended model is:

```text
Public behaviour tests
    protect capabilities and wiring

Focused component tests
    protect combinatorial decisions

Adapter or integration tests
    protect technology-specific semantics
```

## 5. Separate capability changes from refactoring

Do not include product features in the report. A valid initiative MUST preserve observable product behaviour UNLESS explicitly classified as deletion of unused behaviour, correction of an inconsistency, or removal of dead or unreachable paths.

---

# Inspection process

Perform the inspection in the following order.

## Step 1: Understand the delivered system

Identify deployable applications, packages or libraries, public APIs, background workers, databases, external integrations, frontend applications, shared modules, test suites, and build/deployment artifacts.

Determine the real public boundaries of each component: HTTP endpoints, browser-visible behaviour, messages consumed or produced, NuGet public APIs, CLI commands, scheduled jobs, persisted business effects.

Read the repository's documented architecture: a root-level architecture document (e.g. `ARCHITECTURE.md`), and any ADRs under `docs/adr/` or under `<app>/docs/adr/` for each deployable application. Where these exist, the rules, invariants, and boundaries they state are binding ground truth for `architecture-documentation-drift` (`smells/meta.md`) in Step 2 — not general background. Skip this if none of these locations exist or contain nothing binding.

## Step 2: Gather evidence

Look in two places, using what Step 1 surfaced as your starting point.

**History** (when Git history is available): files, methods, components, or modules that repeatedly change together; hotspots with high churn; repeated fixes in the same area; features that repeatedly modify the same conditional logic; tests frequently updated alongside implementation details; modules that accumulate unrelated responsibilities. Change history is strong evidence because architecture should optimise actual change patterns, not hypothetical ones — do not treat churn alone as a problem; explain why the observed co-change suggests an unstable or missing boundary.

**Codebase**: use the smell catalogue below as vocabulary to name what you're looking at. You do not need to read every category for every inspection — read the ones the observed symptom points toward, and do not work through the catalogue as a checklist.

### Smell catalogue

`smells/*.md` groups recurring architectural pressures by category — `structural.md`, `domain.md`, `dependencies.md`, `data.md`, `integration.md`, `testability.md`, `operational.md`, `hygiene.md`, `meta.md` — each as a compact table of smell, description, attributes in tension, candidate movements, and the smell's most common false positive. A handful of smells that need more nuance than a table cell carries have a `## Notes` section below their category's table.

Read `smells/README.md` once, before the first inspection, for the full schema.

For representative capabilities, additionally perform a context reconstruction: start from the public entry point; record the files, symbols, abstractions, and decisions required to explain the behaviour; distinguish essential domain knowledge from forwarding, mapping, configuration, and infrastructure detail; identify which internal concepts consumers or maintainers cannot safely ignore; evaluate whether a narrower and deeper boundary would reduce the required context. This reconstruction is the required evidence for `context-scattering` and `excessive-module-surface` (`smells/structural.md`), and it often surfaces `inappropriate-intimacy` along the way.

Confirm with concrete evidence from this repository that a named smell, not a neighbouring one, is what's actually present. Discard candidates that cannot be supported with evidence.

## Step 3: Identify tension and improvement opportunities

For each candidate confirmed in Step 2:

1. name the tension — which attributes (`attributes.md`) are competing or being balanced badly;
2. name which attributes a change could improve;
3. if a movement is worth proposing, draw it from the smell's candidate movements, or reason one independently when none fit (Principle 3), and name its trade-off — what it costs, not only what it buys.

Before moving on, check whether several candidates confirmed in Step 2 are plausibly explained by the same underlying architectural gap. When they are, do not carry them forward as separate initiatives: investigate and name the shared structural cause, and treat each individual candidate as evidence for it rather than as its own competing initiative. Prefer one initiative that repairs a boundary over several initiatives that each remove one symptom of the same missing boundary — `prioritisation.md` explains why this matters more than any scoring weight.

## Step 4: Prioritise and select

Score every candidate using `prioritisation.md` (Architectural importance, from Consequence, Reach, Frequency and Structural leverage; plus Confidence, Effort, and Regression risk as separate, non-multiplying fields). Weight high-churn, business-critical areas more heavily; a stable, rarely-changed area can still be worth selecting when active code repeatedly pays a cognitive or context cost to cross it — `prioritisation.md`'s Reach and Consequence dimensions are built for exactly this case, fed by the representative maintenance-task trace (see `context-scattering`, `excessive-module-surface` in `smells/structural.md`).

Select only candidates that pass the gate below. Prefer five strong initiatives over twenty weak ones; merge candidates that represent the same underlying pressure rather than reporting one coherent refactor as several.

### Initiative validity gate

Do not include an initiative unless all answers are yes:

```text
[ ] There is concrete repository evidence.
[ ] The affected attributes are explicit, and the attributes under tension are named too.
[ ] The proposed boundary or change has a reason to exist.
[ ] The expected improvement can be observed or measured.
[ ] The initiative is behaviour-preserving or clearly classified otherwise.
[ ] The recommendation is more specific than "clean this up".
[ ] The recommendation does not depend only on a generic best practice.
```

If fewer than three initiatives pass this gate, report fewer than three. Do not fill the report with low-confidence observations.

## Step 5: Identify the required safety net

For each initiative selected in Step 4 — scoped to the specific capability it touches, not the codebase in general — determine what currently protects that capability from regression: look for black-box acceptance tests, component tests, contract tests, package-consumer tests, integration tests against real infrastructure, isolated domain tests, and architecture or static-analysis tests. Coverage is evidence of execution, not evidence of correctness — do not optimise for line coverage alone.

Apply Principle 4: state which tests must remain unchanged, which additional characterisation tests are needed before the movement, and which cases may descend to a smaller component. If what you find here is itself `mock-architecture`, `insufficient-behavioural-coverage`, or `test-fidelity-mismatch` (`smells/testability.md`), note it — but the initiative's safety net still needs stating either way.

## Step 6: Report

Return every initiative selected in Step 4, with its Step 5 safety net, using the template in `output-template.md`.
