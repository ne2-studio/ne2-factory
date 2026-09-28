Ticket: {{TICKET_REFERENCE}}

{{TITLE}}

{{BODY}}

{{COMMENTS}}

---

This session is headless and unattended: there is no live reviewer to answer questions, so
never ask one — resolve everything inferable and report any remaining genuine ambiguity as
missing data instead of guessing.

The only way you can report your outcome back is through your final message, so it is
parsed programmatically. Your very last message must be nothing but a single JSON object —
no markdown code fences, no text before or after it — with this exact shape:
{"refinement_summary": "<implementation-ready shape of the ticket: original intent plus every finding and decision made along the way>", "refinement_outcome": "ready" | "missing_data", "questions": ["<open question>", ...]}

Omit `questions` (or leave it an empty array) when the outcome is `ready` or there's
nothing left open.
