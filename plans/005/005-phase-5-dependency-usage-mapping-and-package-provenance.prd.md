# PRD 005: Phase 5 Dependency Usage Mapping and Package Provenance

Date: 2026-04-19  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Fifth development phase

## Problem Statement

Current analysis captures rich structure (namespace/type/member/method, method relations, property/field reads-writes), but does not provide a first-class dependency usage map with project-correct package provenance.

As a result:

1. We cannot reliably answer which internal types and methods depend on which external types.
2. We cannot group external dependency usage by package and then drill down through namespace, type, and method usage sites.
3. We cannot clearly separate declarative coupling (signatures/shapes) from behavioral coupling (method body usage).
4. We cannot safely drive hotspot and architecture tracing work that depends on high-confidence dependency provenance.

This blocks key architecture-understanding outcomes and weakens the path to coupling-based analysis and cross-system dependency tracing.

## Solution

Implement BL-011 as a complete vertical slice for .NET dependency usage mapping with package provenance:

1. Capture dependency evidence at method granularity with two explicit provenance channels:
   - declaration dependency usage
   - method-body dependency usage
2. Keep ingestion graph lossless by storing raw method-level dependency evidence and deriving rollups in query/wiki.
3. Attribute external dependencies to packages deterministically and project-contextually:
   - use source method/type origin project context
   - map external assembly to package only when deterministic
   - emit explicit unknown/unresolved attribution when certainty is unavailable
4. Add query projections and wiki output that organize dependency usage under package by:
   - namespace
   - type
   - method
   with declaration vs method-body split and deterministic counts/order.
5. Preserve explicit unresolved semantics using dedicated unresolved nodes and reason codes.

## User Stories

1. As an architect, I want method-level dependency evidence, so that I can trace exactly where coupling is introduced.
2. As an architect, I want declaration and method-body dependencies separated, so that I can distinguish structural intent from behavioral usage.
3. As a maintainer, I want type-level dependency rollups derived from raw method evidence, so that I can read summaries without losing drill-down.
4. As a maintainer, I want raw evidence preserved in graph form, so that future analyses do not require re-ingestion.
5. As a wiki reader, I want package pages to show where package types are used, so that dependency navigation starts from external packages.
6. As a wiki reader, I want package usage organized by namespace, type, and method, so that I can quickly move from broad to specific context.
7. As a wiki reader, I want provenance labels (`declaration` vs `method_body`), so that coupling semantics are explicit.
8. As an LLM consumer, I want deterministic dependency output ordering, so that retrieval and diff-based reasoning are stable.
9. As a QA engineer, I want deterministic tests for dependency extraction, so that regressions are caught reliably.
10. As a QA engineer, I want mixed-project package-version fixtures, so that project-scoped package attribution is validated.
11. As a platform engineer, I want package attribution to use origin project context, so that multi-project mixed-version repositories are modeled correctly.
12. As a platform engineer, I want unknown package attribution represented explicitly, so that ambiguity is visible and queryable.
13. As an engineering lead, I want deterministic-only package mapping, so that reported package dependencies remain trustworthy.
14. As a developer, I want attributes included as declaration dependencies, so that framework/config coupling is visible.
15. As a developer, I want generic constraints included as declaration dependencies, so that type-shape coupling is fully represented.
16. As a developer, I want invocation/new/member-access/cast/typeof dependency evidence from method bodies, so that behavior-level coupling is accurate.
17. As a developer, I want `nameof` excluded from dependency evidence in v1, so that non-coupling naming noise is avoided.
18. As a reviewer, I want explicit unresolved dependency nodes with reason codes, so that missing resolution is never hidden.
19. As an operations owner, I want dependency extraction to degrade explicitly rather than silently drop data, so that output quality can be assessed.
20. As a product owner, I want BL-011 delivered as a complete vertical slice, so that users can consume dependency maps immediately.
21. As a product owner, I want MCP/query-surface expansion deferred, so that this phase remains tightly scoped.
22. As an engineer, I want dedicated ontology predicates for each provenance channel, so that graph queries remain simple and explicit.
23. As an engineer, I want no duplicate truth between raw and rollup graph edges, so that maintenance burden stays low.
24. As an architect, I want to see external dependency usage per package before hotspot work, so that coupling-driven prioritization is possible.
25. As a maintainer, I want package usage counts at each hierarchy layer, so that heavy-use dependencies are obvious.
26. As a maintainer, I want links from package usage entries to internal namespaces/types/methods, so that documentation remains navigable.
27. As an analyst, I want unresolved/unknown dependency sections visible in package and type contexts, so that confidence boundaries are explicit.
28. As a developer, I want dependency extraction from both declaration symbols and semantic operations, so that coverage is practical and robust.
29. As a developer, I want internal dependency mapping to internal type entities where possible, so that internal architecture topology is queryable.
30. As a developer, I want external dependency mapping to external-type entities with assembly metadata, so that package attribution can be layered deterministically.
31. As a QA engineer, I want golden/snapshot updates only for intentional output deltas, so that accidental wiki churn is rejected.
32. As a QA engineer, I want tests to assert behavior from public boundaries, so that refactors do not break brittle implementation-coupled tests.
33. As a documentation owner, I want output to remain human-readable first, so that dependency detail does not overwhelm page usability.
34. As a documentation owner, I want IDs to remain hidden from body text, so that pages stay readable while still machine-joinable.
35. As an engineering lead, I want this phase to establish reusable dependency modules, so that future metrics/hotspot/cross-system work builds on stable components.
36. As a release manager, I want deterministic dependency output in CI, so that release confidence does not depend on manual inspection.
37. As a team member, I want the dependency model to align with existing semantic triples, so that cross-feature querying stays coherent.
38. As a team member, I want this phase to avoid endpoint and MCP scope creep, so that delivery remains fast and defensible.
39. As a future planner, I want dependency evidence to support coupling metrics later, so that BL-012 and BL-015 can reuse this foundation.
40. As a future planner, I want dependency provenance to support cross-system tracing later, so that BL-016 can build from trusted evidence.

