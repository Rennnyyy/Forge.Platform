# 0012 — `Forge.Capability.Generators`: CRUD capability generator

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

Writing `ICapabilityHandler<TCommand, TResponse>` implementations that wrap an entity's
active-record CRUD methods (`CreateAsync`, `ReadAsync`, `UpdateAsync`, `DeleteAsync`,
`ListAsync`) is mechanical boilerplate: for every entity-backed CRUD surface the developer
must write five command records, five response records, and five handler classes — all
structurally identical. The entity's shape (property names, types, accessors) already
encodes every piece of information needed to derive those types and bodies.

The existing `Forge.Operations.Generators` precedent demonstrates that Roslyn incremental
source generators can eliminate exactly this class of boilerplate. A CRUD capability
generator follows the same pattern, driven by a new opt-in attribute on the entity class.

## Options

1. **New `Forge.Capability.Generators` source generator project.** A `[CrudCapabilities]`
   attribute in `Forge.Capability` opts an `[Entity]`-annotated class into generation.
   A `CrudMethod` flags enum controls which of the five operations are generated. The
   generator emits command records, response records, and `ICapabilityHandler<,>` implementations
   that delegate to the entity's active-record methods. One `.g.caps.cs` file per entity.
2. **Hand-write boilerplate per entity.** Zero new infrastructure. Con: repetitive;
   consistency depends on discipline; every new entity property requires manual updates
   across five handler files.
3. **Runtime code generation via `System.Reflection.Emit`.** Con: no compile-time
   visibility; no IDE support; inconsistent with the established generator pattern.

## Decision

Option 1.

### Attribute surface added to `Forge.Capability`

```csharp
// src/Capability/CrudMethod.cs
[Flags]
public enum CrudMethod
{
    Create = 1,
    Read   = 2,
    Update = 4,
    Delete = 8,
    List   = 16,
    All    = Create | Read | Update | Delete | List,
}

// src/Capability/CrudCapabilitiesAttribute.cs
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CrudCapabilitiesAttribute : Attribute
{
    public CrudCapabilitiesAttribute(CrudMethod methods = CrudMethod.All) { … }
    public CrudMethod Methods { get; }
}
```

### Generator project

`src/Capability.Generators/Forge.Capability.Generators.csproj` — targets `netstandard2.0`
per ADR-0002. Depends only on the Roslyn analyser stack (`Microsoft.CodeAnalysis.CSharp`,
`Microsoft.CodeAnalysis.Analyzers`).

### Generation model

The generator reads:
- The `[Entity(Path = "…")]` attribute for the capability path segment.
  Fallback when `Path` is absent: `TypeName.ToLowerInvariant()`.
- `[IdentityPart]`-annotated properties (for Create command and Read/List response).
- `[Predicate]`-annotated properties (for Create command and Read/List response; settable
  subset for Update command). `[Owning]` and `[Inverse]` properties are skipped.

### Generated types (per requested `CrudMethod` flag)

| Flag | Command record | Response record | Handler class |
|------|----------------|-----------------|---------------|
| `Create` | `Create{T}Command` — identity parts + all predicate props | `Create{T}Response(string Iri)` | `Create{T}Handler` |
| `Read` | `Read{T}Command(string Iri)` | `Read{T}Response(string Iri, …all props)` | `Read{T}Handler` |
| `Update` | `Update{T}Command(string Iri, …settable predicate props)` | `Update{T}Response(string Iri)` | `Update{T}Handler` |
| `Delete` | `Delete{T}Command(string Iri)` | `Delete{T}Response()` | `Delete{T}Handler` |
| `List` | `List{T}Command()` | `List{T}Response(IReadOnlyList<Read{T}Response> Items)` | `List{T}Handler` |

`Read{T}Response` is also emitted when only `List` (not `Read`) is requested, because it
serves as the item type for `List{T}Response`.

### Capability identities

Each handler carries `[Capability("{path}.{operation}")]` with no HTTP-specific attributes.
The generator is transport-agnostic (Capability.Http `[CapabilityEndpoint]` is not emitted).

### File hint

`{Namespace}.{TypeName}.g.caps.cs` (or `{TypeName}.g.caps.cs` for global-namespace types).

### Opt-out

Entities not annotated with `[CrudCapabilities]` are unaffected. Entities annotated with
`[NoOperations]` (from `Forge.Operations`) should not also carry `[CrudCapabilities]`
because the generated handler bodies call the active-record CRUD methods — a compiler error
will result at the consuming project's build if that combination is attempted.

## Consequences

- Adding `[CrudCapabilities]` to an entity produces fully working handlers with no
  further implementation required by the developer.
- Custom logic (validation, domain events, enrichment) is layered via standard DI
  decoration — the generated handlers remain simple delegation wrappers.
- The generator is additive: existing hand-written handlers are unaffected.
- Tests for the generator live in `tests/Capability.Generators.Tests/` following the
  snapshot-assertion pattern established by `tests/Operations.Tests/`.
