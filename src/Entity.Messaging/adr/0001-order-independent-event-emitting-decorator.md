# 0001 — Order-independent `EventEmittingTransactionalStore` registration

- **Status**: accepted
- **Date**: 2026-06-13
- **Author**: agent

## Context

`AddForgeEntityEvents()` registers `EventEmittingTransactionalStore` under the keyed service
key `ForgeEntityRepositoryBuilder.EventsTxKey`. It also exposes the store through the _unkeyed_
`ITransactionalEntityStore` slot so that hosts that call `AddForgeEntityEvents()` without
`AddForgeAuthorization()` still resolve the event-emitting decorator when they ask for a plain
`ITransactionalEntityStore`.

Prior to this ADR the unkeyed registration used `TryAddSingleton`:

```csharp
services.TryAddSingleton<ITransactionalEntityStore>(sp =>
    sp.GetRequiredKeyedService<ITransactionalEntityStore>(
        ForgeEntityRepositoryBuilder.EventsTxKey));
```

`TryAddSingleton` only registers when no other registration already exists for the service
type. When the application calls `AddForgeAspects()` _before_ `AddForgeEntityEvents()`,
`AddForgeAspects()` has already placed `AspectEnforcingTransactionalStore` in the unkeyed
slot via its own `TryAddSingleton`. The `AddForgeEntityEvents()` call therefore becomes a
no-op for the unkeyed slot: `EventEmittingTransactionalStore` is never inserted, and entity
change events are silently dropped.

The workaround was documented in `samples/Application.Sample/Program.cs` as an explicit
comment: _"MUST be called before AddForgeAspects"_. Root ADR-0021 claims the decorator chain
is order-independent; this workaround directly contradicted that claim.

`AddForgeAspects()` and `AddForgeAuthorization()` both already use a
capture-and-replace pattern (also known as the _decorator swap_ pattern) for their own
unkeyed registrations, making them order-independent relative to each other. The symmetric
fix is to apply the same pattern in `AddForgeEntityEvents()`.

## Decision

Replace the `TryAddSingleton` unkeyed alias in `AddForgeEntityEvents()` with the
capture-and-replace pattern:

1. Capture any existing unkeyed `ITransactionalEntityStore` descriptor at registration time.
2. Remove it from the collection.
3. Register a new `AddSingleton<ITransactionalEntityStore>` that resolves to
   `ForgeEntityRepositoryBuilder.EventsTxKey` at provider-build time.

The `EventsTxKey` keyed registration already builds the full chain
(`EventEmittingTransactionalStore` → `AspectEnforcingTransactionalStore` → backend) by
falling back through `AspectsTxKey` → `BackendStoreKey`. The unkeyed slot therefore always
resolves to the outermost decorator regardless of call order.

When `AddForgeAuthorization()` is also called, it performs its own capture-and-replace on top
of this registration, adding `GuardedTransactionalStore` as the outermost layer.  The final
chain — Guard → Events → Aspects → Backend — matches root ADR-0021.

The call-ordering comment in `samples/Application.Sample/Program.cs` is removed.

## Consequences

- `AddForgeEntityEvents()` is now truly order-independent relative to `AddForgeAspects()`.
- The decorator chain is consistent whether the application calls `AddForgeEntityEvents()`
  before or after `AddForgeAspects()`.
- The forced call-ordering workaround documented in `Application.Sample` is eliminated.
- No observable API surface change; existing hosts that already called the methods in the
  recommended order continue to work identically.
- **Breaking for non-standard configurations**: a host that previously registered a custom
  unkeyed `ITransactionalEntityStore` before calling `AddForgeEntityEvents()` will have
  that registration silently displaced. Under the old `TryAddSingleton` semantics the
  custom registration was preserved; under capture-and-replace it is removed unconditionally.
  Hosts with custom unkeyed store registrations must register them _after_
  `AddForgeEntityEvents()`, or key them and resolve by key.
  `AddForgeAuthorization()` and `AddForgeBranch()` follow the same semantics.
