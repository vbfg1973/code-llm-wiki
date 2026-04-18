# Plan: Phase 2 .NET Namespace, Type, and Declaration Relationship Ingestion and Wiki Output

> Source PRD: `plans/002/002-phase-2-dotnet-namespace-type-declaration.prd.md`

## Execution checklist

- [x] Phase 1: Contracts and Ontology Expansion — completion date: 2026-04-18
- [x] Phase 2: Namespace Vertical Slice — completion date: 2026-04-18
- [x] Phase 3: Internal Type Symbol Vertical Slice — completion date: 2026-04-18
- [x] Phase 4: Partial, Nested, and Generic Identity Hardening — completion date: 2026-04-18
- [ ] Phase 5: Member Declaration Topology Vertical Slice — completion date: 
- [ ] Phase 6: Type Resolution Fallback and External Stub References — completion date: 
- [ ] Phase 7: File Backlink and Traceability Vertical Slice — completion date: 
- [ ] Phase 8: Publication Determinism, Front Matter Validation, and CI Gates — completion date: 

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Execution boundary**: One repository per run, `HEAD` snapshot as canonical provenance, git-tracked files only.
- **Analyzer boundary**: .NET-specific extraction remains self-contained and extends existing analyzer -> triples seam.
- **Data model**: Semantic triples remain authoritative; direct declaration edges only, transitive views computed in query layer.
- **Namespace model**: Namespaces are first-class repository-global entities with explicit hierarchy and containment edges.
- **Type model**: One canonical entity per logical internal symbol (including partial and nested forms).
- **In-scope symbol kinds**: `interface`, `class`, `record`, `struct`, `enum`, `delegate`.
- **Member model**: Properties, fields, enum members, and record declaration members are graph entities rendered on parent type pages.
- **Method/event boundary**: Methods/events are out of scope for this phase (deferred to PRD 003).
- **External symbol policy**: External referenced types are captured as referenced stubs only (no external-type page family in this phase).
- **Identity rule**: Canonical symbol identity includes assembly/namespace/type signature details; human-readable titles/paths remain primary.
- **Primary context rule**: Scalar project/assembly front matter uses deterministic first-declaration context.
- **Front matter policy**: Scalar-only, minimal, conditional fields only when needed.
- **Wiki path contract**: Namespace/type families use human-readable hierarchical paths with deterministic collision suffix fallback.
- **Body readability policy**: IDs are hidden from visible body content and remain in front matter/index surfaces only.
- **Ordering invariant**: Deterministic ordering everywhere (entities, relationships, members, declaration backlinks).
- **Degradation policy**: Partial semantic resolution emits partial output with explicit diagnostics/provenance status.
- **Publication boundary**: Existing query and rendering seam is preserved; outputs remain deterministic and golden-testable.
- **Output budget policy**: Unbounded by default for this phase.

---

## Phase 1: Contracts and Ontology Expansion

**User stories**: 21, 23, 24, 34, 46, 49, 55

### What to build

Expand durable graph/query contracts and ontology vocabulary for namespace/type/member declarations without changing the established architecture seam. Deliver a thin end-to-end tracer bullet proving new predicates/entities can flow through ingestion, query projection, and rendering contracts safely.

### Acceptance criteria

- [ ] Ontology includes approved namespace/type/member declaration predicates with validation passing.
- [ ] Core query/view contracts are extended for namespace/type/member entities while preserving existing phase-1 behavior.
- [ ] Deterministic identity and ordering rules for new entities are documented and testable.
- [ ] Baseline integration path (analyzer -> graph -> query -> wiki) compiles and runs with new contracts enabled.

---

## Phase 2: Namespace Vertical Slice

**User stories**: 1, 8, 9, 10, 19, 20, 35

### What to build

Implement namespace ingestion as first-class graph facts with explicit hierarchy and containment, then render namespace pages under the approved path contract. Pages must provide compact hierarchy and contained-type summaries suitable for human navigation and dataview querying.

### Acceptance criteria

- [ ] Namespace entities are ingested repository-globally with explicit `contains_namespace` and `contains_type` edges.
- [ ] Namespace pages render deterministic hierarchy and contained-type sections.
- [ ] Namespace paths follow approved hierarchy-based contract with deterministic collision handling.
- [ ] Namespace front matter remains minimal scalar with conditional parent-hierarchy fields.

