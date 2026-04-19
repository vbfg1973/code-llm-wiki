# Plan: Phase 10 Semantic Resolution Quality and Ingestion Observability

> Source PRD: [PRD 010](/home/vbfg/dev/dotnet-llm-wiki/plans/010/010-phase-10-semantic-resolution-quality-and-ingestion-observability.prd.md)

## Execution checklist

- [x] Phase 1: Diagnostics Taxonomy and Stage Telemetry Baseline — completion date: 2026-04-19
- [x] Phase 2: Project-Scoped Semantic Call Resolution Slice — completion date: 2026-04-19
- [x] Phase 3: Override Resolution and Type-Fallback Noise Reduction — completion date: 2026-04-19
- [x] Phase 4: Quality Gate Policy and Run Status Integration — completion date: 2026-04-19
- [ ] Phase 5: Bounded Parallelism and Deterministic Merge Hardening
- [ ] Phase 6: Docs Runbook, Contracts, and Regression Hardening

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Semantic context model**: resolve method calls and overrides from owning-project semantic contexts rather than runtime-only ad-hoc references.
- **Fallback policy**: never fabricate edges when semantic certainty is absent; preserve partial output with explicit diagnostic evidence.
- **Diagnostics compatibility**: add cause-level subcodes while preserving aggregate compatibility for existing summary consumers.
- **Quality policy**: enforce unresolved-call ratio against a global default threshold in this phase.
- **Type fallback normalization**: normalize nullable/array representations before unresolved classification and dedupe.
- **Observability contract**: emit stage timings to stderr during execution using stable stage identifiers.
- **Approved stage identifiers**: `project_discovery`, `source_snapshot`, `declaration_scan`, `semantic_call_graph`, `endpoint_extraction`, `query_projection`, `wiki_render`, `graphml_serialize`.
- **Parallelism contract**: allow bounded parallel processing where output ordering remains deterministic.
- **Operational scope**: single-repository, `HEAD`-first snapshot semantics remain unchanged.
- **Documentation boundary**: explanatory diagnostic meanings/remediation belong in repo docs; wiki diagnostics remain data/status focused.

---

## Phase 1: Diagnostics Taxonomy and Stage Telemetry Baseline

**User stories**: 6, 7, 8, 13, 14, 15, 16, 25, 30, 41, 42, 43, 45

### What to build

Deliver a complete vertical slice that introduces cause-level call-resolution diagnostics and live stage timing emission to stderr with stable stage identifiers and deterministic formatting.

### Acceptance criteria

- [x] Call-resolution diagnostics are emitted with explicit cause-level subcodes.
- [x] Aggregate `method:call:resolution:failed` compatibility is preserved in summaries.
- [x] Stage timing events are emitted to stderr at stage boundaries using approved stage identifiers.
- [x] Stage timing output is stable and parse-safe across reruns.

---

## Phase 2: Project-Scoped Semantic Call Resolution Slice

**User stories**: 1, 2, 3, 4, 19, 20, 21, 22, 23, 26

### What to build

Implement project-scoped semantic compilation contexts and route method-call resolution through owning-project analysis so call edges resolve against actual project references and produce improved call-target fidelity.

### Acceptance criteria

- [x] Method call resolution uses owning-project semantic context as primary path.
- [x] Internal resolvable calls are emitted as resolved method-to-method edges.
- [x] Unresolved calls remain explicit with cause-coded diagnostics.
- [x] Multi-project fixtures verify improved resolution behavior and deterministic output.

---

## Phase 3: Override Resolution and Type-Fallback Noise Reduction

**User stories**: 5, 17, 18, 27, 44

### What to build

Extend project-scoped semantic resolution to override relationships and introduce normalized nullable/array type fallback handling to reduce repetitive, low-signal fallback diagnostics.

### Acceptance criteria

- [x] Override relationships resolve through project-scoped semantic contexts.
- [x] Nullable and array type references are normalized before fallback classification.
- [x] Fallback diagnostic dedupe reduces repeated noise while preserving evidence.
- [x] Tests verify override resolution and normalized fallback behavior stability.

---

## Phase 4: Quality Gate Policy and Run Status Integration

**User stories**: 9, 10, 11, 12, 24, 28, 31, 32, 37, 38

### What to build

Implement unresolved-call-ratio quality policy as a first-class run gate, integrate gate outcomes into run status/exit semantics and manifest evidence, and keep non-gated diagnostics behavior explicit.

### Acceptance criteria

- [x] Quality policy computes unresolved-call ratio and evaluates against a global default threshold.
- [x] Gate pass/fail outcomes are reflected in run status and exit behavior.
- [x] Failure output includes measured ratio and threshold values.
- [x] Project discovery fallback remains warning-level and non-gating in this phase.

---

## Phase 5: Bounded Parallelism and Deterministic Merge Hardening

**User stories**: 19, 20, 21, 29, 39, 40

### What to build

Add bounded parallel project processing for semantic analysis and enforce deterministic post-merge ordering for triples, diagnostics, and emitted summaries so performance can improve without output drift.

### Acceptance criteria

- [ ] Project processing supports bounded concurrency configuration.
- [ ] Triple and diagnostic emission ordering is deterministic under parallel execution.
- [ ] Parallel determinism is validated by repeat-run tests.
- [ ] Representative large-fixture runs show stable behavior with improved throughput characteristics.

---

## Phase 6: Docs Runbook, Contracts, and Regression Hardening

**User stories**: 33, 34, 35, 36

### What to build

Publish a repo-level diagnostics runbook with status meanings and remediation guidance, and finalize regression/contract coverage for diagnostics, telemetry, determinism, and quality-gate behavior.

### Acceptance criteria

- [ ] Repo docs include a diagnostics runbook with status meanings, probable causes, and actions.
- [ ] Tests enforce diagnostics contract compatibility and cause-level taxonomy stability.
- [ ] Tests enforce stage timing output contract and manifest quality evidence.
- [ ] Human-readable wiki constraints remain unchanged and validated.
