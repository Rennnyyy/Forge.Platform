# Trunk 6 — ADR housekeeping & slice listings

- **Owner**: Architect / docs agent
- **Prerequisites**: Trunks 1, 2, 3 merged (4 and 5 not strictly required for this
  housekeeping but ideally also merged for consistency)
- **ADRs**: edits only — no new ADR

## Goal

Tie up cross-references after the Aspects slice has shipped:

1. Mark Entity ADR-0007 as superseded for its runtime-enforcement aspect.
2. Add `Forge.Entity.Aspects` to slice listings in user-facing docs and the `architect`
   prompt's slice table.
3. Verify there is no dangling reference to the slice in places we expect it.

## Scope

Documentation / metadata only. No production code changes.

## Deliverables

### Update [src/Entity/adr/0007-required-is-metadata-only.md](../../src/Entity/adr/0007-required-is-metadata-only.md)

Change the Status line:

```diff
-- **Status**: accepted
++ **Status**: superseded by Forge.Entity.Aspects/adr/0001-split-shape-validation.md
```

Add a one-paragraph note at the top (above `## Context`) explaining that the
"metadata-only" stance applied until the Aspects slice landed; the attribute itself
remains unchanged but its runtime enforcement is now the implicit-`[Required]` Local
shape synthesized at startup by the Aspects engine.

This matches the supersession protocol in [adr/0003-adr-policy.md](../../adr/0003-adr-policy.md)
§Rules#2.

### Update [.github/prompts/architect.prompt.md](../../.github/prompts/architect.prompt.md)

The slice table in §2.2 currently lists Entity, Entity.Generators, Entity.Tests, and
Entity.Generators.Tests. Add rows (and similar for the prompt's "Scan src/ and tests/"
guidance) for:

- `src/Entity.Aspects/` — `Forge.Entity.Aspects` SHACL validation engine + `Aspect`
  entity (Code + Repository origin).
- `tests/Entity.Aspects.Tests/` — behavioural tests for the Aspects slice.

### Verify cross-references

Grep for stale text:

```sh
grep -rn "ADR-0007" src/ adr/ spec/
grep -rn "metadata-only" src/ adr/ spec/
```

Ensure each occurrence either references the supersession or is itself the supersession
text.

### Optional: update [src/Entity.Operations/adr/README.md](../../src/Entity.Operations/adr/README.md)

The Operations ADR README index already includes ADR-0005 from Trunk 1; verify the
listing and link are correct.

## Acceptance criteria

- ADR-0007's Status line carries the supersession reference.
- The architect prompt's slice table includes both new projects.
- Grepping for `metadata-only for now` only finds it inside ADR-0007 itself (which
  retains the original prose for historical accuracy) and in references that
  acknowledge the supersession.
- No new ADR file is created (per platform ADR-0003 §Rules — supersession is recorded
  in-status, plus the new ADR that does the superseding).

## Out of scope

- Authoring an `architect` prompt section about Aspects beyond the slice-table addition.
- README rewrites — the project README is intentionally minimal.

## Suggested invocation

> `/Forge-Developer Implement Trunk 6 per spec/aspects-v1/trunk-06-housekeeping.md.`
