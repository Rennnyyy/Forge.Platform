# 0003 — Capability-aspect demonstration chapter in Application.Sample

- **Status**: accepted
- **Date**: 2026-05-05
- **Author**: agent

## Context

Chapters 01–05 demonstrate handler invocation, entity CRUD, catalog management, and the
`CapabilityResult.Fail` path. None of them show the aspect pipeline that runs before and
after every handler: how a caller selects a validation policy via
`X-Forge-Capability-AspectIri`, how the dispatcher resolves a `CapabilityAspect` from
`IAspectStore`, and how the handler can inspect `CapabilityContext.Aspect` to confirm
which policy was active.

Without a concrete working example, contributors have no reference for:
- Registering a `CapabilityAspect` (and the `IMessageAspect` it points to) in the store.
- Confirming that the resolved aspect IRI is forwarded to the handler through
  `CapabilityContext`.
- Observing permissive execution (no aspect header) vs. policy-bound execution (with
  aspect header) side by side via Bruno.

## Options

1. **Dedicated `demo.aspect` capability** — a purpose-built handler `AspectDemoHandler`
   that accepts a `Name` string command, validates it with a registered SHACL shape when
   an aspect IRI is supplied, and reflects `context.Aspect?.Iri` back in the response.
   A Bruno chapter `06-aspect-demo/` contains two requests: one without the aspect header
   (permissive) and one with a registered aspect IRI and conforming data (validation passes).
   Pro: self-contained; shows both the permissive and policy-bound paths clearly.
   Con: the SHACL violation path is not in Bruno (it would surface as an unhandled 500
   in the current HTTP layer — a separate Capability.Http concern).
2. **Extend `demo.greet`** to register a real aspect and assert the reflected IRI.
   Con: mixing concerns in an existing chapter; chapter 01 currently demonstrates only
   the greeting flow.
3. **No sample chapter; rely on unit tests only.**
   Con: contributors cannot observe the IRI lifecycle through a running application.

## Decision

Option 1.

### Handler contract

```csharp
[Capability("demo.aspect")]
public class AspectDemoHandler : ICapabilityHandler<AspectDemoCommand, AspectDemoResponse>
```

- `AspectDemoCommand(string Name)` — a command with one required non-nullable string.
- `AspectDemoResponse(string Name, string? ActiveAspectIri)` — echoes `command.Name`
  and `context.Aspect?.Iri`. `ActiveAspectIri` is `null` when no aspect was resolved
  (permissive dispatch).

### Aspect registration

Two aspects are registered in `Program.cs` post-build (before first request) by
resolving `IAspectStore` directly:

| IRI | Kind | Purpose |
|-----|------|---------|
| `urn:forge:aspects:demo-command-v1` | `IMessageAspect` | SHACL shape: `Name` must have `sh:minLength 1` |
| `urn:forge:aspects:capability:demo-v1` | `CapabilityAspect` | Bundles `CommandAspectIri = urn:forge:aspects:demo-command-v1` |

Direct registration on `IAspectStore` (via `store.RegisterMessage` /
`store.RegisterCapabilityAspect`) is used rather than the `AddMessageAspect` DI helper
because `AddMessageAspect` relies on `PendingAspectRegistrations`, which only executes
when `IEntityStore` or `ITransactionalEntityStore` is first resolved. In the current
`AddForgeAspects` → `UseInMemory` ordering in `Program.cs`, the decorated `IEntityStore`
factory is not created (see `AspectsServiceCollectionExtensions`), making the pending
mechanism unreliable for pure-capability handlers. Direct post-build registration
bypasses this ordering dependency entirely.

### Bruno chapter layout

| File | Request | Assertion |
|------|---------|-----------|
| `01-without-aspect.bru` | POST `/api/capabilities/demo/aspect` — no header | `res.status == 200`, `res.body.activeAspectIri` is null/absent |
| `02-with-aspect-valid.bru` | POST with `X-Forge-Capability-AspectIri: urn:forge:aspects:capability:demo-v1` | `res.status == 200`, `res.body.activeAspectIri == urn:forge:aspects:capability:demo-v1` |

### Violation path

When a registered aspect IRI is supplied and the command fails the SHACL shape,
`IMessageAspectEngine` throws `MessageAspectViolationException`. This exception is
currently unhandled at the HTTP layer (no mapping exists in `EndpointRouteBuilderExtensions`)
and surfaces as a 500. Adding structured error mapping for SHACL violations is a separate
Capability.Http concern and is out of scope for this chapter.

## Consequences

- Contributors now have a living, runnable example of the capability aspect pipeline.
- Bruno chapter 06 is a `[SkippableFact]` in `BrunoIntegrationTests` under
  `Bruno_06_aspect_demo_requests_all_pass`.
- ADR-0013 chapter numbering is extended: chapters 01–05 unchanged; 06 appended.
- The `demo.aspect` endpoint is registered under `api/capabilities/demo/aspect` by the
  HTTP transport following Capability.Http ADR-0002 route derivation rules.
