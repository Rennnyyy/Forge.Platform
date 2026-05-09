# 0014 — Registration-order-independent DI decorator chain

- **Status**: accepted
- **Date**: 2026-05-09
- **Author**: agent

## Context

`AddForgeAspects()` and `AddForgeAuthorization()` decorated `IEntityStore` /
`ITransactionalEntityStore` by scanning `IServiceCollection` at call time for an
existing unkeyed descriptor and capturing it. When a backend (`UseInMemory()` /
`UseGraphDb()`) was registered *after* these decorators, the scan returned nothing
and the SHACL-validation and authorization layers silently fell out of the decorator
chain. Concretely:

```csharp
// Silent misconfiguration — no error, no auth, no SHACL
services.AddForgeAspects();          // captures nothing
services.AddForgeAuthorization();    // captures nothing
builder.AddForgeEntityRepository().UseInMemory();  // registered too late
```

The decorator stack must be: Guard → AspectEnforcing → Backend, regardless of DI
registration order.

## Options

1. **Keyed service constants + deferred resolution.** Add
   `ForgeEntityRepositoryBuilder.BackendStoreKey` and `AspectsTxKey` string constants.
   Each backend registers its raw `IEntityStore` under `BackendStoreKey` immediately.
   `AddForgeAspects` registers `AspectEnforcingTransactionalStore` under `AspectsTxKey`
   and uses a factory that falls back to resolving `BackendStoreKey` at provider-build
   time when no descriptor was captured at registration time.
   `AddForgeAuthorization` similarly falls back to `AspectsTxKey` then `BackendStoreKey`.

2. **`IStartupFilter` / `IHostedService` ordering check.** Detect and throw on wrong
   order at host startup. Pro: simple to implement. Con: still fails; doesn't fix ordering.

3. **Documentation only.** Mark the required order in XML docs and README.
   Rejected — silent misconfiguration with security implications is not acceptable.

## Decision

Option 1.

- `ForgeEntityRepositoryBuilder` (in `Forge.Repository.DependencyInjection`) gains two
  public string constants:
  - `BackendStoreKey = "forge.repository.backend"` — backends register their raw
    `IEntityStore` here via `TryAddKeyedSingleton` immediately on `UseInMemory()` /
    `UseGraphDb()`.
  - `AspectsTxKey = "forge.aspects.tx"` — `AddForgeAspects()` registers the
    `AspectEnforcingTransactionalStore` keyed service here in addition to the unkeyed
    registration.
- `AddForgeAspects()` and `AddForgeAuthorization()` retain backward compatibility: when
  a descriptor is found at registration time (e.g. a direct
  `services.AddSingleton<IEntityStore>(myStore)` in tests) it is captured and used as
  before. The `BackendStoreKey`/`AspectsTxKey` fallback only fires when no descriptor
  was found at registration time.

## Consequences

- All six orderings of `UseInMemory()`, `AddForgeAspects()`, `AddForgeAuthorization()`
  produce the correct Guard → AspectEnforcing → Backend stack at runtime.
- Guard and SHACL validation cannot be silently dropped by call-order mistakes.
- Both constants being `public` lets third-party backends follow the same pattern.
- Tests that use `services.AddSingleton<IEntityStore>(stubStore)` without a proper
  backend continue to work unchanged.
