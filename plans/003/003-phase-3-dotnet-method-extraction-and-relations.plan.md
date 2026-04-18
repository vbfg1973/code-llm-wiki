# Plan: Phase 3 .NET Method Extraction, Relationship, and Data-Flow Ingestion and Wiki Output

> Source PRD: `plans/003/003-phase-3-dotnet-method-extraction-and-relations.prd.md`

## Execution checklist

- [x] Phase 1: Method Contracts and Ontology Expansion — completion date: 2026-04-18
- [x] Phase 2: Method Declaration and Method Page Vertical Slice — completion date: 2026-04-18
- [x] Phase 3: Implements and Overrides Relationship Vertical Slice — completion date: 2026-04-18
- [ ] Phase 4: Calls, External Usage, and Extension Method Vertical Slice — completion date:
- [ ] Phase 5: Read-Write Data-Flow and Type Count Scalars Vertical Slice — completion date:
- [ ] Phase 6: Publication Determinism, Validation, and CI Gates — completion date:

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Execution boundary**: One repository per run, `HEAD` snapshot as canonical provenance, git-tracked files only.
- **Analyzer boundary**: .NET-specific method extraction remains self-contained and extends existing analyzer -> triples seam.
- **Data model**: Semantic triples remain authoritative; direct method/data-flow edges only, transitive views computed in query layer.
- **Method model**: First-class method entities include named type-level methods and constructors; interface/abstract/extern declarations are included even without bodies.
- **Method identity rule**: Canonical method identity = assembly + declaring type canonical identity + method name + ordered parameter type list + generic arity (return type excluded).
- **Behavior edge policy**: Calls/reads/writes edges are created only when semantic binding succeeds; failures emit diagnostics/provenance.
- **Relationship scope**: Capture `implements_method`, `overrides_method`, and `calls` as direct relationships.
- **Data-flow scope**: Capture internal `reads_property`, `writes_property`, `reads_field`, `writes_field` edges from internal methods.
- **External invocation policy**: External usage captured at external type and assembly granularity only (no deep external method page family).
- **Extension method policy**: Extension methods are in scope, explicitly flagged, and linked to extended internal types.
- **Deferred callable scope**: Local functions, operator/conversion methods, accessor methods as method entities, and top-level statement callers are deferred.
- **Wiki path contract**: Method pages use human-readable signature-based slugs with deterministic collision suffix fallback.
- **Page-family contract**: Methods have own page family; owning type pages provide summary sections with method links.
- **Property backlink policy**: Type pages include property read/write metadata with explicit zero counts and deterministic reader/writer lists.
- **Type-count policy**: Publish raw structural count scalars for Dataview/POCO heuristics; no baked POCO classification flag.
- **Front matter policy**: Scalar-only, minimal, conditional fields only when needed.
- **Body readability policy**: IDs are hidden from visible body content and remain in front matter/index surfaces only.
- **Ordering invariant**: Deterministic ordering everywhere (entities, relationships, backlinks, section lists).
- **Degradation policy**: Partial semantic resolution emits partial output with explicit diagnostics/provenance status.
- **Publication boundary**: Existing query and rendering seam is preserved; outputs remain deterministic and golden-testable.

---

## Phase 1: Method Contracts and Ontology Expansion

**User stories**: 3, 34, 35, 36, 45, 48, 50, 61, 62, 64, 65

### What to build

Establish durable method-analysis contracts and ontology support for method declarations and method-level relationships, while preserving the existing analyzer -> triples -> query -> wiki seam. Deliver a thin end-to-end tracer bullet proving that method-capable predicates/entities flow through ingestion, projection, and rendering contracts.

### Acceptance criteria

- [ ] Ontology includes approved method and method-relationship predicates required by PRD 003 phase boundaries.
- [ ] Query/view contracts are extended for method entities and required relationship projections without regressing existing PRD 001/002 behavior.
- [ ] Deterministic method identity and ordering rules are codified and testable via contract-level tests.
- [ ] Baseline end-to-end pipeline compiles/runs with method contracts enabled and no method extraction behavior yet required.

