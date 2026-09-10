# Agent System Design Constitution

## Purpose

This document defines the design principles for our agent system.

Its purpose is to provide a stable baseline for:

* deciding whether something should be an **agent**, **skill**, or **orchestration workflow**;
* designing new agents and skills consistently;
* reviewing existing agents and skills;
* detecting responsibilities that are misplaced;
* preventing monolithic skills and overly generic agents;
* defining clean boundaries and handoffs between autonomous units of work.

The goal is not to maximize the number of agents or skills.

The goal is to create a system where **responsibility, knowledge, execution, and coordination have clear boundaries**.

---

# 1. Core model

The system has three main conceptual building blocks:

> **Agent = owns an outcome.**
> **Skill = knows how to do something.**
> **Orchestration = coordinates who does what and when.**

A useful shorthand is:

> **Agent → "Achieve X."**
> **Skill → "This is how to do X."**
> **Orchestration → "To complete this process, do X, then Y, then Z."**

These categories describe **responsibilities**, not necessarily different technical primitives.

In Claude Code, orchestration may be implemented as a skill invoked by the user and executed by the main Claude instance.

---

# 2. Agents

## Definition

An agent is the **owner of an outcome**.

An agent receives a goal and has enough autonomy to determine how to achieve it within its authority and constraints.

The caller should generally describe **what needs to be achieved**, rather than every step required to achieve it.

For example:

> Determine whether this change is safe to accept.

is an agent goal.

Whereas:

> Run `dotnet test`, then Playwright, then check coverage.

is a procedure.

---

## The goal test

The simplest test for identifying an agent is:

> **Can I give this thing a goal and make it responsible for achieving that goal?**

If yes, it is probably an agent.

Examples:

### Verifier

> Determine whether this change has been sufficiently verified.

The Verifier decides:

* what changed;
* what risks exist;
* which verification mechanisms are relevant;
* which checks need to run;
* how to interpret failures;
* whether evidence is sufficient;
* whether the result is PASS, FAIL, or INCONCLUSIVE.

This is an agent.

### Architecture Gap Scout

> Find architectural problems worth addressing.

The Scout decides:

* where to inspect;
* which signals matter;
* which metrics or techniques to use;
* whether something is actually problematic;
* how important the finding is;
* whether enough evidence exists to report it.

This is an agent.

### Bug Fixer

> Resolve this reported defect and provide a change that fixes its root cause.

The Bug Fixer may:

1. understand the report;
2. reproduce the bug;
3. investigate;
4. form hypotheses;
5. identify the root cause;
6. implement a fix;
7. add regression coverage;
8. produce a handoff for verification.

This is an agent.

The presence of a workflow does **not** make it a skill.

The important property is that the Bug Fixer owns the outcome.

---

# 3. Agent autonomy

Agents exist because the path from input to outcome is not completely predetermined.

An agent may need to:

* inspect context;
* gather evidence;
* choose a strategy;
* invoke different skills;
* react to unexpected results;
* revise hypotheses;
* make decisions within its authority;
* determine whether its goal has been achieved;
* declare itself blocked when it cannot safely continue.

This uncertainty is an important signal.

Compare:

> Run the affected tests.

with:

> Determine whether this change has sufficient test coverage and verification.

The first is procedural.

The second requires judgement.

---

# 4. Agent authority

Different agents may have different permissions and decision rights.

This is an important reason to create separate agents.

For example:

## Refiner

May:

* inspect the repository;
* investigate existing behaviour;
* challenge assumptions;
* ask the user questions;
* propose alternatives;
* clarify requirements;
* update the ticket.

May not:

* implement the ticket.

## Implementer

May:

* inspect the repository;
* modify production code;
* modify tests;
* use implementation skills.

May not:

* redefine product requirements;
* silently resolve meaningful requirement ambiguity;
* ask product questions during autonomous implementation.

If meaningful ambiguity prevents implementation, it should return:

`BLOCKED: REQUIREMENT_AMBIGUITY`

This separation allows interactive work to happen deliberately during refinement while keeping implementation autonomous.

A useful principle is:

> **Differences in authority are often a reason to create different agents.**

---

# 5. Agent internal workflows

An agent **may absolutely contain a workflow**.

For example:

## Bug Fixer internal workflow

1. Understand the defect.
2. Establish a reproduction.
3. Diagnose the root cause.
4. Design the smallest appropriate fix.
5. Implement it.
6. Add regression protection.
7. Produce a change handoff.

This workflow belongs to the Bug Fixer because it describes how that agent normally pursues its mission.

