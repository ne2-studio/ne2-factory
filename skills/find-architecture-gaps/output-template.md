# Output template

Return only a prioritized list of initiatives. Do not modify code, create branches or pull requests, include a generic architecture assessment before the list, or report praise, strengths, or minor stylistic suggestions.

Use this exact structure for every initiative, except Why now, which is conditional (see below). Every field must come from the step named next to it — do not fill a field with a plausible-sounding value that step didn't actually produce; write "none identified" instead.

## `<rank>. <initiative title>`

**Type:** `<smell id>` (Step 2)
**Priority:** Critical | High | Medium | Low — a qualitative summary of Architectural importance, not a separate judgment
**Importance:** `<architectural importance>/30` — Consequence `<1-5>` · Reach `<1-5>` · Frequency `<1-5>` · Structural leverage `<1-5>` (Step 4)
**Confidence:** `<High|Medium|Low> (<1-5>/5)` (Step 4)
**Effort:** `<1-5>` · **Regression risk:** `<1-5>` (Step 4)
**Affected area:** `<modules, files, components or capabilities>`

### Evidence (Step 2)

* `<specific repository observation with file or symbol references — including the cost it causes today: what must change together, what tests can't localise, what stays inconsistent. The cost is part of the evidence, not a separate narrative>`
* `<second supporting observation when available>`
* `<history, test, metric or defect evidence when available>`
* `<representative maintenance task trace when available: files opened, searches performed, layers crossed>`

### Tension and movement (Step 3)

**Tension:** `<attribute id>` vs `<attribute id>` — one sentence: why the current structure balances them badly.
**Improves:** `<attribute id>`, `<attribute id>` — one sentence: why a change here would help.
**Movement:** `<name, from the smell's candidate movements, or "reasoned independently" if none fit>` — the smallest coherent change. No implementation walkthrough.
**Costs:** one sentence — what the movement puts under further tension, or the case where it would not yet be justified.

### Safety net (Step 5)

**Currently protects it:** `<existing black-box, component, contract, or integration tests covering this specific capability — or "none identified">`
**Must remain unchanged:** `<...>`
**Needs adding before the movement:** `<characterisation tests, or "none">`
**May descend to a smaller component:** `<cases, or "none">`
**Also surfaces:** `<mock-architecture | insufficient-behavioural-coverage | test-fidelity-mismatch | none>`

### Validation

Restate the Evidence bullets above as their expected state after the fix — the same files, the same counts, the same gaps, now resolved. Do not introduce a metric that wasn't already part of the Evidence or Safety net sections.

### Why now

Include only when overriding the Architectural importance ranking — an immediate correctness or security risk, unlocking several higher-importance initiatives, or preventing imminent architectural lock-in (see `prioritisation.md`). Omit this section when Importance already explains the order.
