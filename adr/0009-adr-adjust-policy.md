# 0009 — ADR adjustment policy: inline notes for identifier-only changes

- **Status**: accepted
- **Date**: 2026-05-03
- **Author**: agent

## Context

ADR-0003 states that ADRs are append-only and that changing a prior decision requires a
new ADR that marks the old one `superseded by NNNN`. This works well when the underlying
decision or its consequences change. It is over-heavy when a later decision does nothing
more than rename an identifier that appears in earlier ADRs — the decision rationale and
consequences remain entirely valid; only a type name has changed.

Marking such ADRs `superseded` misleads readers: they navigate from the "current"
decision back through a chain of superseded files to find text that is identical except
for a name, creating noise without insight.

## Options

1. **Introduce an "adjust" action.** When a later ADR changes only an identifier that
   appears in earlier ADRs but does not alter the decision or its consequences, the
   earlier ADRs are **adjusted**: a short inline note is appended,
   e.g. `> *Identifier `Foo` renamed to `Bar` due to ADR-NNNN.*`
   The earlier ADR status stays `accepted`. The body is not rewritten.
2. **Keep superseded for all changes.** Consistent; no special case. Con: reader must
   follow a chain of "superseded" stubs to recover context that never changed.
3. **Rewrite the old ADR body in-place.** Keeps a single file but destroys audit
   history — defeats the purpose of append-only records.

## Decision

Option 1.

### Rules

- An adjustment note is only valid when the decision rationale and consequences in the
  earlier ADR are still accurate with only the identifier name substituted.
- The note is appended at the end of the file, formatted as a blockquote:
  `> *\`OldName\` renamed to \`NewName\` due to ADR-NNNN.*`
- The earlier ADR's `Status` remains `accepted` — it is not superseded.
- If the body of the earlier ADR would be misleading after the identifier change (e.g.
  an Options table comparing alternatives now lost to history), a full new ADR
  supersedes it instead.
- The adjusting ADR must list every file it adjusts in its Changes table.

## Consequences

- ADR chains stay short; readers are not bounced through superseded stubs to find
  unchanged content.
- The audit trail is preserved: the note records exactly when and why the name changed.
- The rule is narrow: content changes always require a superseding ADR per ADR-0003.
