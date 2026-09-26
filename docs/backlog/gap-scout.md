# Scheduling gap-scout with systemd

`ne2-factory gap-scout scan <scope|all>` queues one background job per scope and returns
immediately — it doesn't run the scan itself. Those jobs are processed by the job server
hosted inside `ne2-factory run`, so that command has to already be running
(as its own long-lived process) for a queued scan to actually execute. Jobs persist in a
SQLite-backed queue (`.backlog/gap-scout.db`), so a scan queued before the worker is up
still runs once it starts — queueing is safe to call from a systemd timer even if the
worker happens to be down at that moment.

Scopes, and what `all` expands to, come from `GAP_SCOUT_SCOPES` in `.ne2-factory.env`.

`scan <scope|all>` without `--yolo` runs `claude` with a narrow `--allowed-tools` list
(only `gh issue`/`gh label`). A command outside that list pauses for interactive
approval — but a queued job has no attached terminal to answer it on, so it just hangs
until the job's own timeout. Use `--yolo` unless you plan to watch the worker's terminal
live while the scan runs.

`scan <scope|all> --yolo` runs `claude --dangerously-skip-permissions` instead — no
prompts at all, so nothing ever needs answering. That's what makes it safe to leave
running unattended from a timer. Reasonable specifically for this agent (unlike
`work-ticket`'s `--yolo`, which also skips the commit/push approval):
`architecture-gap-scout` never touches git or edits code, so the only thing being
unblocked is shell access to a read-only inspection, not a write path. It still means no
technical safety net if something in the repo's content tried to steer the agent.

## 1. Worker service (long-running)

`ne2-factory run` has to be running continuously for queued gap-scout jobs
(and backlog tickets) to be processed. Run it as its own long-lived systemd service, not a
oneshot:

`/etc/systemd/system/ne2-factory-worker.service`:

```ini
[Unit]
Description=ne2-factory backlog worker and gap-scout job server
After=network-online.target

[Service]
Type=simple
User=<user>
WorkingDirectory=/path/to/your-repo
ExecStart=dotnet run --project /path/to/the/factory/bundle/src/Ne2Factory.Cli -c Release -- run
Restart=on-failure
# needed if claude/gh aren't on systemd's default PATH:
Environment=PATH=/usr/local/bin:/usr/bin:/bin:/home/<user>/.local/bin
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ne2-factory-worker.service
```

## 2. Scan service (oneshot, just queues)

`/etc/systemd/system/gap-scout.service`:

```ini
[Unit]
Description=Queue an architecture-gap-scout scan
After=network-online.target

[Service]
Type=oneshot
User=<user>
WorkingDirectory=/path/to/your-repo
ExecStart=dotnet run --project /path/to/the/factory/bundle/src/Ne2Factory.Cli -c Release -- gap-scout scan all --yolo
Environment=PATH=/usr/local/bin:/usr/bin:/bin:/home/<user>/.local/bin
```

`ExecStart` points at wherever the factory bundle is installed. It only *queues* the
job(s) and exits — it doesn't run the scan itself, so this unit finishes in a second or
two regardless of how long the scan takes (as long as the worker service above is up to
actually process it).

## 3. Timer unit

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

## 4. Enable

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now gap-scout.timer
systemctl list-timers gap-scout.timer   # next run
```

## 5. Run once without waiting for the timer

```bash
sudo systemctl start gap-scout.service        # returns immediately, doesn't block
journalctl -u ne2-factory-worker.service -f   # watch it process the job
```

If a scan for a given scope is already queued or running when the timer fires again,
that scope is skipped (logged, not queued again) rather than starting a second
overlapping job.

## Prerequisites

* `gh auth status` already authenticated as the service's `User` (or `GH_TOKEN` set in
  `Environment`) — needs write access to issues/labels on the repo (`repo` scope for a
  classic PAT, or "Issues: Read and write" for a fine-grained one).
* `claude` on `PATH` and already logged in (or `ANTHROPIC_API_KEY` in `Environment`).
* `dotnet` SDK/runtime on `PATH` — `ne2-factory backlog`/`gap-scout` compile on demand via
  `dotnet run`.

See [`README.md`](README.md) for how filed issues get from proposal to approved ticket.