Therefore:

> **Having steps does not distinguish a skill from an agent.**

The distinction remains:

> Who owns the outcome?

---

# 6. What belongs in an agent definition

An agent definition should normally contain:

## Role

Who the agent is.

Example:

> You are the Verification Agent.

## Mission

The outcome it owns.

Example:

> Determine whether a proposed code change has been sufficiently verified to be accepted.

The mission should describe an outcome, not a sequence of commands.

## Responsibilities

What falls under the agent's ownership.

Example:

* understand the scope of the change;
* identify relevant risks;
* choose an appropriate verification strategy;
* evaluate verification evidence;
* produce a verdict.

## Authority

What decisions and actions the agent is allowed to perform.

## Boundaries

What the agent must not do.

Example:

* do not implement product changes;
* do not silently fix failing tests;
* do not redefine requirements;
* do not claim PASS when required verification could not be performed.

## Available capabilities

Which skills are relevant or available to the agent.

The agent definition should not duplicate the full contents of those skills.

## Internal workflow

The high-level strategy the agent normally follows.

This should describe meaningful phases, not every shell command.

## Decision policies

Rules governing judgement where necessary.

Example:

> Prefer evidence from executable verification over reasoning from code inspection alone.

Or:

> Report architectural findings only when there is concrete evidence of current or likely engineering cost.

## Input contract

What information the agent expects to receive.

## Completion criteria

What must be true before the agent can consider its mission complete.

## Output / handoff contract

What structured information the agent returns.

---

# 7. Agent system prompt template

A useful baseline is:

```text
# Role

You are the [name] Agent.

# Mission

[Outcome this agent owns.]

# Responsibilities

- ...
- ...

# Authority

You may:
- ...
- ...

# Boundaries

You must not:
- ...
- ...

# Available capabilities

Use these skills when appropriate:
- ...
- ...

# Workflow

1. ...
2. ...
3. ...

Adapt the workflow when evidence requires it.

# Decision policies

- ...
- ...

# Input contract

You receive:
- ...
- ...

# Completion criteria

The mission is complete when:
- ...
- ...

# Output / handoff contract

Return:
- ...
- ...
```

Not every agent needs every section.

The purpose is clarity, not template compliance.

---

# 8. What should NOT live in the agent prompt

Avoid embedding large procedural knowledge directly into the agent.

For example:

```text
To calculate namespace instability:

1. Run...
2. Parse...
3. Exclude...
4. Calculate...
```

If this is a reusable technique, it belongs in a skill.

Similarly, avoid embedding the entire system workflow:

```text
After finishing:
run Verifier,
then update GitHub,
then changelog,
then commit,
then...
```

The agent should know its own mission and handoff contract.

It should not need to understand the entire pipeline around it.

---

# 9. Skills

## Definition

A skill encapsulates **reusable knowledge or procedure for performing a capability**.

A skill answers:

> **How do we do this correctly in this environment?**

Examples:

* run the application;
* reproduce a bug;
* execute affected tests;
* calculate architecture metrics;
* update a GitHub Issue;
* update the changelog;
* create a commit;
* inspect dependency structure.

Skills do not need to own a broader outcome.

---

# 10. The procedure test

Ask:

> **Does this primarily explain how to perform something?**

If yes, it is probably a skill.

Examples:

### Run

> How to start and interact with the project's runtime environment.

Skill.

### Reproduce Bug

> How to establish a reliable reproduction of a reported defect.

Skill.

### Commit

> How commits should be prepared and created in this repository.

Skill.

### Update Changelog

> How to determine whether a changelog entry is required and how it should be written.

Skill.

### GitHub Ticket Progress

> How workflow states and progress should be represented in GitHub Issues.

Skill.

---

# 11. Skills should be reusable

Ideally, a skill should know as little as possible about the agent currently using it.

For example:

`reproduce-bug`

should not say:

> When the Bug Fixer calls you...

It should describe how bug reproduction works.

Then it may be reused by:

* Bug Fixer;
* Refiner;
* Verifier;
* Architecture Scout;
* the main Claude session.

This reduces coupling between agents and capabilities.

---

# 12. Skills may have steps

A skill may contain a substantial procedure.

For example:

```text
# Reproduce Bug

1. Understand the reported behaviour.
2. Identify the smallest reproduction surface.
3. Start the required environment.
4. Establish current behaviour.
5. Reduce the reproduction where possible.
6. Capture deterministic evidence.
7. Report reproduction conditions.
```

That does not make it an agent.

It remains a skill because its responsibility is:

> **know how to reproduce a bug**

