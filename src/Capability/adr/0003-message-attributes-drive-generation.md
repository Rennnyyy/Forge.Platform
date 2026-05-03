# 0003 — `[Command]`, `[Response]`, `[Event]` drive SHACL generation

- **Status**: superseded by [0007](0007-dynamic-per-call-aspects.md)
- **Date**: 2026-05-03
- **Author**: agent

## Context

Capability messages carry structured data across the capability boundary. Each message
class should have a SHACL shape that enforces its contract — required fields,
enumeration constraints, datatype correctness — in the same way that `Forge.Entity`
types are validated by `Forge.Aspects`.

Writing SHACL shapes by hand for every message type creates duplication: the same
constraints are already expressed in C# via `[Required]`, `[Enumeration]`, and
property types. The existing generator pattern (Aspects ADR-0001, Entity.Generators)
proves that Roslyn incremental source generators can derive SHACL directly from
C# attribute declarations.

## Options

1. **Roslyn source generator in `Forge.Capability.Generators` reads `[Command]`,
   `[Response]`, `[Event]` on message classes; emits SHACL TTL embedded resource +
   `IMessageAspect` registration code.**
   Reuses `[Required]`, `[Enumeration]`, `[Identity]` from `Forge.Entity` as the
   constraint vocabulary. No new property attributes. Generated TTL is deterministic
   and testable via snapshot tests (same pattern as `Entity.Generators.Tests`).
2. **Hand-author TTL per message type.** No generator needed. Con: duplication between
   C# model and TTL; divergence risk; no enforcement of the contract at compile time.
3. **Runtime reflection-based shape synthesis.** No generator project. Con: startup
   cost; no test snapshots; not consistent with the established generator pattern.

## Decision

Option 1.

### Attribute contracts

```csharp
// Forge.Capability/Attributes/CommandAttribute.cs
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class CommandAttribute : Attribute { }

// Forge.Capability/Attributes/ResponseAttribute.cs
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ResponseAttribute : Attribute { }

// Forge.Capability/Attributes/EventAttribute.cs
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class EventAttribute : Attribute { }
```

### Generator outputs (per decorated message type)

1. **`<TypeName>Shape.ttl`** — embedded resource in `Forge.Capability`; a valid
   SHACL Turtle document with `sh:targetClass` derived from the type name and
   `sh:property` entries for each `[Required]` / `[Enumeration]` property.
2. **`MessageAspectRegistrations.g.cs`** — a `partial` DI extension method body that
   calls `registry.Register(new InlineTtlMessageAspect(…), typeof(T), MessageKind.*)`.

### Reused property attributes

The generator recognises `[Required]` and `[Enumeration]` from `Forge.Entity`
on message class properties. No new property-level attributes are introduced.

### Snapshot testing

`Forge.Capability.Generators.Tests` mirrors the snapshot pattern used in
`Forge.Entity.Generators.Tests`: for each test case, assert the generated TTL content
and the generated registration code match committed snapshots.

## Consequences

- Message contracts are defined once (in C#) and never drift from the SHACL shape.
- Adding a `[Required]` field to a message class automatically tightens the SHACL shape.
- The generator is optional: removing `Forge.Capability.Generators` from a project
  leaves `[Command]`, `[Response]`, `[Event]` as no-op markers; no shapes are registered
  and all message validation is permissive.
