# ne2-factory

Agentic software factory for Claude Code: agents and skills that work a GitHub
Issues backlog autonomously — refine ambiguous tickets, implement changes or fix
bugs, verify the result, and scout for architecture gaps — leaving a trail on the
issues themselves.

This repo is both a Claude Code **plugin** and its own **marketplace**: it's
consumed directly from GitHub, no npm, no submodules, no install scripts.

See [`docs/environment-contract.md`](docs/environment-contract.md) for the
interface the factory expects from a project (the only part that's genuinely
project-specific), and [`docs/agent-system-design-constitution.md`](docs/agent-system-design-constitution.md)
for how the agent/skill system is designed.

## Install (once per machine)

```bash
claude plugin marketplace add ne2-studio/ne2-factory
claude plugin install ne2-factory@ne2 --scope user
```

This installs the plugin for your user, available in every project you open —
nothing to add to a project's own `.claude/settings.json`.

To pick up changes later:

```bash
claude plugin marketplace update ne2
```

To remove it:

```bash
claude plugin uninstall ne2-factory@ne2
claude plugin marketplace remove ne2
```

## Set up a project to be worked by the factory

The factory depends on a small, stable contract instead of knowing anything about
a specific project (see [`docs/environment-contract.md`](docs/environment-contract.md)).
A consuming repo provides that contract with two files and two-to-three skills.

### 1. `.ne2-factory/project.md`

Prose context the agents reason about — reviewer, default branch, and (as needed)
services and documentation paths. Create it at the root of the project:

```bash
mkdir -p .ne2-factory
cat > .ne2-factory/project.md <<'EOF'
# Project context for ne2-factory

Prose context factory agents reason about. See the ne2-factory plugin's
`docs/environment-contract.md`.

## Reviewer

<name> — who an agent asks when a decision needs a human, and whose approval a
change targets.

## Default branch

`main` — trunk-based, so also the integration branch.

## Services

<Only if the project has more than one independently deployable part; otherwise
drop this section. One bullet per service, and the boundary rule if one exists.>

## Documentation

<Only if there are docs an architectural/cross-cutting change must be read
against first. One bullet per doc, with what it covers.>
EOF
```

### 2. `.ne2-factory.env` (optional)

Shell-only config for the orchestration scripts (`bin/backlog`, `bin/gap-scout`).
Every value has a built-in default — only create this file to override one:

```bash
cat > .ne2-factory.env <<'EOF'
# Shell config for ne2-factory's orchestration scripts (backlog, gap-scout).
# Only bash reads this. Anything an agent reasons about goes in .ne2-factory/project.md.
# Every value has a built-in default; this file only overrides.

# tmux session the backlog worker and gap-scout windows share.
TMUX_SESSION=backlog

# Scopes gap-scout may scan, space-separated. `scan all` expands to this list.
GAP_SCOUT_SCOPES="<space-separated top-level dirs, e.g. app api admin>"
EOF
```

### 3. Required skills: `run` and `verify`

Create `.claude/skills/run/SKILL.md` and `.claude/skills/verify/SKILL.md` in the
project, implementing the `run` and `verify` capabilities described in
[`docs/environment-contract.md`](docs/environment-contract.md) — how *this* project
brings up a live environment and what counts as evidence a diff is correct. Their
internals (commands, ports, test suites) are the project's business; the factory
never names them.

### 4. Optional skill: `update-changelog`

Create `.claude/skills/update-changelog/SKILL.md` if the project keeps a
user-facing changelog. If it's absent, the factory just skips that step instead of
failing.

### Verify the setup

```bash
cd /path/to/the/project
claude
```

Then, in a session, ask a `verifier` (or `implementer`) agent to work a real diff —
it should read `.ne2-factory/project.md` and drive `run`/`verify` without ever
naming a project-specific command.

## Running the backlog worker and gap-scout

From the root of the project being worked on (once the plugin is installed and the
project has the adapter above):

```bash
~/.claude/plugins/marketplaces/ne2/bin/backlog --help
~/.claude/plugins/marketplaces/ne2/bin/gap-scout --help
```

Both read `.ne2-factory.env` for their shell-only config. See
[`docs/backlog/README.md`](docs/backlog/README.md) for the full workflow (queueing
tickets, refining, running unattended, approving gap-scout findings).
