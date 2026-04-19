# PRD 009: Phase 9 Endpoint Discovery and Matchability Breadcrumbs

Date: 2026-04-19  
Status: Draft (grilled and approved baseline, subject to iteration)  
Phase: Ninth development phase

## Problem Statement

The current wiki can explain structure, types, methods, dependencies, and complexity, but it does not yet model executable entry points as first-class entities.

From the user perspective:

1. There is no durable inventory of callable entry points (HTTP endpoints, message handlers, gRPC endpoints, CLI commands).
2. Cross-system mapping goals are blocked without endpoint-level breadcrumbs that can later be matched to outbound calls.
3. Endpoint styles evolve over time, so hard-coded one-off extraction logic is brittle unless detection rules are explicitly modeled.
4. Without route/handler identity and resolution confidence, endpoint output is hard to trust at scale.
5. Without endpoint-first wiki pages and links from declaring types/methods, navigation for architecture analysis remains slower than required.

This leaves a major gap between code-level analysis and architecture-level flow mapping.

## Solution

Implement BL-014 as a first-class endpoint discovery capability for .NET, with a rule-catalog contract that is DSL-shaped for future extensibility.

1. Add endpoint-family discovery for:
   - ASP.NET Core Controllers
   - ASP.NET Core Minimal APIs
   - Message handlers (including interface-pattern based handlers)
   - gRPC service endpoints
   - CLI entry points/commands (semantic registration patterns)
2. Introduce endpoint entities in the graph and wiki, with one page per canonical endpoint signature.
3. Record authored route text and normalized route keys, plus confidence and unresolved-reason metadata when extraction is partial.
4. Preserve strong declaration traceability: endpoint -> method, method -> type, endpoint -> source file/location.
5. Emit matchability breadcrumbs now (route/protocol/shape fingerprints and limited outbound call candidates) to support later cross-system correlation.
6. Publish deterministic endpoint views grouped by family/protocol, with compact front matter and readable body sections.
7. Capture rule provenance/version/source for every detection to keep extraction behavior auditable.

## User Stories

