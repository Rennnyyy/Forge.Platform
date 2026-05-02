# Trunk 3 — `Aspect` entity, hand-written CUD, hybrid origin

- **Owner**: Aspects agent B
- **Prerequisites**: Trunk 1 (`[NoOperations]`), Trunk 2 (engine + resolver)
- **ADRs**: [Aspects ADR-0003](../../src/Entity.Aspects/adr/0003-aspect-as-first-class-entity.md)
- **Unblocks**: Trunk 4

## Goal

Introduce the `Aspect` entity as a first-class type with hybrid origin
(`Code` | `Repository`), hand-written CUD that bypasses the active-record generator,
and a unified read API that merges both origins. Refactor the resolver from Trunk 2 to
include the Repository origin.

## Scope

- Define the `Aspect` partial class in `Forge.Entity.Aspects`, decorated with
  `[NoOperations]`.
- Implement hand-written static + instance CUD methods.
- Refactor `IAspectResolver` to merge Code- and Repository-origin shapes; snapshot the
  active set at transaction begin; key parsed-shape caches by `ShaclSha256`.
- Convert `AddCodeAspect(...)` registrations into materialized `Aspect` instances at
  startup (eager loading per ADR-0003).
- Add referential-integrity shape registration for delete-time integrity (ADR-0001
  §"Referential-integrity context shapes" — deferred from Trunk 2 because it needs
  typed-Aspect routing).
- Tests for hybrid lifecycle, parse failure handling, and cache eviction.

## Deliverables

### `Aspect` entity

```csharp
namespace Forge.Entity.Aspects;

public enum AspectOrigin { Code, Repository }

[Entity(Path = "aspects", PredicatePrefix = "forge/aspects")]
[Identity(IdentityGenerator.Path)]
[NoOperations]
public partial class Aspect
{
    [IdentityPart(0), Predicate("forEntity")] public partial string TargetEntityType { get; set; }
    [IdentityPart(1), Predicate("name")]      public partial string Name { get; set; }
    [Predicate("kind")]                       public partial AspectKind AppliesTo { get; set; }
    [Predicate("origin")]                     public partial AspectOrigin Origin { get; }
    [Predicate("shaclTtl")]                   public partial string ShaclAsTtl { get; set; }
    [Predicate("shaclSha256")]                public partial string ShaclSha256 { get; }
    [Predicate("localFragmentTtl")]           public partial string? LocalShaclTtl { get; set; }
    [Predicate("contextFragmentTtl")]         public partial string? ContextShaclTtl { get; set; }
}
```

Notes:

- `Origin` and `ShaclSha256` are **read-only** to consumers; the slice writes them
  through internal generated setters or backing fields.
- `[NoOperations]` (Trunk 1) prevents the Operations generator from emitting CRUD that
  would collide with the hand-written methods.
- The structural generator still emits identity / refs as for any other `[Entity]`.

### Hand-written CUD — co-located partial file in this slice

```csharp
public partial class Aspect
{
    public static ValueTask<Aspect?> ReadAsync(string iri, CancellationToken ct = default);
    public static IAsyncEnumerable<Aspect> ListAsync(
        string? targetEntityType = null,
        AspectOrigin? origin = null,
        CancellationToken ct = default);

    public ValueTask CreateAsync(CancellationToken ct = default);
    public ValueTask UpdateAsync(CancellationToken ct = default);
    public ValueTask DeleteAsync(CancellationToken ct = default);
}
```

Behaviour (per Aspects ADR-0003 §"CUD surface"):

- `ReadAsync(iri)`: consult `IShapeRegistry` (Code) first; on miss, load from the bound
  `IEntityStore`. Code-origin wins.
- `ListAsync(...)`: union both sources; Code-origin entries surface before
  Repository-origin entries with the same IRI. Filter by `targetEntityType` /
  `origin` if provided.
- `CreateAsync` / `UpdateAsync` / `DeleteAsync`:
  - If the IRI matches a Code-origin Aspect → `InvalidOperationException`
    (Code is read-only).
  - Parse `ShaclAsTtl` via dotNetRDF; throw `AspectTtlParseException` on failure
    **before** any store write.
  - Compute SHA-256 over the canonical UTF-8 bytes of `ShaclAsTtl`; assign to the
    `ShaclSha256` backing field.
  - Persist via `EntityOperations.RequireStore().SaveAsync` /
    `DeleteAsync`. Use the standard `WriteMode.Create` / `WriteMode.Replace` codes.
  - Evict the IRI from `IShapeCache`.
- All three methods route through the ambient `EntityOperations.RequireStore()`, same
  as generated active-record methods (Operations ADR-0001).

### Resolver refactor

