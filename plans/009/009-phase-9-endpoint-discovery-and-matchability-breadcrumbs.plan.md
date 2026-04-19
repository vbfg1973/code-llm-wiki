# Plan: Phase 9 Endpoint Discovery and Matchability Breadcrumbs

> Source PRD: [PRD 009](/home/vbfg/dev/dotnet-llm-wiki/plans/009/009-phase-9-endpoint-discovery-and-matchability-breadcrumbs.prd.md)

## Execution checklist

- [x] Phase 1: Endpoint Core Contracts and Controller Baseline — completion date: 2026-04-19
- [x] Phase 2: Minimal API Endpoint Slice — completion date: 2026-04-19
- [ ] Phase 3: Message Handler and CLI Endpoint Slice
- [ ] Phase 4: gRPC Endpoint Slice and Partial-Resolution Semantics
- [ ] Phase 5: Matchability Fingerprints and Outbound Breadcrumbs
- [ ] Phase 6: Publication Hardening, Determinism, and Performance

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Endpoint model topology**: represent endpoints as first-class graph entities (`endpoint`, `endpoint_group`, `call_site_candidate`) with links to declaring namespace, type, method, and file.
- **Identity contract**: one canonical endpoint page per synthesized endpoint signature with deterministic tie-break ordering.
- **Route contract**: persist authored route text and a normalized route key for dedupe, grouping, and future matching.
- **Detection strategy**: use a .NET rule catalog with a DSL-shaped contract defined in code for v1; keep detector internals language-specific while output contracts remain language-agnostic.
- **Endpoint families in scope**: controllers, minimal APIs, message handlers, gRPC, and CLI registration semantics.
- **Confidence and diagnostics**: emit confidence enum (`high`, `medium`, `low`, `unknown`) and reason-coded partial/unresolved diagnostics instead of dropping uncertain detections.
- **Provenance contract**: attach rule provenance (catalog version, rule source identifier) to each detection.
- **Navigation and readability**: group endpoint outputs by family/protocol, keep front matter minimal scalar-only, and render rare sections only when populated.
- **Future-linkage breadcrumbs**: emit matchability fingerprints and bounded outbound call breadcrumbs to support later cross-system correlation.
- **Operating scope**: present-state single-repository HEAD snapshot; no cross-system resolution in this phase.

---

## Phase 1: Endpoint Core Contracts and Controller Baseline

**User stories**: 1, 2, 3, 4, 5, 8, 9, 10, 19, 20, 21, 22, 26, 27, 28, 35, 48, 49

### What to build

Deliver a complete controller-endpoint vertical slice that establishes the shared endpoint model, canonical identity, confidence/provenance payloads, graph ingestion, and baseline endpoint wiki publication with method/type/file backlinks.

### Acceptance criteria

- [x] Endpoint entities and required relations are ingested as first-class graph facts.
- [x] Controller attribute-routed endpoints are discovered and published as one page per canonical endpoint signature.
- [x] Endpoint pages include required declaration traceability links (namespace/type/method/file).
- [x] Confidence enum and rule provenance are emitted and validated for controller detections.

---

## Phase 2: Minimal API Endpoint Slice

**User stories**: 11, 16, 17, 18, 23, 25, 41, 42, 46

### What to build

Add a complete minimal-API slice that discovers `Map*` endpoint chains, composes group prefixes and resolved metadata where available, and projects minimal-API endpoints into the same graph/wiki contracts as controllers.

### Acceptance criteria

- [x] Minimal API endpoint detections are ingested with canonical endpoint identity and route normalization.
- [x] Group-prefix route composition is reflected in published endpoint route values.
- [x] Minimal API endpoints appear in family-grouped index/navigation output.
- [x] Fixture-based tests cover grouped mappings and deterministic rendering.

---

## Phase 3: Message Handler and CLI Endpoint Slice

**User stories**: 12, 13, 15, 24, 25, 44, 48

### What to build

Deliver end-to-end endpoint extraction for interface-pattern message handlers and CLI command registrations, including rule-catalog pattern configuration support and publication in shared endpoint views.

### Acceptance criteria

- [ ] Message handlers are detected through interface-pattern rules (including custom interfaces).
- [ ] CLI endpoints are detected from semantic registration patterns without generic `Main` heuristics.
- [ ] Both families publish endpoint pages and are linked from declaring methods/types.
- [ ] Tests verify custom handler-interface pattern behavior and deterministic output.

---

## Phase 4: gRPC Endpoint Slice and Partial-Resolution Semantics

**User stories**: 6, 7, 14, 34, 37, 38

### What to build

Add gRPC endpoint discovery from registration/service semantics and implement the shared fallback policy for partial and unresolved endpoints across all families, including reason-coded diagnostics and confidence behavior.

### Acceptance criteria

- [ ] gRPC endpoints are discovered from service registration semantics and projected into standard endpoint contracts.
- [ ] Partial/unresolved detections are retained and rendered with explicit reason codes.
- [ ] Confidence values are assigned consistently across resolved and unresolved states.
- [ ] Diagnostics are queryable/countable by endpoint family and reason.

---

## Phase 5: Matchability Fingerprints and Outbound Breadcrumbs

**User stories**: 30, 31, 32, 33, 36, 39, 40, 43, 45, 47

### What to build

Add deterministic endpoint matchability fingerprints and bounded outbound call breadcrumbs from endpoint method contexts, including declaration-vs-body usage context tagging and package-provenance linkability where relevant.

### Acceptance criteria

- [ ] Endpoint fingerprint payload is emitted with stable, deterministic fields for later matching.
- [ ] Outbound call breadcrumbs are emitted from endpoint method contexts with bounded scope.
- [ ] Dependency-like breadcrumbs include declaration-context vs method-body-context tagging.
- [ ] External dependency breadcrumbs support navigation to package-level provenance views when applicable.

---

## Phase 6: Publication Hardening, Determinism, and Performance

**User stories**: 17, 18, 24, 25, 29, 41, 42, 50

### What to build

Harden endpoint extraction/publication with deterministic ordering contracts, golden regression coverage, parse-safe link guarantees, and bounded-performance validation on representative repositories.

### Acceptance criteria

- [ ] Endpoint pages and indexes are deterministically ordered and stable across reruns.
- [ ] Publication tests enforce front matter minima, anchor/link contracts, and family-grouped navigation invariants.
- [ ] Golden snapshots cover endpoint family outputs and partial/unresolved rendering.
- [ ] Extraction and publication overhead remain within approved performance budget envelopes.
