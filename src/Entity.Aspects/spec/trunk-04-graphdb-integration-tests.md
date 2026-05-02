# Trunk 4 — GraphDB integration tests for Aspects

- **Owner**: Repository.GraphDb agent
- **Prerequisites**: Trunk 3 complete
- **ADRs**:
  - [Aspects ADR-0001](../../src/Entity.Aspects/adr/0001-split-shape-validation.md) §"Engine seam" — Context shapes must execute against `POST {txUrl}` (transaction-local state) on GraphDB.
  - [Aspects ADR-0002](../../src/Entity.Aspects/adr/0002-no-native-graphdb-shacl.md) — Forge does not write to `<rdf4j:SHACLShapeGraph>`.

## Goal

Verify Aspects v1 end-to-end against a live Ontotext GraphDB instance, mirroring the
InMemory-backed coverage from Trunks 2 and 3. Add a regression guard that asserts
Forge never writes to GraphDB's reserved SHACL shape graph.

## Scope

- Test additions only. No production changes expected; if any are needed (e.g. a small
  fix to ensure Context shapes correctly use `{txUrl}`), they belong in this trunk.
- Use the existing `GraphDbFixture` (Entity ADR-0014 — Podman / Docker auto-detect).

## Deliverables

### `tests/Entity.Repository.GraphDb.Tests/Aspects/`

A new folder mirroring the structure of `tests/Entity.Aspects.Tests/`. Add a
`ProjectReference` to `Forge.Entity.Aspects` and `Forge.Entity.Aspects.Tests` (only if
shared test helpers exist; otherwise duplicate the minimal fixture wiring).

Tests required, all gated on `GraphDbFixture.Available`:

1. **Local-shape violation on Create rolls back**: end-to-end Create against GraphDB
   with a violating entity → `AspectViolationException`; verify the entity is **not**
   present in the repository afterwards (REST tx rolled back per Repository.GraphDb
   ADR-0002).
2. **Context-shape violation (SPARQL) on Update rolls back**: same pattern for Update.
   Critically, the Context shape's SPARQL must run against the transaction-local state
   — i.e. against `POST {txUrl}`, not against the committed graph. Verify by including
   in the same transaction an earlier operation whose effects must be visible to the
   Context shape; assert the Context shape sees them.
3. **Per-operation queue-order semantics**: same scenario as Trunk 2 test #3 but
   end-to-end on GraphDB.
4. **Implicit `[Required]` shape**: Create on GraphDB rejects an entity missing a
   required scalar.
5. **Repository-origin Aspect round-trip**: persist an `Aspect` to GraphDB,
   `Aspect.ReadAsync` from another store reference returns it, and the engine enforces
   it on a subsequent transaction.
6. **Referential-integrity shape**: Delete with a violating closure fails on GraphDB.
7. **Regression — `<rdf4j:SHACLShapeGraph>` is never written by Forge**:
   - Start with a fresh fixture-managed repository.
   - Register multiple code-origin Aspects via `AddCodeAspect(...)` and one
     Repository-origin Aspect via `aspect.CreateAsync()`.
   - Issue a SPARQL query that selects all triples in
     `GRAPH <http://rdf4j.org/schema/rdf4j#SHACLShapeGraph> { ?s ?p ?o }`.
   - Assert the result set is **empty**. This is the runtime enforcement of ADR-0002.

### Skip behaviour

All tests must skip gracefully when `GraphDbFixture.Available` is `false`, matching the
existing `Entity.Repository.GraphDb.Tests` skip convention.

## Acceptance criteria

- `dotnet test` passes locally with Podman or Docker available.
- `dotnet test` passes (with skips) on environments without a container runtime.
- All seven test cases above are covered.
- No production code changes outside `Forge.Entity.Aspects` are required; if a fix to
  `Forge.Entity.Repository.GraphDb` is required to route Context-shape SPARQL through
  `{txUrl}` rather than the base endpoint, it is bundled with this trunk and a small
  follow-up note is added to Repository.GraphDb ADR-0002 (no new ADR; the existing one
  already describes the txUrl-bound query path).

## Out of scope

- Performance benchmarking.
- Native GraphDB SHACL — explicitly forbidden by ADR-0002; the regression test guards
  against accidental introduction.

## Suggested invocation

> `/Forge-Developer Implement Trunk 4 per spec/aspects-v1/trunk-04-graphdb-integration-tests.md and Aspects ADR-0001 / ADR-0002.`
