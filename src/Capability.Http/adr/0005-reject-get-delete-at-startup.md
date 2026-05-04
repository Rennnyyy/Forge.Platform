# 0005 — Reject GET and DELETE in `MapCapabilities()` at startup

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0004 notes that GET & DELETE are "explicitly out of scope" for `MapCapabilities()`
auto-discovery because ASP.NET Minimal API does not bind complex types from the request
body for those HTTP methods. However, the attribute still accepted any method string, and
the discovery loop did not validate the method. A handler decorated with
`[CapabilityEndpoint("GET")]` would be silently registered and would only throw a
cryptic binding error at request time, not at startup.

The documented workaround — register GET handlers manually via `app.MapGet()` outside the
auto-discovery path — was added to the sample to illustrate the pattern. Users found this
two-tier registration model confusing and error-prone, and the escape hatch undermined the
zero-ceremony guarantee of `MapCapabilities()`.

## Options

1. **Fail fast at `MapCapabilities()` time** — if any registered handler carries
   `[CapabilityEndpoint("GET")]` or `[CapabilityEndpoint("DELETE")]`, throw
   `InvalidOperationException` with a clear message naming the handler. Remove the
   manual-registration escape hatch from documentation and samples.
2. Support GET/DELETE with query-string or route-parameter binding by requiring the
   command type to implement a dedicated marker interface and generating the binding
   delegate at startup. Con: significant complexity; defers the binding-model contract
   to caller conventions outside this slice's control.
3. Keep the current behaviour (silent runtime failure) with an updated warning comment.
   Con: poor developer experience; nothing in the normal startup log surface this issue.

## Decision

Option 1.

### Guard condition: bodyless HTTP methods

HTTP methods that prohibit request-body binding (`"GET"` and `"DELETE"`) are rejected by
`MapCapabilities()` at startup. If a handler carries
`[CapabilityEndpoint("GET")]` or `[CapabilityEndpoint("DELETE")]`, an
`InvalidOperationException` is thrown naming the handler type and explaining the
constraint.

### Allowed methods

Methods that conventionally carry a request body are supported:
`POST` (default), `PUT`, `PATCH`. Any other non-bodyless method (e.g. custom verbs)
is also allowed; the guard is limited to the two known bodyless methods because they
are the only ASP.NET Minimal API methods that reject complex-type body binding.

### Sample cleanup

The manual `app.MapGet()` escape hatch demonstrated in the `Capability.Http.Sample` and
the corresponding `GetItemHandler` are removed. GET query use-cases should be modelled
as POST commands or handled outside `MapCapabilities()` at the application level.

## Consequences

- Developers who mistakenly annotate a handler with `[CapabilityEndpoint("GET")]`
  receive a clear `InvalidOperationException` at startup rather than a cryptic 400 at
  request time.
- The `Capability.Http.Sample` no longer demonstrates manual-registration workarounds,
  reinforcing the zero-ceremony model.
- Applications that legitimately need GET endpoints must implement them outside the
  `MapCapabilities()` pipeline (e.g. standard `app.MapGet()`) — this is an accepted
  trade-off for the simplicity and correctness guarantees inside the pipeline.