1. As an architect, I want a complete list of discovered endpoints, so that I can understand system ingress points quickly.
2. As an architect, I want endpoint pages with stable identities, so that I can link findings across analyses and discussions.
3. As a maintainer, I want one page per endpoint signature, so that each callable surface has a single canonical reference.
4. As a maintainer, I want endpoint pages linked from declaring method pages, so that I can navigate from implementation to ingress behavior.
5. As a maintainer, I want endpoint pages linked from declaring type pages, so that I can navigate from type topology to callable surfaces.
6. As a reviewer, I want endpoint extraction confidence values, so that I can judge trust in partially inferred matches.
7. As a reviewer, I want unresolved reason codes, so that I can see exactly why a route or signature was only partially resolved.
8. As a platform owner, I want route text preserved exactly as authored, so that debugging and source comparison are straightforward.
9. As a platform owner, I want normalized route keys alongside authored routes, so that dedupe and matchability are deterministic.
10. As a user, I want controller endpoints detected via routing semantics, so that conventional MVC APIs are covered.
11. As a user, I want minimal API endpoints detected from `Map*` semantics, so that endpoint discovery reflects modern ASP.NET styles.
12. As a user, I want message handlers detected by interface-implementation patterns, so that CQRS and event-driven systems are covered.
13. As a user, I want custom handler interfaces configurable in rules, so that non-standard conventions can still be discovered.
14. As a user, I want gRPC endpoints detected from service registration semantics, so that service contracts appear in endpoint inventories.
15. As a user, I want CLI endpoints detected from registration semantics, so that command surfaces are modeled consistently.
16. As an architect, I want endpoint pages grouped by protocol/family, so that discovery navigation is fast.
17. As an architect, I want deterministic endpoint sorting, so that diffs between runs are stable.
18. As an architect, I want endpoint-family index pages, so that I can inspect coverage and extraction quality by style.
19. As a developer, I want endpoint data in graph triples, so that querying and downstream projections remain consistent.
20. As a developer, I want endpoint entities to include method backlinks, so that call and dependency context can be traversed.
21. As a developer, I want rule provenance captured per endpoint match, so that behavior changes can be audited.
22. As a developer, I want rule catalog version surfaced in output, so that consumers can reason about extraction compatibility.
23. As a QA engineer, I want fixtures for each endpoint family, so that regressions are detected with high specificity.
24. As a QA engineer, I want tests for unresolved/partial scenarios, so that fallback behavior is deliberate and stable.
25. As a QA engineer, I want deterministic output tests, so that ranking and ordering do not flap across runs.
26. As a product owner, I want endpoint data scoped to current repository/head snapshot, so that output reflects present state.
27. As a product owner, I want endpoint pages to remain human-readable first, so that wiki usability is preserved.
28. As a product owner, I want compact front matter only, so that metadata does not dominate content.
29. As a product owner, I want body sections optimized for quick scanning, so that both human and LLM consumers can use them.
30. As a future planner, I want endpoint fingerprints now, so that cross-system call matching can be layered in later phases.
31. As a future planner, I want limited outbound call breadcrumbs from endpoint methods, so that future correlation starts from existing evidence.
32. As a future planner, I want explicit declaration-vs-body context tagging on breadcrumbs, so that usage meaning is unambiguous.
33. As a security reviewer, I want protocol and route shape visible, so that exposed surfaces can be triaged quickly.
34. As a security reviewer, I want unresolved endpoints still listed, so that potential blind spots are not hidden.
35. As a documentation reader, I want endpoint pages to state declaring namespace/type/method clearly, so that ownership is obvious.
36. As a documentation reader, I want links to package pages for external endpoint-related dependencies when relevant, so that provenance navigation is preserved.
37. As an operations engineer, I want endpoint extraction to degrade gracefully on partial semantic failures, so that runs remain useful.
38. As an operations engineer, I want extraction diagnostics countable by family/reason, so that quality can be monitored.
39. As a team lead, I want rare sections hidden unless populated, so that pages stay concise.
40. As a team lead, I want defaults that avoid noisy internals in visible bodies, so that readability remains high.
41. As an LLM user, I want endpoint pages with explicit anchors for key sections, so that prompts can reference exact evidence.
42. As an LLM user, I want endpoint route and signature fields normalized predictably, so that prompt automation is robust.
43. As an LLM user, I want confidence as enum values (`high`, `medium`, `low`, `unknown`), so that interpretation is consistent.
44. As an analyst, I want inherited and implemented handler patterns represented in endpoint ownership pages, so that framework abstractions are transparent.
45. As an analyst, I want controller token expansion behavior documented in endpoint output, so that route composition is understandable.
46. As an analyst, I want minimal API group-prefix composition reflected in final routes, so that nested route groups are accurate.
47. As an analyst, I want conventional controller routes included only when deterministically inferable, so that false certainty is avoided.
48. As a maintainer, I want endpoint detection to be modular and extensible, so that new endpoint styles can be added without redesign.
49. As a maintainer, I want endpoint detection contracts language-agnostic at output boundaries, so that future non-.NET analyzers can project similarly.
50. As a maintainer, I want this phase to stop short of full cross-system matching, so that delivery remains focused and testable.

## Implementation Decisions

1. Implement a .NET endpoint detection pipeline backed by a rule catalog with a DSL-shaped contract, defined in code for v1.
2. Keep .NET-specific semantic logic internal to analyzer modules; expose language-agnostic endpoint output contracts.
3. Introduce graph entities for `endpoint`, `endpoint_group`, and `call_site_candidate` rather than method-only annotations.
4. Endpoint canonical identity is based on synthesized endpoint signature and includes deterministic tie-break components.
5. Preserve authored route text exactly and also emit normalized route key for grouping/deduping/matching.
6. Publish one endpoint wiki page per canonical endpoint signature.
7. Endpoint pages include explicit backlinks to declaring method/type/namespace/file.
8. Capture endpoint confidence as enum (`high|medium|low|unknown`) and emit reason-coded unresolved/partial diagnostics.
9. Persist rule provenance for each match, including catalog version and rule source identifier.
10. Controller extraction semantics:
    - resolve class + method route attributes
    - perform token expansion where determinable
    - include conventional route mappings only when deterministic in current semantic context
