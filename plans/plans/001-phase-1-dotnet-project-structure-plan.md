# Plan: Phase 1 .NET Repository Structure Ingestion and Wiki Output

> Source PRD: `plans/prds/001-phase-1-dotnet-project-structure-prd.md`

## Architectural decisions

Durable decisions that apply across all phases:

- **Execution surface**: CLI-first with one verb, short/long options, versioned config file plus CLI overrides.
- **Run boundary**: One repository per ingestion run.
- **Provenance**: `HEAD` commit is canonical; dirty working tree is recorded as diagnostics.
- **Data model**: Strict semantic triples, including first-class literal/value nodes.
- **Ontology**: Canonical machine-readable ontology file with semantic versioning and migration discipline.
- **Ontology namespaces**: Language-agnostic core predicates plus `dotnet:*` extension predicates.
- **Identity**: Stable natural keys with deterministic derived IDs.
- **.NET discovery strategy**: MSBuild evaluation first, fallback parsing on evaluation failure.
- **Git file history**: Full timeline, rename-aware identity, true merge commits only.
- **Mainline merge target**: Resolve from `origin/HEAD`, fallback `main`, then `master`.
- **Output model**: Run-scoped artifacts and success-gated atomic promotion to `latest`.
- **Wiki contract**: Deterministic full regeneration with five page types (repository, solution, project, package, file).
- **Front matter**: Mandatory minimal common fields, minimal entity-specific fields, richer details in body sections.
- **Query boundary**: Wiki generation consumes formal query interfaces, not graph internals.
- **Validation/quality**: Schema and ontology validation, golden integration tests, generated-artifact freshness gate in CI.
- **Governance**: ADRs required for major contracts and kept current with changes.
- **Performance envelope**: Target up to ~500k LOC / 20k tracked files in <=10 minutes and <=2 GB memory.

---

## Phase 1: Executable Skeleton and Contracts

**User stories**: 1, 2, 3, 11, 12, 13, 14, 15, 41, 45

### What to build

Deliver a runnable CLI skeleton that can execute a no-op ingestion pipeline end-to-end while enforcing the core contracts: ontology loading/versioning, stable identity model, triple data model, and ADR-backed governance. This phase establishes the stable seams all later slices build on.

### Acceptance criteria

- [ ] CLI command executes with config file and option overrides, and returns deterministic run status.
- [ ] Ontology source-of-truth is loaded and validated before ingestion work begins.
- [ ] Core graph identity/triple contracts are defined and usable by downstream modules.
- [ ] ADR set exists for ontology, identity, query boundary, front matter policy, and exit semantics.

---

## Phase 2: Repository and Solution/Project Vertical Slice

**User stories**: 4, 7, 8, 16, 19, 34, 40

### What to build

Implement the first real vertical slice that ingests repository structure and solution/project topology into triples, exposes the data through the query contract, and renders minimal repository/solution/project pages. The slice must support partial completion with diagnostics and remain demoable end-to-end.

### Acceptance criteria

- [ ] Repository root, solutions, and projects are ingested from real repositories into triples.
- [ ] MSBuild-first discovery works, with fallback behavior producing diagnostics when needed.
- [ ] Query service can retrieve repository/solution/project views used by wiki generation.
- [ ] Wiki pages for repository/solution/project render deterministically from query results.

---

## Phase 3: Package Graph Vertical Slice

**User stories**: 9, 10, 19, 34, 35

### What to build

Extend ingestion/query/wiki to include package dependencies as graph facts, capturing declared dependencies always and resolved versions when locally available. This slice must preserve strict ontology boundaries and demonstrate clean extension of the existing query and wiki contracts.

### Acceptance criteria

- [ ] Declared package dependencies are represented and queryable for each project.
- [ ] Resolved package versions are ingested when local artifacts are available, with explicit diagnostics otherwise.
- [ ] Package wiki pages are generated and linked from project pages.
- [ ] Ontology/version validation passes with package predicates added under approved namespaces.

---

## Phase 4: File Inventory and Classification Vertical Slice

**User stories**: 5, 6, 19, 22, 33

### What to build

Add repository-wide file ingestion for all git-tracked `HEAD` files, including classification metadata and file-level pages focused on structural metadata (no code excerpts). The slice should provide useful filtering context for humans and LLMs while keeping pages concise.

### Acceptance criteria

- [ ] All git-tracked files at `HEAD` are represented in the graph.
- [ ] File classification facts are populated and exposed through query interfaces.
- [ ] File wiki pages render metadata-first content with no source code excerpts.
- [ ] Front matter remains minimal and body sections carry richer human-readable context.

---

