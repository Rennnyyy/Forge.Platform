---
description: "Generate /docs — multi-page HTML documentation site (style.css + ten HTML pages) covering architecture, reference, and usability content."
name: "Forge Documentation"
argument-hint: "Optionally name a single page to regenerate (index | projects | architecture | adr | getting-started | cookbook | api-reference | configuration | testing | sample), or press Enter to regenerate everything."
agent: agent
tools: [read, search, web, agent, edit, execute, search, todo, vscode]
---
# Forge.Platform — Documentation Generator

You generate the `./docs/` site for the Forge.Platform repository.
You write **only** files inside `./docs/`. You do **not** touch any source files.

The site consists of eleven files:

| File | Purpose |
|------|------|
| `docs/style.css` | Shared stylesheet — dark theme, design tokens, layout primitives |
| `docs/index.html` | Landing page — platform summary, slice map, quick-links |
| `docs/projects.html` | Package reference — Mermaid dependency graph + per-project cards |
| `docs/architecture.html` | Architecture deep-dive — layer diagram (Mermaid), ECharts metrics |
| `docs/adr.html` | ADR index — ECharts timeline + expandable decision records |
| `docs/getting-started.html` | Step-by-step setup — add packages, register DI, define an entity, run a query |
| `docs/cookbook.html` | Recipes — copy-paste patterns for common scenarios |
| `docs/api-reference.html` | API reference — key interfaces and extension methods per package |
| `docs/configuration.html` | Configuration reference — every `AddForge…()` DI registration with options |
| `docs/testing.html` | Testing guide — unit tests with InMemory backends and shared fixtures |
| `docs/sample.html` | Sample walkthrough — annotated tour of `Application.Sample` |

If the user supplied an argument naming a single page, regenerate only that file (plus
`style.css` if it does not exist yet). Otherwise regenerate all eleven files.

---

## Phase 1 — Collect data

### 1.1 Projects

List `./src/` and read every `*.csproj` file. For each project collect:

- `name` — `Forge.<Name>` (derived from folder / file name)
- `targetFramework` — value of `<TargetFramework>`
- `rootNamespace` — value of `<RootNamespace>` (fall back to project name)
- `isGenerator` — `true` when `<IsRoslynComponent>true</IsRoslynComponent>` is present
- `runtimeDeps` — list of projects referenced via plain `<ProjectReference>`
- `analyzerDeps` — list of projects referenced via
  `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" …>`
- `description` — one-to-two sentence summary derived from reading the `.cs` source files
  and any `README.md` present in the project directory

### 1.2 ADRs

Read every `./adr/*.md` file. For each ADR extract:

- `number` — four-digit prefix (`0001`, `0002`, …)
- `title` — first `# Heading` line, stripped of the number prefix
- `status` — look for a `**Status**` or `Status:` line; default to `Accepted`
- `date` — look for a `**Date**` or `Date:` line; leave blank if absent
- `summary` — first non-heading paragraph (≤ 40 words)

### 1.3 Slice groups

Map every `src/` project into one of these architectural layers (used on the
architecture page). Derive the assignment from the project name suffix:

| Layer | Suffix pattern |
|-------|---------------|
| Foundations | `Execution`, `Aspects.Abstractions`, `Messaging.Abstractions`, `ObjectStorage.Abstractions` |
| Entity & Persistence | `Entity`, `Entity.Generators`, `Repository`, `Repository.*`, `Sparql` |
| Operations | `Operations`, `Operations.Generators`, `Operations.Http` |
| HTTP Cross-Cutting | `Execution.Http` |
| Authorization | `Authorization`, `Authorization.Http` |
| Branch & Snapshot | `Branch`, `Branch.Http` |
| Capability | `Capability`, `Capability.Http`, `Capability.Messaging` |
| Messaging | `Messaging.InMemory`, `Messaging.Kafka`, `Entity.Messaging` |
| Object Storage | `ObjectStorage.InMemory`, `ObjectStorage.Http` |

### 1.4 Application.Sample

Read the files inside `./samples/Application.Sample/` to understand:

- Which Forge packages are referenced and how they are registered in `Program.cs`
  (or `Startup.cs`).
- What entity types are defined (look for `[Entity]` attributes).
- What capability, HTTP endpoint, or messaging wiring is present.
- Any noteworthy configuration or middleware order decisions.

This data is used for the **Getting Started**, **Cookbook**, and **Sample** pages.

---

## Phase 2 — Generate `docs/style.css`

