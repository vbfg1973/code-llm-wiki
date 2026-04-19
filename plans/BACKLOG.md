# Feature Backlog

Last updated: 2026-04-19

Purpose: Track durable product capabilities from the original specification (not just implementation phases), with traceability to PRDs/plans/issues.

## Capability Catalog

- [x] `BL-001` Repository structure ingestion
  - Detail: repository metadata, solutions, projects, package references and versions, file inventory.
  - Data shape examples: `Repository -> Contains -> Solution`, `Project -> ReferencesPackage -> Package`.
  - Implemented in:
    - [PRD 001](/home/vbfg/dev/dotnet-llm-wiki/plans/001/001-phase-1-dotnet-project-structure.prd.md)

- [x] `BL-002` File history and merge-to-mainline context
  - Detail: file edit count, latest change metadata, merge events to mainline with source-branch file commit count.
  - Implemented in:
    - [PRD 001](/home/vbfg/dev/dotnet-llm-wiki/plans/001/001-phase-1-dotnet-project-structure.prd.md)

- [x] `BL-003` Namespace and namespace hierarchy model
  - Detail: namespace declarations, parent/child hierarchy, containment of types, declaration files.
  - Implemented in:
    - [PRD 002](/home/vbfg/dev/dotnet-llm-wiki/plans/002/002-phase-2-dotnet-namespace-type-declaration.prd.md)

- [x] `BL-004` Type declaration model
  - Detail: interfaces, classes, records, structs, enums, delegates; nested/partial/generic identity handling.
  - Implemented in:
    - [PRD 002](/home/vbfg/dev/dotnet-llm-wiki/plans/002/002-phase-2-dotnet-namespace-type-declaration.prd.md)

- [x] `BL-005` Type relationships (declaration-level)
  - Detail: direct inheritance and direct interface implementation relationships, with fallback resolution handling.
  - Implemented in:
    - [PRD 002](/home/vbfg/dev/dotnet-llm-wiki/plans/002/002-phase-2-dotnet-namespace-type-declaration.prd.md)

- [x] `BL-006` Member declaration model (non-method)
  - Detail: fields, properties, enum members, record parameters and declared types.
  - Implemented in:
    - [PRD 002](/home/vbfg/dev/dotnet-llm-wiki/plans/002/002-phase-2-dotnet-namespace-type-declaration.prd.md)

- [x] `BL-007` Declaration traceability between files and symbols
  - Detail: grouped backlinks on file pages, declaration files on symbol pages, deterministic ordering and source-location tie-breaks.
  - Implemented in:
    - [PRD 002](/home/vbfg/dev/dotnet-llm-wiki/plans/002/002-phase-2-dotnet-namespace-type-declaration.prd.md)

- [x] `BL-008` Method declarations
  - Detail: methods, signatures, parameters, return types, declaring types, declaration files/locations.
  - Implemented in:
    - [PRD 003](/home/vbfg/dev/dotnet-llm-wiki/plans/003/003-phase-3-dotnet-method-extraction-and-relations.prd.md)
    - Plan: [PRD 003 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/003/003-phase-3-dotnet-method-extraction-and-relations.plan.md)
    - Implementation issues (closed): `#42`, `#43`, `#44`, `#45`, `#46`, `#47`
    - Parent GitHub issue (closed): `#41`

- [x] `BL-009` Method relationships
  - Detail: interface method implementation links, overrides, method call graph edges.
  - Implemented in:
    - [PRD 003](/home/vbfg/dev/dotnet-llm-wiki/plans/003/003-phase-3-dotnet-method-extraction-and-relations.prd.md)
    - Plan: [PRD 003 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/003/003-phase-3-dotnet-method-extraction-and-relations.plan.md)
    - Implementation issues (closed): `#42`, `#43`, `#44`, `#45`, `#46`, `#47`
    - Parent GitHub issue (closed): `#41`

- [x] `BL-010` Property access behavior
  - Detail: property/field reads and writes captured as graph relationships.
  - Implemented in:
    - [PRD 003](/home/vbfg/dev/dotnet-llm-wiki/plans/003/003-phase-3-dotnet-method-extraction-and-relations.prd.md)
    - Plan: [PRD 003 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/003/003-phase-3-dotnet-method-extraction-and-relations.plan.md)
    - Implementation issues (closed): `#42`, `#43`, `#44`, `#45`, `#46`, `#47`
    - Parent GitHub issue (closed): `#41`

