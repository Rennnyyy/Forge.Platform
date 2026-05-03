# Forge.Platform

> **New here?** Start with `/Forge-Explorer` in GitHub Copilot Chat for a quick friendly pitch,  
> or `/Forge-Storyline` for the full guided tour. See [Explore the platform](#explore-the-platform) below.

A .NET library platform for building richly-typed, RDF-aware domain models.  
Define clean POCOs, get globally unique IRI-based identity for free, and navigate relations lazily — all without reflection, magic base classes, or forced dependencies.

---

## Explore the platform

Open **GitHub Copilot Chat** in VS Code and pick your adventure:

| Command | What it does |
|---|---|
| `/Forge-Storyline` | The medieval guided tour — warm, vivid, no technical scrolls. Great first stop. |
| `/Forge-Explorer` | Quick friendly pitch, dwarf-style. Good for sharing with teammates. |
| `/Forge-Architect` | Deep technical tour: every ADR explained, every type catalogued, every test file mapped. |
| `/Forge-Developer` | Fires up the full dev workflow — reads ADRs, locates the right slice, implements with proper design discipline. |
| `/Forge-Documentation` | Generates the HTML dependency diagram and project overview for `docs/`. |
| `/Forge-Automation` | Draft a new prompt and wire it into every existing guide. |

**Examples:**
```
/Forge-Storyline
/Forge-Architect — give me the full technical picture
/Forge-Developer I want to add a validation API to the Entity slice.
```

---

## What's inside

| Slice | Purpose |
|---|---|
| `Forge.Entity` | Runtime type system. Annotate a `partial class` with `[Entity]`, `[Identity]`, `[Owning]`, `[Inverse]` — the Roslyn source generator handles the rest at compile time. |
| `Forge.Repository` | Persistence abstractions: `IEntityStore`, `IEntityRepository<T>`, `IRdfMapper<T>`. Translates .NET objects to/from RDF triples via your attribute metadata. |
| `Forge.Repository.InMemory` | dotNetRDF-backed in-memory store — ideal for tests and fixtures. |
| `Forge.Repository.GraphDb` | HTTP-backed Ontotext GraphDB store for production. Backend is config-driven (`Forge:EntityRepository:Backend`). |
| `Forge.Aspects` | Two-pass write validation: local SHACL shape checks (`IWriteAspect`), then cross-entity SPARQL constraints — zero changes to your entity model required. |
| `Forge.Operations` | Source-generated typed create / update / delete command objects for your entities. |
| `Forge.Sparql` | SPARQL query construction, rewriting, security filters, and federation. |
| `Forge.Capability` | `IMessageAspect` validation leg for capability messages (commands, responses, events) — permissive by default, SHACL-enforced when a shape is registered. |
| `Forge.Validation` | `IOperationGuard` authorization hook — allow-all by default, opt-in enforcement via the guarded store decorator. |

---

## Quick start

```csharp
[Entity]
public partial class Product
{
    [Identity(IdentityStrategy.PathSlug)]
    public string Slug { get; set; }

    [Owning]
    public Category Category { get; set; }
}
```

- Stable IRIs — no GUIDs leaking into your URLs unless you want them.
- `await entity.Neighbour` — lazy relation loading with no boilerplate.
- Identity flavours: path slug, UUID v4, or deterministic UUID v5.
- Override config per-request via `EntityOptions.Use(...)` — test-friendly by design.

---

## Repository layout

```
src/
  Entity/
  Repository/
  Repository.InMemory/
  Repository.GraphDb/
  Aspects/
  Operations/
  Sparql/
tests/
  ...
```

Every slice is independently NuGet-consumable. Pure-domain projects never pay for persistence or validation.  
Every design decision lives in an ADR alongside its slice — nothing is mysterious.

---

## Architecture decision records

Each slice carries its own `adr/` folder. Start with `src/Entity/adr/` for the core type system design.  
Notable decisions include:

- **Root ADR-0005** — `Forge.Entity` owns the type system only; persistence is a separate opt-in slice with two backends sharing one abstraction.
- **Entity ADR-0002** — `[Entity]` on a `partial class` triggers Roslyn code generation at compile time — no reflection, no runtime overhead, no magic base class.
- **Entity ADR-0004** — Lazy neighbours are loaded via `await foo.Bar`; `EntityRef<T>` is awaitable and `EntitySession` carries the load context without polluting entity constructors.
- **Aspects ADR-0001** — Aspect validation runs in two passes: a local SHACL check (intra-entity) then a SPARQL context check (cross-entity), keeping each concern independently testable.
- **Aspects ADR-0003** — Aspects are caller-declared per operation, not engine-discovered globally; the default is a no-op, so unvalidated operations are intentional rather than misconfigured.

---

## Requirements

- .NET 9+
- Roslyn (source generation, compile-time only)
- Ontotext GraphDB *(production backend only)*