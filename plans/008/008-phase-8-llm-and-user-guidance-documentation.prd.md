# PRD 008: Phase 8 LLM and User Guidance Documentation

Date: 2026-04-19  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Eighth development phase

## Problem Statement

The wiki has reached a level of breadth where users and LLM agents need explicit operating guidance to navigate it correctly and consistently.

From the user perspective:

1. There is no dedicated, wiki-native guidance for humans on where to start and how to traverse structure, dependencies, and hotspots efficiently.
2. There is no explicit LLM operating contract that defines traversal rules, evidence requirements, and response shape.
3. Guidance exists in contributor-facing repo docs, but that does not reliably travel with each generated wiki snapshot.
4. Without stable guidance anchors and deterministic entry points, prompts and deep links are brittle.
5. Without test-enforced guidance contracts, guidance can drift or disappear as rendering evolves.

This creates inconsistent navigation behavior, lower trust in agent outputs, and avoidable friction in architecture analysis workflows.

## Solution

Implement BL-019 as a first-class wiki publication capability that generates two snapshot-scoped guidance pages and links them from canonical entry points.

1. Generate two guidance pages in every wiki snapshot:
   - `guidance/human.md`
   - `guidance/llm-contract.md`
2. Keep guidance human-readable and concise while making the LLM page normative (`MUST`/`SHOULD`) and operational.
3. Add stable explicit anchors for key guidance sections and named query recipes.
4. Add compact guidance entry sections to both repository and repository-index pages.
5. Enforce guidance contracts with automated tests (presence, front matter, anchors/sections, and entry links).
6. Include a compact capability matrix in the LLM contract that distinguishes available vs deferred capabilities and links to backlog truth.
7. Keep minimal scalar front matter and include snapshot branch context in front matter only.

## User Stories

1. As a human wiki reader, I want a dedicated navigation guide, so that I can quickly orient in large repositories.
2. As an LLM agent, I want a strict operating contract, so that my traversal behavior is consistent and reliable.
3. As an LLM agent, I want explicit start points, so that I do not waste cycles on low-value discovery.
4. As a human reviewer, I want guidance linked from repository entry pages, so that I can find it immediately.
5. As a platform owner, I want guidance generated with every snapshot, so that instructions match the analyzed branch state.
6. As a maintainer, I want guidance to include branch context metadata, so that branch mismatches are detectable.
7. As a maintainer, I want guidance front matter to stay minimal, so that pages remain readable and dataview-friendly.
8. As an LLM consumer, I want a deterministic set of named query recipes, so that task execution is repeatable.
9. As an LLM consumer, I want stable recipe anchors, so that prompts can reference exact sections durably.
10. As an LLM user, I want fixed response sections (`Summary`, `Evidence Links`, `Gaps/Risks`, `Next Queries`), so that outputs are scannable and comparable.
11. As an LLM user, I want material claims backed by wiki links, so that responses are evidence-grounded.
12. As an LLM user, I want uncertainty isolated to `Gaps/Risks`, so that confidence boundaries are explicit.
13. As an LLM agent, I want explicit link-format rules, so that I avoid markdown parsing regressions.
14. As an LLM agent, I want guidance on anchor-safe links, so that deep navigation remains reliable.
15. As a documentation owner, I want guardrails and non-goals documented, so that agents do not overreach beyond current capability.
16. As an architecture user, I want guidance to distinguish current vs deferred capabilities, so that analysis requests stay in scope.
17. As a planner, I want capability status linked to backlog items, so that scope truth is traceable.
18. As a maintainer, I want deterministic guidance page paths, so that external references do not churn.
19. As a QA engineer, I want tests to assert guidance page existence, so that accidental removal is prevented.
20. As a QA engineer, I want tests for required front matter keys, so that metadata contracts stay stable.
21. As a QA engineer, I want tests for required anchors/sections, so that the LLM contract remains actionable.
22. As a QA engineer, I want tests for repository/index entry links, so that discoverability remains intact.
23. As a release manager, I want deterministic ordering and stable rendering for guidance pages, so that reruns are diff-friendly.
24. As a release manager, I want guidance changes isolated and reviewable, so that output drift is controlled.
25. As a human reader, I want concise guidance that does not dominate the wiki, so that primary analysis pages remain central.
26. As a human reader, I want clear task-oriented navigation patterns, so that I can pivot from questions to pages quickly.
27. As an LLM agent, I want explicit prohibited behaviors, so that I avoid unsupported cross-repo claims in single-repo mode.
28. As an LLM agent, I want instructions not to expose internal IDs in narrative output, so that outputs stay human-readable.
29. As an operations owner, I want guidance to remain compatible with Obsidian graph/dataview use, so that current workflows continue.
30. As a documentation owner, I want guidance-kind tagging per page, so that consumers can filter human vs LLM guidance.
31. As a maintainer, I want recipe count bounded and compact, so that the contract stays focused.
32. As a maintainer, I want recipe steps link-first, so that navigation is actionable rather than abstract.
33. As a governance owner, I want contract wording to use normative language, so that policy interpretation is unambiguous.
34. As a governance owner, I want implicit assumptions made explicit in guidance, so that agent behavior can be audited.
35. As a developer, I want deep modules for guidance generation and contract validation, so that behavior is testable in isolation.
36. As a developer, I want guidance generation to reuse existing rendering patterns, so that maintenance overhead stays low.
37. As a product owner, I want this phase focused on guidance and navigation instructions only, so that delivery remains tight.
38. As a future planner, I want this guidance foundation to support later capabilities without redesign, so that scale-up is incremental.
39. As an LLM evaluator, I want recipe outputs to be structurally predictable, so that automated assessment is feasible.
40. As a repository user, I want guidance to be present in `latest` and run outputs alike, so that there is no ambiguity about where to look.

