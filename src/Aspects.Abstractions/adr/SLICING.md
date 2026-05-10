# SLICING — Forge.Aspects.Abstractions

Applied per [root ADR-0010](../../../adr/0010-slice-folder-structure.md).

## Sub-folder map

| Sub-folder | Namespace | Sub-concern | Rule |
|------------|-----------|-------------|------|
| _(root)_ | `Forge.Aspects.Abstractions` | Pure aspect contracts and well-known constants. | All files live at the root. No sub-folders: the assembly has no frameworks, no engines, and no external dependencies — only BCL types. |

## Excluded sub-folders

| Sub-folder | Reason excluded |
|------------|-----------------|
| `adr/` | ADR folder; excluded per ADR-0010. |

## ADR-0010 exemption

This slice has 15 flat `.cs` files, which exceeds the 11–20 threshold where sub-folders are
"recommended" under ADR-0010. The flat layout is retained because **all types are pure contracts
sharing a single public surface with no framework or external dependencies — only BCL types**.
Logical groupings (contracts, exceptions, enums) do not form meaningful architectural
boundaries: they are all aspects-vocabulary types consumed as a single unit. Introducing
sub-folders would create cosmetic groupings without adding navigational value.

This exemption is documented here as required by ADR-0010, which states that exceptions must
explain why the threshold is not meaningful for the slice.

## File assignment

### Root (`Forge.Aspects.Abstractions`)

#### Contracts

- `IAspect.cs` — marker interface for a named validation policy (e.g. `"book-pub-year-v1"`).
- `Aspect.cs` — well-known aspect token singletons (`NoOp`).
- `IAspectStore.cs` — contract for resolving a registered aspect by IRI.
- `IOperationAspect.cs` — contract for a SHACL + SPARQL write-path validation policy.
- `IQueryAspect.cs` — contract for a read-path filter-injection + result-graph validation policy.
- `IMessageAspect.cs` — contract for a capability-message validation policy.
- `IMessageAspectEngine.cs` — contract for validating message objects against a SHACL shape.
- `MessageKind.cs` — enum distinguishing `Command` from `Response` in message aspects.
- `CapabilityAspect.cs` — well-known capability aspect token (e.g. `CapabilityAspect.Message`).

#### Exceptions and violation records

- `AspectNotFoundException.cs` — thrown when no aspect is registered for a given IRI.
- `AspectException.cs` — base exception for aspect-enforcement failures.
- `AspectTtlParseException.cs` — thrown at startup when a code-origin TTL file fails to parse.
- `AspectViolation.cs` — record carrying a single SHACL constraint violation.
- `MessageAspectViolationException.cs` — thrown when message-aspect SHACL validation fails.
- `QueryAspectViolationException.cs` — thrown when query-aspect access validation fails.
