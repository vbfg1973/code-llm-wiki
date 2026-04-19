# PRD 006: Phase 6 Dependency Navigation and Type Relationship Fidelity

Date: 2026-04-19  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Sixth development phase

## Problem Statement

Current wiki output captures a large amount of structural and dependency data, but navigation fidelity is not aligned with intended usage for fast human and LLM traversal.

From the user perspective, key pain points remain:

1. Package dependency views do not provide the required direct navigation granularity from external dependency targets to internal usage sites.
2. Markdown tables are vulnerable to parsing errors because of link formatting choices that conflict with table delimiters.
3. Type pages do not provide complete direct relationship navigation for inheritance and interface implementation in both forward and reverse directions.
4. External relationship and dependency references are not consistently linked through package context with stable deep anchors.

This blocks quick, reliable navigation and weakens trust in the generated wiki as the primary architecture context surface.

## Solution

Implement a navigation-fidelity hardening phase that refines query projection and wiki rendering contracts without expanding analytical scope.

1. Re-shape package dependency sections to target-first navigation:
   - declaration dependencies as `External Type -> Internal Type`
   - method-body dependencies as `External Type -> Internal Method`
2. Replace problematic table link rendering with parse-safe markdown links and preserve anchor-safe deep links.
3. Complete direct type relationship navigation on type pages with explicit reverse sections:
   - `Inherits From`
   - `Inherited By`
   - `Implements`
   - `Implemented By`
4. Add stable deep-link anchors for external type groupings on package pages and route external references through package pages.
5. Add conditional rare-case section for package-owned types inherited by internal code (`Inherited Package Types`) only when data exists.
6. Preserve deterministic ordering and concise, human-readable output with unresolved/fallback handling that remains explicit but non-noisy.

## User Stories

1. As an architect, I want package pages organized around external types, so that I can start from a dependency and immediately see where it is used.
2. As an architect, I want declaration dependencies shown as `External Type -> Internal Type`, so that structural coupling is easy to trace.
3. As an architect, I want method-body dependencies shown as `External Type -> Internal Method`, so that behavioral coupling is easy to trace.
4. As a maintainer, I want fast click-through from external references to package pages, so that I can navigate dependency context in one step.
5. As a maintainer, I want deep links to external-type sections on package pages, so that navigation lands on the exact target context.
6. As a documentation reader, I want table links to render reliably in markdown, so that pages are always readable in Obsidian and other markdown viewers.
7. As a documentation reader, I want table cells free of parsing artifacts, so that information density does not degrade readability.
8. As an LLM consumer, I want deterministic target-first dependency ordering, so that retrieval and diffing remain stable.
9. As an engineer, I want direct inheritance links on type pages, so that base-type relationships are obvious.
10. As an engineer, I want reverse inheritance links (`Inherited By`), so that I can discover concrete descendants quickly.
11. As an engineer, I want direct interface links (`Implements`), so that contract adoption is visible.
12. As an engineer, I want reverse implementation links (`Implemented By`), so that I can find concrete implementations quickly.
13. As a reviewer, I want internal relationship targets rendered as links, so that traversal remains actionable.
14. As a reviewer, I want external or unresolved targets rendered as plain text with explicit status, so that broken links are avoided.
15. As a maintainer, I want unresolved dependency evidence preserved in dedicated buckets, so that missing resolution is visible and queryable.
16. As a maintainer, I want unresolved entries grouped consistently at the end, so that normal navigation remains clean.
17. As a wiki reader, I want method aliases in dependency sections to remain unambiguous, so that similarly named methods are distinguishable.
18. As a wiki reader, I want package dependency sections to avoid duplicated caller-first and target-first views, so that pages stay concise.
19. As a documentation owner, I want rare-case sections shown only when necessary, so that low-value noise is suppressed.
20. As a documentation owner, I want `Inherited Package Types` only when data exists, so that empty scaffolding is avoided.
21. As a QA engineer, I want deterministic ordering assertions for new sections, so that output drift is caught early.
22. As a QA engineer, I want tests to verify external deep-link generation, so that navigation contracts remain stable.
23. As a QA engineer, I want tests to verify table link parse safety, so that markdown integrity regressions are prevented.
24. As a QA engineer, I want relationship section coverage for direct and reverse links, so that class/interface graph regressions are visible.
25. As a release manager, I want golden updates limited to intentional navigation deltas, so that release risk remains controlled.
26. As a product owner, I want this phase to focus on navigation fidelity rather than new analysis domains, so that delivery stays fast.
27. As a product owner, I want existing graph truth reused instead of duplicated ingestion truth, so that maintenance cost stays low.
28. As an architect, I want external links to route through package ownership context, so that dependency provenance remains coherent.
29. As an architect, I want relationship navigation to be direct-only in this phase, so that type pages remain readable.
30. As an operations owner, I want deterministic stable anchors across runs, so that links shared in docs and tickets remain durable.
31. As an engineering lead, I want reusable deep modules for dependency navigation projection and link rendering, so that future phases can extend safely.
32. As an engineering lead, I want this phase to avoid MCP/query-surface expansion, so that scope remains defensible.
33. As a future planner, I want the target-first dependency model to compose with hotspot analysis later, so that prioritization workflows can reuse this structure.
34. As a future planner, I want relationship completeness to support broader architecture graph exploration, so that downstream cross-system reasoning benefits.
35. As a team member, I want human readability preserved as the first principle, so that wiki output remains a practical collaboration artifact.

