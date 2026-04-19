# Plan: Phase 7 Complexity and Maintainability Metrics

> Source PRD: [PRD 007](./007-phase-7-complexity-and-maintainability-metrics.prd.md)

## Execution checklist

- [x] Phase 1: Metric Facts Baseline (Method + Type Core) — completion date: 2026-04-19
- [ ] Phase 2: Rollups and Scope Semantics (File/Namespace/Project/Repository) — completion date:
- [ ] Phase 3: Hotspot Ranking and Severity Contracts — completion date:
- [ ] Phase 4: Publication Surfaces (Wiki + Manifest + Front Matter) — completion date:
- [ ] Phase 5: Determinism, Parallelism, and Performance Hardening — completion date:

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Primary metric facts**: Persist metric facts during ingestion as first-class triples.
  - method: cyclomatic, cognitive, Halstead core set, LOC breakdown, maintainability index
  - type: CBO declaration/method-body/total breakdown
- **Aggregation topology**: Derive rollups in query/publication layers for file, namespace hierarchy (direct and recursive), project, and repository.
- **Hotspot topology**: Methods/types are primary rankings; file/namespace/project/repository are rollup dashboards.
- **Normalization and severity**:
  - repository-relative normalization for ranking
  - raw values always exposed
  - percentile severity defaults with optional fixed overrides
  - composite severity primary, per-metric severities retained
- **Code-kind defaults**: Production-only default ranking; test/generated remain filterable.
- **Generated-code handling**: Heuristic detection with default exclusion from ranked lists and explicit inclusion filter support.
- **Method scope**: Include executable member bodies; fold local functions/lambdas into containing methods; exclude no-body members from rankings while reporting coverage counts.
- **CBO semantics**:
  - include declaration-context and method-body dependencies
  - dedupe per owning type
  - include generic containers and concrete generic arguments
  - normalize wrappers to underlying named types
- **Namespace semantics**: Include synthetic `(global)` namespace and recursive + direct rollup views.
- **Determinism contract**:
  - ranking tie-break: score desc, severity desc, stable identity asc
  - bounded parallel compute is allowed but emission ordering must be deterministic
- **Output surfaces**:
  - dedicated `hotspots/` wiki section with separate pages per entity kind
  - minimal scalar front matter
  - compact metrics summary in run manifest
- **Execution policy**: Partial analysis results are allowed and explicitly reported (completeness/skip reasons) except fatal repository setup failures.
- **Scope boundary**: Single snapshot (`HEAD`) only; no trends/deltas and no MCP/query-surface expansion.

---

## Phase 1: Metric Facts Baseline (Method + Type Core)

**User stories**: 1, 2, 3, 4, 5, 6, 18, 19, 37, 38, 39, 40, 41

### What to build

Implement ingestion-time metric extraction for core method/type facts and persist them as stable semantic triples with explicit coverage markers for analyzable and non-analyzable members.

### Acceptance criteria

- [x] Method metric facts are persisted as triples (cyclomatic, cognitive, Halstead core set, LOC breakdown, MI).
- [x] Type CBO facts are persisted as triples with declaration/method-body/total breakdown.
- [x] Methods without analyzable bodies are excluded from rankings but represented in coverage/completeness counts.
- [x] CBO dependency normalization rules (generics + wrapper normalization) are applied deterministically and tested.

---

## Phase 2: Rollups and Scope Semantics (File/Namespace/Project/Repository)

**User stories**: 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 24, 25, 26, 27

### What to build

Build query-layer rollups for each structural scope, including namespace hierarchy direct/cumulative behavior, code-kind filtering semantics, generated-code exclusions, and explicit insufficient-data handling.

### Acceptance criteria

- [ ] File, namespace, project, and repository metric rollups are available and deterministic.
- [ ] Namespace hierarchy supports both direct-only and recursive cumulative rollups, including synthetic `(global)` namespace.
- [ ] Production-default ranking with test/generated filters is available and generated code is excluded by default rankings.
- [ ] Insufficient-data scopes produce explicit `severity: none` semantics and are excluded from ranked tables by default.

---

## Phase 3: Hotspot Ranking and Severity Contracts

**User stories**: 7, 20, 21, 25, 26, 27, 28, 29, 43, 44

### What to build

Implement ranking/severity engines for per-metric and composite hotspot views with repository-relative normalization, configurable weights/thresholds, row-budget controls, and deterministic tie-break behavior.

### Acceptance criteria

- [ ] Per-metric rankings are primary and composite ranking is available as secondary triage score.
- [ ] Composite weighting and threshold configuration are supported with stable defaults and explicit effective values.
- [ ] Deterministic ordering/tie-break contract is enforced and tested across reruns.
- [ ] Default row budget is applied with explicit unbounded override behavior.

---

## Phase 4: Publication Surfaces (Wiki + Manifest + Front Matter)

**User stories**: 22, 23, 30, 31, 32, 33

### What to build

Publish BL-012 outputs into dedicated hotspot wiki pages and manifest summaries while keeping front matter minimal and preserving detailed metric payloads in body sections.

### Acceptance criteria

- [ ] Dedicated `hotspots/` wiki pages are rendered for methods, types, files, namespaces, projects, and repository views.
- [ ] Hotspot/entity front matter remains minimal scalar-only and supports Dataview use.
- [ ] Full metric detail remains in readable body sections with stable links/navigation.
- [ ] Run manifest includes compact metric summary, effective configuration, and coverage/completeness indicators.

---

## Phase 5: Determinism, Parallelism, and Performance Hardening

**User stories**: 34, 35, 36, 42, 45

### What to build

Harden BL-012 with bounded parallel extraction, deterministic post-merge emission, strict dependency/version controls, and end-to-end regression/performance validation under accepted budgets.

### Acceptance criteria

- [ ] Bounded parallel metric computation is implemented with configurable concurrency and deterministic output stability.
- [ ] Dependency versions and analyzer semantics are pinned for reproducible metric behavior.
- [ ] Unit + integration fixture coverage and golden snapshots validate metric correctness and publication contracts.
- [ ] Performance overhead remains within approved budget envelope on representative repositories.
