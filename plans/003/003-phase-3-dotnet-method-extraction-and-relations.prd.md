# PRD 003: Phase 3 .NET Method Extraction, Relationship, and Data-Flow Ingestion and Wiki Output

Date: 2026-04-18  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Third development phase

## Problem Statement

After completing project structure (PRD 001) and declaration topology for namespaces/types/members (PRD 002), we still cannot model runtime-relevant behavior through code. We cannot yet answer key questions such as:

1. Which methods are declared on each type, and how are signatures shaped?
2. Which methods implement interface contracts or override base methods?
3. Which methods call which internal methods?
4. Which methods depend on external types and assemblies?
5. Which methods read and write properties/fields, especially for POCO-heavy domains?

Without method-level modeling, architecture understanding remains static and incomplete. Humans and LLMs can browse declarations, but cannot trace behavior, ownership, and data flow through the graph and wiki.

## Solution

Extend the existing .NET analyzer and publication pipeline to ingest method declarations and direct method-level relationships for one repository at `HEAD`, with deterministic output and graceful degradation.

Phase 3 will:

1. Add first-class method entities for named type-level methods and constructors.
2. Capture canonical method identity by assembly + containing type + method name + ordered parameter type list + generic arity (return type excluded).
3. Include declarations without bodies (interface/abstract/extern) as first-class method entities.
4. Add direct relationship edges for `implements_method`, `overrides_method`, and `calls`.
5. Capture behavior edges only when Roslyn semantic binding succeeds; emit diagnostics/provenance when it does not.
6. Capture property/field data-flow edges for internal targets only: `reads_property`, `writes_property`, `reads_field`, `writes_field`.
7. Keep external method invocation shallow: record external usage at type and assembly level (not deep external method entities).
8. Include extension methods as first-class methods, mark them explicitly, and link them to extended internal types.
9. Publish dedicated method page family with deterministic human-readable paths, and link methods from owning type pages.
10. Add type-level scalar counts (`constructor_count`, `method_count`, `property_count`, `field_count`, `enum_member_count`, `record_parameter_count`, `behavioral_method_count`) for Dataview-friendly POCO detection.
11. Keep output deterministic and ID-light in body content, with IDs retained in front matter/index for queryability.

## User Stories