Write `./docs/style.css`. This file is **the single source of truth** for all visual
design. Every HTML page links it with `<link rel="stylesheet" href="style.css">` and
does **not** contain a `<style>` block of its own.

### Design tokens (CSS custom properties on `:root`)

```css
--bg:        #0f1117;   /* page background          */
--surface:   #1a1d27;   /* card / panel background  */
--surface2:  #22263a;   /* nested / hover surface   */
--border:    #2e3144;   /* dividers, card borders   */
--text:      #e2e8f0;   /* primary text             */
--muted:     #64748b;   /* secondary / caption text */
--accent:    #3b82f6;   /* primary accent (blue)    */
--accent2:   #8b5cf6;   /* secondary accent (violet)*/
--accent3:   #10b981;   /* tertiary accent (emerald)*/
--warning:   #f59e0b;   /* amber — warnings / ADR   */
--danger:    #ef4444;   /* red                      */
--mono:      'JetBrains Mono', 'Cascadia Code', ui-monospace, monospace;
--sans:      'Segoe UI', system-ui, -apple-system, sans-serif;
--radius:    6px;
--shadow:    0 2px 8px rgba(0,0,0,.45);
```

### Layout & components to include in style.css

- **Reset**: `*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }`
- **Body**: dark background, `--sans` font, `--text` color, `font-size: 15px`,
  `line-height: 1.65`
- **`<nav>`** (site-wide top bar): fixed, `height: 52px`, `background: var(--surface)`,
  `border-bottom: 1px solid var(--border)`, flex row, logo on the left, nav links
  right-aligned with `overflow-x: auto` so they scroll horizontally on narrow screens.
  Active link highlighted with `--accent` underline (`border-bottom: 2px solid var(--accent)`).
  Nav links use `<a class="nav-link">` and `<a class="nav-link active">`.
  The ten nav links (in order): **Index · Getting Started · Projects · Architecture ·
  API Reference · Cookbook · Configuration · Testing · Sample · ADRs**.
- **`<main>`**: `max-width: 1100px`, `margin: 0 auto`, `padding: 5rem 2rem 4rem`
  (top padding clears the fixed nav)
- **`.page-header`**: section at top of `<main>` — `h1` (`font-size: 2rem`,
  `font-weight: 700`), `p.lead` (`color: var(--muted)`, `max-width: 680px`)
- **`.card`**: `background: var(--surface)`, `border: 1px solid var(--border)`,
  `border-radius: var(--radius)`, `padding: 1.5rem`, `box-shadow: var(--shadow)`
- **`.card-grid`**: CSS grid, `grid-template-columns: repeat(auto-fill, minmax(280px,1fr))`,
  `gap: 1.25rem`
- **`.badge`**: inline pill — `font-size: 0.7rem`, `font-weight: 700`,
  `padding: 0.15em 0.55em`, `border-radius: 999px`, `vertical-align: middle`.
  Variants: `.badge-blue` (`background:#1e3a8a; color:#93c5fd`),
  `.badge-violet` (`background:#3b0764; color:#c4b5fd`),
  `.badge-green` (`background:#064e3b; color:#6ee7b7`),
  `.badge-amber` (`background:#451a03; color:#fcd34d`)
- **`<dl>.meta`**: two-column definition list for project metadata
  (`grid-template-columns: max-content 1fr`, `gap: 0.25rem 1rem`)
- **`<code>` / `<pre>`**: monospace, `background: var(--surface2)`,
  `border-radius: var(--radius)`, `font-family: var(--mono)`
- **`.diagram-wrap`**: `background: var(--surface)`, `border: 1px solid var(--border)`,
  `border-radius: var(--radius)`, `padding: 1.5rem`, `overflow-x: auto`
- **`.chart-wrap`**: same as `.diagram-wrap` but with an explicit `height` attribute
  set inline per chart
- **`<section.topic>`**: top-level content section, `margin-bottom: 3rem`
- **`h2.section-title`**: `font-size: 1rem`, `font-weight: 700`,
  `text-transform: uppercase`, `letter-spacing: .08em`, `color: var(--muted)`,
  `border-bottom: 1px solid var(--border)`, `padding-bottom: .5rem`,
  `margin-bottom: 1.5rem`
- **`details.adr-record`**: expandable ADR item — `border: 1px solid var(--border)`,
  `border-radius: var(--radius)`, `margin-bottom: .5rem`,
  `summary` styled as a flex row with number badge, title, status badge, date
