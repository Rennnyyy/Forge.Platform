# SLICING — Forge.Repository

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Repository` | Store contracts, entity repository facade, write mode. | A file belongs here if it is a primary public contract consumed by the widest range of callers (`IEntityStore`, `IEntityRepository<T>`, `ISparqlQueryStore`, `WriteMode`, `EntityRepositoryOptions`). The `IAspect` and `Aspect` types that originally lived here have moved to `Forge.Aspects.Abstractions` to break a circular dependency — see Repository ADR-0001. |
| `Rdf/` | `Forge.Repository.Rdf` | RDF model types and vocabulary constants. | A file belongs here if it defines a low-level RDF model type (`RdfTerm`, `RdfTriple`, `RdfGraph`) or a vocabulary constant set (`RdfVocab`). |
| `Mapping/` | `Forge.Repository.Mapping` | RDF ↔ entity mapping contracts and reflection-based implementation. | A file belongs here if its primary subject is the translation between C# entities and RDF triples: mapper contracts (`IRdfMapper`, `IRdfTripleSink`, `IRdfMapperRegistry`), the registry implementation (`RdfMapperRegistry`), and supporting helpers (`ReflectionRdfMapper`, `PredicateResolver`, `LiteralCodec`). |
| `Transaction/` | `Forge.Repository.Transaction` | Multi-operation ACID transaction model. | A file belongs here if its primary subject is transactional semantics: the `ITransactionalEntityStore` contract, `EntityTransaction` builder, and `TransactionOperation` hierarchy. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `DependencyInjection/` | Framework/architecture-driven; excluded per ADR-0010. |
| `adr/` | ADR folder; excluded per ADR-0010. |

## File assignment

### Root (`Forge.Repository`)

- `IEntityStore.cs` — backend boundary; type-agnostic store.
- `ISparqlQueryStore.cs` — optional SPARQL query capability.
- `IEntityRepository.cs` — typed application-facing facade.
- `EntityRepository.cs` — default `IEntityRepository<T>` implementation.
- `EntityRepositoryOptions.cs` — configuration options for the repository.
- `WriteMode.cs` — enum controlling create vs. replace semantics.
- `BranchScope.cs` — ambient `AsyncLocal<string?>` carrying the active branch IRI; see Repository ADR-0002.

### `Rdf/` (`Forge.Repository.Rdf`)

- `RdfTerm.cs`
- `RdfTriple.cs`
- `RdfGraph.cs`
- `RdfVocab.cs`

### `Mapping/` (`Forge.Repository.Mapping`)

- `IRdfMapper.cs`
- `IRdfTripleSink.cs`
- `IRdfMapperRegistry.cs` _(in `RdfMapperRegistry.cs`)_
- `RdfMapperRegistry.cs`
- `ReflectionRdfMapper.cs`
- `PredicateResolver.cs`
- `LiteralCodec.cs`

### `Transaction/` (`Forge.Repository.Transaction`)

- `ITransactionalEntityStore.cs`
- `EntityTransaction.cs`
- `TransactionOperation.cs` — includes `EntityWriteOperation`, `CreateOperation<T>`, `UpdateOperation<T>`, `DeleteOperation`, and `DropGraphOperation` (see Repository ADR-0003).
