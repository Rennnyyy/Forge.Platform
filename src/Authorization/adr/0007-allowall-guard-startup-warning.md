# 0007 — AllowAllAspectGuard startup log warning

- **Status**: accepted
- **Date**: 2026-06-13
- **Author**: agent

## Context

`AddForgeAuthorization()` accepts a `null` guard argument and falls back silently to
`AllowAllAspectGuard.Instance`, which permits every write operation unconditionally.
This default makes the slice safe to include in any host without requiring an explicit
guard to be provided (see Authorization ADR-0001).

The existing safeguard against unintentional production use is
`AuthorizationOptions.RequireExplicitGuard`. When set to `true`, the DI factory for the
unkeyed `ITransactionalEntityStore` throws `InvalidOperationException` at provider-build time
if no real guard was supplied. HTTP hosts additionally receive `AllowAllGuardStartupFilter`,
which fires slightly earlier and provides richer error context.

Both mechanisms require the application operator to _opt in_ — they are not active by
default. An application that ships `AllowAllAspectGuard` to a production environment
produces no observable diagnostic signal in its logs, making it impossible to detect via
structured log monitoring.

## Decision

Register a one-shot `IHostedService` (`AllowAllGuardWarningService`) inside
`AddForgeAuthorization()` whenever `effectiveGuard is AllowAllAspectGuard`. The service logs
a single `LogWarning` at `IHostedService.StartAsync` time:

```
Forge Authorization: AllowAllAspectGuard is active — every write operation is permitted
unconditionally. Register an explicit IAspectGuard via AddForgeAuthorization(yourGuard)
before deploying to a production environment.
```

The hosted service:
- is only registered when `effectiveGuard is AllowAllAspectGuard` at DI setup time.
- is `internal sealed` — not a public API surface.
- has no dependencies beyond `ILogger<AllowAllGuardWarningService>`.
- does not throw; it is purely advisory.
- uses category `Forge.Authorization.AllowAllGuardWarningService` for log filtering.

## Consequences

- Operators can detect `AllowAllAspectGuard` in production via standard log monitoring
  (e.g. filter on category `Forge.Authorization` at Warning level).
- Development and test hosts are unaffected provided they suppress the warning via
  log-level configuration (`Forge.Authorization: Warning → None`).
- `RequireExplicitGuard` remains the hard enforcement mechanism; the new service is a
  complementary observability tool.
- `Forge.Authorization.csproj` gains dependencies on
  `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`.
