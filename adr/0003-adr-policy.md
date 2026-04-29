# 0003 — Architecture Decision Records as the design source of truth

- **Status**: accepted
- **Date**: 2026-04-29
- **Author**: bootstrap

## Context

Design context tends to evaporate. New agents (human or AI) need a stable place to learn why the code looks the way it does. A single bloated `copilot-instructions.md` accumulates contradictions and stops being read carefully.

## Options

1. **MADR-minimal ADRs, root + per-slice, plus a tiny router prompt** that tells implementors which ADR set to read.
2. One large `copilot-instructions.md` covering the whole platform. Pro: single file. Con: unreadable past ~200 lines; merge conflicts; agents skim.
3. ADRs only at the root, no per-slice. Pro: simpler tree. Con: slice-local rules drown in cross-cutting noise.

## Decision

Option 1.
- Root ADRs at `./adr/` cover cross-cutting decisions (build, layout, conventions).
- Slice ADRs at `./src/<Slice>/adr/` cover decisions specific to that library.
- `.github/copilot-instructions.md` is a **router prompt only**: it tells the agent to ask the user which slice the change belongs to, then read root ADRs and the slice's ADRs before acting.
- Format is MADR-minimal (see [README](README.md)).
- ADRs are append-only and numbered.

## Consequences

- Decisions become discoverable, auditable, and supersedable.
- The router prompt stays small and rarely changes.
- Every new design decision must produce a new ADR; failing to do so is a code-review block.