`IAspectResolver` (introduced in Trunk 2) now merges:

1. The in-process `IShapeRegistry` (Code-origin).
2. `IEntityStore.QueryByTypeAsync<Aspect>(...)` filtered by `TargetEntityType` and
   `AppliesTo` (Repository-origin).

Snapshot semantics:

- The active set is captured **once at transaction begin** (i.e. at
  `AspectEnforcingTransactionalStore.ExecuteTransactionAsync` entry).
- Parsed `ShapesGraph` instances are cached by `ShaclSha256` in `IShapeCache`. A change
  to a Repository-origin Aspect mid-flight does not affect the in-flight transaction.

### Referential-integrity context shapes

Per Aspects ADR-0001 §"Referential-integrity context shapes":

- Allow code-origin Aspects to declare a flag (e.g. `kind: Referential`) or be
  registered through a separate API
  (`services.AddReferentialAspect(...)`). Pick one approach and document it in the
  slice's README.
- Evaluated **once** at the end of `ExecuteTransactionAsync`, **before** the underlying
  store commit, only if the transaction contained at least one Delete touching the
  type.
- Use the same Context-pass execution path (SPARQL via `ISparqlQueryStore`).

### Eager startup loading

Move the eager-load logic from Trunk 2's provisional registration code into the
`Aspect`-aware path:

- `AddCodeAspect("path/to/file.ttl", forType: typeof(Artist), kind: AspectKind.Create)`
  reads the file at startup, parses, computes SHA-256, materializes a runtime `Aspect`
  instance with `Origin = Code`, registers in `IShapeRegistry`.
- Malformed TTL → throw at `AddForgeAspects()` startup (existing behaviour from Trunk 2).

### Test coverage — extend `tests/Entity.Aspects.Tests/`

Against InMemory backend, using `Entity.Tests.Fixtures`:

1. **Repository-origin CUD round-trip**: create an `Aspect` for `Artist` /
   `AspectKind.Update` that requires `:country` to be `"us"`. Persist via
   `aspect.CreateAsync()`. Verify it's retrievable via `Aspect.ReadAsync(iri)` and
   `Aspect.ListAsync(forEntity: "music:Artist")`. Verify it now blocks an Update on a
   non-US Artist.
2. **Code-origin Aspect listed but not editable**: register one via `AddCodeAspect(...)`.
   Verify it appears in `Aspect.ListAsync(...)`. Verify
   `aspect.UpdateAsync()` throws `InvalidOperationException`.
3. **Identity collision**: register a Code-origin Aspect with IRI `X`; attempt to
   `CreateAsync` a Repository-origin Aspect with the same IRI. Expect
   `InvalidOperationException`. `Aspect.ReadAsync(X)` returns the Code-origin instance.
4. **Malformed TTL on CreateAsync**: write fails with `AspectTtlParseException` and the
   store contains no Aspect at that IRI afterwards.
5. **Cache eviction**: persist a Repository-origin Aspect that disallows X; observe
   blocked Update; `aspect.UpdateAsync()` to a relaxed shape; observe a previously
   blocked Update now succeed.
6. **Resolver snapshot semantics**: begin a transaction, edit a Repository-origin
   Aspect outside the transaction, verify that the in-flight transaction continues
   using the pre-edit shape until commit.
7. **Referential-integrity shape**: declare one for `Artist` that disallows orphan
   `Album` references; deleting an Artist with Albums fails the transaction.
8. **Startup malformed TTL**: a code-origin TTL file with a syntax error makes
   `AddForgeAspects()` throw `AspectTtlParseException` at startup.

## Acceptance criteria

- `dotnet test` passes.
- All eight test cases above are covered.
- `Aspect.ReadAsync` / `ListAsync` consistently merge both origins per ADR-0003.
- The structural generator emits the identity / ref half of `Aspect` correctly (snapshot
  test in `Entity.Generators.Tests` for the new entity).
- The Operations generator emits **nothing** for `Aspect` (snapshot test in
  `Entity.Generators.Tests`).
- The Aspects engine in Trunk 2 keeps working unchanged for code-origin only consumers
  who don't load Repository-origin Aspects.

## Out of scope

- GraphDB backend integration tests (Trunk 4).
- Per-call `EntityTransaction.WithAspect(...)` (Trunk 5).
- Aspect-edit isolation from concurrent in-flight transactions beyond the snapshot
  semantics above (deferred — see ADR-0001 Known restrictions).
- Self-Aspect application (deferred — see ADR-0001 Known restrictions).

## Suggested invocation

> `/Forge-Developer Implement Trunk 3 per spec/aspects-v1/trunk-03-aspect-entity-and-cud.md and Aspects ADR-0003.`