11. Minimal API extraction semantics:
    - detect from `Map*` call chain semantics
    - include group prefix and metadata chain composition where resolvable
12. Message-handler extraction semantics:
    - detect via interface-pattern rules (framework and custom)
    - support configurable handler-interface patterns (for example `ICommandHandler<T>`)
13. gRPC extraction semantics:
    - detect via registration/service semantics in v1
    - defer deep `.proto` enrichment and contract-surface expansion
14. CLI extraction semantics:
    - detect from explicit command-registration semantics
    - avoid generic `Main`-method naming heuristics
15. Emit endpoint matchability fingerprints (protocol, normalized route shape, handler signature features, selected metadata tokens).
16. Emit bounded outbound-call breadcrumbs from endpoint method context sufficient for later endpoint correlation.
17. Include declaration-context vs method-body-context tagging where dependency-like breadcrumbs are captured.
18. Endpoint wiki publication grouped by family/protocol path with deterministic ordering.
19. Front matter remains minimal scalar-only and limited to vital indexing/navigation fields.
20. Rare sections (for example unresolved diagnostics, inheritance exposure extras) appear only when populated.

## Testing Decisions

1. Good tests assert externally visible behavior and contracts, not internal implementation detail.
2. Delivery follows strict red-green-refactor TDD for each endpoint-family slice and for shared infrastructure.
3. Test modules (deep modules targeted for isolation):
   - endpoint rule-catalog and evaluator
   - endpoint identity/signature normalizer
   - endpoint extraction orchestrator per family
   - breadcrumb/fingerprint emitter
   - endpoint wiki projector/renderer
4. Unit tests must cover:
   - route normalization rules
   - signature identity determinism
   - confidence/reason-code mapping
   - rule provenance emission
   - declaration-vs-body context tagging
5. Integration tests must cover fixture repositories with:
   - controllers (attribute + deterministic conventional cases)
   - minimal APIs (grouped and chained mappings)
   - interface-based message handlers (including custom interfaces)
   - gRPC registrations
   - CLI command registrations
6. Negative and partial tests must validate unresolved behavior, including preserved partial records and reason codes.
7. Publication tests must verify endpoint pages, index pages, links, anchors, front matter contracts, and deterministic ordering.
8. Graph query tests must verify endpoint entities and relations are retrievable with expected cardinality and linkage.
9. Golden/snapshot tests must include endpoint outputs and detect accidental rendering regressions.
10. Performance tests must validate bounded overhead on larger repositories and stable behavior under configured limits.
11. Prior art should follow existing ingestion + projection + CLI test patterns already used in prior phases, extended for endpoint entities.

## Out of Scope

1. Full cross-system endpoint matching and automatic external system resolution.
2. Distributed tracing correlation with runtime telemetry.
3. Deep protocol contract extraction (for example full gRPC `.proto` semantic modeling in this phase).
4. Automatic identification of all custom framework endpoint styles without explicit rule definitions.
5. Historical endpoint churn/trend analysis across snapshots.
6. Non-.NET endpoint extraction implementations.
7. Policy-based security classification beyond structural endpoint metadata.

## Further Notes

1. This phase prioritizes endpoint discoverability and navigation fidelity while laying deterministic breadcrumbs for future cross-system inference.
2. Human readability remains primary: compact front matter, clear ownership links, parse-safe links, and concise sectioning.
3. Output must remain defensive: when certainty is low, publish partial findings with explicit confidence and reasons rather than hiding data.
4. Rule-catalog structure should be designed so future DSL externalization can happen without reworking downstream graph/wiki contracts.
