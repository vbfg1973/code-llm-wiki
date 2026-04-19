# PRD 010: Phase 10 Semantic Resolution Quality and Ingestion Observability

Date: 2026-04-19  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Tenth development phase

## Problem Statement

Large-repository runs are producing severe diagnostic volume dominated by unresolved method-call targets, which materially reduces trust in call graph output.

From the user perspective:

1. A very large proportion of call edges resolve to unresolved targets, making method relationship output weak for architecture analysis.
2. Diagnostics are too coarse (`method:call:resolution:failed`), so likely causes cannot be triaged quickly.
3. Some fallback type-resolution diagnostics are noisy/repetitive (for example nullable/array forms), reducing signal quality.
4. Runs are slow on large repositories, and current output does not clearly show where time is spent by stage.
5. There is no hard quality gate to prevent accepting runs with unacceptable semantic-resolution quality.
6. Diagnostic status meanings and recurring remediation guidance are not centralized in repo docs.

This blocks reliable dependency tracing, slows debugging, and weakens confidence in downstream wiki navigation.

## Solution

Implement a quality-hardening phase for semantic resolution with deterministic diagnostics and stage observability.

1. Introduce project-aware semantic analysis contexts (owning-project compilation model) for method-call and override resolution.
2. Add bounded parallel execution across projects with deterministic merge ordering.
3. Add a run quality gate based on unresolved-call ratio with a global default threshold (first pass).
4. Split call-resolution diagnostics into explicit subcodes while retaining aggregate compatibility for existing summaries.
5. Normalize common type-reference forms (nullable/array) before fallback classification to reduce false-noise diagnostics.
6. Emit ingestion stage timing telemetry to stderr for distinguishable stages, with durations and key counters.
7. Add a repo runbook for diagnostic status meanings, recurring causes, and operator actions.
8. Preserve graceful degradation: partial semantic failure still produces useful output with explicit quality status.

## User Stories

1. As an architect, I want call edges to resolve to internal methods when resolvable, so that navigation reflects real execution paths.
2. As an architect, I want unresolved call volume reduced materially on large repositories, so that I can trust graph traversal.
3. As a maintainer, I want project-aware semantic contexts, so that symbol resolution aligns with actual project references.
4. As a maintainer, I want method-body resolution to use owning-project context, so that cross-project calls resolve correctly.
5. As a maintainer, I want override resolution quality improved under the same semantic context model, so that inheritance behavior is reliable.
6. As an operator, I want explicit stage timings emitted during runs, so that long stages are immediately visible.
7. As an operator, I want timings written to stderr as the run progresses, so that I can monitor live execution.
8. As an operator, I want stage timing output to use stable stage identifiers, so that automation can parse it.
9. As a user, I want a hard quality gate for unresolved-call ratio, so that low-trust runs fail clearly.
10. As a user, I want that gate to be global-default-driven first, so that behavior is consistent across repositories.
11. As a user, I want failure reason text to include measured ratio and threshold, so that remediation is immediate.
12. As a user, I want successful runs with diagnostics to remain possible when quality is above threshold, so that usefulness is preserved.
13. As an analyst, I want call-resolution diagnostics split by cause, so that triage is fast and accurate.
14. As an analyst, I want the aggregate legacy code preserved, so that existing dashboards do not break abruptly.
15. As an analyst, I want unresolved-call diagnostics to report both count and unique-target count, so that issue concentration is visible.
16. As an analyst, I want unresolved target names grouped by cause, so that dominant patterns are obvious.
17. As a maintainer, I want nullable and array type names normalized before fallback classification, so that avoidable noise is removed.
18. As a maintainer, I want type fallback diagnostics deduplicated meaningfully, so that repeated messages do not overwhelm reports.
19. As a maintainer, I want deterministic diagnostics ordering under parallel execution, so that diffs are stable.
20. As a maintainer, I want deterministic triple merge ordering from parallel project workers, so that output is reproducible.
21. As a maintainer, I want bounded concurrency controls, so that large runs scale without resource collapse.
22. As an engineer, I want semantic analysis modules to be deep and isolated, so that they can be tested independently.
23. As an engineer, I want diagnostic classification separated from extraction logic, so that taxonomy changes are low-risk.
24. As an engineer, I want quality-gate evaluation separated from CLI formatting, so that policy is reusable.
25. As an engineer, I want stage timing collection separated from rendering, so that observability can evolve safely.
26. As a QA engineer, I want fixture tests that previously reproduced high unresolved rates, so that regressions are controlled.
27. As a QA engineer, I want tests for each new diagnostic subcode, so that cause classification remains stable.
28. As a QA engineer, I want tests for quality-gate pass/fail semantics, so that threshold behavior is explicit.
29. As a QA engineer, I want tests for parallel determinism, so that concurrency does not change outputs.
30. As a QA engineer, I want tests for timing-emission format contracts, so that stderr telemetry is parse-safe.
31. As a documentation user, I want a diagnostics runbook in repo docs, so that recurring issues have clear signposts.
32. As a documentation user, I want run statuses explained with operational meaning, so that failure handling is consistent.
33. As a documentation user, I want remediation guidance mapped to diagnostic families, so that debugging is efficient.
34. As a platform owner, I want this phase to improve semantic quality without changing wiki readability defaults, so that output remains human-first.
35. As a platform owner, I want no visible internal IDs added to page bodies, so that noise stays low.
36. As a platform owner, I want this phase to keep single-repo HEAD snapshot semantics, so that operating assumptions remain stable.
37. As a release owner, I want quality status represented in run artifacts, so that CI can enforce acceptance.
38. As a release owner, I want gate failures to be explicit and non-ambiguous in exit behavior, so that automation is robust.
39. As a future planner, I want this quality foundation before deeper hotspot and cross-system work, so that later analysis builds on reliable edges.
40. As a future planner, I want module boundaries to support later per-repo threshold overrides, so that policy extension is straightforward.
41. As a developer, I want stage-level counters aligned with stage timings, so that cost/volume relationships can be investigated.
42. As a developer, I want stage names to include project discovery, declaration scan, semantic call graph, endpoint extraction, query projection, wiki render, and graph serialization, so that bottlenecks map to capabilities.
43. As a developer, I want telemetry for source snapshot and graph serialization specifically, so that I can isolate IO vs analysis cost.
44. As an architect, I want unresolved internal-target mismatches clearly distinguished from symbol-unresolved cases, so that type of defect is obvious.
45. As a governance owner, I want this PRD to codify diagnostic semantics now, so that future phases remain consistent.