- [x] `BL-011` Dependency usage mapping
  - Detail: class/type dependency map with source provenance (namespace/project/package; internal vs external).
  - Implemented in:
    - [PRD 005](/home/vbfg/dev/dotnet-llm-wiki/plans/005/005-phase-5-dependency-usage-mapping-and-package-provenance.prd.md)
    - Plan: [PRD 005 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/005/005-phase-5-dependency-usage-mapping-and-package-provenance.plan.md)
    - Parent GitHub issue (closed): `#61`
    - Implementation issues (closed): `#62`, `#63`, `#64`, `#65`, `#66`
    - Follow-up hardening: [PRD 006](/home/vbfg/dev/dotnet-llm-wiki/plans/006/006-phase-6-dependency-navigation-and-type-relationship-fidelity.prd.md)
    - Follow-up plan: [PRD 006 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/006/006-phase-6-dependency-navigation-and-type-relationship-fidelity.plan.md)
    - Follow-up parent GitHub issue (closed): `#72`
    - Follow-up implementation issues (closed): `#73`, `#74`, `#75`, `#76`, `#77`, `#78`

- [ ] `BL-012` Complexity and maintainability metrics
  - Detail: cognitive complexity, cyclomatic complexity, Halstead metrics, LOC, maintainability index, coupling between objects.
  - Planned in:
    - [PRD 007](/home/vbfg/dev/dotnet-llm-wiki/plans/007/007-phase-7-complexity-and-maintainability-metrics.prd.md)
    - Plan: [PRD 007 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/007/007-phase-7-complexity-and-maintainability-metrics.plan.md)
    - Parent GitHub issue (open): `#86`
    - Implementation issues (open): `#87`, `#88`, `#89`, `#90`, `#91`

- [ ] `BL-013` Domain term extraction and linking
  - Detail: terms from type/method/parameter names, graph linkage, counts by method/type/application.
  - Planned in:
    - PRD TBD

- [ ] `BL-014` Endpoint discovery and behavior metadata
  - Detail: API endpoints, CLI entry points, message handlers, gRPC endpoints, attributes and attribute property metadata.
  - Planned in:
    - [PRD 009](/home/vbfg/dev/dotnet-llm-wiki/plans/009/009-phase-9-endpoint-discovery-and-matchability-breadcrumbs.prd.md)
    - Plan: [PRD 009 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/009/009-phase-9-endpoint-discovery-and-matchability-breadcrumbs.plan.md)
    - Parent GitHub issue (open): `#106`
    - Implementation issues (mixed): `#107` (closed), `#108` (closed), `#109`, `#110`, `#111`, `#112` (open)

- [ ] `BL-015` Hotspot analysis
  - Detail: combine complexity, coupling, edit frequency, and (future) test coverage to rank hotspots.
  - Planned in:
    - PRD TBD

- [ ] `BL-016` Cross-system dependency tracing
  - Detail: infer cross-system flows via API calls, messages, Redis, SQL, and other infrastructure touchpoints.
  - Planned in:
    - PRD TBD

- [x] `BL-017` Query and publication expansion
  - Detail: richer wiki projections and ad hoc query interfaces (including MCP-facing usage).
  - Implemented in:
    - [PRD 004](/home/vbfg/dev/dotnet-llm-wiki/plans/004/004-phase-4-wiki-navigation-link-consistency.prd.md)
    - Plan: [PRD 004 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/004/004-phase-4-wiki-navigation-link-consistency.plan.md)
    - Implementation issues (closed): `#55`, `#56`, `#57`
    - Parent GitHub issue (closed): `#54`

- [ ] `BL-018` Multi-language analyzer expansion
  - Detail: extend shared repository analysis interfaces to Python and frontend frameworks (React, Vue, Angular).
  - Planned in:
    - PRD TBD

- [x] `BL-019` LLM and user guidance documentation
  - Detail: add explicit wiki-native guidance for LLM agents and human users, including operating instructions, navigation conventions, and task-oriented usage patterns.
  - Implemented in:
    - [PRD 008](/home/vbfg/dev/dotnet-llm-wiki/plans/008/008-phase-8-llm-and-user-guidance-documentation.prd.md)
    - Plan: [PRD 008 plan](/home/vbfg/dev/dotnet-llm-wiki/plans/008/008-phase-8-llm-and-user-guidance-documentation.plan.md)
    - Parent GitHub issue (closed): `#97`
    - Implementation issues (closed): `#98`, `#99`, `#100`, `#101`

## Cross-Cutting Constraints (from original intent)

- `HEAD` snapshot is canonical for present-state ingestion.
- One repository per run in current operating mode.
- Human-readable wiki output first; IDs stay in front matter/index, not visible body.
- Graph is semantic-triple based; deterministic ordering required.
- Analyzer modules remain language-specific and self-contained behind shared interfaces.

## Linking Rule

- Every backlog item must link to a PRD once one exists.
- When a PRD is split into plan/issues, add those links under the same backlog item.
- When delivered, mark item complete and record implemented PRD(s).