---

## Phase 2: Method Declaration and Method Page Vertical Slice

**User stories**: 1, 2, 4, 5, 13, 15, 16, 17, 18, 19, 20, 44

### What to build

Deliver the first complete method vertical slice: ingest method/constructor declarations as first-class entities using canonical identity, include declaration provenance, and publish a dedicated method page family linked from owning type pages with deterministic, human-readable contracts.

### Acceptance criteria

- [ ] Named type-level methods and constructors are ingested as first-class method entities with canonical deterministic identity.
- [ ] Method entities include declarations without bodies (interface/abstract/extern) and declaration file/location provenance.
- [ ] Method pages render deterministic signature-oriented summaries and are linked from owning type pages.
- [ ] Method front matter remains minimal scalar and body output remains ID-light/human-readable.

---

## Phase 3: Implements and Overrides Relationship Vertical Slice

**User stories**: 6, 7, 8, 22, 56, 57

### What to build

Add direct method relationship mapping for interface implementation and override behavior, including explicit and implicit interface implementations, with deterministic query/render output surfaces on method pages.

### Acceptance criteria

- [ ] `implements_method` edges are captured for explicit and implicit interface implementations.
- [ ] `overrides_method` edges are captured for overriding methods with deterministic target resolution.
- [ ] Method pages render `Implements` and `Overrides` sections with deterministic ordering.
- [ ] Relationship projection degrades safely when semantic resolution is partial, without fabricating edges.

---

## Phase 4: Calls, External Usage, and Extension Method Vertical Slice

**User stories**: 9, 10, 11, 12, 14, 21, 29, 30, 31, 32, 33, 58

### What to build

Deliver method call relationship extraction for internal callers, including internal and external callee handling, extension method call resolution, and shallow external dependency usage projection at type and assembly level.

### Acceptance criteria

- [ ] `calls` edges are created for internal method callers with semantically resolved targets where possible.
- [ ] External invocation usage is represented at external type and external assembly granularity (no deep external method pages).
- [ ] Extension methods are captured, flagged, resolved from extension-call syntax, and linked to extended internal types.
- [ ] Method pages render deterministic `Calls`/`Called By` sections and degradation diagnostics/provenance when semantic binding fails.

---

## Phase 5: Read-Write Data-Flow and Type Count Scalars Vertical Slice

**User stories**: 23, 24, 25, 26, 27, 28, 42, 43, 59

### What to build

Add internal property/field read-write data-flow extraction from methods and publish POCO-oriented summary outputs: explicit per-property read/write counts and method backlink lists on owning type pages, plus type-level structural count scalars for Dataview usage.

### Acceptance criteria

- [ ] Internal data-flow edges are captured for `reads_property`, `writes_property`, `reads_field`, and `writes_field`.
- [ ] Type pages render per-property read/write counts with deterministic reader/writer method link lists, including explicit zero counts.
- [ ] Type front matter publishes approved structural count scalars (`constructor_count`, `method_count`, `property_count`, `field_count`, `enum_member_count`, `record_parameter_count`, `behavioral_method_count`).
- [ ] Method pages render deterministic `Reads`/`Writes` sections for internal targets.

---

## Phase 6: Publication Determinism, Validation, and CI Gates

**User stories**: 46, 47, 49, 51, 52, 53, 54, 55, 60, 63

### What to build

Harden PRD 003 publication and quality gates: validate method page/front matter contracts, preserve ID-light readability constraints and operational boundaries, and extend deterministic behavioral/golden regression coverage for method relationships and degraded semantic behavior.

### Acceptance criteria

- [ ] Method/type front matter validation enforces approved minimal scalar contracts and conditional fields.
- [ ] Visible wiki body content remains ID-light/human-readable while IDs remain queryable via front matter/index.
- [ ] `HEAD` + git-tracked boundary behavior remains enforced for method/data-flow publication outputs.
- [ ] Golden and behavioral test suites verify deterministic method outputs, relationship rendering, and degraded semantic diagnostics/provenance.
