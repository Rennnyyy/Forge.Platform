# 0010 — `CapabilityAspect.OperationAspectIri` for entity-graph validation in CUD capabilities

- **Status**: accepted
- **Date**: 2026-05-06
- **Author**: agent

## Context

`CapabilityAspect` bundles per-capability validation policy for the message layer
(`CommandAspectIri`, `ResponseAspectIri`, `EventAspectIris`). These all resolve to
`IMessageAspect` instances and are evaluated by `IMessageAspectEngine` on the command/
response/event JSON graphs.

Generated CUD capability handlers (`CreateXHandler`, `UpdateXHandler`, `DeleteXHandler`)
delegate to the entity's active-record CRUD methods, which call `IEntityStore.SaveAsync`
directly. This path bypasses `ITransactionalEntityStore` entirely, meaning that
`IOperationAspect` — the two-pass (Local SHACL + Context SPARQL) entity-graph
validation — is unreachable from a capability dispatch call.

A caller who wants to gate a CUD capability behind an entity-graph constraint (e.g.
"published year must be ≥ 1800" or "cannot delete a checked-out book") currently has no
supported mechanism.

## Options

1. **Add `OperationAspectIri` to `CapabilityAspect`.** A single nullable string that,
   when set, names the `IOperationAspect` the handler should use when opening its
   entity transaction. `null` = no entity-graph validation. The generated handlers read
   `context.Aspect?.OperationAspectIri` to pick up the IRI at dispatch time.
   Pro: one field; symmetric with the existing `CommandAspectIri` etc.; no new types.
   Con: `CapabilityAspect` now spans two validation families (message and operation).
   Acceptable: the record is explicitly described as a "bundle" and already does so.

2. **Separate `OperationCapabilityAspect` record.** A new type for CUD-only aspects.
   Con: callers must register and supply a second bundle; two records per capability
   instead of one. The extra abstraction solves no identified problem.

3. **Caller passes the operation aspect IRI directly as a second HTTP header.** Con:
   breaks the "single IRI governs the full policy" invariant established in ADR-0003.

## Decision

Option 1.

### Change to `CapabilityAspect`

```csharp
// src/Aspects.Abstractions/CapabilityAspect.cs
public sealed record CapabilityAspect : IAspect
{
    public required string Iri { get; init; }
    public string? CommandAspectIri { get; init; }
    public string? ResponseAspectIri { get; init; }
    public IReadOnlyDictionary<Type, string> EventAspectIris { get; init; } = …;

    /// <summary>
    /// IRI of the <see cref="IOperationAspect"/> applied when this capability
    /// performs a write transaction (Create, Update, Delete).
    /// <c>null</c> = no entity-graph validation (uses <see cref="Aspect.NoOpIri"/>).
    /// </summary>
    public string? OperationAspectIri { get; init; }
}
```

The field is optional (nullable). Existing `CapabilityAspect` registrations that omit it
continue to behave identically — the generated handlers fall back to `Aspect.NoOpIri`.

## Consequences

- Callers can enforce `IOperationAspect` SHACL / SPARQL constraints on CUD capability
  handlers by registering a `CapabilityAspect` with `OperationAspectIri` set.
- The generated CUD handlers must be updated to consume this field (see Capability
  ADR-0015).
- The message-layer and entity-graph-layer policies remain independently nullable:
  a capability that validates the command JSON only sets `CommandAspectIri`; one that
  validates entity state only sets `OperationAspectIri`; a full-stack policy sets both.
