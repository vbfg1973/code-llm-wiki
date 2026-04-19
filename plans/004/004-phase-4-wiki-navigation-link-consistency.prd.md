# PRD 004: Phase 4 Wiki Navigation Link Consistency Hardening

Date: 2026-04-18  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Fourth development phase

## Problem Statement

Wiki navigation is inconsistent in critical places where related entities already have dedicated pages.

In current output:

1. Namespace pages list contained types as plain text instead of links.
2. File pages list declared methods as plain text instead of links.
3. Users have observed at least one large-codebase run where method listings appeared non-linked in a type context, reducing confidence in navigation consistency.

This undermines the primary human-readable objective of the wiki and weakens Obsidian graph traversal. It also introduces uncertainty for LLM-assisted traversal because link structure is not reliably present when pages exist.

## Solution

Harden wiki navigation consistency for the immediate scope by enforcing a strict rendering and publication invariant for two sections:

1. `Namespace -> Contained Types` must render links to type pages when type pages exist.
2. `File -> Declared Symbols -> Methods` must render links to method pages when method pages exist.

In addition:

3. Add a focused regression guard to ensure type method listings remain linked.
4. Add a publication validation gate that fails artifact publication if either of the two scoped sections emits non-link bullets for resolvable targets.
5. Preserve current human-readable alias text and section layout; only navigability behavior changes.

## User Stories

1. As a wiki reader, I want contained types on namespace pages to be links, so that I can navigate namespace structure without path guessing.
2. As a wiki reader, I want declared methods on file pages to be links, so that I can jump directly from file provenance to behavior details.
3. As an architect, I want link behavior to be consistent where page families already exist, so that graph exploration is trustworthy.
4. As an Obsidian user, I want namespace and file surfaces to contribute predictable links, so that graph view is representative.
5. As a maintainer, I want link presence to be deterministic across runs, so that regressions are obvious in diffs.
6. As a maintainer, I want publication to fail when scoped link invariants are broken, so that invalid outputs are not silently promoted.
7. As a developer, I want failure messages to identify offending pages/sections, so that remediation is fast.
8. As a reviewer, I want alias text preserved while links are added, so that readability does not regress.
9. As a reader, I want unresolved targets to remain explicit, so that missing links are never ambiguous.
10. As an engineer, I want this change limited to renderer/publisher behavior, so that model/query contracts remain stable.
11. As an engineering lead, I want phase scope tightly constrained, so that delivery is fast and low-risk.
12. As a QA engineer, I want dedicated tests for namespace contained-type links, so that this gap cannot recur.
13. As a QA engineer, I want dedicated tests for file method links, so that this gap cannot recur.
14. As a QA engineer, I want a regression test guarding type method linking, so that large-codebase edge cases are covered.
15. As a QA engineer, I want publication-gate tests for invariant violations, so that CI enforces correctness.
16. As an operations owner, I want failed link invariants to prevent `latest` promotion, so that consumers never read invalid snapshots.
17. As an LLM consumer, I want link-based traversal to be reliable in scoped sections, so that retrieval chains are stable.
18. As a documentation owner, I want no front matter expansion for this phase, so that metadata remains minimal.
19. As a documentation owner, I want no new page families introduced, so that scope remains focused on navigation consistency.
20. As a product owner, I want this delivered as a managed phase before broader link audit work, so that immediate user pain is resolved quickly.
21. As a maintainer, I want known-good sections that already link correctly to remain unchanged, so that no unrelated churn is introduced.
22. As a maintainer, I want markdown output style to remain human-first, so that links improve navigation without visual noise.
23. As a team member, I want this phase to provide a reusable invariant pattern for future sections, so that later hardening is faster.
24. As a CI user, I want failures to be reproducible on large repositories, so that confidence does not rely on toy fixtures.
25. As a project stakeholder, I want deterministic link behavior in these high-traffic sections, so that wiki quality is defensible.

## Implementation Decisions

1. Scope is strictly limited to two section families:
   - Namespace page `Contained Types`
   - File page `Declared Symbols -> Methods`
2. Rendering policy for this phase: when a target entity has a page and is resolvable, render as a wiki link.
3. Preserve current alias style while adding links:
   - contained type entry stays `TypeName (kind)` with linked `TypeName`
   - file method entry stays `MethodAlias (kind)` with linked `MethodAlias`
4. Introduce a focused navigation invariant validator module for publication-time checks (deep module):
   - input: rendered wiki pages
   - output: pass/fail + explicit violations
   - responsibility: enforce scoped link invariants only
5. Keep unresolved behavior explicit:
   - unresolved targets may remain non-link text with explicit unresolved semantics
   - resolvable targets in scoped sections must be links
6. Fail artifact publication when scoped invariants fail; do not silently continue with invalid wiki output.
7. Keep data/query contracts unchanged:
   - no graph schema changes
   - no query model changes
   - no front matter key changes
8. Do not introduce member page family in this phase.
9. Do not run a broader global link audit in this phase.
10. Keep deterministic ordering unchanged; this phase is navigability hardening, not ordering redesign.
11. Keep path and naming behavior unchanged except for link rendering and scoped validation outcomes.
12. Maintain compatibility with existing Obsidian-first markdown structure and index usage.

## Testing Decisions

1. Good tests validate external behavior and output contracts, not internal implementation details.
2. Add renderer-focused tests for namespace contained-type link rendering.
3. Add renderer-focused tests for file declared-method link rendering.
4. Add regression test to assert type method listings are links under representative large-signature/large-surface cases.
5. Add publication validator tests that fail on scoped non-link violations and pass on compliant output.
6. Ensure publication failure behavior is asserted at artifact-publisher boundary (including non-promotion behavior expectations).
7. Keep deterministic output tests (golden and snapshot) in place; update only where link output intentionally changes.
8. Reuse prior test patterns from:
   - publication snapshot/front matter contract tests
   - artifact publisher behavior tests
   - wiki renderer vertical-slice tests
9. Preserve large-repository confidence by including at least one fixture path that exercises high method/type volume patterns.
10. Keep test assertions section-specific to avoid accidental expansion into out-of-scope link audits.

## Out of Scope

1. Creating member pages or linking member listings.
2. Global link consistency audit across every wiki section.
3. Changes to graph triples, ontology predicates, query DTOs, or front matter schema.
4. New page families or index schema redesign.
5. Broad markdown formatting/style refactors unrelated to link consistency.
6. Endpoint/complexity/hotspot or cross-system dependency work.
7. Any multi-language analyzer work.

## Further Notes

1. This phase is intentionally narrow and defensive, driven by observed navigation inconsistency on real large-codebase runs.
2. Publication-time invariant enforcement is required to provide certainty, not just best-effort rendering.
3. This phase establishes a practical pattern for future hardening work without reopening broad scope now.
