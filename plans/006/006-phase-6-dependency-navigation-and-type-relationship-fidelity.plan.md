# Plan: Phase 6 Dependency Navigation and Type Relationship Fidelity

> Source PRD: `plans/006/006-phase-6-dependency-navigation-and-type-relationship-fidelity.prd.md`

## Execution checklist

- [x] Phase 1: Link Contract and Anchor Foundation — completion date: 2026-04-19
- [x] Phase 2: Target-First Package Dependency Navigation — completion date: 2026-04-19
- [x] Phase 3: Type Relationship Completeness (Direct + Reverse) — completion date: 2026-04-19
- [x] Phase 4: Conditional Rare/Edge Dependency Sections — completion date: 2026-04-19
- [x] Phase 5: Determinism and Publication Hardening — completion date: 2026-04-19

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Package dependency topology**: Package pages use target-first grouping.
  - declaration dependencies: `External Type -> Internal Type`
  - method-body dependencies: `External Type -> Internal Method`
- **Relationship scope**: Type relationship rendering is direct-only in this phase.
  - required sections: `Inherits From`, `Inherited By`, `Implements`, `Implemented By`
- **Link contract**:
  - markdown links in tables
  - markdown links for any deep-anchor target
  - wikilinks only for non-anchored internal links in non-table text
- **External routing**: External references route through package pages and should land on stable deep anchors.
- **Conditional rendering**: Rare/edge sections render only when non-empty.
- **Deterministic ordering**: New dependency/relationship sections use stable lexical ordering with id tie-breakers.
- **Scope boundary**: No MCP/query-surface expansion in this phase.

---

## Phase 1: Link Contract and Anchor Foundation

**User stories**: 4, 5, 6, 7, 22, 23, 30, 35

### What to build

Introduce and enforce a parse-safe link rendering contract across wiki outputs while adding stable deep-anchor generation for external type groupings on package pages. Ensure external references can route to precise package-page anchors without markdown table breakage.

### Acceptance criteria

- [x] Table link rendering uses parse-safe markdown link format without delimiter conflicts.
- [x] Deep-link anchors for package external type sections are deterministic and stable across runs.
- [x] External link routing to package-page anchors is available for dependency and relationship surfaces.
- [x] Tests verify table parse safety and deep-link determinism behavior.

---

## Phase 2: Target-First Package Dependency Navigation

**User stories**: 1, 2, 3, 8, 17, 18, 28, 33

### What to build

Replace current package dependency presentation with target-first sections that prioritize quick navigation from dependency target to internal usage site. Publish declaration and method-body dependency sections as canonical package dependency views.

### Acceptance criteria

- [x] Package pages show declaration dependencies as `External Type -> Internal Type`.
- [x] Package pages show method-body dependencies as `External Type -> Internal Method`.
- [x] Duplicate caller-first package dependency sections are removed from package pages.
- [x] Target-first sections enforce deterministic ordering and preserve readability.

---

## Phase 3: Type Relationship Completeness (Direct + Reverse)

**User stories**: 9, 10, 11, 12, 13, 14, 24, 29, 34

### What to build

Complete direct relationship navigation on type pages by adding reverse relationship sections for inheritance and interface implementation while preserving explicit handling for external and unresolved targets.

### Acceptance criteria

- [x] Type pages include `Inherits From` and `Inherited By` sections with direct relationships.
- [x] Type pages include `Implements` and `Implemented By` sections with direct relationships.
- [x] Internal targets render as links; external/unresolved targets render as status-bearing plain text.
- [x] Relationship sections are deterministic and covered by regression tests.

---

## Phase 4: Conditional Rare/Edge Dependency Sections

**User stories**: 15, 16, 19, 20, 27

### What to build

Introduce conditional rendering for rare and edge dependency contexts, including package-owned inherited types and unresolved external target buckets, while keeping output concise and non-noisy.

### Acceptance criteria

- [x] Package pages conditionally render `Inherited Package Types` only when data exists.
- [x] Unresolved external targets are grouped into explicit terminal buckets with concise reasons.
- [x] Unknown/unresolved sections preserve navigability without introducing broken links.
- [x] Tests verify conditional rendering and reason-label consistency.

---

## Phase 5: Determinism and Publication Hardening

**User stories**: 21, 25, 26, 31, 32

### What to build

Harden output contracts by adding deterministic assertions for all new navigation sections and updating publication golden artifacts only for intentional navigation-fidelity deltas.

### Acceptance criteria

- [x] Deterministic ordering assertions cover new package and type navigation sections.
- [x] Regression coverage validates split dependency views and relationship completeness end-to-end.
- [x] Golden/snapshot outputs are updated only for intentional PRD 006 deltas.
- [x] No MCP/query-surface expansion is introduced.