---

## Phase 3: Internal Type Symbol Vertical Slice

**User stories**: 2, 4, 5, 6, 7, 14, 15, 22

### What to build

Ingest canonical internal type symbols for all approved kinds and publish type pages with direct declaration relationships. Include accessibility metadata and direct inheritance/implementation edges, deferring transitive inference to query behavior.

### Acceptance criteria

- [ ] Internal symbols for all approved type kinds are ingested and queryable.
- [ ] Type pages render identity and direct relationship sections for base type/interfaces/nesting context.
- [ ] Accessibility metadata is captured and available for filtering.
- [ ] Stored graph relationships remain direct-only; transitive views are query-derived.

---

## Phase 4: Partial, Nested, and Generic Identity Hardening

**User stories**: 3, 16, 17, 18, 23, 24, 36

### What to build

Harden symbol identity behavior for partial declarations, nested declarations, and generic signatures. Ensure one canonical type representation per logical symbol, with readable paths/titles and conditional nested metadata in front matter.

### Acceptance criteria

- [ ] Partial declarations resolve to one canonical type entity/page with multiple declaration locations.
- [ ] Nested types expose conditional scalar metadata (`is_nested_type`, conditional declaring type ID).
- [ ] Generic identity includes deterministic arity/parameter/constraint metadata without path ambiguity.
- [ ] Path/title output stays human-readable with deterministic suffixing only when required.

---

## Phase 5: Member Declaration Topology Vertical Slice

**User stories**: 25, 26, 27, 28, 29

### What to build

Add declaration-level member topology for properties, fields, enum members, and record declaration members. Model members as graph entities, but render them on parent type pages for readability and controlled page volume.

### Acceptance criteria

- [ ] Member entities for in-scope declaration members are ingested and linked to declaring types.
- [ ] Declared member-type relationships are captured for query and rendering.
- [ ] Enum members and constant values are represented on type pages.
- [ ] Type pages include deterministic member sections without introducing standalone member pages.

---

## Phase 6: Type Resolution Fallback and External Stub References

**User stories**: 30, 31, 32, 33, 43

### What to build

Implement robust type-resolution behavior for member declared types and declaration relationships: prefer resolved symbol identity, fallback to source type text when unresolved, and surface explicit resolution status. Capture external referenced types as dependency stubs without dedicated page families.

### Acceptance criteria

- [ ] Declared type links use resolved symbol identity when available.
- [ ] Unresolved declared types retain source-text fallback with explicit resolution status.
- [ ] External referenced types are captured and rendered as dependency stubs without external-type pages.
- [ ] Partial semantic failure still produces usable output with explicit diagnostics/provenance indicators.

---

## Phase 7: File Backlink and Traceability Vertical Slice

**User stories**: 11, 12, 13, 39, 44, 45

### What to build

Complete bidirectional declaration traceability by enriching file pages with grouped declared-symbol backlinks and ensuring type pages provide deterministic declaration location sections. Include deterministic primary context selection for scalar type front matter.

### Acceptance criteria

- [ ] Type declarations link back to all declaration files/locations deterministically.
- [ ] File pages include grouped declaration backlinks by kind (namespace/type/member).
- [ ] Backlink ordering is deterministic by file path/source location/symbol identity tie-break.
- [ ] Primary scalar project/assembly context is chosen deterministically by first declaration.

---

## Phase 8: Publication Determinism, Front Matter Validation, and CI Gates

**User stories**: 37, 38, 40, 41, 42, 47, 48, 50, 51, 52, 53, 54

### What to build

Harden phase-2 publication and validation behavior: enforce scalar front matter contracts, preserve body readability constraints, validate repository-boundary rules, and extend golden/behavioral tests to protect deterministic output and degraded-resolution behavior.

### Acceptance criteria

- [ ] Namespace/type/file page front matter validation enforces minimal scalar contracts with conditional fields.
- [ ] Visible wiki body content remains ID-light/human-readable while IDs remain queryable via front matter/index.
- [ ] HEAD + git-tracked boundary behavior is preserved, including build-artifact exclusion rules.
- [ ] Golden and behavioral test suites verify deterministic output, edge cases (partial/nested/generic), and degradation diagnostics.