1. As an architect, I want method pages, so that I can navigate behavior-level structure directly.
2. As a developer, I want methods linked from owning type pages, so that type-to-behavior traversal is immediate.
3. As a maintainer, I want canonical method identity, so that overloads and duplicates are modeled deterministically.
4. As a maintainer, I want constructor entities included, so that object creation flow is visible.
5. As a developer, I want static constructors represented, so that initialization behavior is not hidden.
6. As an architect, I want interface method implementations mapped explicitly, so that contract realization is auditable.
7. As an architect, I want explicit and implicit interface implementations both captured, so that interface usage is complete.
8. As a maintainer, I want override relationships captured, so that inheritance-based behavior can be traced.
9. As a developer, I want calls edges between internal methods, so that in-repo call chains can be explored.
10. As an architect, I want external call dependencies captured at type and assembly level, so that external architectural coupling is visible.
11. As a maintainer, I want unresolved call targets to degrade gracefully, so that partial semantic failures still produce usable output.
12. As a QA engineer, I want diagnostics for degraded semantic binding, so that confidence boundaries are explicit.
13. As a developer, I want methods without bodies (interface/abstract/extern) included as entities, so that contracts are complete.
14. As a repository reader, I want calls extraction only from analyzable method bodies, so that fabricated behavior is avoided.
15. As a developer, I want method signatures rendered clearly, so that overload distinctions are understandable.
16. As a user, I want parameter metadata preserved in declaration order, so that signatures are faithful.
17. As a user, I want return type metadata captured, so that behavior contracts are explicit.
18. As a maintainer, I want method pages to include declaration files/locations, so that source provenance is traceable.
19. As a maintainer, I want deterministic ordering for method lists and relationship lists, so that output diffs are stable.
20. As an Obsidian user, I want method page structure to be consistent, so that scanning and Dataview usage remain predictable.
21. As an architect, I want `Called By` and `Calls` sections, so that impact and dependency traversal are bilateral.
22. As an architect, I want `Implements` and `Overrides` sections, so that contract and inheritance behavior is visible.
23. As a developer, I want `Reads` and `Writes` sections on method pages, so that data-flow intent is local and explicit.
24. As a POCO-heavy domain owner, I want property read/write patterns visible per property, so that data manipulation hotspots are clear.
25. As a POCO-heavy domain owner, I want explicit zero read/write counts, so that absence of usage is unambiguous.
26. As an Obsidian user, I want type pages to show read/write backlinks per property, so that property-centric analysis is simple.
27. As an analyst, I want reader and writer method lists per property, so that data-flow provenance is queryable.
28. As a maintainer, I want field read/write edges tracked similarly, so that non-property state mutation is visible.
29. As an architect, I want extension methods included in method inventory, so that common .NET composition patterns are represented.
30. As an architect, I want extension methods flagged explicitly, so that they can be filtered easily.
31. As a developer, I want extension-call syntax resolved to extension method entities, so that call graphs are semantically correct.
32. As a developer, I want extended internal types to list extension methods, so that behavior attached via extensions is discoverable from the type page.
33. As a maintainer, I want external extended types handled without external page sprawl, so that scope and readability stay controlled.
34. As an engineer, I want direct relationships only in stored facts, so that graph assertions remain clean and non-redundant.
35. As a query consumer, I want transitive method views computed at query time, so that inference remains centralized.
36. As a developer, I want method entities to remain separate from non-method member entities, so that model semantics stay clear.
37. As a maintainer, I want property/event accessors excluded as method entities in this phase, so that method catalog noise is reduced.
38. As a maintainer, I want local functions deferred, so that first method-page contract remains stable.
39. As a maintainer, I want operator/conversion methods deferred, so that initial method identity rules remain focused.
40. As a maintainer, I want top-level-statement callers deferred, so that caller identity stays unambiguous in v1.
41. As a maintainer, I want endpoint discovery deferred, so that method graph delivery stays coherent.
42. As a user, I want type-level structural counts in front matter, so that POCO heuristics can be expressed in Dataview.
43. As an engineer, I want counts to be raw and non-opinionated, so that classification rules can evolve externally.
44. As an Obsidian user, I want method pages and type summaries to stay human-readable first, so that docs remain approachable.
45. As an LLM consumer, I want deterministic page contracts, so that retrieval and synthesis quality remain high.
46. As a reliability-focused user, I want output to remain deterministic across repeated runs on same `HEAD`, so that regression checks are trustworthy.
47. As a reliability-focused user, I want publication validation gates for new method page contracts, so that schema drift is caught in CI.
48. As a maintainer, I want front matter to remain scalar and minimal, so that metadata does not dominate content.
49. As a maintainer, I want IDs hidden from visible body content, so that human readability remains primary.
50. As a query user, I want IDs preserved in front matter/index, so that joins and lookups stay reliable.
51. As a maintainer, I want one repository per run preserved, so that phase behavior aligns with established operational boundaries.
52. As an operations owner, I want `HEAD` snapshot boundary preserved, so that behavior extraction remains deterministic.
53. As an operations owner, I want git-tracked boundary preserved, so that untracked build artifacts do not pollute outputs.
54. As an engineer, I want tracked generated code still included, so that intended behavior is not silently omitted.
55. As a QA engineer, I want dedicated tests for overload identity and generic method signatures, so that method keys are stable.
56. As a QA engineer, I want dedicated tests for interface implementation mapping, so that explicit/implicit cases are correct.
57. As a QA engineer, I want dedicated tests for override mapping, so that inheritance behavior is trustworthy.
58. As a QA engineer, I want dedicated tests for call extraction including extension methods, so that semantic binding behavior is correct.
59. As a QA engineer, I want dedicated tests for read/write extraction and zero-count rendering, so that POCO analysis is robust.
60. As a QA engineer, I want degraded semantic tests for method relations, so that failure behavior is explicit and stable.
61. As a platform engineer, I want the analyzer -> triples -> query -> wiki seam preserved, so that architecture remains modular.
62. As a platform engineer, I want method extraction isolated in deep modules, so that contracts are stable and testable.
63. As a future roadmap owner, I want endpoint and complexity work to build on method graph outputs, so that later phases avoid rework.
64. As a future language-analyzer owner, I want method contracts language-agnostic where possible, so that cross-language expansion is feasible.
65. As an engineering manager, I want PRD 003 delivered in deterministic vertical slices, so that progress and quality are measurable.

## Implementation Decisions

