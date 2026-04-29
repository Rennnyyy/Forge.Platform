# 0001 — Identity is owned by Entity (no separate Identity project)

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

Originally a separate `Forge.Identity` project was scaffolded to host IRI-generation code. In RDF terms identity *is* the entity — splitting them creates a dependency cycle in spirit (Entity needs identity to exist; Identity has nothing else to do).

## Options

1. **Identity types live inside `Forge.Entity`.** Generators, attributes, runtime helpers all in one assembly.
2. Keep `Forge.Identity` as a sibling. Pro: physical isolation. Con: every entity drags in two assemblies; circular evolution.
3. Identity as `internal` namespace inside Entity but published separately later if needed.

## Decision

Option 1. The `Forge.Identity` project was deleted. All identity code lives under `Forge.Entity` (attributes, runtime types, source generator).

## Consequences

- One assembly to reference.
- If a different identity backend is ever needed, introduce it as a strategy plugin behind `IdentityGenerator`, not as a separate project.
