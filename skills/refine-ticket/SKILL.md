---
name: refine-ticket
description: "Interactive session that refines one backlog ticket, given its full context in the prompt, via the refiner agent, so ne2-factory backlog only ever picks up implementation-ready tickets."
disable-model-invocation: true
model: haiku
---

## Goal

Turn one backlog ticket into an implementation-ready one before `ne2-factory backlog run`
picks it up. This is a deliberate, interactive session the reviewer runs themselves — it's
the only place in the pipeline where clarifying questions are asked live, keeping
interactive refinement and unattended implementation separate. "The reviewer" is the person
named in `.ne2-factory/project.md`.

`ne2-factory backlog refine` drives this skill, one ticket per fresh `claude` session: it
picks the next unrefined ticket, fetches its number, title, body, comments, and URL, and
gives you all of that in the prompt — this skill never fetches anything from GitHub itself.

## Workflow

### 1. Read the ticket

The prompt gives you the ticket's number, title, body, comments, and URL already. Read them
per the `read-github-ticket` skill's guidance on treating comments as part of the ticket and
resolving any embedded images before proceeding — screenshots often resolve ambiguity the
text alone can't.

### 2. Refine it

Spawn a `refiner` agent with the ticket's number, title, body, comments, and URL exactly as
given to you. It asks you questions directly as needed — answer them as they come. Wait for
it to finish.

### 3. Report

Summarize what was clarified or decided. The ticket is now labeled `refined` and will be
picked up by `ne2-factory backlog run` on its next pass.

## Constraints

- Never implement anything here — this session only clarifies and records decisions on
  the ticket.
- Never skip a ticket's questions to save time; unresolved ambiguity is exactly what this
  session exists to remove before unattended implementation.
- Never fetch the ticket or any other issue from GitHub yourself — the ticket you're given
  in the prompt is the only one in scope; if it's missing something you need, say so rather
  than reaching for `gh`.
