# 0008 — Remove `Entity.` prefix from satellite package/namespace names

- **Status**: accepted; supersedes [0004](0004-namespace-convention.md)
- **Date**: 2026-05-02
- **Author**: agent

## Context

ADR-0004 established `Forge.<Library>` as the namespace and assembly-name pattern. In
practice the first cohort of libraries all grew around the Entity core, so every satellite
was named `Forge.Entity.<Something>` (e.g. `Forge.Repository`,
`Forge.Aspects`, `Forge.Sparql`).

This creates two problems:

1. **Misleading coupling signal** — a consumer of `Forge.Repository` is not consuming
   something specific to Entity; it is consuming a general RDF repository abstraction.
   The `Entity.` infix implies the library is internal to Entity, when the intent is for
   it to be a first-class, independently-versioned slice.
2. **Future naming collision** — as new slices appear, every cross-cutting concern would
   need an `Entity.` prefix even when it has nothing conceptually to do with Entity's
   type-system machinery.

## Options

1. **Remove `Entity.` prefix from every satellite** — libraries that stand on their own
   (`Repository`, `Aspects`, `Operations`, `Sparql`, their sub-libraries and generators)
   are renamed to drop the `Entity.` segment. Core and its generator keep the prefix:
   `Forge.Entity` and `Forge.Entity.Generators`. Directories, `.csproj` names, assembly
   names, and root namespaces all change together.
2. Keep status quo. Con: the problems above grow as the platform matures.
3. Rename only the directory/project, keep the C# namespace. Con: the project name and
   namespace diverge, violating ADR-0004's requirement that they match.

## Decision

Option 1. The `Entity.` infix is removed from every satellite. Specifically:

| Old name | New name |
|----------|----------|
| `Forge.Aspects` | `Forge.Aspects` |
| `Forge.Operations` | `Forge.Operations` |
| `Forge.Operations.Generators` | `Forge.Operations.Generators` |
| `Forge.Repository` | `Forge.Repository` |
| `Forge.Repository.GraphDb` | `Forge.Repository.GraphDb` |
| `Forge.Repository.InMemory` | `Forge.Repository.InMemory` |
| `Forge.Sparql` | `Forge.Sparql` |

The following names are **unchanged**:

| Name | Reason |
|------|--------|
| `Forge.Entity` | Core identity library; the `Entity` segment *is* the library concept |
| `Forge.Entity.Generators` | Source generator for the Entity core; tightly scoped to it |
| `Forge.Entity.Tests` | Test project for the Entity core |
| `Forge.Entity.Tests.Fixtures` | Shared sample domain for Entity-adjacent tests |
| `Forge.Entity.Generators.Tests` | Test project for the Entity generator |

ADR-0004's fundamental rule — `project name = assembly name = root namespace = Forge.<Library>` — is preserved; only the library segment changes.

## Consequences

- All `using Forge.Repository;` etc. statements in every `.cs` file must be
  replaced.
- Directory names under `src/` and `tests/` change accordingly.
- `.slnx` paths and `<ProjectReference>` paths in every `.csproj` must be updated.
- ADR prose that cites old names must be updated (in-place, as prose corrections, not new ADRs).
- Consumers of the NuGet packages (if any) must update their package references.
