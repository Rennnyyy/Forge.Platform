# 0002 — Error-path demonstration chapter in Application.Sample

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0013 established that Application.Sample is organised as story chapters, each
demonstrating one thematic group of capabilities. Chapters 01–04 cover the happy path:
greet, CRUD for Book and DataRecord, and catalog management. None of them show what
happens when a capability intentionally signals failure.

Capability ADR-0005 introduced `CapabilityResult<TResponse>.Fail(CapabilityError)` as the
structured, non-exception way for a handler to communicate a business-rule rejection or
any other expected failure condition. The Capability.Http transport maps `Fail` to
`422 Unprocessable Entity` with the `CapabilityError` payload as the JSON body.

Without a concrete example in the sample app, contributors have no living reference for
how to write and test an error path:

- No handler in the sample ever returns `Fail`.
- The Bruno collection never exercises the 422 branch of the HTTP transport.
- `BrunoIntegrationTests` has no test asserting a non-2xx response is delivered correctly.

## Options

1. **Add a dedicated `TriggerFaultHandler` as chapter 05** — a capability whose sole
   purpose is to return `Fail` on every call. No command payload is semantically
   significant; the handler is a pure demonstration artefact.
   Pro: chapter is self-explanatory; one request, one assertion, zero ambiguity.
   Con: the capability has no real-world business meaning (by design).

2. **Extend the Catalog chapter** with an update-nonexistent-item request that
   triggers the existing `ITEM_NOT_FOUND` `Fail` path.
   Pro: exercises a realistic failure (item lookup miss).
   Con: requires state from an earlier request (create) and assumes ordering; using a
   random UUID that never existed would work but mixes concerns in the catalog chapter.

3. **No dedicated demo** — rely on the `CatalogCapability` `Fail` paths already present
   in production code for documentation.
   Con: the `Fail` path has no Bruno coverage at all; onboarding contributors cannot see
   end-to-end what a failure response looks like.

## Decision

Option 1. A new handler `TriggerFaultHandler` with capability identity `demo.fault` is
added to `samples/Application.Sample/Capabilities/`. It always returns

```csharp
new CapabilityResult<TriggerFaultResponse>.Fail(
    new CapabilityError("DEMO_FAULT", "This handler always fails by design."))
```

A fifth Bruno chapter `05-error-demo/` contains one request (`01-trigger-fault.bru`)
that POSTs to `POST /api/capabilities/demo/fault` and asserts:

- `res.status == 422`
- `res.body.code == "DEMO_FAULT"`

`BrunoIntegrationTests` receives a corresponding test `Bruno_05_error_demo_requests_all_pass`.

## Consequences

- The 422 / `Fail` branch of `EndpointRouteBuilderExtensions.RegisterEndpoint` is now
  covered by the Bruno integration test suite.
- Future capabilities that return `Fail` can point contributors to
  `TriggerFaultHandler` + chapter 05 as the canonical reference example.
- ADR-0013 chapter numbering: chapters 01–04 remain unchanged; 05 is appended.
- ADR-0013 rule: "Any new capability or entity added to Application.Sample gets a new
  numbered chapter" is satisfied by this ADR.