- **`.steps`**: ordered-list variant for Getting Started steps —
  `counter-reset: step`, each `li` uses `counter(step)` as a circular accent-coloured
  badge before the step title
- **`.recipe`**: a `.card` variant with a `<h3 class="recipe-title">` and a
  `<pre class="recipe-code">` block; `<pre>` gets `overflow-x: auto`, `padding: 1rem`,
  `border-radius: var(--radius)`, `background: var(--surface2)`
- **`.api-table`**: `width:100%`, `border-collapse: collapse`, `th` and `td` with
  `padding: .5rem .75rem`, `border-bottom: 1px solid var(--border)`, `th` with
  `text-align: left`, `color: var(--muted)`, `font-size: .8rem`
- **`.config-section`**: wraps one DI registration family — `margin-bottom: 2rem`
- **`.tip`**: callout box — `background: var(--surface2)`,
  `border-left: 3px solid var(--accent3)`, `padding: .75rem 1rem`,
  `border-radius: 0 var(--radius) var(--radius) 0`, `margin: 1rem 0`

---

## Phase 3 — Generate `docs/index.html`

Landing page. Sections:

1. **Page header** (`.page-header`): title "Forge.Platform", lead sentence describing
   the platform in one line.

2. **What is Forge.Platform?** (`.topic`): two paragraphs — what problem it solves,
   what the modular slice architecture enables.

3. **Platform slices** (`.topic`): a Mermaid `graph LR` showing the nine architectural
   layers as labelled rectangular clusters with representative packages inside each.
   Use `subgraph` for each layer. Keep it readable — omit dependency arrows here,
   focus on membership.

4. **Quick links** (`.topic`): a `.card-grid` with one `.card` per docs page.
   Include all nine non-index pages: Getting Started, Projects, Architecture,
   API Reference, Cookbook, Configuration, Testing, Sample, ADRs — each with
   a short one-line caption and an `<a href="page.html">` link.

CDN scripts required (in `<head>` or before `</body>`):
```html
<script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
```
Initialise Mermaid with:
```js
mermaid.initialize({ startOnLoad: true, theme: 'dark' });
```

---

## Phase 4 — Generate `docs/projects.html`

### 4.1 Dependency diagram

A `<div class="mermaid">` block using `graph TD`. Rules:

- Node IDs: short name without `Forge.` prefix, camelCase (e.g. `EntityGenerators`).
- Node labels: full NuGet name in double-quotes
  (e.g. `EntityGenerators["Forge.Entity.Generators"]`).
- `-->` for runtime `<ProjectReference>` edges.
- `-.->` for analyzer/generator edges (`OutputItemType="Analyzer"`).
- `classDef generator fill:#1e3a8a,stroke:#3b82f6,color:#bfdbfe` applied to
  every `isGenerator` project.
- `classDef abstract fill:#1c1f2e,stroke:#4b5563,color:#9ca3af` applied to
  pure-abstraction packages (`*.Abstractions`).
- Do **not** invent edges absent from the `.csproj` files.
- HTML-escape `<`, `>`, `"`, `&` inside the `<div class="mermaid">` block.

### 4.2 ECharts: dependency-count bar chart

Below the diagram, a `.chart-wrap` `<div id="dep-chart" style="height:320px">` rendered
by ECharts. For each non-generator project, chart `runtimeDeps.length` as a horizontal
bar sorted descending. X axis = number of direct runtime dependencies.
Color bars by layer (use `--accent`, `--accent2`, `--accent3`, `--warning` cycling by
layer group).

### 4.3 Project cards

After the charts, one `.card` per project inside a `.card-grid`, ordered from most
fundamental to most derived. Each card contains:

```html
<div class="card" id="forge-<slug>">
  <h3><code>Forge.Name</code> <span class="badge badge-…">layer</span></h3>
  <dl class="meta">
    <dt>Framework</dt><dd><code>…</code></dd>
    <dt>Namespace</dt><dd><code>…</code></dd>
  </dl>
  <p>Description sentence(s).</p>
</div>
```

Badge colour by layer:
- Foundations → `.badge-green`
- Entity & Persistence → `.badge-blue`
- Operations → `.badge-violet`
- HTTP Cross-Cutting → `.badge-violet`
- Authorization → `.badge-amber`
- Branch & Snapshot → `.badge-blue`
- Capability → `.badge-violet`
- Messaging → `.badge-green`
- Object Storage → `.badge-green`

CDN scripts required:
```html
<script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/echarts@5/dist/echarts.min.js"></script>
```

---

