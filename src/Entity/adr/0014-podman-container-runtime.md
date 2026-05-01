# 0014 — Podman as the preferred container runtime for integration tests

- **Status**: accepted
- **Date**: 2026-05-01
- **Author**: agent

## Context

`GraphDbFixture` starts and stops the Ontotext GraphDB container needed by the GraphDB
integration tests. The original implementation hard-coded `docker` as the container CLI
and referenced `docker compose` in all comments and README copy.

macOS development environments increasingly use **Podman** (rootless, daemonless) instead
of Docker Desktop. Podman and its companion `podman compose` (or `podman-compose`) accept
the same compose file format and the same CLI arguments as `docker compose`, so no changes
to `docker-compose.graphdb.yml` are required.

## Options

1. **Auto-detect at runtime: probe `podman`, fall back to `docker`.**
   `GraphDbFixture.FindContainerCli()` tries to run each candidate with `--version`; the
   first one whose process exits with code 0 wins. A `FORGE_CONTAINER_CLI` environment
   variable can override the auto-detected value for CI or unusual setups.
2. Hard-code `podman` and drop `docker` support.
   Pro: simpler. Con: breaks CI environments that still use Docker.
3. Expose a build-time MSBuild property or test setting to pick the CLI.
   Pro: explicit. Con: overhead for a simple preference; env-var override in option 1
   already covers the explicit-override use case.

## Decision

Option 1.

### Runtime probe order

`FindContainerCli()` iterates `["podman", "docker"]`. The first executable that responds
to `--version` with exit code 0 is used for the lifetime of the fixture. The
`FORGE_CONTAINER_CLI` env var, when set, skips the probe entirely.

### Compose sub-command

Both runtimes use the same sub-command `compose`, so `TryComposeUpAsync` and
`TryComposeDownAsync` call `{cli} compose -f "..." up -d` and
`{cli} compose -f "..." down` uniformly.

### Compose file

`docker-compose.graphdb.yml` is retained unchanged (Podman reads the same format).
Comments in that file are updated to show both `podman compose` and `docker compose`
invocations.

## Consequences

- Developers on Podman-only machines get working out-of-the-box integration tests.
- CI environments that only have Docker continue to work unchanged.
- The `FORGE_CONTAINER_CLI` override gives escape-hatch control in edge cases.
