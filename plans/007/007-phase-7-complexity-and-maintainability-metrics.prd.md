# PRD 007: Phase 7 Complexity and Maintainability Metrics

Date: 2026-04-19  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Seventh development phase

## Problem Statement

The current system captures structural and dependency relationships, but it does not yet quantify maintainability risk in a way that supports rapid triage.

From the user perspective:

1. There is no consistent method/type-level complexity baseline for comparing risky code areas.
2. Namespace/project/repository rollups do not currently provide cumulative risk visibility.
3. Wiki readers cannot quickly navigate to metric-driven points of concern.
4. The graph lacks first-class metric facts that downstream query and automation surfaces can consume.
5. Without deterministic ranking and explicit coverage flags, output trust and repeatability are weaker than needed.

This limits the ability to prioritize refactoring work and identify maintainability risk at speed.

## Solution

Implement BL-012 as a complete vertical slice for complexity and maintainability metrics, persisted as graph facts and published as deterministic wiki hotspot views.

1. Compute core metrics at method/type granularity and persist as semantic triples.
2. Compute deterministic rollups across file, namespace hierarchy, project, and repository.
3. Publish dedicated hotspot wiki indexes under `hotspots/` with methods/types primary and broader rollups as navigation dashboards.
4. Apply repository-relative severity/ranking with configurable composite weighting and explicit effective configuration publication.
5. Keep output human-readable and minimal in front matter while preserving full numeric detail in body tables and graph facts.
6. Support bounded parallel metric extraction with deterministic post-merge ordering and stable tie-break contracts.
7. Emit explicit coverage/completeness metadata so partial analysis remains auditable and useful.

## User Stories

1. As an architect, I want cyclomatic complexity per method, so that I can detect branching risk quickly.
2. As an architect, I want cognitive complexity per method, so that I can prioritize readability-related refactoring.
3. As an architect, I want Halstead metrics per method, so that I can assess implementation effort characteristics.
4. As an architect, I want maintainability index per method, so that I can triage low-maintainability hotspots.
5. As an architect, I want LOC breakdowns, so that I can distinguish dense code from noisy files.
6. As an architect, I want coupling between objects per type, so that I can find tightly coupled areas.
7. As a maintainer, I want method and type hotspot rankings first, so that remediation work can start at the most actionable level.
8. As a maintainer, I want namespace rollups at each hierarchy level, so that I can navigate top-down from large risk regions.
9. As a maintainer, I want project and repository rollups, so that I can compare risk concentration across bounded scopes.
10. As a maintainer, I want file-level metric rollups, so that I can assign remediation to concrete files.
11. As a maintainer, I want recursive namespace cumulative scores, so that parent namespaces reflect total descendant risk.
12. As a maintainer, I want direct-only namespace rollups too, so that I can isolate locally declared risk from inherited rollup risk.
13. As an engineer, I want generated code excluded from default rankings, so that hotspot lists stay relevant.
14. As an engineer, I want generated code still queryable, so that nothing is hidden.
15. As an engineer, I want production code ranked by default, so that triage prioritizes shipping code.
16. As an engineer, I want test and generated filters available, so that alternate analysis modes remain possible.
17. As an engineer, I want local functions/lambdas folded into owning method metrics, so that method indexes do not explode.
18. As an engineer, I want methods without bodies excluded from rankings, so that rankings only include analyzable behavior.
19. As an engineer, I want counts of methods without bodies, so that coverage remains transparent.
20. As a wiki reader, I want dedicated `hotspots/` pages by entity kind, so that navigation stays focused and readable.
21. As a wiki reader, I want links from entity pages to hotspot indexes, so that I can pivot between local and global context.
22. As a wiki reader, I want minimal scalar front matter only, so that pages stay clean while remaining Dataview-friendly.
23. As a wiki reader, I want full metric details in body tables, so that deep inspection remains possible.
24. As a product owner, I want deterministic ordering and tie-breaks, so that reruns produce stable output diffs.
25. As a product owner, I want repository-relative normalization with raw values visible, so that rankings are meaningful and auditable.
26. As a product owner, I want severity bands, so that high-risk areas are visually obvious.
27. As a product owner, I want per-metric and composite severities, so that I can balance diagnosis and prioritization.
28. As a product owner, I want configurable composite weights with defaults, so that teams can tune prioritization while retaining baseline behavior.
29. As a product owner, I want effective thresholds and weights published in outputs, so that results are reproducible.
30. As a pipeline owner, I want reporting-only mode by default, so that ingestion does not unexpectedly block delivery.
31. As a pipeline owner, I want optional fail-on-threshold capability, so that strict CI gating can be enabled when desired.
32. As a pipeline owner, I want compact metric summary in run manifest, so that automation can consume results without parsing wiki.
33. As a pipeline owner, I want explicit completeness flags and skipped-reason counts, so that partial results are trustworthy.
34. As a platform engineer, I want bounded parallel metric extraction, so that runtime scales on larger repositories.
35. As a platform engineer, I want deterministic merge ordering after parallel work, so that speed gains do not break reproducibility.
36. As a platform engineer, I want strict dependency version pinning for metric engines, so that scores do not drift due to package updates.
37. As a platform engineer, I want source-based analysis semantics, so that async/iterator compiler transformations do not distort scores.
38. As a domain lead, I want CBO to include declaration and method-body usage, so that coupling reflects both design and behavior.
39. As a domain lead, I want CBO breakdown fields, so that coupling provenance is inspectable.
40. As a domain lead, I want generic containers and generic arguments counted for CBO, so that generic-heavy coupling is not underreported.
41. As a domain lead, I want wrapper normalization (nullable/tuple/function-like wrappers), so that coupling is consistent across syntax forms.
42. As a QA engineer, I want unit and integration fixture coverage with golden snapshots, so that metric regressions are caught early.
43. As a QA engineer, I want deterministic rerun assertions, so that nondeterminism is detected before merge.
44. As a release manager, I want output row caps with unbounded override, so that wiki output remains readable by default.
45. As a future planner, I want BL-012 metric facts to be reusable by BL-015 hotspot expansion and BL-019 guidance docs, so that future phases build on stable foundations.

