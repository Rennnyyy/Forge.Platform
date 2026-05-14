# ADRs — Forge.Execution.Http

Slice-local ADRs for `Forge.Execution.Http`.

Architectural decisions for this slice are recorded in the parent slice
([`src/Execution/adr/`](../../Execution/adr/)):

- [ADR-0001](../../Execution/adr/0001-execution-shared-contracts.md) — `Forge.Execution` shared execution contracts
- [ADR-0002](../../Execution/adr/0002-execution-http-sibling.md) — `Forge.Execution.Http` ASP.NET transport companion (describes this slice)

See also the [root ADR index](../../../adr/README.md).

## Slice-local ADRs

- [0001 — Branch HTTP carrier: X-Forge-BranchIri header and BranchScopeMiddleware](0001-branch-http-carrier.md)
- [0002 — BranchScopeMiddleware and carrier types move to Forge.Branch.Http](0002-branch-scope-middleware-moves-to-branch-http.md)