rather than:

> **own the resolution of this bug**.

---

# 13. Skills may use other capabilities

A skill may depend conceptually on other capabilities.

For example:

```text
reproduce-bug
    ↓
requires running application
    ↓
run
```

However, avoid excessive skill decomposition.

Do not turn the skill system into:

```text
fix
 → reproduce
    → run
       → docker
          → shell
             → process
```

Create a separate skill when the capability is:

* independently meaningful;
* reusable;
* complex enough to deserve documentation;
* likely to evolve independently.

Not merely because it can technically be separated.

---

# 14. Capability skills vs orchestration skills

Although both may technically be represented as Claude Code skills, they serve different conceptual purposes.

## Capability skill

Provides reusable knowledge.

Examples:

```text
run
verify
reproduce-bug
commit
update-changelog
github-ticket-progress
```

These are primarily **agent-facing/model-facing**.

The model selects them when they are useful for achieving a goal.

## Orchestration skill

Defines a high-level workflow.

Example:

```text
work-ticket
```

This may coordinate:

```text
read ticket
    ↓
Refiner
    ↓
Implementer / Bug Fixer
    ↓
Verifier
    ↓
update changelog
    ↓
commit
    ↓
update GitHub ticket
```

These are often **human-facing entry points**.

For example:

```text
/work-ticket #123
```

starts the process.

This distinction is a design convention rather than a fundamental Claude Code primitive.

---

# 15. Orchestration

## Definition

Orchestration defines:

> **Who does what, and when?**

It coordinates agents and capabilities to complete a larger process.

Example:

```text
Human
  ↓
/work-ticket #123
  ↓
Refiner
  ↓
Implementer
  ↓
Verifier
  ↓
Delivery operations
```

No individual worker needs to own this entire process.

---

# 16. Internal workflow vs system workflow

This distinction is critical.

## Internal agent workflow

Belongs to an agent.

Example:

```text
Bug Fixer

reproduce
   ↓
diagnose
   ↓
implement
   ↓
regression coverage
   ↓
handoff
```

This describes how the Bug Fixer pursues its own outcome.

## System workflow

Coordinates several independent responsibilities.

Example:

```text
Refiner
   ↓
Bug Fixer
   ↓
Verifier
   ↓
Commit
```

This belongs to orchestration.

A useful rule:

> **An agent owns its workflow, but should not own the entire workflow surrounding it.**

---

# 17. Handoffs

Agents communicate through handoffs.

A handoff represents the contract between the producer of an outcome and its consumer.

Conceptually, treat handoffs like APIs between agents.

Example:

```text
Bug Fixer
    │
    ▼
ChangeHandoff
{
    goal
    implementation_summary
    changed_files
    regression_tests
    known_risks
}
    │
    ▼
Verifier
```

The Bug Fixer owns producing a valid change.

The Verifier owns deciding whether that change is sufficiently verified.

Neither needs to know the other's internal implementation.

---

# 18. Input and output contracts

An agent's input and output are part of its boundary.

For example:

## Verifier input

```text
ChangeHandoff
- goal
- implementation summary
- changed files
- known risks
- verification hints
```

## Verifier output

```text
VerificationReport
- status: PASS | FAIL | INCONCLUSIVE
- checks performed
- checks skipped
- failures
- evidence
- residual risks
```

Structured handoffs make agent composition predictable.

---

# 19. Where reporting formats belong

A common smell is a skill containing:

> Report back using exactly this format...

Whether this is misplaced depends on **why that format exists**.

If the format exists because another agent or workflow consumes the result, it belongs to the **handoff contract**, not the skill.

For example:

```text
Summary
Evidence
Impact
Recommendation
Confidence
```

If that structure exists specifically so the Architecture Scout can report findings to another component, it belongs to the Scout's output contract.

However, a skill may define the structure of an artifact when that structure is intrinsic to the technique itself.

For example, an ADR skill may define:

```text
Context
Decision
Consequences
```

because that structure is part of producing an ADR correctly.

The rule is:

> **Technique requires this structure → Skill.**

> **Consumer requires this structure → Handoff contract.**

---

# 20. Human interaction and refinement

Human interaction should be deliberate rather than available everywhere.

A dedicated Refiner Agent can own:

> Turn an ambiguous request into an implementation-ready ticket.

Its workflow may involve:

```text
inspect
   ↓
investigate
   ↓
identify ambiguity
   ↓
ask human
   ↓
challenge assumptions
   ↓
clarify
   ↓
produce implementation-ready ticket
```

The Implementer can then operate under a stricter contract:

