---
name: read-github-ticket
description: "How to read a GitHub issue as ticket context in this repo's backlog pipeline: treating comments as part of the ticket, and resolving embedded images."
model: haiku
---

## Goal

Turn a GitHub issue into full context for acting on it, without missing what later
comments or embedded screenshots added.

## Fetching

```bash
gh issue view <n> --json title,body,url,comments
```

Format each comment as a block, oldest first, so it reads the same way whether it
arrives pre-formatted in a prompt or is fetched directly:

```bash
jq -r '.comments[]? | "-- comment by \(.author.login) (\(.createdAt)) --\n\(.body)"'
```

## Comments are part of the ticket

Later comments can narrow, correct, or override the original issue body — treat them as
part of the ticket, not optional extra context. Read them in order; the most recent take
on a point wins.

## Resolve embedded images

If the ticket body or any comment contains GitHub-hosted image URLs (e.g.
`![...](https://github.com/user-attachments/...)` or other GitHub-hosted image links),
download and view each one before proceeding — the text alone may not convey what a
screenshot shows:

```bash
mkdir -p /tmp/ticket-images && curl -sL "<url>" -o /tmp/ticket-images/<n>.png
```

Then `Read` the downloaded file. If a download fails (private attachment, expired URL),
note it and continue with the text you do have rather than blocking on it.
