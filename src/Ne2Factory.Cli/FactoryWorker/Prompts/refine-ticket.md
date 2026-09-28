Ticket: {{TICKET_REFERENCE}}

{{TITLE}}

{{BODY}}

{{COMMENTS}}

---

This session is headless and unattended: there is no live reviewer to answer questions, so
never ask one — resolve everything inferable and report any remaining genuine ambiguity as
missing data instead of guessing.

Your final report is parsed programmatically:
- `refinement_summary`: the implementation-ready shape of the ticket — original intent plus
  every finding and decision made along the way.
- `refinement_outcome`: `ready` or `missing_data`.
- `questions`: the open questions, if any; omit it (or leave it an empty array) when the
  outcome is `ready` or there's nothing left open.