1. Extend the .NET analyzer with a dedicated method extraction capability for named type-level methods and constructors only.
2. Introduce method entities as first-class graph nodes, separate from existing non-method member entities.
3. Use canonical method identity composed of assembly + declaring type canonical identity + method name + ordered parameter type list + generic arity; exclude return type from identity.
4. Merge partial declarations into one logical method entity when identity matches.
5. Include interface/abstract/extern declarations as method entities even when no body exists.
6. Capture direct method relationships only: `implements_method`, `overrides_method`, `calls`.
7. Require Roslyn semantic binding for behavior edge creation; no syntax-only guessing for calls/reads/writes.
8. On semantic failure, emit explicit diagnostics and provenance/fallback data while preserving partial output.
9. Capture internal data-flow edges from methods to internal properties/fields: `reads_property`, `writes_property`, `reads_field`, `writes_field`.
10. Capture external invocation/dependency usage at type and assembly granularity only (no deep external method entities in this phase).
11. Represent external dependency usage with lightweight external type and external assembly stubs and direct linking.
12. Include extension methods in method extraction, mark as extension methods, and link to extended internal types.
13. Add explicit method-to-extended-type relationship for extension methods and render extended-type backlinks for internal types.
14. Defer local functions, operator/conversion methods, accessor methods as method entities, and top-level statement callers.
15. Add dedicated method wiki page family with human-readable signature-based slugs and deterministic collision suffixing when necessary.
16. Keep type pages concise with method summary sections and links to method pages.
17. Extend type pages’ property sections with read/write metadata: explicit scalar counts and deterministic reader/writer method link lists.
18. Add and publish type-level scalar structural counts: `constructor_count`, `method_count`, `property_count`, `field_count`, `enum_member_count`, `record_parameter_count`, `behavioral_method_count`.
19. Keep front matter minimal scalar with conditional fields for method metadata; keep IDs out of visible body content.
20. Preserve established operational boundaries: one repository per run, `HEAD` snapshot, git-tracked files.
21. Preserve existing architecture seam (analyzer -> triples -> query projection -> wiki renderer) and extend contracts incrementally.
22. Deliver PRD 003 in tracer-bullet order:
    - contracts/ontology
    - method declaration ingestion and method pages
    - implements/overrides
    - calls
    - reads/writes and count scalars
    - publication determinism and validation gates

## Testing Decisions

1. Good tests validate external behavior and stable contracts, not internal implementation details.
2. Keep strict TDD (red-green-refactor) for each PRD 003 vertical slice.
3. Test method identity determinism across overloads, generics, partial declarations, and constructors.
4. Test interface implementation mapping for explicit and implicit implementations.
5. Test override mapping across inheritance hierarchies.
6. Test call extraction with semantic resolution, including extension-method call syntax.
7. Test degraded semantic behavior to confirm diagnostics/provenance and partial output continuity.
8. Test read/write extraction for properties and fields, including explicit zero-count rendering.
9. Test type-page property backlink sections for deterministic ordering and correctness.
10. Test method page structure and deterministic section ordering.
11. Test front matter contract validation for method pages and extended type/type-count schema additions.
12. Test repeat-run publication determinism (wiki, graph artifacts, manifest hashes) with method features enabled.
13. Reuse prior art from existing vertical-slice tests, golden publication snapshot tests, and front matter validation gates already established in this repository.
14. Prioritize module-level tests for deep modules with stable interfaces (method identity resolver, method relationship resolver, behavioral flow extractor, method/wiki query projection).

## Out of Scope

1. Endpoint discovery and endpoint behavior metadata (controllers, gRPC, handlers, CLI command surfaces).
2. Complexity and maintainability metrics (cognitive, cyclomatic, Halstead, LOC, maintainability index, CBO).
3. Domain-term extraction from methods/parameters in this phase.
4. Test-coverage ingestion and hotspot scoring in this phase.
5. Local function entities.
6. Operator/conversion method entities.
7. Accessor methods (`get_`/`set_`/event add/remove) as first-class method pages.
8. Top-level statement caller entities.
9. Deep external method-level page families.
10. Cross-system flow tracing beyond what can be inferred from method/type/assembly dependencies in a single repository.
11. Non-.NET analyzers (Python/frontend frameworks) in this PRD.

## Further Notes

1. PRD 003 is the behavioral bridge between declaration topology (PRD 002) and future endpoint/metrics/hotspot phases.
2. The strongest immediate value is deterministic method relationship visibility and internal data-flow traceability.
3. External dependency signal is intentionally shallow but designed to be extensible.
4. POCO analysis is intentionally query-driven via raw counts and read/write metadata, not baked heuristics.
5. Documentation discipline remains strict: backlog, PRD/plan/issues, and GitHub tracking must remain synchronized.