## Implementation Decisions

1. Compute and persist metric facts in ingestion as first-class triples; avoid transient render-only metric computation.
2. Primary computed entities:
   - method: cyclomatic, cognitive, Halstead core set, LOC breakdown, MI
   - type: CBO with declaration/method-body/total breakdown
3. Rollups are computed in query/publication layers for:
   - file
   - namespace (direct and recursive cumulative at each hierarchy level)
   - project
   - repository
4. Method/type are primary hotspot ranking surfaces; namespace/project/repository/file are secondary dashboard/assignment surfaces.
5. Hotspot publication is a dedicated wiki section under `hotspots/` with separate pages per entity kind.
6. Front matter policy remains minimal scalar-only; detailed metric payloads remain in body tables.
7. Composite scoring is configurable with stable defaults; effective weighting and threshold configuration is emitted in output metadata.
8. Severity model includes:
   - per-metric severities
   - primary composite severity
   - default percentile-based bands with optional fixed overrides
9. Normalization is repository-relative for ranking; raw values are always retained and shown.
10. Methods without analyzable bodies are excluded from rankings but represented in coverage metadata.
11. Local functions/lambdas are folded into containing methods in v1.
12. CBO includes distinct dependencies from both declaration context and method-body usage and deduplicates per owning type.
13. CBO counting includes generic container and concrete generic arguments and normalizes wrapper constructs to underlying named types.
14. Generated-code detection is heuristic-based and default-excluded from ranking, with explicit opt-in filtering.
15. Default ranking scope is production code; test/generated remain filterable.
16. Determinism contract is fixed:
   - score descending
   - severity descending
   - stable identity tie-break (method signature / fully qualified name)
17. Bounded parallel extraction is permitted, but emission ordering and downstream ranking must remain deterministic.
18. Run behavior for analysis failures is partial-success with explicit skipped counts/reasons except for fatal repository setup failures.
19. Manifest includes compact metrics summary section for automation and traceability.
20. Output growth is controlled with default top-N limits and explicit unbounded override.
21. Metric engines should reuse established analyzer semantics where available, especially cognitive complexity, with strict package version pinning.
22. Async/iterator semantics are measured on source syntax/semantic model, not transformed IL/state machine representation.
23. Namespace model includes synthetic `(global)` node for declarations outside explicit namespaces.
24. Partial-type handling aggregates at type level across parts and attributes file rollups only to members declared in each file.

## Testing Decisions

1. Good tests verify externally observable behavior and stable contracts, not internal implementation details.
2. Testing will follow strict red-green-refactor TDD for each vertical slice.
3. Test modules (all approved deep modules):
   - metric extraction engine
   - metric aggregation engine
   - hotspot ranking engine
   - metric publication adapter
   - bounded parallel execution coordinator
4. Unit tests must validate formula contracts and edge-case semantics for each metric family.
5. Integration tests must run against curated C# fixtures that cover:
   - generics
   - partial types
   - async/iterators
   - local functions/lambdas
   - generated-code heuristics
   - namespace hierarchy rollups
6. Golden/snapshot tests must assert wiki and manifest output contracts, with updates limited to intentional BL-012 deltas.
7. Determinism tests must verify stable ordering and stable results across repeated runs.
8. Coverage/completeness tests must validate skipped-reason reporting and severity `none` behavior for insufficient-data scopes.
9. Performance validation must assert BL-012 overhead stays within approved budget envelope on representative repositories.
10. Prior art should mirror existing vertical-slice, deterministic-output, and publication-snapshot patterns already used in PRD 003-006 work.

## Out of Scope

1. Historical trend/delta metrics across runs.
2. Multi-repository or cross-system aggregation.
3. Full BL-015 multi-signal hotspot fusion using edit history/test coverage weighting.
4. Endpoint discovery and endpoint behavior metrics.
5. MCP command/query surface expansion.
6. New non-.NET language metric analyzers.
7. Code-fix or auto-remediation recommendation generation.
8. Broad wiki redesign outside metric-specific additions.

## Further Notes

1. BL-012 delivers metric-centric hotspot navigation and deterministic maintainability scoring for present-state (`HEAD`) snapshots.
2. BL-015 will extend hotspot sophistication by combining additional signals (for example edit history and coverage) on top of BL-012 foundations.
3. BL-019 (LLM and user guidance docs) is intentionally deferred until BL-012 implementation is complete.
4. Human readability remains primary: concise front matter, clear sectioning, and predictable ordering are non-negotiable output contracts.
