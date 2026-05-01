---
description: "Generate /docs — HTML page with Mermaid dependency diagram and descriptions for all src/ projects."
name: "Forge Documentation"
argument-hint: "Optionally focus on a specific project or area, or hit Enter to generate all docs."
mode: agent
tools: [read_file, create_file, replace_string_in_file, list_dir, file_search, grep_search]
---
# Forge.Platform — Documentation Generator

You generate documentation for the Forge.Platform repository. Your output is written to
`./docs/projects.html`. You do **not** change any source files.

## What to do

### 1 — Discover all projects

List `./src/` and collect every `*.csproj` file. For each project file:

- Read the `<ProjectReference>` elements to map inter-project dependencies.
- Note the `<TargetFramework>` and any special properties (`<IsRoslynComponent>`, etc.).
- Note which projects are consumed as `OutputItemType="Analyzer"` (source generators wired into
  another project as a build-time analyzer rather than a runtime dependency).

### 2 — Understand each project

For each project, read the source files in its directory (`.cs` files, `README.md` if present) at
a high level to produce a one-to-two sentence description of what the project does and who its
consumers are.

Use this lore as a starting point but verify and correct it against the actual source:

| Project | Role |
|---------|------|
| `Forge.Entity` | Core runtime library — entity model, attributes, identity strategies, IRI materialization, lazy-loading wiring. |
| `Forge.Entity.Generators` | Roslyn incremental source generator (`netstandard2.0`) that processes `[Entity]` annotations at compile time and emits all boilerplate code for `Forge.Entity`. |
| `Forge.Entity.Repository` | Abstract persistence layer — `IEntityRepository`, `IEntityStore`, `IRdfMapper`, codec and predicate resolution. |
| `Forge.Entity.Repository.InMemory` | In-memory `IEntityStore` / `ISparqlQueryStore` implementation backed by dotNetRDF — ideal for unit tests. |
| `Forge.Entity.Repository.GraphDb` | GraphDB (Ontotext) `IEntityStore` / `ISparqlQueryStore` implementation over HTTP using dotNetRDF and `IHttpClientFactory`. |
| `Forge.Entity.Sparql` | SPARQL query builder — translates `IQueryable<T>` and predicate maps into SPARQL SELECT/CONSTRUCT queries against an `ISparqlQueryStore`. |
| `Forge.Entity.Operations` | High-level entity operations (load, save, delete) that orchestrate `IEntityRepository`, `IEntityStore`, and `Forge.Entity.Sparql`. |
| `Forge.Entity.Operations.Generators` | Roslyn incremental source generator (`netstandard2.0`) for `Forge.Entity.Operations` — emits operation dispatch boilerplate. |

### 3 — Produce `./docs/projects.html`

Create (or overwrite) `./docs/projects.html` as a self-contained HTML file. Use the
Mermaid CDN (`https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js`) so the page
renders without a build step. Structure:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <title>Forge.Platform — Projects</title>
  <!-- minimal reset + readable typography, no external CSS framework -->
  <style>/* inline styles only */</style>
</head>
<body>
  <h1>Forge.Platform — Projects</h1>
  <p><!-- one-paragraph platform overview --></p>

  <h2>Dependency diagram</h2>
  <div class="mermaid">
    graph TD
    <!-- diagram body -->
  </div>

  <!-- one <section> per project -->
  <section>
    <h2><code>Forge.&lt;Name&gt;</code></h2>
    <dl>
      <dt>Target framework</dt><dd><code>&lt;framework&gt;</code></dd>
      <dt>Namespace</dt><dd><code>&lt;RootNamespace&gt;</code></dd>
    </dl>
    <p><!-- one-to-two sentence description --></p>
  </section>

  <script type="module">
    import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.esm.min.mjs';
    mermaid.initialize({ startOnLoad: true });
  </script>
</body>
</html>
```

#### 3.1 — Header and intro

Title `<h1>Forge.Platform — Projects</h1>` followed by one short paragraph: what this
platform is and what these projects collectively deliver.

#### 3.2 — Dependency diagram

A `<div class="mermaid">` block using `graph TD` showing every inter-project dependency.
Rules:

- Node IDs are the short project names without the `Forge.` prefix
  (e.g. `Entity`, `EntityGenerators`, `EntityRepository`, …).
- Node labels use the full NuGet-style name in double quotes
  (e.g. `Entity["Forge.Entity"]`).
- Use `-->` for runtime `<ProjectReference>` dependencies.
- Use `-.->` (dashed arrow) for analyzer/generator references
  (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`).
- Add a `classDef generator` style for `netstandard2.0` Roslyn generator projects
  and apply it with `class … generator`.
- Do not invent edges that don't exist in the `.csproj` files.
- HTML-escape any `<`, `>`, `"`, `&` that appear inside the `<div class="mermaid">` block.

#### 3.3 — Project sections

For every project (ordered from most fundamental to most derived), one `<section>` element:

```html
<section id="forge-<slug>">
  <h2><code>Forge.&lt;Name&gt;</code></h2>
  <dl>
    <dt>Target framework</dt><dd><code>&lt;framework&gt;</code></dd>
    <dt>Namespace</dt><dd><code>&lt;RootNamespace&gt;</code></dd>
  </dl>
  <p>&lt;One-to-two sentence description derived from reading the source.&gt;</p>
</section>
```

### 4 — Final check

After writing the file, read it back and confirm:
- The Mermaid block is syntactically valid (balanced brackets, no duplicate node IDs, all
  referenced nodes declared).
- The Mermaid CDN `<script>` tag is present and uses the ESM import form.
- All special characters inside the `<div class="mermaid">` block are HTML-escaped.

Report the path of the generated file and a one-line summary of what was written.
