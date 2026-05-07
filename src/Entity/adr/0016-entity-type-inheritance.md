# 0016 — Entity type inheritance via explicit `[Entity]` on partial subclass

- **Status**: accepted
- **Date**: 2026-05-07
- **Author**: agent

## Context

All entities so far are flat, independent partial classes. Domain models often require
specialisation hierarchies (e.g. a `FeaturedArtist` that is still an `Artist` in every
meaningful sense but carries additional predicates). Without inheritance support, shared
predicates and relations must be duplicated across every concrete type, and the RDF graph
has no structural record of the hierarchy.

## Options

1. **Explicit `[Entity]` on child partial class; generator detects entity base type and
   emits `: ParentType` instead of `: EntityBase`.** Identity is always inherited from
   the root ancestor; `[Identity]` on a child is a compile error (FORGE0006). Path on
   `[Entity]` is invalid on a child (FORGE0007). Child's `rdf:type` IRI is derived as
   `{parent-type-IRI}/{ChildClassName}`. The mapper walks the C# type chain to resolve
   identity, predicates, and emits one `rdf:type` triple per ancestor level.
2. Automatic detection by base-type chain without requiring `[Entity]` on the child.
   Pro: less ceremony. Con: silent; any class that happens to inherit another entity
   would be treated as a subtype, which is surprising for intermediate base classes or
   mix-ins.
3. New `[EntitySubtype]` attribute. Pro: explicit, distinct from `[Entity]`. Con: an
   extra attribute concept; feels like duplication given that `[Entity]` already signals
   "this is an entity class".
4. No inheritance support; require full duplication of shared predicates. Con: violates
   DRY; domain hierarchies become painful to maintain.

## Decision

Option 1.

### Rules

| Rule | Enforcement |
|------|-------------|
| A child entity is a `partial class` annotated with `[Entity]` whose direct or transitive base type is also `[Entity]`-annotated | Generator detects by walking the C# base-type chain |
| `[Identity]` must **not** appear on a child entity class | FORGE0006 (error) |
| `Path` must **not** be set on `[Entity]` on a child entity class | FORGE0007 (error) |
| `PredicatePath` **may** be set on a child's `[Entity]` to scope its own predicates | Optional; defaults to null |
| Child inherits the root ancestor's identity strategy, IRI path, and materialization | Inherited via C# base class |
| Generator emits `partial class Child : Parent` instead of `partial class Child : EntityBase` | Emitter checks `BaseEntityTypeFqn` |
| Generator only wires members declared directly on the child partial class | Roslyn `GetMembers()` returns declared members only |
| For UuidV4 parent + child: generator emits `public Child() : base() { }` and `internal Child(Guid) : base(Guid) { }` | Hydration path requires the `(Guid)` ctor |

### RDF type model

At projection time `ReflectionRdfMapper<T>.Project()` walks the C# type chain and emits
one `rdf:type` triple per ancestor level:

```
<child-iri>  rdf:type  <types/parentPath/ChildClassName>   # concrete discriminator
<child-iri>  rdf:type  <types/parentPath>                  # parent (and grandparent, etc.)
```

The child's type IRI path component is `{parentEntityPath}/{ChildClassName}`. This is
computed recursively by `ComputeEntityPathForTypeIri(Type t)` in the mapper, which uses
`[Entity(Path)]` of the root ancestor and appends each intermediate class name.

### `QueryByTypeAsync<T>()` behaviour (unchanged contract)

`QueryByTypeAsync<Studio>()` returns all IRIs carrying `rdf:type <types/studios>`,
which now includes both pure `Studio` and `GoldStudio` instances. The concrete type
can be identified via the most-derived `rdf:type` triple (the discriminator).

### `LoadAsync<Parent>(childIri)` (deferred enforcement)

Loading a child IRI via the parent mapper succeeds in v1 and returns a partial view of
the entity (child-specific predicates are absent). Full enforcement (throw on type
mismatch) requires the mapper to receive and inspect `rdf:type` triples at hydration
time — deferred to a follow-up ADR once the mapper interface evolves to carry options.

### Lazy collection inheritance note

Inherited lazy collections whose short predicate names are baked into the generated
code resolve against the `PredicatePath` of the concrete entity type at load time.
If the child declares a different `PredicatePath` from the parent, inherited lazy
collection predicates may resolve to the wrong IRI. Until the generator emits resolved
absolute IRIs for lazy predicates, avoid combining inheritance with lazy collections
that have different `PredicatePath` values across the hierarchy.

## Consequences

- Domain authors can express shallow and deep type hierarchies by annotating the child
  partial class with `[Entity]` and inheriting from the parent class.
- Child entities carry their parent's type IRI in the graph, enabling polymorphic
  queries via the shared ancestor `rdf:type` triple.
- The generator remains incremental: each class is processed independently; no
  cross-type state is shared between the generator pass for the parent and the child.
- `ReflectionRdfMapper<T>` correctly walks the C# type chain for identity, predicates,
  relations, and multi-type projection without changes to the `IEntityStore` or
  `IRdfMapper<T>` interfaces.
- New FORGE0006 and FORGE0007 diagnostics enforce that children do not re-declare
  identity or override the instance IRI path.