## Implementation Decisions

1. Publish two first-class guidance artifacts per snapshot: a human navigation guide and an LLM operating contract.
2. Keep both artifacts as normal wiki pages with minimal scalar front matter and explicit `guidance_kind` classification.
3. Include snapshot branch identity in front matter only; avoid repeating branch metadata in page bodies.
4. Keep deterministic fixed page identities/paths for guidance artifacts.
5. Add compact guidance entry sections on repository and repository-index pages with exactly two links (human and LLM).
6. Use explicit stable anchor IDs for key sections and named query recipes in the LLM contract.
7. LLM contract language is normative and policy-oriented (`MUST`/`SHOULD`), not advisory prose.
8. Include a bounded set of named query recipes for common workflows (structure discovery, hotspot triage, dependency tracing, uncertainty reporting).
9. Standardize recipe response shape to four sections: `Summary`, `Evidence Links`, `Gaps/Risks`, `Next Queries`.
10. Require evidence-backed claims: material statements should include wiki link evidence; uncited statements are permitted only in `Gaps/Risks` and must be marked uncertain.
11. Encode link-style policy in the contract:
   - wiki links for internal page references in prose
   - markdown links for deep anchors and any table-cell links
12. Add explicit guardrails/non-goals (for example: no unsupported cross-repository assertions, no hidden capability claims, no visible internal IDs in body output).
13. Include a compact capability matrix section that distinguishes currently available capabilities from deferred backlog capabilities.
14. Capability matrix must link back to backlog identifiers to preserve source-of-truth traceability.
15. Implement guidance publication as a dedicated deep module with a narrow rendering interface and deterministic output ordering.
16. Implement guidance contract validation as a dedicated deep module that checks required sections/anchors/links independently of renderer internals.

## Testing Decisions

1. Good tests validate external behavior and published contracts; they do not test internal implementation details.
2. Follow strict TDD (red-green-refactor) for each vertical slice in this PRD.
3. Test modules:
   - guidance publication projector/renderer behavior
   - guidance contract validator behavior
   - repository/index entry-link integration behavior
4. Required behavioral tests:
   - guidance pages exist in generated output
   - front matter keys are present and minimal
   - required anchors and required contract sections exist
   - repository and repository-index pages include guidance links
   - capability matrix section and evidence-policy section exist
5. Determinism tests:
   - repeated runs produce identical guidance markdown for unchanged input
   - anchor IDs remain stable across reruns
6. Snapshot/golden tests should capture intentional guidance-output additions only.
7. Prior art should mirror existing wiki rendering invariants, scoped-link validation patterns, and golden publication snapshot patterns already used in the repo.

## Out of Scope

1. Any new code-analysis extraction capability (types, methods, metrics, endpoints, etc.).
2. MCP command surface expansion.
3. Cross-repository or cross-system inference features.
4. Major wiki information architecture redesign outside guidance additions.
5. Non-.NET analyzer expansion.
6. Auto-remediation or code-change recommendation features.
7. Historical trend analytics for guidance usage.

## Further Notes

1. This phase is about operational guidance quality, not new analysis depth.
2. Human readability remains primary; guidance must stay concise and practical.
3. LLM utility is achieved through deterministic contract structure, stable anchors, and explicit evidence rules.
4. Backlog linking discipline must be maintained: once PRD/plan/issues exist, BL-019 should reference them.
