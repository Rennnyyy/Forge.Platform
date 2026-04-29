# 0002 — `[Entity]` partial class + Roslyn source generator

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

User-authored entities must stay clean POCOs but also need IRI materialization, equality, owning/inverse plumbing, and hydration support. We must pick a way to express "this is an entity" and generate the boilerplate.

## Options

1. Abstract base class users inherit from. Pro: simplest. Con: blocks single inheritance, every entity carries identical fields by hand.
2. Marker interface + extension methods. Pro: composable. Con: interfaces can't carry private state.
3. **`[Entity]` attribute on a `partial class`; a Roslyn incremental source generator emits the second partial half** (inheriting `EntityBase`, implementing `IEntity`, providing identity / refs / collections).
4. Runtime reflection / proxies. Pro: zero codegen. Con: AOT-hostile, slower startup, hard to debug.

## Decision

Option 3. Users write a `partial class` annotated with `[Entity(Path = "...", PredicatePrefix = "...")]` plus exactly one `[Identity(...)]`. The generator project (`Forge.Entity.Generators`, `netstandard2.0`) emits a sibling `partial` half that inherits `EntityBase`. Properties on owning/inverse references are declared `partial` by the user; the generator emits their bodies.

## Consequences

- Compile-time errors for missing `partial`, missing `[Identity]`, disallowed primitive types, broken inverse links (`FORGE0001..0004` and growing).
- Generator output is debuggable (`EmitCompilerGeneratedFiles` is on by default in modern SDKs).
- AOT-friendly, no runtime reflection over user types for identity/equality/refs.
- The user's project must reference `Forge.Entity.Generators` as `OutputItemType="Analyzer"`.
