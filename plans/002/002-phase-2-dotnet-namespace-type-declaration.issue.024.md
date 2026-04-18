# Issue 024: PRD 002: Phase 2 .NET Namespace, Type, and Declaration Relationship Ingestion and Wiki Output

> Parent PRD: [PRD 002](./002-phase-2-dotnet-namespace-type-declaration.prd.md)

- Issue: [#24](https://github.com/vbfg1973/code-llm-wiki/issues/24)
- [ ] Status: open
- [ ] Completion date: 

## Notes

# PRD 002: Phase 2 .NET Namespace, Type, and Declaration Relationship Ingestion and Wiki Output

Date: 2026-04-18  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Second development phase

## Problem Statement

As maintainers and architects of .NET repositories, we still lack a deterministic, navigable model of type structure and namespace topology that connects what code declares to where it is declared. Project-structure output from phase 1 is useful, but it does not yet explain the type system itself: interfaces, classes, records, structs, enums, delegates, inheritance, implementations, nested declarations, and declaration-level member typing.

Without this, humans and LLMs cannot reliably answer core structural questions such as:

1. Which classes implement a given interface?
2. Which namespaces contain which types across projects?
3. Where is a symbol declared (especially partial types)?
4. What types are used by properties and fields, including external dependencies?

We need a declaration-accurate, deterministic, human-readable phase that expands the graph and wiki contracts while preserving strict organization, minimal scalar front matter, and strong queryability.

## Solution

Extend the existing .NET analyzer and wiki pipeline to ingest and publish declaration-level type and namespace structure for one repository at `HEAD`, using Roslyn syntax and semantics with graceful degradation when full semantic resolution is unavailable.

Phase 2 will:

1. Add first-class namespace entities with explicit hierarchy and containment edges.
2. Add canonical type symbol entities for internal declarations (`interface`, `class`, `record`, `struct`, `enum`, `delegate`), including partial and nested types.
3. Capture direct declaration relationships only (`inherits`, `implements`, nesting, ownership, declaration locations), leaving transitive closure to query-time.
4. Capture declaration-level member entities for fields/properties (and enum members) with declared type linkage and type-resolution status.
5. Link symbols back to file pages through deterministic declaration backlinks.
6. Keep external symbols as referenced stubs in graph/body output (no dedicated external-type pages in this phase).
7. Publish new namespace/type wiki page families with human-readable paths and minimal scalar front matter.
8. Preserve deterministic ordering and deterministic IDs/path collision handling for stable golden outputs.

## User Stories

1. As an architect, I want namespace pages, so that I can navigate solution structure by logical domain boundaries.
2. As a developer, I want type pages for interfaces/classes/records/structs/enums/delegates, so that I can inspect declaration topology quickly.
3. As a maintainer, I want one canonical page per type symbol, so that partial types are represented as one logical unit.
4. As an architect, I want direct inheritance edges captured, so that I can reason about base-type structure.
5. As an architect, I want direct interface-implementation edges captured, so that I can identify contract adoption.
6. As an engineer, I want direct edges only in stored facts, so that graph assertions stay non-redundant and maintainable.
7. As a query consumer, I want transitive hierarchy views computed at query time, so that inference logic stays centralized.
8. As a repository reader, I want namespace hierarchy edges explicit, so that hierarchy does not depend on string parsing alone.
9. As a repository reader, I want namespace pages to show child namespaces, so that traversal is intuitive.
10. As a repository reader, I want namespace pages to show contained types, so that ownership is visible.
11. As a maintainer, I want type-to-file declaration links, so that I can jump from architecture to source declarations.
12. As a maintainer, I want file-to-symbol backlinks grouped by kind, so that file pages remain structurally informative.
13. As a developer, I want deterministic declaration ordering by file/location, so that output diffs are stable.
14. As a team lead, I want all declared internal types in scope regardless of accessibility, so that internal architecture is not hidden.
15. As a team lead, I want `accessibility` metadata captured, so that I can filter public/internal/private views.
16. As an architect, I want nested type declarations represented explicitly, so that containment structure is accurate.
17. As an Obsidian user, I want nested-type metadata as scalar flags, so that dataview filters remain simple.
18. As a query user, I want `declaring_type_id` only when nested, so that metadata remains minimal and unambiguous.
19. As an architect, I want namespaces modeled repository-globally by canonical name, so that duplicate namespace pages are avoided.
20. As an architect, I want namespace pages to show participating projects/assemblies, so that cross-project usage is visible.
21. As a maintainer, I want type identity to include assembly + namespace + type signature details, so that collisions are avoided.
22. As a maintainer, I want readable type page titles and paths, so that pages stay human-navigable.
23. As a developer, I want generic arity/parameter metadata captured, so that generic symbol identities are precise.
24. As an engineer, I want generic constraints summarized, so that type semantics are discoverable without deep code reads.
25. As a documentation reader, I want properties and fields documented on type pages, so that declaration shape is complete.
26. As an architect, I want member declared-type links, so that dependency structure is queryable.
27. As a maintainer, I want enum members and values captured, so that domain constants are explicit.
28. As a developer, I want record positional/declared members captured, so that record structure is accurately represented.
29. As a maintainer, I want member entities in the graph but not separate pages, so that query depth and page readability are both preserved.
30. As an architect, I want external referenced types recorded, so that dependency surfaces are visible.
31. As an architect, I want external type pages deferred, so that phase scope stays focused and output noise stays low.
32. As a reliability-focused user, I want semantic-resolution failures to degrade gracefully, so that useful output still ships.
33. As a reliability-focused user, I want explicit diagnostics and provenance flags on degraded results, so that trust boundaries are clear.
34. As a maintainer, I want all ordering deterministic across namespaces/types/members/links, so that golden tests are stable.
35. As a maintainer, I want human-readable wiki paths for namespaces and types, so that navigation mirrors code semantics.
36. As a maintainer, I want deterministic path collision suffixes only when required, so that links stay stable and readable.
37. As a documentation consumer, I want IDs hidden from visible body content, so that pages remain readable.
38. As a dataview user, I want IDs retained in front matter/index, so that queries remain reliable.
39. As a repository reader, I want file pages to include declared namespace/type/member summaries, so that structure is visible from file context.
40. As a maintainer, I want HEAD snapshot and git-tracked-file boundaries preserved, so that runs are deterministic and environment-agnostic.
41. As a maintainer, I want build outputs excluded unless tracked, so that generated noise does not dominate docs.
42. As a user, I want generated code still included when it is tracked/part of project intent, so that documentation remains complete.
43. As a user, I want generated-code classification metadata when detectable, so that later hotspot and filtering analysis is possible.
44. As an architect, I want declaration ownership represented separately by namespace/project/file, so that structure is not conflated.
45. As an architect, I want a deterministic primary declaring context in scalar front matter, so that page metadata stays minimal but consistent.
46. As a platform engineer, I want phase-2 output integrated into existing graph/query/wiki pipeline contracts, so that architecture remains modular.
47. As a QA engineer, I want behavior and golden tests for namespace/type/member outputs, so that regressions are caught early.
48. As a QA engineer, I want tests for partial and nested type handling, so that edge cases are trustworthy.
49. As an LLM user, I want page contracts consistent and predictable, so that retrieval and synthesis quality improves.
50. As a team lead, I want strict scope boundaries excluding methods/events in this phase, so that delivery remains coherent.
51. As a future PRD owner, I want PRD 003 to take method/event/call behavior explicitly, so that roadmap boundaries stay clear.
52. As an Obsidian user, I want compact namespace hierarchy sections and type relationship sections, so that pages are skimmable.
53. As an operations owner, I want diagnostics persisted as structured output facts, so that degraded semantics can be tracked in CI.
54. As a documentation maintainer, I want unbounded default output in this phase, so that no declaration data is silently truncated.
55. As an engineering manager, I want this phase delivered as deterministic vertical slices, so that quality and progress are measurable.

## Implementation Decisions

1. Extend the .NET analyzer with Roslyn-backed declaration extraction for namespace, type, and member symbols in scope.
2. Represent namespaces as first-class graph entities with explicit hierarchy and containment relationships.
3. Represent internal type declarations as canonical symbol entities for interfaces, classes, records, structs, enums, and delegates.
4. Use one canonical type entity per logical symbol, including partial declarations across multiple files.
5. Model nested type containment explicitly and capture nested metadata in minimal conditional front matter.
6. Model declaration ownership/location as separate facts: namespace membership, project membership, and declaration file linkage.
7. Capture direct declaration relationships only (`inherits`, `implements`, containment, declaration links); defer transitive edges to query logic.
8. Capture member entities for fields/properties/enum members/record declaration members and link members to declared types.
9. Use symbol identity when type resolution succeeds; store source-text fallback plus resolution status when it does not.
10. Treat external referenced types as lightweight referenced entities/stubs in graph and page content; do not create dedicated external-type page families in phase 2.
11. Keep methods/events and method-derived references out of scope for this phase.
12. Keep namespace identity repository-global by canonical namespace name, with cross-project/assembly participation represented in graph/body sections.
13. Set deterministic rule for scalar primary project/assembly context using first declaration by sorted path and location.
14. Keep front matter scalar-only and minimal, with conditional fields only when needed.
15. Keep IDs out of page body content; retain IDs in front matter and index surfaces for machine queryability.
16. Add new wiki page families and contracts:
    - namespace pages under namespace hierarchy paths
    - type pages under assembly + namespace hierarchy paths
17. Keep member documentation on parent type pages (no standalone member pages in this phase).
18. Extend file pages with grouped, deterministic declaration backlinks for namespace/type/member.
19. Maintain deterministic ordering globally for rendering and querying to preserve reproducible outputs.
20. Preserve existing input boundary: HEAD snapshot and git-tracked repository files, excluding non-tracked build artifacts.
21. Include generated code when in scope and tracked; capture generated-code classification metadata when detectable.
22. Preserve existing architecture seam: analyzer -> triples -> query model -> wiki renderer -> artifacts.
23. Emit diagnostics/provenance indicators when semantic resolution is partial or degraded.

## Testing Decisions

1. Good tests validate external behavior and output contracts, not internal implementation details.
2. Phase 2 will continue test-first vertical slices (red-green-refactor) for each output contract increment.
3. Analyzer behavior tests will cover declaration extraction for namespaces/types/members, including partial and nested symbols.
4. Query model tests will cover direct relationship projection, deterministic ordering, and transitive-query behavior where applicable.
5. Renderer tests will cover namespace/type/file page contracts, conditional front matter, relationship sections, and backlink sections.
6. Golden tests will validate deterministic wiki and GraphML publication with phase-2 schema additions.
7. Degraded-resolution tests will verify partial-output behavior, fallback member type text, and explicit diagnostics.
8. Identity/path tests will verify canonical symbol identity rules, generic/nested handling, and collision-safe pathing.
9. Accessibility and generated-code metadata tests will verify scalar metadata correctness and filtering readiness.
10. Prior art for test style remains the current vertical-slice and golden publication test patterns already established in this repository.

## Out of Scope

1. Method declarations and method signature/body relationships.
2. Event declarations and event-handler relationship modeling.
3. Call graph extraction and invocation-site linkage.
4. Property read/write flow extraction.
5. Override/virtual dispatch analysis.
6. Endpoint discovery and endpoint behavior metadata.
7. Complexity metrics (cognitive/cyclomatic/Halstead/maintainability/CBO) in this PRD.
8. Test coverage ingestion in this PRD.
9. Dedicated wiki page family for external types.
10. Non-.NET analyzers (Python/frontend frameworks) in this PRD.
11. Output budget caps/pagination for namespace/type/member sections (default remains unbounded in this phase).

## Further Notes

1. Human readability remains primary; determinism and strict organization remain hard constraints.
2. This phase is the structural type-topology bridge between project structure (phase 1) and behavioral analysis (planned phase 3).
3. External-type recording is strategically important for future dependency tracing across systems.
4. Namespace and type pages are intended to improve both human navigation and LLM context grounding in Obsidian.
5. Decision and change history must remain synchronized across PRD/plan/issues and implementation artifacts.
