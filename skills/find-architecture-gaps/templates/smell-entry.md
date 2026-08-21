# New smell — table row template

Add one row to the relevant `smells/<category>.md` file:

```markdown
| `smell-id` | Human-readable name | One or two sentences describing the general pattern — not this codebase's instance of it | `attribute-id`, `attribute-id` | Candidate movement; another candidate movement | The one situation most likely to look like this smell but not be worth acting on |
```

* `smell-id` — kebab-case, stable. This is what shows up as `Type:` in a report.
* Attribute ids must exist in `../attributes.md`. Add the attribute there first if it's genuinely missing — don't invent a name inline.
* Candidate movements are names to weigh against evidence, not instructions to follow.

Add a `## Notes` section below the table, referencing the smell by id, only if the false-positive cell genuinely can't hold the nuance — a subtlety about applicability, a distinction from a neighbouring smell, a worked boundary case. Most smells will never need one; see `smells/README.md` for what's expected there.

See `smells/README.md` for the full schema, the design rules for a good smell, and how the scout is expected to use this catalogue.
