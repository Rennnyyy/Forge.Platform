# 0007 — `[Required]` is metadata-only for now

- **Status**: superseded by Forge.Entity.Aspects/adr/0001-split-shape-validation.md
- **Date**: 2026-04-29
- **Author**: bootstrap

> **Supersession note (2026-05-02):** The "metadata-only" stance described below applied until the `Forge.Entity.Aspects` slice landed in Trunk 3. The `[Required]` attribute itself is unchanged — domain authors continue to annotate required predicates with it — but its runtime enforcement is now the responsibility of the Aspects engine, which synthesises an implicit Local shape for every `[Required]` property at startup and evaluates it during the Local validation pass. See `Forge.Entity.Aspects/adr/0001-split-shape-validation.md` for the full validation contract.

## Context

Lazy loading and "required" are in tension: a freshly-constructed entity reference may know only the IRI of its target, with the value not yet resolved. We still want the domain-level statement "this relation must exist" to be expressible.

## Options

1. **`[Required]` is a semantic marker. No runtime checks are wired up. Validation runs later, owned by a persistence/validation slice.**
2. Throw at access time if a `[Required]` value is null. Pro: fails fast. Con: forces eager loads or pre-validation, breaks the lazy story.
3. Use C#'s `required` keyword + non-nullable types. Pro: compiler-enforced. Con: incompatible with constructing-from-IRI-then-loading workflow.

## Decision

Option 1. `RequiredAttribute` exists in `Forge.Entity.Attributes` and is honored by attribute discovery only. No `Validate()` API is published yet.

## Consequences

- Domain authors can mark required attributes / references today and keep the markup forever.
- A future ADR will define the validation contract and the invocation point (likely persistence flush).
- Until then, inspecting `[Required]` is the consumer's responsibility.
