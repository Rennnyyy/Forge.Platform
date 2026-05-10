# SLICING — Forge.Entity.Tests.Fixtures

Shared test fixture project per [root ADR-0007](../../adr/0007-shared-test-fixtures.md).

## Purpose

Provides a canonical set of sample entity types (e.g. `Artist`) and the
`EntityOptionsFixture` xUnit collection fixture to all test projects that exercise
entity persistence behavior. Using a shared fixture avoids duplicating entity
registration boilerplate and ensures all backends are validated against the same
domain model.

## Dependencies

| Package / Project | Reason |
|-------------------|--------|
| `Forge.Entity` | Entity base types, `IEntity`, `EntityOptions`. |
| `Forge.Entity.Generators` | Analyzer-only reference: generates identity properties on sample entities. |
| `Forge.Operations` | **Active-record methods** (`CreateAsync`, `ReadAsync`, etc.) generated on sample entity classes via the operations generator reference below. |
| `Forge.Operations.Generators` | Analyzer-only reference: generates `.CreateAsync()`, `.ReadAsync()`, etc. stubs on sample entities. |

### Note on Forge.Operations coupling

The dependency on `Forge.Operations` is intentional: the generated active-record stubs
on fixture entities (e.g. `artist.CreateAsync()`) are exercised by behavioral test
projects (Operations.Tests, Aspects.Tests, etc.). This means all test projects that
reference `Forge.Entity.Tests.Fixtures` transitively pull in `Forge.Operations`.

Test projects that only need sample entity *types* without the active-record surface
(Repository.Tests, Sparql.Tests, Authorization.Tests, Repository.GraphDb.Tests) reference
`Forge.Entity.Tests.Fixtures.Core` instead. See
[tests/Entity.Tests.Fixtures.Core/SLICING.md](../Entity.Tests.Fixtures.Core/SLICING.md)
for details.

**Partial-class constraint**: The entity type definitions in `Sample/` are duplicated
verbatim in the Core project. This is unavoidable because C# partial classes cannot span
assemblies — `Forge.Operations.Generators` generates `partial class Artist { … }` in the
assembly that references it, so the original entity type must live in the same project.
When updating a sample entity class, update both copies (`Sample/` here and in Core).

### Consumer guide

| Test project needs | Reference |
|---|---|
| Active-record methods (`.CreateAsync()`, `.ListAsync()`, etc.) | `Forge.Entity.Tests.Fixtures` |
| Entity types + `EntityOptionsFixture` only (no active-record) | `Forge.Entity.Tests.Fixtures.Core` |

## File assignment

| File | Description |
|------|-------------|
| `EntityOptionsFixture.cs` | xUnit `IClassFixture` that registers `EntityOptions` for all sample types. |
| `Sample/` | Subfolder containing sample entity class definitions (`Artist`, etc.). |
