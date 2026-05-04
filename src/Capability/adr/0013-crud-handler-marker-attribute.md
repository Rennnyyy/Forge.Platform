# 0013 — `[CrudCapabilityHandler]` marker attribute on generated handlers

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0012 established that the `Forge.Capability.Generators` source generator emits handler
classes decorated with `[Capability("…")]` for entities carrying `[CrudCapabilities]`.
These generated handlers are structurally identical to hand-written handlers from the
perspective of the `MapCapabilities()` auto-discovery pipeline.

The HTTP routing slice (`Forge.Capability.Http`) needs to distinguish CRUD-generated
handlers from hand-written handlers so it can apply a different URL prefix:
`api/entities/` for CRUD handlers, `api/capabilities/` for all others (see
Capability.Http ADR-0006).

There is currently no attribute on generated handler classes that identifies them as
CRUD-generated. Without such a marker the routing layer would need to inspect the
handler type's name or use a side-channel registry — both fragile and coupling-heavy.

## Options

1. **New `[CrudCapabilityHandler]` marker attribute in `Forge.Capability`**, emitted by
   the generator on every handler class it produces. The attribute carries no data; its
   presence alone signals CRUD provenance. `Forge.Capability.Http` reads it without any
   additional registration.
2. Register generated handler types in a static list at generator time and consult it
   at routing time. Con: side-channel; breaks with incremental compilation; requires
   additional DI setup.
3. Use naming convention (class name starts with `Create`, `Read`, etc.). Con: brittle;
   breaks if a consumer writes a hand-crafted handler following the same naming pattern.

## Decision

Option 1.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CrudCapabilityHandlerAttribute : Attribute { }
```

The generator emits `[global::Forge.Capability.CrudCapabilityHandlerAttribute]` on every
handler class it produces, alongside the existing `[global::Forge.Capability.CapabilityAttribute]`.

Consumers outside the platform may also apply `[CrudCapabilityHandler]` to hand-written
handlers if they want them routed under the entity prefix — this is an intentional,
documented use of the attribute.

## Consequences

- `Forge.Capability.Http` can distinguish CRUD handlers from general capability handlers
  with a single `GetCustomAttribute<CrudCapabilityHandlerAttribute>()` call.
- The attribute adds no data (zero-allocation presence check) and no compile-time cost.
- All existing generated handler classes gain the attribute transparently when the
  generator is updated; no consumer changes required.
