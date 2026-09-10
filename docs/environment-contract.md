# ne2-factory environment contract

The interface between `ne2-factory` and the repo it works on. Factory agents own
outcomes; the project owns how it builds, runs, tests, records changes. This is the
seam: a small set of **capabilities** a project implements so the same agents work
against any repo.

Not a skill, not an agent — a spec the factory programs against.

## Providing a capability

A capability is a skill of that exact name in the consuming repo's
`.claude/skills/<name>/`. Its internals (commands, ports, suites) are the project's
business and never named by the factory.

Semantic context the agents reason about (reviewer, default branch, later: services,
doc paths) goes in `.ne2-factory/project.md`. Values only a shell script reads go in
`.ne2-factory.env`.

## Capabilities

| Capability | Status | Missing means |
|---|---|---|
| `run` | required | repo can't be driven by the factory |
| `verify` | required | repo can't be driven by the factory |
| `update-changelog` | optional | changelog step skipped, not failed |

### `run`

- **Purpose.** One deterministic local environment, plus a report of what's up.
- **In.** Request for a live environment, optionally narrowed to a surface. No
  commands or ports passed.
- **Out.** Running environment + summary: primary URL, other URLs, how to auth as a
  test identity, logs, teardown.
- **Guarantees.** Reproducible. Brings the surface to usable (not just an open port)
  or says why not. Touches only project-owned processes. Never falls back to "start it
  by hand".

### `verify`

- **Purpose.** Owns what counts as evidence a diff is correct.
- **In.** A diff + one or two sentences of intent.
- **Out.** The diff's risks, and per risk the narrowest check: which suite/command,
  whether a test must be added, when to escalate to real infra.
- **Guarantees.** Risk-based — every changed observable behavior maps to evidence;
  "looks correct" is not evidence. Minimal set. Escalation conditions explicit.

### `update-changelog`

- **Purpose.** Human-facing record of user-visible changes, on the project's terms.
- **In.** The change about to be committed.
- **Out.** An entry in the project's format/location, or "nothing to record".
- **Guarantees.** Project decides what's user-visible and how it's recorded.