## Phase 5: Git History and Merge-to-Mainline Vertical Slice

**User stories**: 27, 28, 29, 30, 31, 32, 43, 44

### What to build

Integrate git-history depth into file entities: full rename-aware timelines, summary metrics, and merge-to-mainline events constrained to true merge commits. Render branch derivation context and file-specific merge-source commit counts on file pages.

### Acceptance criteria

- [ ] Full per-file commit history is ingested with rename continuity.
- [ ] File summaries include edit count and last-change provenance fields.
- [ ] Merge-to-mainline entries include timestamp, author, target branch context, and file-specific source-branch commit count.
- [ ] Submodule presence is represented as opaque dependency facts without recursive ingestion.

---

## Phase 6: Deterministic Wiki + GraphML Publication

**User stories**: 20, 21, 23, 24, 25, 26, 34

### What to build

Complete output publication behavior: deterministic full regeneration of wiki outputs, GraphML serialization, run-scoped artifact layout, machine-readable run manifest, and atomic success-gated `latest` promotion. This slice makes the system operationally consumable.

### Acceptance criteria

- [ ] All five wiki entity page types generate deterministically from query interfaces.
- [ ] GraphML export is emitted for each run and aligned with graph facts.
- [ ] Run manifest includes status, timings, counts, diagnostics summary, and artifact references.
- [ ] `latest` is updated atomically only when generation and validation succeed.

---

## Phase 7: Validation, Golden Tests, and CI Gates

**User stories**: 17, 18, 36, 37, 38, 39

### What to build

Harden delivery with behavioral tests and enforcement gates: unit coverage for stable module interfaces, golden end-to-end snapshots for wiki and GraphML, exit code semantics verification, and CI checks that fail on stale generated artifacts or contract violations.

### Acceptance criteria

- [ ] Unit tests verify external behavior for core modules without coupling to internals.
- [ ] Golden integration tests validate deterministic wiki and GraphML outputs for fixture repositories.
- [ ] CLI exit behavior is verified for success, partial-success, and override modes.
- [ ] CI fails when generated artifacts are stale or ontology/front matter validation fails.

---

## Phase 8: Human-Readable Obsidian Page Contracts

**User stories**: 19, 20, 21, 22, 23, 34

### What to build

Replace ID-based wiki filenames with human-readable names and mirrored file layout for navigability in Obsidian. Switch internal references to Obsidian wikilinks and introduce a canonical repository index page that maps stable IDs to page paths.

### Acceptance criteria

- [ ] File pages are emitted under mirrored repository-relative paths (for example `files/src/App/Program.cs.md`).
- [ ] Solution/project/package filenames are idiomatic human-readable names with deterministic non-ID disambiguation only on collision.
- [ ] Internal cross-page links use Obsidian wikilink syntax.
- [ ] A canonical index page exists at `index/repository-index.md` with per-entity tables and `entity_id -> page_link` mappings.

---

## Phase 9: Scalar Front Matter Schema v1

**User stories**: 20, 21, 22, 27, 34

### What to build

Implement and validate a strict scalar-only front matter schema optimized for Obsidian Dataview and graph usage while preventing metadata bloat.

### Acceptance criteria

- [ ] Common front matter exists on all pages: `entity_id`, `entity_type`, `repository_id`.
- [ ] Entity-specific scalar fields are emitted exactly as approved (repository, solution, project, package, file).
- [ ] Front matter keys are snake_case and timestamps are normalized UTC ISO-8601 with `Z`.
- [ ] Validation tests fail when required front matter fields are missing or malformed.

---

## Phase 10: Package Membership and Version Context

**User stories**: 9, 10, 19, 22, 34, 35

### What to build

Extend package outputs from aggregate versions to project membership context so package pages show which projects reference them and which declared/resolved versions are in use per project.

### Acceptance criteria

- [ ] Package pages include a deterministic per-project table: project, project path, declared version, resolved version.
- [ ] Project outputs remain human-readable while retaining path and framework context.
- [ ] Package linking remains deduplicated via canonical package identity and key normalization.
- [ ] Golden tests cover package membership table output determinism.

---

## Phase 11: File History Presentation Controls

**User stories**: 27, 28, 30, 31, 32, 33

### What to build

Align file-page history presentation to human readability: most-recent-first merge history, unbounded by default, with optional operator-configured entry caps for budgeted runs.

### Acceptance criteria

- [ ] File page titles use repository-relative file paths.
- [ ] Merge-to-mainline entries render most recent first and default to unbounded output.
- [ ] CLI/config support optional `max_merge_entries_per_file` without changing default behavior.
- [ ] Golden and behavioral tests verify ordering, cap behavior, and unchanged default output.
