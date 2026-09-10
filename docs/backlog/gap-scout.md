# Scheduling gap-scout with systemd

Paths below are relative to the factory bundle; run `bin/gap-scout` from the root of the
repo being worked on.

`bin/gap-scout scan <scope|all>` dispatches one `claude` session per scope into its
own window inside the tmux session (the same one `bin/backlog run` uses),
creating that session if it doesn't exist yet, and returns immediately — it doesn't wait for
the session(s) to finish. That makes it safe and cheap to call from a systemd timer. A
finished window stays around (doesn't auto-close) so you can inspect what it found even
after it's done — useful if filing an issue failed partway through.

Scopes, and what `all` expands to, come from `GAP_SCOUT_SCOPES` in `.ne2-factory.env`.

Without `--yolo`, a command outside the narrow allow-list (only `gh issue`/`gh label`)
pauses for approval in its tmux window instead of hanging with no one able to answer —
attach and answer it, same as any other prompt from `bin/backlog`'s worker. Expect
this fairly often: the scout runs a lot of ad-hoc shell pipelines (churn analysis, grep/sed
chains) that a narrow allow-list can't realistically cover.

`scan <scope|all> --yolo` runs `claude --dangerously-skip-permissions` instead — no
prompts at all, so nothing ever needs answering or gets forwarded to your phone. That's what
makes it safe to leave running unattended from a timer while still landing in tmux for you
to check on later. Reasonable specifically for this agent (unlike `work-ticket`'s `--yolo`,
which also skips the commit/push approval): `architecture-gap-scout` never touches git or
edits code, so the only thing being unblocked is shell access to a read-only inspection, not
a write path. It still means no technical safety net if something in the repo's content
tried to steer the agent.

## 1. Service unit

`/etc/systemd/system/gap-scout.service`:

```ini
[Unit]
Description=Dispatch an architecture-gap-scout scan into tmux
After=network-online.target

[Service]
Type=oneshot
User=<user>
WorkingDirectory=/path/to/your-repo
ExecStart=/path/to/your-repo/.claude/skills/ne2-factory/bin/gap-scout scan all --yolo
# needed if claude/gh/tmux aren't on systemd's default PATH:
Environment=PATH=/usr/local/bin:/usr/bin:/bin:/home/<user>/.local/bin
# without this, systemd's default KillMode=control-group kills everything left in this
# unit's cgroup once ExecStart exits — including the tmux server the script just spawned,
# even though it detached. process-only killing leaves it (and the claude sessions inside)
# running after the oneshot is done.
KillMode=process
```

`ExecStart` points at wherever the factory bundle is installed under the repo. It only
*dispatches* the tmux windows and exits — it doesn't run the scan itself, so this unit
finishes in a second or two regardless of how long the scan takes.

## 2. Timer unit

`/etc/systemd/system/gap-scout.timer`:

```ini
[Unit]
Description=Run gap-scout.service weekly

[Timer]
OnCalendar=Mon 06:00
Persistent=true
RandomizedDelaySec=30m

[Install]
WantedBy=timers.target
```

## 3. Enable

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now gap-scout.timer
systemctl list-timers gap-scout.timer   # next run
```

## 4. Run once without waiting for the timer

```bash
sudo systemctl start gap-scout.service   # returns immediately, doesn't block
tmux attach -t backlog                   # watch it, or check in later (session name = TMUX_SESSION)
```

If a scan for a given scope is already running when the timer fires again, that scope is
skipped (logged, not queued) rather than starting a second overlapping session — check
`journalctl -u gap-scout.service` if a scan seems to never get picked up.

## Prerequisites

* `gh auth status` already authenticated as the service's `User` (or `GH_TOKEN` set in
  `Environment`) — needs write access to issues/labels on the repo (`repo` scope for a
  classic PAT, or "Issues: Read and write" for a fine-grained one).
* `claude` on `PATH` and already logged in (or `ANTHROPIC_API_KEY` in `Environment`).
* `tmux` installed.

See [`README.md`](README.md) for how filed issues get from proposal to approved ticket.
