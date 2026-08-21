# Prioritisation model

Two questions look similar but are not the same:

* **How much does this problem matter, architecturally?** — answered by **Architectural importance**.
* **How would we approach fixing it, and how confident are we in the diagnosis?** — answered by **Confidence**, **Effort**, and **Regression risk**.

Rank initiatives by Architectural importance. The other three are decision support for *how* to tackle a selected initiative — never a divisor, and never a reason to drop a high-importance initiative to the bottom of the list because it is hard. A deep, expensive fix and a shallow, cheap one must be able to both score high on importance; the difference between them belongs in the Effort and Regression risk fields, where a reader can see it and reason about sequencing, not baked into a single number that quietly favours whichever one was cheaper to do.

## Prefer the structural cause over its symptoms

Before scoring, check whether several confirmed candidates are plausibly explained by the same underlying architectural gap — a missing boundary, a misplaced responsibility, an absent concept. When they are:

* investigate and score the structural cause as one initiative;
* do not also report the individual symptoms as separate initiatives competing for a rank;
* attach each symptom to the structural initiative's Evidence instead, as one of the concrete manifestations that motivate it.

Duplicated DTO mapping, N+1 queries, and query DTOs reaching into domain navigation properties are usually not three initiatives — they are three pieces of evidence for one: the read path has no boundary of its own. Report the boundary. A local smell earns its own initiative when it is not materially explained by a broader gap already in the list, or when no broader gap is evidenced yet.

This matters more than any weighting choice below. A prioritisation model can only rank the initiatives it's given; if the list is symptoms instead of causes, no formula recovers the loss.

## Architectural importance

Score four dimensions from 1 to 5.

| Dimension | 1 | 3 | 5 |
|---|---|---|---|
| Consequence | cosmetic or local — naming, small duplication, ceremony | maintainability cost or change amplification — scattered transformations, shotgun surgery, unstable dependencies | boundary/ownership erosion, or correctness, data integrity, concurrency, or resilience risk |
| Reach | one isolated location | one important module or workflow | multiple features, teams, or future changes |
| Frequency | rare path | recurring area | central or frequently changed behaviour |
| Structural leverage | fixes one occurrence | improves a reusable component or pattern, or fixes several similar occurrences at once | repairs a boundary or systemic design problem — removes a root cause behind multiple observed symptoms |

```text
Architectural importance = 2 × Consequence + Reach + Frequency + 2 × Structural leverage
```

Range: 6–30. Consequence and Structural leverage are weighted double because they are what distinguishes an architectural gap from a local cleanup: a change that only clears one occurrence, however costly that occurrence is today, is not doing the same kind of work as a change that repairs the boundary generating several occurrences.

Score Consequence by the worst class of damage the current structure plausibly causes, not by how many places exhibit it — reach and frequency already count occurrences. A boundary that is broken in exactly one place today is still a boundary problem.

Score Structural leverage from the relationship between initiatives already surfaced, not in isolation: if this initiative's evidence already appears, in whole or in part, as evidence for another candidate initiative, that is what leverage 4–5 looks like, and the two should probably be merged per the rule above rather than scored as two initiatives with a citation between them.

### Change surface, context cost, and failure ambiguity are evidence, not a score

A representative maintenance-task trace (Step 2 of `SKILL.md`) — how many files a small change touches, how many collaborators must be read to locate behaviour, whether a failure can be attributed to one component — is exactly the kind of concrete observation Consequence and Reach should be scored from. It does not need a parallel mini-score of its own: "adding a field to a photo requires changing 11 files across four layers" is evidence that argues for a specific Reach and Consequence value, stated with the trace itself in the initiative's Evidence section.

## Confidence

Score 1 to 5: how much of the Consequence/Reach/Frequency/Structural leverage judgement rests on demonstrated fact (history, defects, tests, measurements, a completed trace) versus plausible inference from reading the code once.

Confidence is not a multiplier and is never combined into Architectural importance. A correctness risk that is under-investigated is still a correctness risk — reducing its score because it hasn't been fully traced yet would hide the most important gaps behind the least understood ones, exactly backwards from what a scout should do. Report it alongside the score instead: `Importance: 27/30 · Confidence: High (5/5)`. Low confidence is a prompt to investigate further before recommending, or to say plainly that the estimate is provisional — not a way to quietly discount the finding.

## Effort and regression risk

Two more dimensions, scored 1 to 5, kept separate from each other and never summed into a single "difficulty":

| Dimension | 1 | 3 | 5 |
|---|---|---|---|
| Implementation effort | small focused refactor | several coordinated changes | broad migration or high uncertainty |
| Regression risk | well-protected and local | moderate behavioural surface | weakly protected or cross-cutting |

Regression risk is a function of behavioural surface *and* how well that surface is protected, not of effort — a large, mechanical, well-characterised migration can be low-risk and high-effort at once (see Step 5's safety net); a small change to an untested, ambiguous area can be low-effort and high-risk. Keep them legible as two separate numbers so that relationship stays visible instead of cancelling out inside a sum.

Neither dimension gates whether an initiative is reported, and neither divides or discounts Architectural importance. They exist to help decide *how* to sequence and stage an already-selected initiative — e.g. behind characterisation tests first, incrementally, or in one pass — not *whether* it belongs near the top of the list.

## Ranking

Order the report by Architectural importance, descending. Use Confidence, Effort, and Regression risk as the delivery narrative for each initiative, not as re-ranking input.

A lower-importance initiative may still be placed ahead of a higher one when it removes an immediate correctness or security risk, unlocks several higher-importance initiatives, establishes missing tests required for later work, or prevents imminent architectural lock-in. Explain any such manual promotion explicitly in the initiative's `Why now` field — do not silently reorder.
