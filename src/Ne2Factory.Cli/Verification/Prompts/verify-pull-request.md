Verify pull request {{PR_REFERENCE}}.

## Intent

What the change is meant to do:

{{INTENT}}

## Base

`{{BASE_BRANCH}}`, at `{{BASE_COMMIT}}`. The change is the current checkout
(`{{HEAD_COMMIT}}`): diff it from its merge base with `{{BASE_COMMIT}}`.

## Hints from whoever produced the change

The pull request description, written by the agent that implemented the change. Treat it
as known risks and verification hints, not as evidence:

{{PR_DESCRIPTION}}

---

This session is headless and unattended: there is no one to answer questions, so never
ask one.

Your final report is parsed programmatically:
- `status`: `PASS`, `FAIL`, or `INCONCLUSIVE`.
- `report`: your full verification result, in your handoff format, as markdown.