## Phase 5 — Generate `docs/architecture.html`

### 5.1 Layer diagram (Mermaid)

`graph TD` with one `subgraph` per architectural layer. Inside each subgraph, list the
member projects (short IDs). Add inter-layer edges to show the primary data-flow
direction (top-down from Foundations → Entity → Operations → HTTP). Use the same
`classDef` definitions as in `projects.html`.

### 5.2 ECharts: package count per layer (donut)

`<div id="layer-donut" style="height:340px">` — ECharts pie chart with `radius: ['40%','68%']`
showing how many packages belong to each layer. Use the accent colours from the design
tokens for each slice.

### 5.3 ECharts: dependency heat-map

`<div id="dep-heatmap" style="height:460px">` — ECharts heatmap where both axes are the
project short names (sorted by layer), and each cell value is 1 if a runtime dependency
edge exists, 0 otherwise. Color scale: `0 → var(--surface2)`, `1 → var(--accent)`.

### 5.4 Slice descriptions

After the charts, one `.card` per architectural layer explaining what the layer is
responsible for, which packages belong to it, and which layers it depends on.

CDN scripts required:
```html
<script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/echarts@5/dist/echarts.min.js"></script>
```

---

## Phase 6 — Generate `docs/adr.html`

### 6.1 ECharts: ADR timeline

`<div id="adr-timeline" style="height:260px">` — ECharts scatter chart where the X axis
is time (dates from the ADR files, or evenly spaced ordinal positions when dates are
absent) and each point is labelled with the ADR number. Color points by status
(Accepted → `--accent3`, Superseded → `--muted`, Proposed → `--warning`).

### 6.2 ECharts: ADR count per topic area (bar)

`<div id="adr-topics" style="height:280px">` — categorise ADRs into broad topic buckets
(e.g. Repository, Messaging, HTTP, Testing, Tooling, Governance) by keyword matching the
title, then show a bar chart of counts per bucket.

### 6.3 ADR list

After the charts, render every ADR as a `<details class="adr-record">` element:

```html
<details class="adr-record" id="adr-NNNN">
  <summary>
    <span class="badge badge-blue">NNNN</span>
    <span class="adr-title">Title</span>
    <span class="badge badge-green">Accepted</span>
    <span class="adr-date">YYYY-MM-DD</span>
  </summary>
  <div class="adr-body">
    <p>Summary sentence.</p>
  </div>
</details>
```

CDN scripts required:
```html
<script src="https://cdn.jsdelivr.net/npm/echarts@5/dist/echarts.min.js"></script>
```

---

## Phase 7 — Generate `docs/getting-started.html`

A step-by-step guide for a developer adding Forge.Platform to a new ASP.NET Core project.
Derive concrete package names, method names, and attribute names from the actual source;
do **not** invent APIs. Use `<ol class="steps">` for the numbered flow.

### Sections

1. **Page header**: title "Getting Started", lead line describing the goal.

2. **Prerequisites** (`.card`): .NET version (`<TargetFramework>` from any project),
   recommended IDE, link to NuGet.

3. **Add packages** (`.topic`): a table (`.api-table`) listing the NuGet package name
   (= project name) and a one-line role for each package the user is likely to add first
   (Entity, Entity.Generators, Repository, Repository.InMemory, Operations,
   Operations.Http, Execution.Http). Show a `dotnet add package` snippet in a
   `<pre class="recipe-code">`.

4. **Wire DI** (`.topic`): a `<pre class="recipe-code">` block showing a minimal
   `Program.cs` with the core `AddForge…()` registrations derived from reading the
   Application.Sample `Program.cs`. Annotate each call with an inline comment.

5. **Define your first entity** (`.topic`): a `<pre class="recipe-code">` block showing
   a minimal entity class with `[Entity]`, `[Identity]`, and one `[Predicate]` property.
   Derive the exact attribute signatures from the source generator or entity source.
   Follow with a `.tip` explaining what the source generator emits.

6. **Save and query** (`.topic`): two `<pre class="recipe-code">` blocks —
   one for saving an entity via `IEntityRepository`, one for a LINQ query via
   `IQueryable<T>` on the SPARQL layer. Derive method signatures from the source.

7. **Next steps** (`.topic`): a `.card-grid` linking to Cookbook, Configuration,
   and Testing pages.

No CDN scripts required on this page.

---

## Phase 8 — Generate `docs/cookbook.html`