> Implement the agreed ticket without redefining requirements.

If material ambiguity remains:

```text
BLOCKED
reason: REQUIREMENT_AMBIGUITY
question: ...
```

This creates an important system invariant:

> **Interactive clarification happens during refinement. Autonomous execution happens after refinement.**

If an Implementer frequently becomes blocked by requirement ambiguity, that is evidence that the refinement process needs improvement.

---

# 21. GitHub Issues and progress

Updating GitHub should generally be a reusable capability rather than embedded independently into every agent.

For example:

```text
github-ticket-progress
```

may define how to represent:

* IN PROGRESS;
* BLOCKED;
* READY FOR VERIFICATION;
* DONE;
* relevant implementation notes;
* verification evidence.

The skill should know:

> **How is this state represented in GitHub?**

The agent or workflow should decide:

> **What state are we actually in?**

This keeps policy and mechanism separate.

---

# 22. Recommended conceptual architecture

A useful mental model is:

```text
                    HUMAN
                      │
                      ▼
                  WORKFLOWS
                /work-ticket
                      │
          ┌───────────┼───────────┐
          ▼           ▼           ▼
       Refiner    Implementer   Bug Fixer
                                  │
                                  ▼
                               Verifier

                  AGENTS
          own goals / outcomes
                      │
                      ▼
                   SKILLS
          reusable capabilities
                      │
                      ▼
              execution mechanisms
```

Or, more compactly:

```text
Human
  ↓
Workflow
  ↓
Agents
  ↓
Skills
```

This is a conceptual model, not a rigid dependency graph.

---

# 23. Current candidate architecture

Based on the current system, the following decomposition is a reasonable direction.

## Agents

### Refiner

**Goal:** make work implementation-ready.

### Implementer

**Goal:** implement an agreed change correctly.

### Bug Fixer

**Goal:** resolve a reported defect and address its root cause.

### Verifier

**Goal:** determine whether a change has been sufficiently verified.

### Architecture Gap Scout

**Goal:** discover architectural problems worth addressing.

---

## Orchestration workflows

### Work Ticket

Coordinates the lifecycle of a ticket.

Potentially:

```text
load ticket
   ↓
refine if necessary
   ↓
mark IN PROGRESS
   ↓
select appropriate implementation agent
   ↓
implement
   ↓
verify
   ↓
handle verification failure if necessary
   ↓
update changelog
   ↓
commit
   ↓
update ticket
```

---

## Capability skills

Potential examples:

```text
run
verify
reproduce-bug
debug
find-architecture-gaps
update-github-ticket
update-changelog
commit
```

These should only be split further when there is demonstrated value in doing so.

---

# 24. Decision tree

When creating or reviewing something, ask:

## Question 1

**Does it own an outcome that I can express as a goal?**

Example:

> Resolve this bug.

Yes → probably an **Agent**.

If no:

## Question 2

**Does it describe how to perform a reusable capability?**

Example:

> How to reproduce a bug.

Yes → probably a **Skill**.

If no:

## Question 3

**Does it primarily coordinate several responsibilities in order to complete a larger process?**

Example:

> Refine → Implement → Verify → Commit.

Yes → probably **Orchestration**.

---

# 25. Design smells

## Monolithic skill

A skill:

* receives a broad goal;
* investigates freely;
* chooses strategy;
* coordinates multiple capabilities;
* decides when the overall mission is complete;
* owns a meaningful outcome.

It is probably an agent disguised as a skill.

---

## Procedural agent

An agent definition mostly contains:

```text
run command A
run command B
parse C
write D
```

with little autonomy or judgement.

It is probably a skill disguised as an agent.

---

## Agent knows too much about downstream workflow

Example:

> After completing implementation, invoke Verifier, update GitHub, update changelog, commit, close issue...

This is probably orchestration leaking into the agent.

---

## Skill knows its caller

Example:

> When Architecture Scout calls this skill...

This creates unnecessary coupling.

Prefer caller-independent capability definitions.

---

## Handoff hidden inside a skill

Example:

> After completing analysis, report Summary/Evidence/Priority/Recommendation...

when those fields exist because another agent consumes them.

Move that structure to the agent's output contract.

---

## Everyone can ask the human

If every agent can interrupt execution with questions, autonomous workflows become unpredictable.

Human interaction should be an explicit authority.

---

## Agent owns no meaningful decision

If an agent simply executes deterministic instructions, reconsider whether it needs to exist.

---

## Excessive skill decomposition

Avoid creating tiny skills merely because something can technically be extracted.

