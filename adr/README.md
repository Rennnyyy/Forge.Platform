# Architecture Decision Records — Forge.Platform (root)

This folder holds **platform-wide** decisions. Slice-local decisions live next to the slice, e.g. [src/Entity/adr/](../src/Entity/adr/).

## Format

Each ADR is one Markdown file, MADR-minimal:

```
# NNNN — Title

- **Status**: proposed | accepted | superseded by NNNN | deprecated
- **Date**: YYYY-MM-DD
- **Author**: <name or agent tag>

## Context
Why is this decision needed? Forces, constraints.

## Options
1. Option A — pros / cons
2. Option B — pros / cons
...

## Decision
The chosen option, stated imperatively.

## Consequences
What this enables or forecloses. Follow-ups.
```

## Rules

1. ADRs are **append-only** and **numbered sequentially** within their folder. Never renumber.
2. To change a prior decision, write a new ADR with `Status: accepted` and mark the old one `Status: superseded by NNNN`.
3. Keep them short. One screen is the goal.
4. New agents working in the platform must read root ADRs first, then the slice ADRs of the slice they will modify.

## Index

- [0001 — Single-solution monorepo with src/ and tests/](0001-monorepo-layout.md)
- [0002 — .NET 10 + Central Package Management](0002-dotnet-and-cpm.md)
- [0003 — Architecture Decision Records as the design source of truth](0003-adr-policy.md)
- [0004 — Default namespace = Forge.<library>](0004-namespace-convention.md)
- [0005 — RDF-backed Entity Repository as a separate slice](0005-rdf-repository.md)
- [0006 — Canonical base URL is https://forge-it.net](0006-canonical-base-url.md)
- [0007 — Shared test fixtures project for sample entities](0007-shared-test-fixtures.md)
- [0008 — Remove `Entity.` prefix from satellite package/namespace names](0008-remove-entity-prefix-from-satellite-packages.md)
- [0009 — ADR adjustment policy: inline notes for identifier-only changes](0009-adr-adjust-policy.md)
- [0010 — Sub-folder structure inside slice directories](0010-slice-folder-structure.md)
- [0011 — Samples folder for runnable demonstration applications](0011-samples-folder-layout.md)
- [0012 — Integration-testing samples via Bruno CLI and a subprocess host](0012-sample-integration-tests-bruno.md)
- [0013 — Bruno collection organised as story chapters](0013-bruno-collection-story-structure.md)
- [0014 — Registration-order-independent decorator chain](0014-registration-order-independent-decorator-chain.md)
- [0015 — Entity tests fixtures split](0015-entity-tests-fixtures-split.md)
- [0016 — Bruno collection expanded to 14 chapters](0016-bruno-chapter-expansion.md)
- [0017 — Platform entity HTTP must use shared operation contracts](0017-platform-entity-http-must-use-shared-operation-contracts.md)
- [0018 — Bruno integration tests also run against GraphDB backend](0018-bruno-integration-tests-graphdb-backend.md)
- [0019 — Managed-entity HTTP layers must wire aspect enforcement](0019-managed-entity-http-aspect-enforcement.md)
- [0015 — Split entity test fixtures into Core and full-CRUD projects](0015-entity-tests-fixtures-split.md)
- [0016 — Bruno collection expanded to 14 chapters](0016-bruno-chapter-expansion.md)
- [0017 — Platform entity HTTP layers must use shared `Forge.Operations.Http` contracts](0017-platform-entity-http-must-use-shared-operation-contracts.md)
- [0020 — Messaging abstractions slice (`Forge.Messaging.Abstractions`, `.Kafka`, `.InMemory`)](0020-messaging-abstractions-slice.md)
- [0021 — Entity change event stream (history + compacted state topics via `Forge.EntityEvents`)](0021-entity-change-event-stream.md)
- [0022 — Async capability and operations command bus (`Forge.Capability.Messaging`)](0022-async-capability-command-bus.md)
- [0023 — Object Storage slices (`Forge.ObjectStorage.Abstractions`, `.InMemory`, `.Http`)](0023-object-storage-slices.md)
- [0024 — Entity change event slice named `Forge.Entity.Messaging`, not `Forge.EntityEvents`](0024-entity-events-slice-named-entity-messaging.md)
- [0025 — Entity and Capability as the exclusive platform extension model](0025-entity-capability-exclusive-extension-model.md)