A collection of copy-paste recipes. Each recipe is a `.recipe` card with a title,
a "when to use" sentence, and a `<pre class="recipe-code">` code block.
Derive all API names, attribute names, and method signatures from the actual source;
do **not** invent APIs.

Include **at minimum** the following recipes (add more if useful patterns are found
in Application.Sample or the src/ tests):

| # | Recipe title | Derive from |
|---|-------------|-------------|
| 1 | Define a simple entity | `Entity` attrs, generator output |
| 2 | Define a related entity (object reference) | relation attributes in `Forge.Entity` |
| 3 | HTTP CRUD endpoints for an entity | `Forge.Operations.Http` / `MapOperations()` |
| 4 | Scope a request to a named branch | `BranchScopeMiddleware`, `X-Forge-Branch` header |
| 5 | Implement a synchronous capability | `ICapabilityHandler<TReq,TResp>`, `[Capability]` |
| 6 | Dispatch a capability over messaging (async) | `IAsyncCapabilityDispatcher`, ADR-0022 |
| 7 | Emit entity change events | `EventEmittingTransactionalStore`, ADR-0021 |
| 8 | Object-bearing entity (file upload / download) | `[ObjectBearing]`, `MapObjectOperations()` |
| 9 | Add a SHACL aspect guard | `AddForgeAspectsForKeyedStore()`, inline TTL |
| 10 | Apply an authorization rule | `GuardedTransactionalStore`, `AuthorizationOptions` |

Group recipes under `<h2 class="section-title">` headings by theme
(Entity, HTTP, Branching, Capabilities, Messaging, Storage, Security).

No CDN scripts required on this page.

---

## Phase 9 — Generate `docs/api-reference.html`

A lightweight API browser — one section per package.

For each non-generator `src/` project:

1. Read all public `interface`, `class`, and `static class` (extension method classes)
   declarations from the `.cs` files.
2. Collect: interfaces with a one-line description, the most important classes, and
   every public extension method class with its methods.

Render as:

```html
<section class="topic" id="api-forge-<slug>">
  <h2 class="section-title">Forge.Name</h2>

  <h3>Interfaces</h3>
  <table class="api-table">
    <thead><tr><th>Name</th><th>Description</th></tr></thead>
    <tbody>
      <tr><td><code>IFoo</code></td><td>One-line description.</td></tr>
    </tbody>
  </table>

  <h3>Key types</h3>
  <!-- same table structure -->

  <h3>Extension methods</h3>
  <!-- same table structure; Name column = ClassName.MethodName(…) -->
</section>
```

Omit sections that have no entries. Order packages from most fundamental to most derived
(same order as `projects.html`).

No CDN scripts required on this page.

---

## Phase 10 — Generate `docs/configuration.html`

Every `AddForge…()` and `UseForge…()` / `Map…()` registration method, with its
parameters and what it wires up.

Discover methods by searching for `public static` extension methods named `Add*` or
`Use*` or `Map*` in each project's `.cs` files.

Render as a `.config-section` per package:

```html
<div class="config-section" id="config-forge-<slug>">
  <h2 class="section-title">Forge.Name</h2>
  <table class="api-table">
    <thead><tr><th>Method</th><th>Parameters</th><th>Registers / configures</th></tr></thead>
    <tbody>
      <tr>
        <td><code>AddForgeFoo()</code></td>
        <td><code>Action&lt;FooOptions&gt;? configure = null</code></td>
        <td>Registers <code>IFoo</code> as singleton; binds <code>FooOptions</code> from configuration.</td>
      </tr>
    </tbody>
  </table>

  <!-- If the package has configurable Options types, add a code block showing defaults -->
  <pre class="recipe-code">// FooOptions defaults
new FooOptions { … };</pre>
</div>
```

Add a `.tip` at the top of the page: *"All registrations follow the managed-entity keyed
store pattern described in ADR-0019. Registration order is irrelevant thanks to the
decorator chain described in ADR-0014."*

No CDN scripts required on this page.

---

## Phase 11 — Generate `docs/testing.html`

How to test code that depends on Forge.Platform.

### Sections

1. **Page header**: title "Testing Guide".

2. **Philosophy** (`.topic`): one paragraph — the InMemory backends make all Forge
   components testable without a running database or broker; shared test fixtures
   (ADR-0007, ADR-0015) keep setup DRY.

3. **Unit tests with InMemory backends** (`.topic`):
   - Explain `Forge.Repository.InMemory` (`InMemoryEntityStore`) and
     `Forge.Messaging.InMemory` (`InMemoryMessageBroker`).
   - Show a minimal xUnit fact using `InMemoryEntityStore` derived from the
     `tests/` source files — real code, not invented.
   - Add a `.tip` about registering the store in a `WebApplicationFactory`.

