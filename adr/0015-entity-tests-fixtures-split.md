# 0015 — Split entity test fixtures into Core and full-CRUD projects

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

`Entity.Tests.Fixtures` provides shared sample entity types (`Artist`, `Album`, `Label`,
`Track`, `FeaturedArtist`) consumed by tests that exercise the full active-record surface
(`.CreateAsync()`, `.ReadAsync()`, `.UpdateAsync()`, `.DeleteAsync()`). The active-record
stubs are emitted by `Forge.Operations.Generators` via source generation.

`Entity.Tests.Fixtures.Core` was introduced (prior to this ADR) to provide a lighter
fixture set for tests that only need entity identity properties, not CRUD operations.
`Core` references `Forge.Entity.Generators` but **not** `Forge.Operations.Generators`,
keeping it free of the Operations stack.

The same five sample entity source files were verbatim-duplicated across both fixture
projects. The duplication was unavoidable with the initial approach because Roslyn source
generators emit `partial class` extensions per-compilation: each project that wants
generated extensions must compile the base class itself.

## Problem

Verbatim duplication of five files violates the single-source-of-truth principle:

- A change to an entity definition (e.g. adding a property) must be applied in both
  projects; forgetting one causes silent divergence that only surfaces as a test failure.
- Reviewers see two copies of the same change, increasing review noise.

## Options

1. **`<Compile Include>` link (MSBuild item link)** — Keep the source files in Core only;
   add an MSBuild `<Compile Include="..\Entity.Tests.Fixtures.Core\Sample\*.cs" LinkBase="Sample" />`
   item to `Forge.Entity.Tests.Fixtures.csproj`. Both projects compile the same physical
   files; each Roslyn generator sees the class in its own compilation and emits its own
   `partial` extensions. Source = one copy; generated output = two copies (correct).

2. **Extract a shared non-generated assembly** — Move entities to a third project with no
   generators; both fixture projects reference it. Requires that the generated extensions
   target a type defined in an external assembly, which Roslyn generators currently do
   not support (generators use syntax trees of the compilation's own source).

3. **Keep duplication; add snapshot test** — Accept two copies and add a CI step that
   diffs the two file sets. Detects drift but does not prevent it.

## Decision

Option 1 — MSBuild `<Compile Include>` link.

The five sample entity files live exclusively in `Entity.Tests.Fixtures.Core/Sample/`.
`Forge.Entity.Tests.Fixtures.csproj` links them via:

```xml
<ItemGroup>
  <Compile Include="..\Entity.Tests.Fixtures.Core\Sample\*.cs" LinkBase="Sample" />
</ItemGroup>
```

`LinkBase="Sample"` preserves the `Sample/` logical path in the IDE file tree so
navigation is unaffected. Both compilations see all five classes; `Entity.Generators`
and `Operations.Generators` each emit their respective `partial` extensions
independently, which is the correct behaviour.

## Consequences

- A single physical copy of each entity file; drift is structurally impossible.
- Each fixture project still gets its own generated extensions (entity-only in Core;
  entity + CRUD in the full fixture project).
- Moving or renaming an entity file requires updating only `Entity.Tests.Fixtures.Core`.
- No change to the public API of either fixture assembly; consumers are unaffected.