A skill should represent a meaningful reusable capability.

---

# 26. Dos

## DO define agents around outcomes

Good:

> Determine whether this change is safe to accept.

Not:

> Run the verification scripts.

---

## DO give agents explicit boundaries

Define what they cannot do as carefully as what they can do.

---

## DO separate authority

Use different agents when different phases require meaningfully different permissions or interaction models.

---

## DO make skills reusable

Prefer:

> How to reproduce a bug in this repository.

over:

> How Bug Fixer reproduces bugs.

---

## DO keep procedural detail in skills

Agents should reason about **what needs doing**.

Skills should preserve knowledge about **how we do it here**.

---

## DO make handoffs explicit

Treat agent boundaries like API boundaries.

Inputs and outputs should be understandable without knowing the agent's internal reasoning.

---

## DO define completion criteria

An agent should know what evidence allows it to declare its mission complete.

---

## DO allow agents to adapt their internal workflow

The workflow is a strategy, not necessarily a rigid script.

Unexpected evidence may require iteration.

---

## DO keep orchestration visible

The overall process should be understandable without inspecting every agent.

---

## DO prefer the smallest useful abstraction

Do not create an agent, skill, or handoff merely for architectural purity.

Create boundaries because they provide:

* autonomy;
* reuse;
* isolation;
* clarity;
* testability;
* composability;
* or explicit authority.

---

# 27. Don'ts

## DON'T classify based on number of steps

Both agents and skills may contain multi-step workflows.

Classify based on **ownership and intent**.

---

## DON'T make every complex thing an agent

Complexity alone does not imply agency.

A complicated but deterministic procedure may still be a skill.

---

## DON'T make every reusable thing a skill

An autonomous worker may also be reused.

Reuse is not the defining property.

Outcome ownership is.

---

## DON'T duplicate skill knowledge in agents

Agents should reference capabilities rather than copy their implementation knowledge.

---

## DON'T let skills own system outcomes

A skill should not quietly become responsible for completing an entire business/development workflow.

---

## DON'T make agents responsible for the entire pipeline

A Bug Fixer should fix bugs.

It should not need to understand the complete ticket lifecycle.

---

## DON'T couple skills to specific agents without necessity

Capabilities should normally remain caller-agnostic.

---

## DON'T put consumer-specific output formats in generic skills

Those belong to handoff contracts.

---

## DON'T allow autonomous agents to invent product decisions

Missing product decisions should result in refinement or a BLOCKED outcome, not silent assumptions.

---

## DON'T split prematurely

Start with meaningful capabilities.

Extract smaller skills when actual reuse, complexity, or independent evolution justifies it.

---

# 28. Review checklist

When reviewing an existing agent or skill, ask:

### Ownership

* What outcome does this component own?
* Can that outcome be expressed as a goal?
* Is it actually a procedure rather than an outcome?

### Responsibility

* Is it doing work that belongs to another agent?
* Is it coordinating the entire system unnecessarily?

### Authority

* What decisions may it make?
* Can it ask the human?
* Can it change requirements?
* Can it modify code?
* Can it declare success or failure?

### Skills

* What reusable capabilities does it contain internally?
* Should any be extracted?
* Is extracted knowledge genuinely reusable?

### Workflow

* Is this an internal workflow or system orchestration?
* Does the component need to know what happens before and after it?

### Handoffs

* What does it receive?
* What does it guarantee on completion?
* Who consumes its result?
* Is consumer-specific formatting incorrectly living in a skill?

### Completion

* How does it know it is done?
* What evidence is required?
* What happens when it cannot safely complete the goal?

### Interaction

* Is it allowed to ask the human questions?
* Should it be?
* Would ambiguity be better handled by Refiner?

---

# 29. Final principles

The architecture should optimize for **clear ownership rather than maximum decomposition**.

The fundamental rules are:

> **Agents own outcomes.**

> **Skills encode reusable ways of doing things.**

> **Orchestration coordinates outcomes and capabilities.**

> **Handoffs define contracts between autonomous responsibilities.**

> **Authority belongs to agents and should be explicit.**

> **Interactive clarification and autonomous implementation should be intentionally separated.**

> **Internal workflows belong to the agent that owns the outcome.**

> **System workflows belong to orchestration.**

> **Skills should not know more about the surrounding system than necessary.**

> **Do not create abstractions until they establish a meaningful boundary.**

When classification is unclear, ask one question first:

> **Am I telling something what outcome it owns, or am I teaching it how to do something?**

The answer will usually reveal whether it should be an **agent** or a **skill**.
