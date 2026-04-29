# 0002 — .NET 10 + Central Package Management

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

We need a single target framework and a uniform way to manage NuGet versions across projects.

## Options

1. **.NET 10 everywhere, CPM via `Directory.Packages.props`** at the root.
2. .NET 10 with PackageReferences carrying `Version=` per project. Pro: simple. Con: drift between projects.
3. Multi-targeting (`net10.0;net8.0`). Pro: wider consumer base. Con: more matrix complexity, no current consumer requires it.

## Decision

Option 1. All projects target `net10.0` except Roslyn source generators which target `netstandard2.0` (Roslyn requirement). Versions are pinned exclusively in `Directory.Packages.props`. Package source mapping is left to user-level NuGet config; warning `NU1507` is suppressed at the repo level.

## Consequences

- Adding a package = add `<PackageVersion>` once at root + `<PackageReference Include="..." />` (no version) in the project.
- Bumping versions touches one file.
- Generator projects keep their own `<TargetFramework>netstandard2.0</TargetFramework>`; they may not use BCL APIs newer than .NET Standard 2.0.
