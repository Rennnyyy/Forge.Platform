# 0016 — `IsIdentitySealed` is excluded from JSON serialization

- **Status**: accepted
- **Date**: 2026-05-07
- **Author**: agent

## Context

`IsIdentitySealed` on `EntityBase` is an internal state-machine flag: it is `true` once
an IRI has been assigned and is used exclusively by C# caller code to guard against
premature identity access. It carries no domain meaning — it is not a predicate, it is
not part of the entity's RDF representation, and no external consumer should ever rely
on it being present in a serialized payload.

However, `System.Text.Json` serializes all public readable properties by default. When
`Results.Ok(entity)` is returned from an ASP.NET Core Minimal API endpoint (e.g.
`Operations.Http`) the JSON body includes `"isIdentitySealed": true` on every entity
response — a confusing, leak of internal framework state.

## Options

1. **`[JsonIgnore]` on `EntityBase.IsIdentitySealed`.**
   Declarative, one place, zero runtime cost.
   All JSON serialization contexts exclude the property automatically.
2. Configure a `JsonSerializerOptions` modifier in `Operations.Http` to strip the
   property at the endpoint layer.
   Pro: keeps Entity free of `System.Text.Json` references.
   Con: `System.Text.Json` is part of the .NET shared framework (no additional package);
   the property leaks if any other serialisation context forgets the modifier.
3. Introduce a separate DTO / projection layer for HTTP responses.
   Con: significant churn for zero domain gain; every new entity property requires a
   matching DTO property.

## Decision

Option 1. `[JsonIgnore]` is placed on the `IsIdentitySealed` property in `EntityBase`.
`System.Text.Json` is already transitively available in every consumer of `Forge.Entity`
(it is part of `Microsoft.NETCore.App`); no new `PackageReference` is required.

## Consequences

- `isIdentitySealed` is absent from all JSON outputs produced by any serializer that
  honours `[JsonIgnore]` — including Minimal API responses and any future serialization
  contexts.
- The property remains fully accessible in C# code; the attribute has no effect on
  normal property reads.
- Serializers that do not honour `[JsonIgnore]` (e.g. `Newtonsoft.Json` with default
  settings) would still output the property; that case is out of scope for this ADR.
