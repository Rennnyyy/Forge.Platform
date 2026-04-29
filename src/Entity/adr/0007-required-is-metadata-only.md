# 0007 — `[Required]` is metadata-only for now

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

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
