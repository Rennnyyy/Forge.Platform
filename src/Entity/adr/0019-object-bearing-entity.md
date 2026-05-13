# 0019 — `[ObjectBearing]` attribute and auto-generated object metadata properties

- **Status**: accepted
- **Date**: 2026-05-13
- **Author**: agent

## Context

Some entity types are primarily metadata wrappers for a single binary object (root ADR-0023).
The generator, `MapOperations()`, and the HTTP layer all need a reliable signal that a given
entity class owns a blob.

Three properties are universally required on every blob-owning entity:

| Property | Type | Semantics |
|----------|------|-----------|
| `ObjectKey` | `string?` | Opaque, globally unique key under which the blob is stored in `IObjectStore`. `null` until the first content upload. |
| `ContentType` | `string?` | MIME type of the stored content (e.g. `application/pdf`). `null` until first upload. |
| `ObjectStoreKey` | _compile-time constant_ | The DI key used to resolve `IObjectStore` from `IObjectStoreProvider`. Emitted as a `string` constant on the generated partial class, not as an entity property. |

If the entity author had to write `ObjectKey` and `ContentType` as ordinary
`[Predicate]`-annotated properties by hand, they would be error-prone and inconsistently
named. The generator already emits identity properties from `[Identity]`/`[IdentityPart]`
without requiring the author to write them; the same pattern applies here.

## Options

1. **`[ObjectBearing(string StoreKey)]` in `Forge.Entity.Attributes`; generator emits
   `ObjectKey` and `ContentType` on the partial class as if they were hand-authored
   `[Predicate]`-annotated properties.**
   Pro: zero author ceremony; consistent naming enforced by the generator; `Forge.Entity`
   has no object-storage dependency (the attribute is a plain `[AttributeUsage]` type).
   Con: the generator must detect and skip these properties if the author mistakenly
   declares them manually (FORGE0008 error).

2. Author writes `ObjectKey` and `ContentType` explicitly with `[Predicate]`.
   `[ObjectBearing]` acts only as a skip-signal for `MapOperations()`.
   Pro: transparent.
   Con: naming and attribute placement drift across entity types; `MapOperations()` cannot
   statically guarantee these properties exist.

3. A dedicated `ObjectBearingEntityBase` abstract base class that provides the two
   properties.
   Con: C# single-inheritance; any entity that already inherits a domain base type cannot
   use this; contradicts the explicit-`[Entity]`-on-child rule of ADR-0016.

## Decision

Option 1.

### Attribute definition — `Forge.Entity.Attributes`

```csharp
namespace Forge.Entity;

/// <summary>
/// Marks an <c>[Entity]</c>-annotated entity class as owning a single binary object
/// in an <c>IObjectStore</c>.
/// </summary>
/// <remarks>
/// The source generator emits <c>ObjectKey</c> (<c>string?</c>, <c>[Predicate]</c>) and
/// <c>ContentType</c> (<c>string?</c>, <c>[Predicate]</c>) on the generated partial class,
/// and a <c>public const string ForgeObjectStoreKey</c> compile-time constant.
///
/// <c>MapOperations()</c> in <c>Forge.Operations.Http</c> silently skips entity types
/// annotated with this attribute; <c>MapObjectOperations()</c> in
/// <c>Forge.ObjectStorage.Http</c> picks them up instead.
/// </remarks>
/// <param name="storeKey">
/// The DI key used to resolve <c>IObjectStore</c> from <c>IObjectStoreProvider</c>.
/// Emitted as <c>ForgeObjectStoreKey</c> on the generated class.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ObjectBearingAttribute : Attribute
{
    public ObjectBearingAttribute(string storeKey) => StoreKey = storeKey;
    public string StoreKey { get; }
}
```

The attribute lives in `Forge.Entity.Attributes` and shares the `Forge.Entity` namespace
(per ADR-0004 and existing convention for entity attributes).

### Generator treatment

When the entity model parser encounters `[ObjectBearing]` on an entity class:

1. **Emit `ObjectKey` property** on the generated partial class:
   ```csharp
   [Predicate("objectKey")]
   public string? ObjectKey { get; set; }
   ```
2. **Emit `ContentType` property** on the generated partial class:
   ```csharp
   [Predicate("contentType")]
   public string? ContentType { get; set; }
   ```
3. **Emit `ForgeObjectStoreKey` constant**:
   ```csharp
   public const string ForgeObjectStoreKey = "<storeKey value>";
   ```

The predicate short names `"objectKey"` and `"contentType"` resolve to absolute IRIs
using the entity's `PredicatePath` — identical to any other `[Predicate]`-annotated
property (ADR-0012).

4. **FORGE0008 diagnostic** — if the author has also declared a property named `ObjectKey`
   or `ContentType` on the user-authored partial class, the generator emits a compile-time
   error: _"ObjectBearing entity '<Type>' must not declare 'ObjectKey' or 'ContentType'
   manually; they are emitted by the generator."_

### `MapOperations()` skip logic

`OperationEndpointsEndpointRouteBuilderExtensions.MapOperations()` checks each discovered
entity type for `[ObjectBearing]` before dispatching to `RegisterEndpointsFor<T>`. If the
attribute is present, the type is skipped and a single `ILogger.LogWarning` entry is
written at startup:

> `Skipping MapOperations registration for {TypeName}: annotated with [ObjectBearing]. Use MapObjectOperations() instead.`

This prevents silent omission from going unnoticed if `MapObjectOperations()` is not wired.

### `UploadState` — deferred

Whether to encode a formal upload-state enum on the entity is deferred. Calling code
checks `entity.ObjectKey != null` to determine whether content has been uploaded. A
follow-up ADR may introduce an `[Enumeration]`-backed `UploadState` property if richer
state tracking is needed.

## Consequences

- Entity authors annotate the partial class with `[ObjectBearing("myStore")]` and nothing
  else; `ObjectKey`, `ContentType`, and `ForgeObjectStoreKey` appear automatically in the
  generated file.
- `Forge.Entity` gains no reference to `Forge.ObjectStorage.Abstractions` or any blob
  SDK — the attribute is a plain annotation type.
- FORGE0008 catches accidental double-declaration during development.
- `MapOperations()` warns and skips rather than silently registering broken JSON-only
  CRUD endpoints for blob-owning entity types.
- The generator change is additive: entity types without `[ObjectBearing]` are unaffected.