4. **Shared test fixtures** (`.topic`):
   - Read `tests/Entity.Tests.Fixtures/` and `tests/Entity.Tests.Fixtures.Core/`
     to explain what they provide.
   - Show how to inherit from the shared fixture base class (derive from actual code).

5. **Integration tests** (`.topic`):
   - Explain the `tests/Application.Sample.Tests/` project and how it uses
     `WebApplicationFactory`.
   - Brief note on Bruno integration tests (ADR-0012, ADR-0016, ADR-0018).

6. **Test project overview** (`.topic`): a `.api-table` listing every `tests/` project
   (read `./tests/` directory) with a one-line description of what it tests.

No CDN scripts required on this page.

---

## Phase 12 — Generate `docs/sample.html`

An annotated walkthrough of the `./samples/Application.Sample/` project.

### Sections

1. **Page header**: title "Sample Application Walkthrough".

2. **What the sample demonstrates** (`.topic`): one paragraph derived from reading the
   project files — what entity types are defined, what endpoints are exposed, which
   optional slices (branching, capabilities, messaging, object storage) are wired in.

3. **Project structure** (`.topic`): a `<pre class="recipe-code">` tree of the sample
   project's files (read the directory), annotated with inline comments.

4. **Program.cs walkthrough** (`.topic`): reproduce the actual `Program.cs` content in
   a `<pre class="recipe-code">` block, then below it a table (`.api-table`) mapping
   each `AddForge…()` call to its purpose and the docs page where it is explained in
   detail (link to `configuration.html#config-forge-<slug>`).

5. **Entity definitions** (`.topic`): for each `[Entity]` class found in the sample,
   show its code in a `<pre class="recipe-code">` and explain what the generator emits.

6. **Endpoints** (`.topic`): a `.api-table` listing every HTTP route registered by the
   sample (`MapOperations`, `MapBranches`, `MapCapabilities`, `MapObjectOperations`, …),
   the HTTP verb, path pattern, and a one-line description.

7. **Key decisions** (`.topic`): a `.card-grid` where each card links to the ADR that
   explains a decision visible in the sample (e.g. ADR-0019 → aspect enforcement,
   ADR-0017 → shared operation contracts).

No CDN scripts required on this page.

---

## Phase 13 — Quality gate

After writing all files, perform these checks and fix any issues found before reporting:

1. Every HTML file has `<link rel="stylesheet" href="style.css">` and **no** `<style>`
   block of its own.
2. Every nav `<a class="nav-link">` correctly marks the current page as `active`; all
   ten nav links are present on every page.
3. All Mermaid `<div class="mermaid">` blocks: balanced brackets, no duplicate node IDs,
   every referenced node declared, all special HTML characters escaped.
4. All ECharts `<div>` targets have a matching `echarts.init(document.getElementById(…))`
   call in a `<script>` block at the bottom of `<body>`.
5. CDN `<script>` tags are present only for libraries actually used on that page.
6. `style.css` defines every CSS class referenced across all HTML files
   (including `.steps`, `.recipe`, `.recipe-code`, `.api-table`, `.config-section`, `.tip`).
7. All `<pre class="recipe-code">` blocks contain real API names derived from the source,
   not placeholder or invented identifiers.
8. Every internal `<a href>` link (between pages, to `#anchor` IDs) resolves to a target
   that exists in the generated files.

Report the list of generated files and a one-line summary for each.

---

### 🏰 Continue your visit

| Command | What awaits you |
|---|---|
| `/Forge-Storyline` | *"Tell me the city story."* The medieval guided tour — warm, vivid, no technical scrolls. |
| `/Forge-Explorer` | *"Show me around, but faster."* The quick friendly pitch, dwarf-style, for sharing with teammates. |
| `/Forge-Architect` | *"Take me to the blueprints room."* Full technical deep-dive — every ADR, every type, every test file explained. |
| `/Forge-Developer` | *"I want to add a new district."* The full build workflow — ADRs first, then implementation, tests green before done. |
| `/Forge-Documentation` | *"Show me the map."* Generates the full docs site: style.css + ten HTML pages (index, projects, architecture, adr, getting-started, cookbook, api-reference, configuration, testing, sample). |
| `/Forge-Automation` | *"Register a new voice in the herald network."* Draft a new prompt and wire it into every existing guide. |
