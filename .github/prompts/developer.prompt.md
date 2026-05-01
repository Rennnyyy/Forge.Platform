---
description: "Forge.Platform developer workflow: locate the slice, read all ADRs, then implement with full design discipline."
name: "Forge Developer"
argument-hint: "Describe your change or feature…"
mode: agent
tools: [read_file, create_file, replace_string_in_file, run_in_terminal, file_search, grep_search, semantic_search]
---
# Forge.Platform — Agent Router

You are about to make changes in the Forge platform repository. **Before writing or modifying any code**, follow this protocol.

## Step 1 — Locate the slice

Ask the user (or infer from the request, then confirm with one short question if uncertain):

> Which slice does this change belong to? Examples: `Entity`, future slices under `src/`. If the change is cross-cutting (build, layout, conventions), say "root".

## Step 2 — Read the ADRs

In this exact order, read every Markdown file end-to-end:

1. **Root ADRs** — `./adr/README.md` then every `./adr/NNNN-*.md`.
2. **Slice ADRs** — `./src/<Slice>/adr/README.md` then every `./src/<Slice>/adr/NNNN-*.md`.

Do not skim. The ADRs are short on purpose; read them fully. They are the source of truth for design decisions and override anything you might assume from general best practices.

If a slice has no `adr/` folder, treat that as a signal to create one and write the first ADR(s) for the decisions your task implies — see [`./adr/0003-adr-policy.md`](../../adr/0003-adr-policy.md).

## Step 3 — Honor the rules

- ADRs are **append-only**. Never edit a past decision; supersede it with a new ADR.
- Every new design decision your task introduces must produce a new ADR in the same change.
- Tests live under `./tests/<Slice>.Tests/`. Generator tests use snapshot tests; behavioral tests live alongside other Entity tests.
- **Datatype coverage rule**: any test that exercises a datatype-relevant extension (e.g. SPARQL generation, any repository backend) **must** drive its assertions through the shared sample domain in `./tests/Entity.Tests.Fixtures/Sample/`. Use the `Artist` entity — or extend it — to cover every supported scalar CLR type (`string`, `bool`, `int`, `long`, `float`, `double`, `decimal`, `DateOnly`, `DateTimeOffset`, `Guid`, `Uri`) and their nullable variants. Do not write datatype tests against ad-hoc or stripped-down inline entities when the shared sample already provides the coverage surface.
- Build (`dotnet build`) and tests (`dotnet test`) must be green at the end of every change.
- **No deferrals**: `// TODO`, `// FIXME`, `// build later`, stub implementations, `throw new NotImplementedException()`, and similar placeholders are **never acceptable**. Every line of code committed must be complete and correct. If something cannot be finished now, do not start it — see the context-overflow rule below instead.
- **Context-overflow rule**: if at any point the task feels too large to implement fully and correctly within the current context (too many files, too many ADRs, too many moving parts), **stop immediately** and tell the user: _"This task is too large for a single agent run. I recommend splitting it into the following smaller slices: …"_ Then invoke the `/Forge-Architect` command to help decompose the work. Do not produce partial or incomplete code as a workaround.
- Never re-litigate a decision recorded in an ADR without an explicit user instruction; raise the conflict to the user instead.

## Step 4 — Implement

Only after Steps 1–3 are complete. When in doubt, ask. When still in doubt, write a fresh ADR proposing the new direction and stop for review.

**Hard stop conditions** — if any of the following arise mid-implementation, **abort the entire change** (do not commit partial work), explain the problem, and suggest smaller slices using `/Forge-Architect`:

- The number of files you need to read or modify makes the context uncomfortably large.
- You find yourself writing a placeholder, stub, or deferred comment.
- A single logical change would touch more than one slice.

## Step 5 — Present the result

After the build and tests are green, summarize what changed and **point the user directly to the relevant test file(s)** under `./tests/<Slice>.Tests/` so they can verify the behavior themselves. Name the specific test cases that cover the new functionality.
