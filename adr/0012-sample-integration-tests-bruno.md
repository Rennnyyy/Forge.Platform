# 0012 — Integration-testing samples via Bruno CLI and a subprocess host

- **Status**: accepted
- **Date**: 2026-05-04
- **Author**: agent

## Context

ADR-0011 established `samples/` as the home for runnable demonstration apps. Those apps
expose HTTP behaviour that needs automated verification: does the endpoint respond with the
right status code and payload? The test must exercise the full HTTP stack (serialisation,
route registration, DI wiring) — not a mocked subset.

Two concerns must be balanced:

1. **Realistic HTTP** — the test must send real HTTP requests, not in-memory ones, because
   the Bruno collection (committed alongside the sample) is also the manual/CI smoke-test
   tool. The same `.bru` files run interactively and run in CI.
2. **Test isolation** — the sample app must be started and stopped reproducibly within the
   test runner, without requiring an operator to start it manually.

## Options

1. **`dotnet exec <sample-dll>` subprocess + Bruno CLI** — the test launches the already-built
   sample DLL as a subprocess on a random TCP port, waits for a successful probe request,
   then invokes `npx @usebruno/cli run` against the bruno collection.  
   Pro: the same `.bru` files that developers run interactively drive CI assertions.  
   Con: requires Node.js / npm on the CI agent; skips gracefully via `[SkippableFact]`
   when `npx` is absent.
2. **`WebApplicationFactory<Program>` with real Kestrel** — configure the factory to bind
   to a random port and run Bruno against that port.  
   Con: requires `public partial class Program {}` exposure; adds significant setup
   complexity to force `WebApplicationFactory` off its default in-memory `TestServer`.
3. **Replicate Bruno assertions in C# (no Bruno CLI)** — write equivalent `HttpClient`
   tests that mirror the `.bru` requests.  
   Con: two sources of truth; the `.bru` files can diverge silently from the C# tests.
4. **No test project; only manual Bruno runs** — con: CI has no automated coverage.

## Decision

Option 1.

### Mechanics

- The test project lives at `tests/Capability.Http.Sample.Tests/`.
- It carries a **build-only** `<ProjectReference ReferenceOutputAssembly="false">` to the
  sample project, ensuring the sample DLL is up-to-date before the test binary runs.
- The test locates the sample DLL by walking up from `AppContext.BaseDirectory` until
  `Forge.Platform.slnx` is found (repo root anchor), then navigates to
  `samples/Capability.Http.Sample/bin/{config}/net10.0/Forge.Capability.Http.Sample.dll`.
- A free TCP port is obtained via `TcpListener(IPAddress.Loopback, 0)`.
- The app is started with `dotnet exec <dll>` and `ASPNETCORE_URLS` set to the chosen
  port. The test polls `POST /demo/greet` until `200 OK` or a 30-second timeout.
- `npx --yes @usebruno/cli run <collection-dir> --env local --env-var baseUrl=<url>` is
  invoked.  Exit code `0` = all Bruno assertions passed.
- The test is decorated `[SkippableFact]` and skips when `npx` is not on `PATH`.
- The subprocess is killed via `Process.Kill(entireProcessTree: true)` in the
  `IAsyncLifetime.DisposeAsync` teardown.

### Node.js prerequisite

CI pipelines that must not skip the test must install Node.js before running `dotnet test`.
A CI note is added to the sample's `README.md` (future work; not required for green tests).

## Consequences

- Bruno collections committed under `samples/` are living integration tests.
- Adding a new `.bru` request to the collection automatically increases test coverage.
- The test passes on machines with Node.js and skips gracefully on machines without it.
- `WebApplicationFactory` / ASP.NET Core testing infrastructure is not needed in this
  test project; the stack is `xunit` + `Shouldly` + `Xunit.SkippableFact` + `Process`.
