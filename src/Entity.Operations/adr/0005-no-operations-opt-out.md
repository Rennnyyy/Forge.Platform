# 0005 — `[NoOperations]` opt-out from Operations.Generators

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`Forge.Entity.Operations.Generators` (Operations ADR-0002) emits active-record CRUD
methods (`CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ReadAsync`, `ListAsync`) into a
sibling partial file for **every** type carrying `[Entity]`. The Aspects slice
(Aspects ADR-0003) introduces an entity (`Aspect`) that needs hand-written CUD with
extra behaviour around it (TTL parsing, hash computation, shape-cache eviction,
read-only Code origin). With the generator running unconditionally, the hand-written
methods would collide with the generated ones at compile time.

A general escape hatch is needed for the rare cases where a slice owns a type that
should participate in the structural generator (identity / refs / collections) but
should **not** receive the active-record CUD layer.

## Options

1. **Add a `[NoOperations]` attribute in `Forge.Entity.Operations`. The Operations
   generator skips emission when the symbol carries it.** The attribute lives in the
   Operations slice; consumers reference it the same way they reference `Forge.Entity.Operations`.
2. Add a `GenerateOperations` flag to the `[Entity]` attribute in `Forge.Entity` core.
   Pro: one attribute. Con: couples core to Operations vocabulary by name; core has no
   reason to know about Operations; violates the slice-per-concern discipline (root
   ADR-0005).
3. Drop the convention "generator runs for every `[Entity]`" and require an explicit
   opt-in attribute (`[GenerateOperations]`).
   Pro: maximally explicit. Con: breaking change to every existing entity in
   `Entity.Tests.Fixtures` and any consumer; violates the friction-free authoring
   experience the slice was designed for.

## Decision

Option 1.

### Attribute

```csharp
namespace Forge.Entity.Operations;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class NoOperationsAttribute : Attribute { }
```

### Generator behaviour

`Forge.Entity.Operations.Generators` reads `NoOperationsAttribute` by symbol name from
the consuming compilation. When present on a `[Entity]`-decorated class, the generator
emits **nothing** for that type (no `.g.ops.cs` file). The structural generator
(`Forge.Entity.Generators`, ADR-0002) is unaffected and still emits its `.g.cs` file.

### Authoring contract

A type carrying `[NoOperations]` is responsible for providing its own active-record
surface if it wants one. Aspects ADR-0003 is the first such consumer.

The opt-out is per-type and not inheritable. Sub-types of a `[NoOperations]` class
(if ever introduced) get the generator's default behaviour again — which is correct,
because each `[Entity]` is independently identified.

## Consequences

- The Aspects slice can ship hand-written `Aspect.CreateAsync` / `UpdateAsync` /
  `DeleteAsync` / `ReadAsync` / `ListAsync` without colliding with generator output.
- Application-defined entities can opt out for similar reasons (rare; the attribute is
  intentionally low-ceremony).
- The Operations generator gains a single conditional check; the existing
  `.g.ops.cs` snapshot tests are unaffected for non-decorated types.
- The `[NoOperations]` attribute lives in `Forge.Entity.Operations`, so a consumer
  must reference that slice to use it. This is correct: a project that does not
  reference Operations cannot have generated operations to opt out of.
