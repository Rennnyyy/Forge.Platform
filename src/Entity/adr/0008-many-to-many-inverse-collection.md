# ADR-0008 — Many-to-Many Relations via `EntityRefCollection<T>` on Both Sides

**Date**: 2025-08-01  
**Status**: Accepted

## Context

1:N owning collections already synchronise into a single inverse ref (`EntityRef<T>?`) on the target.  
There is a class of relations where *both* sides carry a collection — an `Author` has many `Tag`s and a `Tag` is used by many `Author`s (m:n). The generator must keep both sides consistent without requiring callers to do it manually.

## Decision

### Attribute placement

The owning side is annotated with `[Owning(predicate)]` and declares `partial EntityRefCollection<T> Property { get; }` — no change.

The inverse side is annotated with `[Inverse(nameof(Owner.Property), predicate)]` and also declares `partial EntityRefCollection<T> Property { get; }` (rather than `EntityRef<T>?`).

### Parser classification

`EntityParser` inspects the property type of an `[Inverse]`-decorated property:

- `EntityRefCollection<T>` → `RefKind.InverseCollection`
- `EntityRef<T>?` → `RefKind.InverseSingle` (unchanged)

### Generator output

**Owning entity** (`Author`):  
The `__Forge_Build_Tags()` factory detects `RefKind.InverseCollection` for the inverse side and wires async lambdas that call `__Forge_AddTo_Authors`/`__Forge_RemoveFrom_Authors` on the child entity.

**Inverse entity** (`Tag`):  
`EmitInverseCollections` emits:
- A backing field `EntityRefCollectionImpl<Author>? __forge_invcoll_Authors`
- A property getter using `??=` lazy init (no hooks needed — this side is purely passive)
- `internal ValueTask __Forge_AddTo_Authors(Author item)` — initialises the impl on first use, then calls `AddAsync(item)`
- `internal ValueTask __Forge_RemoveFrom_Authors(Author item)` — no-ops if field is null (item was never added), then calls `RemoveAsync(item)`

### Consistency guarantee

Calling `await author.Tags.AddAsync(tag)` will, through the `onAdd` hook, call `await tag.__Forge_AddTo_Authors(author)`, inserting the author into `tag.Authors`. Symmetrically for `RemoveAsync`. Both collections stay in sync without application code.

### `EntityRegistry` change

`FindInverse(targetFqn, owningPropertyName)` returns `(PropertyName, IsCollection)?` so the emitter can dispatch to the correct hook variant from a single method.

## Alternatives Considered

- **Bidirectional callbacks on the inverse side** — rejected; the inverse collection has no owning semantics and does not fire its own hooks to avoid infinite recursion.
- **Separate `[ManyToMany]` attribute** — rejected; the existing `[Inverse]` annotation disambiguates via the declared property type, avoiding proliferation of attributes.
- **Lazy init in the property getter only** — kept for the passive side. The `__Forge_AddTo_X` method also initialises the field so the `onAdd` hook can never cause a null-dereference even if the property was never accessed first.

## Consequences

- m:n relations are declarative and generator-driven — no application code needed beyond the annotations.
- The inverse collection is always *passive*: items must be added/removed through the owning side.  Attempting to call `AddAsync` on the inverse collection directly will not synchronise back to the owning side.
- The `EntityRefCollectionImpl<T>` constructor without callbacks is reused for the inverse side — the zero-arg ctor is public and intentional.
