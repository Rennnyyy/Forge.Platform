# 0025 — Entity and Capability as the exclusive platform extension model

- **Status**: accepted
- **Date**: 2026-05-15
- **Author**: agent

## Context

As the platform has grown, successive slices have independently introduced bespoke HTTP
routes, custom response DTOs, and ad-hoc DI helpers instead of reaching for the entity and
capability machinery that already exists. ADR-0017 and ADR-0019 corrected two concrete
cases of this drift after the fact (`Forge.Branch.Http`). No upfront, platform-wide rule
existed to prevent the pattern from recurring in the next slice.

The `Forge.Structure` slice — introduced concurrently — demonstrates the target state in
full: structure nodes are `[Entity]` types with `[OperationEndpoints]`-driven CRUD, and the
non-trivial configured-tree query is an `ICapabilityHandler` decorated with `[Capability]`.
No bespoke routes, no custom response envelopes, no escape hatches.

A forward-looking rule is needed that makes the Structure pattern the *only* permitted path
and reserves the backing-store exception for the sole current case that genuinely requires it.

## Options

### Option A — Remain ADR-reactive (status quo)

Document the pattern in each new slice's ADR; correct divergence post-hoc.  
Con: the cost of correction compounds; ADR-0017 and ADR-0019 together required
significant rework. Pattern knowledge is scattered across slice ADRs rather than
being a single authoritative mandate.

### Option B — Platform-wide mandate in a root ADR

A single root ADR that states the rule positively and names the sole recognised exception.
All future slice ADRs defer to this rule; they only need to document what their `[Entity]`
types and `[Capability]` handlers are, not re-argue the model.  
Con: a mandatory rule is only as useful as its enforcement. No automated check exists
yet; compliance relies on ADR review.

## Decision

**Option B.**

### The rule

Every platform concept that carries identity and mutable state must be modelled as an
`[Entity]`-annotated type. Its create / read / update / delete surface must be exposed
through `[OperationEndpoints]` + `MapOperations()`. Any operation beyond plain CRUD —
whether exposed in-process or over HTTP — must be expressed as an
`ICapabilityHandler<TCommand, TResponse>` carrying a `[Capability]` identity attribute.
No other mechanism is admissible for externalising platform functions.

This rule applies regardless of transport. Dispatching a capability asynchronously via
`Forge.Capability.Messaging` is the same handler over a different transport, not a
separate surface category (see also Capability ADR-0020).

### The sole recognised exception

A slice may hand-write its own CRUD only when it must directly orchestrate RDF store
topology in ways that `EntityTransaction` cannot express — for example named-graph
isolation, cascade graph drop, or snapshot seeding that spans named graphs.
`Forge.Branch` / `Forge.Snapshot` is the canonical and currently sole instance of this
exception. Any future slice claiming the exception must argue the case in its own ADR
and must still satisfy ADR-0017 and ADR-0019 in full.

### Canonical reference implementation

`Forge.Structure` (`src/Structure/`):

- `Node`, `Usage` — `[Entity]` types; CRUD via `[OperationEndpoints]`.
- `GetConfiguredTreeCapability` — `ICapabilityHandler` for the depth-first tree query.

No bespoke routes, no custom response envelopes.

### Read-only capability queries

Read-only operations on entity state that require runtime assembly (e.g. graph traversal,
aggregation, multi-entity projections) are expressed as `ICapabilityHandler` instances
dispatched through `ICapabilityDispatcher`. A future *views* mechanism for lightweight
dynamic queries will be introduced separately and will supersede this pattern for suitable
read-only scenarios when it lands; that change will require its own ADR.

### Intra-process vs. HTTP scope

The rule is not transport-scoped. A slice that exposes a function to another slice
in-process — e.g. by injecting `ICapabilityDispatcher<TCommand, TResponse>` — is subject
to the same constraint as an HTTP surface. The entity + capability model is the boundary
contract for all callers, not just HTTP clients.

## Consequences

- Future slices do not need to re-argue the entity / capability model; they cite this ADR
  and document their `[Entity]` types and `[Capability]` handlers.
- `Forge.Branch` remains compliant as the acknowledged exception; no rework required.
- Any proposal for a bespoke HTTP route or custom response envelope in a new slice
  constitutes a conflict with this ADR and must be escalated before implementation.
- Platform review of new slices can use this ADR as a checklist:
  - Does every stateful concept have an `[Entity]` type? ✓
  - Does every CRUD surface use `[OperationEndpoints]`? ✓
  - Does every non-CRUD operation have an `[Capability]`-annotated handler? ✓
  - If CRUD is hand-written, is the RDF topology exception documented and ADR-0017 +
    ADR-0019 satisfied? ✓
- The transport-control flag spike on `[Capability]` (Capability ADR-0020) is the
  immediate follow-up to this rule: if all handlers are transport-agnostic, individual
  capabilities may need to declare which transports they support.