## Implementation Decisions

1. Introduce explicit dependency predicates split by provenance:
   - dependency from declaration context
   - dependency from method-body context
2. Persist raw dependency evidence at method granularity in ingestion; do not persist redundant type rollup edges.
3. Compute type-level and package-level rollups in query projection and wiki rendering.
4. Build/extend deep modules with narrow, testable interfaces:
   - dependency evidence extraction (declaration + method-body forms)
   - external assembly to package attribution resolver (project-scoped, deterministic-first)
   - dependency query projector (hierarchical aggregations and counts)
   - package usage wiki projection/renderer (namespace -> type -> method trees)
5. Use source method/type origin project to determine package/version context for external dependency attribution.
6. When deterministic package attribution is unavailable, retain assembly attribution and emit package attribution as unknown.
7. Represent unresolved dependencies explicitly as dedicated graph entities with reason codes; do not silently drop unresolved evidence.
8. Include declaration dependencies from:
   - inheritance/implementation
   - fields/properties/events/record parameters
   - method signatures (return and parameter types)
   - attributes
   - generic constraints
9. Include method-body dependency evidence from:
   - invocation targets
   - object creation
   - static/instance member access
   - casts and `is`/`as`
   - `typeof`
10. Exclude `nameof` as dependency evidence in BL-011 v1.
11. Keep BL-011 focused on type dependency mapping; do not add external method/member-level dependency targets in this phase.
12. Preserve deterministic ordering at all hierarchy layers used in query and wiki output.
13. Keep graph/query contracts additive and compatible with existing ingestion/query/writer workflows.
14. Keep analyzer self-contained in .NET analyzer module boundaries and shared interfaces.

## Testing Decisions

1. Good tests verify observable behavior via public interfaces and rendered/query outputs, not internal helper implementation details.
2. Test modules:
   - dependency evidence extraction behavior (declaration and method-body forms)
   - assembly-to-package deterministic attribution and unknown fallback
   - query projections for package -> namespace -> type -> method grouping and counts
   - wiki rendering of package usage trees with provenance split and deterministic ordering
   - unresolved dependency representation and propagation
3. Testing style:
   - red-green-refactor TDD per vertical slice
   - deterministic integration/vertical-slice fixtures over brittle micro-mocking
4. Prior art to mirror:
   - ingestion vertical-slice patterns used across existing declaration/method/data-flow tests
   - package dependency vertical-slice tests
   - file backlink and method relationship deterministic tests
   - publisher and golden publication snapshot behavior tests
5. Include mixed-version multi-project fixture coverage to validate project-scoped package attribution.
6. Include ambiguity fixtures to assert unknown/unresolved attribution behavior.
7. Keep golden updates limited to intentional dependency-view output deltas.

## Out of Scope

1. MCP interactive query-surface expansion.
2. Endpoint discovery and endpoint metadata extraction.
3. Complexity/maintainability metric computation.
4. Hotspot scoring and ranking logic.
5. Cross-system flow inference across API/message/store boundaries.
6. Multi-language analyzer work (Python/frontend frameworks).
7. External method/member target granularity beyond type dependencies.
8. Broad wiki IA redesign beyond dependency usage sections required for this phase.

## Further Notes

1. This phase is foundational for coupling, hotspot, and cross-system analysis and should prioritize evidence quality over speculative attribution.
2. Deterministic-only package attribution is a deliberate trust policy; unknown attribution is acceptable and preferred over guesswork.
3. The dependency output must remain human-readable first, with machine usefulness achieved through stable links, front matter, and deterministic structure.
4. BL-011 should be delivered as a strict vertical slice that is directly useful without waiting on later roadmap items.