## Implementation Decisions

1. Introduce a project-scoped semantic compilation provider that builds and caches Roslyn compilations per owning project.
2. Resolve method calls and override relationships against project-scoped compilations before falling back to unresolved targets.
3. Keep fallback behavior explicit and conservative; do not fabricate resolved edges when semantic certainty is absent.
4. Add bounded parallel project processing with deterministic post-merge ordering for triples and diagnostics.
5. Introduce a dedicated call-resolution diagnostic classifier module with cause-level subcodes.
6. Preserve compatibility by continuing to emit aggregate `method:call:resolution:failed` rollup semantics in run summaries.
7. Add a quality-policy module that computes unresolved-call ratio and determines pass/fail against a global default threshold.
8. Enforce quality policy at run completion and map failures to explicit exit behavior.
9. Add a type-reference normalization module for nullable/array forms before unresolved type fallback classification.
10. Reduce repetitive fallback noise through deterministic dedupe keys over normalized representations.
11. Introduce stage telemetry instrumentation with stable identifiers and per-stage elapsed milliseconds.
12. Emit stage telemetry to stderr during runtime, not only in final manifest output.
13. Adopt the approved stage set for timing output: `project_discovery`, `source_snapshot`, `declaration_scan`, `semantic_call_graph`, `endpoint_extraction`, `query_projection`, `wiki_render`, `graphml_serialize`.
14. Include lightweight stage counters where available (for example files/projects processed and edge counts).
15. Add a repo-level diagnostics runbook documenting status codes, meanings, likely causes, and remediation steps.
16. Keep wiki diagnostics publication data-focused; explanatory/operator guidance belongs in repo docs.
17. Keep project discovery fallback as warning-level behavior in this phase; do not gate on it yet.
18. Keep this phase within current single-repository, HEAD-first operational model.
19. Preserve human-readable wiki output constraints and existing parse-safe link policies.
20. Design module boundaries to allow later per-repo quality threshold overrides without redesign.

## Testing Decisions

1. Good tests assert externally visible behavior and stable contracts; they do not couple to internal implementation details.
2. Follow strict TDD (red-green-refactor) for each quality-hardening slice.
3. Unit tests should cover:
   - call-resolution diagnostic cause classification
   - unresolved ratio computation and threshold evaluation
   - type-reference normalization and dedupe behavior
   - stage timing event lifecycle and output contract formatting
4. Integration tests should cover:
   - project-scoped semantic resolution across multi-project fixtures
   - quality-gate pass/fail execution paths with explicit exit behavior
   - deterministic output under bounded parallel execution
   - improved call-resolution outcomes on representative larger fixtures
5. Behavioral/contract tests should cover:
   - diagnostic summary compatibility (aggregate + subcode views)
   - stderr stage timing presence and stable stage IDs
   - run manifest quality status and ratio/threshold reporting
6. Snapshot/golden tests should cover deterministic wiki/GraphML and diagnostics ordering under concurrency.
7. Prior art should follow existing ingestion pipeline tests, CLI artifact publication tests, and deterministic output contracts already used in prior phases.

## Out of Scope

1. Throughput gating policy based on runtime duration (timings are observational only in this phase).
2. Per-repository threshold override UX.
3. Cross-system endpoint matching and multi-repository inference.
4. New non-.NET analyzers or multi-language expansion.
5. Full redesign of project discovery fallback behavior.
6. Runtime-distributed telemetry backends or external observability stacks.
7. Historical trend analytics across multiple runs.

## Further Notes

1. This phase is a quality and operability hardening step driven by observed large-repository failure modes.
2. Trust in semantic call graph edges is the primary outcome; timing telemetry supports focused performance work next.
3. Parallelization is in scope where it does not compromise deterministic output contracts.
4. Repo docs should become the canonical operator reference for diagnostic statuses and recurring remediation.