## Implementation Decisions

1. Keep analytical scope additive and focused on navigation/representation contracts; do not broaden extraction scope beyond already captured data.
2. Replace package dependency section shape with target-first grouping:
   - declaration: external type to internal type
   - method body: external type to internal method
3. Remove duplicate caller-first package dependency sections to reduce noise and ambiguity.
4. Introduce stable per-external-type anchor generation policy for package pages and use deep-link targets for external references.
5. Standardize link formatting policy:
   - markdown links for tables
   - markdown links for all anchor/deep-link targets
   - wikilinks only for non-anchored internal links in non-table body text
6. Extend type page relationship rendering to include reverse direct relationships for inheritance and interface implementation.
7. Keep relationship scope direct-only in this phase; defer transitive chain rendering.
8. Render internal relationship targets as links and external/unresolved targets as plain status-bearing text.
9. Add conditional package section `Inherited Package Types` for rare inheritance cases where package-owned types are inherited by internal types.
10. Keep unresolved external targets in dedicated terminal buckets with concise reason suffixes.
11. Enforce deterministic ordering across all new sections using lexical sort plus stable id tie-breakers.
12. Preserve graph/query contract compatibility by deriving views from existing method-level evidence and relationship edges.
13. Keep front matter minimal and avoid introducing noisy visible identifiers in body content.
14. Keep module boundaries deep and testable:
   - dependency navigation projection module
   - relationship backlink projection module
   - markdown/deep-link formatting policy module
   - anchor generation policy module

## Testing Decisions

1. Good tests verify external behavior and rendered/query outcomes, not internal helper implementation details.
2. Test modules:
   - package target-first dependency projection behavior
   - type relationship forward/reverse section behavior
   - table/deep-link formatting safety and stability
   - unresolved bucket rendering and reason labeling
   - deterministic ordering and anchor stability
3. Testing style:
   - red-green-refactor TDD
   - vertical-slice integration tests using representative repository fixtures
   - deterministic rerun assertions for rendered markdown output
4. Prior art to mirror:
   - package dependency vertical-slice tests
   - method/type declaration wiki rendering tests
   - deterministic snapshot/golden publication tests
   - existing unresolved/unknown dependency semantics tests
5. Golden strategy:
   - update golden artifacts only for intentional navigation-fidelity deltas
   - reject unrelated snapshot drift
6. Relationship tests should explicitly cover both class inheritance and interface implementation reverse-link discoverability.
7. Markdown formatting tests should explicitly validate table parsing safety for rendered links.

## Out of Scope

1. New dependency extraction domains beyond existing declaration and method-body evidence.
2. Transitive inheritance/interface chain visualization.
3. MCP query-surface or command surface expansion.
4. New language analyzers or cross-language schema changes.
5. Complexity/hotspot metric computation.
6. Endpoint discovery changes.
7. Cross-system flow inference expansions.
8. Broad information architecture redesign beyond agreed navigation-fidelity sections.

## Further Notes

1. This phase is a navigation-fidelity hardening phase intended to make already-captured knowledge reliably explorable.
2. Parse-safe markdown rendering is treated as a first-class contract, not a cosmetic preference.
3. Target-first package navigation is the canonical dependency view for this phase.
4. Human readability remains the governing output principle; machine utility is achieved through deterministic, stable structure.
