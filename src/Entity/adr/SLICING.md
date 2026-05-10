# SLICING — Forge.Entity

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Entity` | Entity base types, session, collection helpers, configuration, and IRI utilities. | A file belongs here if it defines a primary public contract or base type consumed across the stack: `IEntity`, `EntityBase`, `EntityOptions`, `EntitySession`, `EntityRef`, `EntityCollection`, `Iri`, and the supporting interfaces and runtime helpers. |
| `Attributes/` | `Forge.Entity` | Source-generator-facing attribute annotations. | A file belongs here if it is an attribute type used to annotate entity classes for codegen: `EntityAttribute`, `IdentityAttributes`, `PredicateAttribute`, `RelationAttributes`. Attributes share the root namespace (`Forge.Entity`) per convention. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Entity`)

- `IEntity.cs` — marker interface for all entity types.
- `EntityBase.cs` — abstract base providing `Iri` and change-tracking state.
- `EntityOptions.cs` — static registry of `IEntityOptions` instances per type.
- `EntityOptionsInstance.cs` — concrete `IEntityOptions` implementation.
- `IEntityOptions.cs` — per-type configuration contract.
- `EntitySession.cs` — ambient unit-of-work managing loaded entity identity.
- `EntityRef.cs` — typed lazy reference to a related entity.
- `EntityRefCollection.cs` — collection of typed lazy references.
- `EntityCollection.cs` — ordered mutable collection of resolved entity instances.
- `ICollectionLoader.cs` — contract for deferred collection loading.
- `IEntityLoader.cs` — contract for loading an entity by IRI.
- `IInverseRefLoader.cs` — contract for loading inverse (back-)references to an entity.
- `Iri.cs` — IRI value type with validation and factory helpers.

### `Attributes/` (`Forge.Entity`)

- `EntityAttribute.cs` — marks a class as a generated entity.
- `IdentityAttributes.cs` — `[IriIdentity]` and related identity-strategy attributes.
- `PredicateAttribute.cs` — maps a property to an RDF predicate IRI.
- `RelationAttributes.cs` — `[Relation]`, `[InverseRelation]`, and many-to-many attributes.
