# 0003 — IRI sealed once materialized

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

In RDF the IRI **is** identity. Other entities and external systems hold references by IRI, so silently changing it would corrupt the graph.

## Options

1. **Seal IRI once it has been materialized; throw on any subsequent identity-part mutation or IRI reassignment.**
2. Allow IRI to be regenerated whenever an identity part changes. Pro: mutable models. Con: dangling references everywhere.
3. Keep IRI mutable, leave consistency to the user. Pro: zero mechanism. Con: footgun.

## Decision

Option 1. `EntityBase` exposes:
- `Iri` (get; protected internal set) — first assignment seals; subsequent different assignment throws.
- `IsIdentitySealed` — `true` once an IRI is set.
- `GuardIdentityMutation()` — generated identity-part setters call this; throws if sealed.
- `HydrateIri(string)` — used by the loader to assign the persisted IRI without re-running materialization. Throws if a different IRI is already sealed.

## Consequences

- To "rename" an entity, create a new instance with the desired identity parts and migrate references.
- Hydration round-trips preserve the original identity exactly.
- The generator must always route identity-part mutation through `GuardIdentityMutation()`.
