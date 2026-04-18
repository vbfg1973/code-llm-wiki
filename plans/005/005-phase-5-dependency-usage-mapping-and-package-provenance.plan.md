# Plan: Phase 5 Dependency Usage Mapping and Package Provenance

> Source PRD: `plans/005/005-phase-5-dependency-usage-mapping-and-package-provenance.prd.md`

## Execution checklist

- [ ] Phase 1: Declaration Dependency Usage Vertical Slice — completion date:
- [ ] Phase 2: Method-Body Dependency Usage Vertical Slice — completion date:
- [ ] Phase 3: Project-Scoped Package Attribution + Unknown/Unresolved Semantics Slice — completion date:
- [ ] Phase 4: Rollup, Determinism, and Contract Hardening Slice — completion date:

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Dependency provenance model**: Capture dependency evidence in two explicit channels using separate predicates:
  - declaration dependency usage
  - method-body dependency usage
- **Evidence granularity**: Persist raw dependency evidence at method granularity; do not persist redundant type rollup edges in ingestion.
- **Rollup boundary**: Derive type-level and package-level summaries in query/wiki projection only.
- **Attribution policy**: Resolve external dependency package attribution deterministically and project-contextually (source method/type origin project).
- **Ambiguity policy**: When deterministic package attribution is unavailable, retain assembly attribution and emit package attribution as unknown.
- **Unresolved policy**: Emit explicit unresolved dependency entities with reason codes; never silently drop unresolved evidence.
- **Method-body evidence scope (v1)**: Include invocation targets, object creation, static/instance member access, casts and `is`/`as`, and `typeof`; exclude `nameof`.
- **Declaration evidence scope (v1)**: Include inheritance/implementation, declared member and method signature types, attributes, and generic constraints.
- **Package usage navigation contract**: Package-centric dependency usage must be rendered/grouped as namespace -> type -> method with split provenance sections and deterministic counts/order.
- **Type-target scope boundary**: BL-011 v1 models type dependencies only; external method/member target granularity is deferred.
- **Contract boundary**: Keep this phase additive and compatible with existing graph/query/wiki contracts and deterministic output expectations.
- **Delivery boundary**: Include ingestion + ontology + query + wiki + deterministic tests; exclude MCP/query-surface expansion in this phase.

---

## Phase 1: Declaration Dependency Usage Vertical Slice

**User stories**: 1, 2, 5, 6, 7, 8, 9, 13, 14, 15, 20, 22, 24, 25, 26, 33, 34, 37

### What to build

Implement the first end-to-end dependency slice for declaration provenance. Add additive graph predicates and extraction/projection behavior to capture declaration dependency evidence at method granularity, then publish package-centric wiki usage trees grouped by namespace, type, and method with deterministic ordering and human-readable links.

### Acceptance criteria

- [ ] Declaration provenance dependency predicate(s) are emitted from ingestion for declaration dependency evidence.
- [ ] Query projections expose declaration dependency usage grouped as package -> namespace -> type -> method with deterministic counts/order.
- [ ] Package wiki output includes declaration dependency usage sections with navigable links and deterministic ordering.
- [ ] Tests verify declaration extraction behavior through public boundaries and deterministic rendering/output.

---

## Phase 2: Method-Body Dependency Usage Vertical Slice

**User stories**: 1, 2, 5, 6, 7, 8, 9, 16, 17, 20, 22, 24, 25, 26, 28, 29, 30, 33, 34, 37

### What to build

Implement the second end-to-end dependency slice for method-body provenance. Capture method-body dependency evidence for the approved operation forms, project it into package-centric grouping, and render deterministic wiki sections parallel to declaration output while preserving readability and explicit provenance semantics.

### Acceptance criteria

- [ ] Method-body provenance dependency predicate(s) are emitted from ingestion for approved operation forms.
- [ ] `nameof` is excluded from method-body dependency evidence in v1.
- [ ] Query projections expose method-body dependency usage grouped as package -> namespace -> type -> method with deterministic counts/order.
- [ ] Package wiki output includes method-body dependency usage sections with navigable links and deterministic ordering.
- [ ] Tests verify method-body extraction/projection/render behavior through public boundaries.

---

## Phase 3: Project-Scoped Package Attribution + Unknown/Unresolved Semantics Slice

**User stories**: 10, 11, 12, 13, 18, 19, 27, 30, 36, 39, 40

### What to build

Implement deterministic package attribution over external dependency usage using source project context, including mixed-version correctness and explicit fallback behavior for ambiguous mappings. Add explicit unresolved dependency entities with reason codes and ensure these semantics are propagated into query and wiki dependency views.

### Acceptance criteria

- [ ] External dependency package attribution uses source method/type project context.
- [ ] Deterministic mapping is applied only when attribution certainty is available.
- [ ] Unknown package attribution is emitted explicitly when deterministic mapping is unavailable.
- [ ] Unresolved dependency entities with reason codes are emitted and queryable.
- [ ] Package/wiki dependency views surface unknown/unresolved dependency semantics explicitly.
- [ ] Tests cover mixed-version multi-project attribution and ambiguity/unresolved scenarios.

---

## Phase 4: Rollup, Determinism, and Contract Hardening Slice

**User stories**: 3, 4, 8, 23, 24, 25, 31, 32, 35, 36, 38

### What to build

Harden the BL-011 contract by finalizing derived type-level rollups from raw method evidence, enforcing deterministic output invariants, and expanding regression/golden coverage to ensure only intentional dependency-view deltas are accepted. Confirm no scope creep into deferred surfaces.

### Acceptance criteria

- [ ] Type-level dependency rollups are derived in query/wiki from method-level evidence without duplicating ingestion truth.
- [ ] Deterministic ordering/count invariants are enforced across dependency query and wiki outputs.
- [ ] Regression coverage validates stable behavior for declaration/method-body split, attribution semantics, and unresolved semantics.
- [ ] Golden/snapshot tests are updated only for intentional BL-011 output deltas.
- [ ] No MCP/query-surface expansion is introduced in this phase.
