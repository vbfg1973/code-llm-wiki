# PRD 001: Phase 1 .NET Repository Structure Ingestion and Wiki Output

Date: 2026-04-18  
Status: Draft (approved baseline, subject to iteration)  
Phase: First development phase

## Problem Statement

As maintainers of .NET repositories, we need a reliable way to extract and share accurate structural context about a codebase. Today, that context is fragmented across solution files, project files, package declarations, Git history, and implicit build behavior, making it expensive for both humans and LLMs to understand system shape quickly.

We need a deterministic, repeatable process that ingests repository structure and history into a strict graph model, then publishes human-first wiki output that remains synchronized with implementation decisions and ontology evolution.

## Solution

Build a CLI-first ingestion system in .NET 10 that performs one-repository-per-run analysis, with a strict semantic-triple graph model backed by QuikGraph and serialized to GraphML. Phase 1 focuses on project structure only: repository, solutions, projects, packages, and files with Git history.

The ingestion will use `HEAD` as the canonical source commit for deterministic output while recording diagnostics when the working tree is dirty. It will gather:

1. Repository and solution/project topology.
2. Package dependencies (declared, plus resolved when locally available).
3. Full per-file commit timeline with summaries, rename-aware history, and true merge-commit-to-mainline events.

The output will be:

1. Run-scoped artifacts per ingestion run.
2. A deterministic markdown wiki (human-first) with minimal front matter and rich body content.
3. GraphML export for graph-capable downstream tooling.
4. A machine-readable run manifest for CI/operations.

Wiki generation will consume a formal query interface contract rather than graph internals, preserving modularity for future MCP and ad hoc querying.

## User Stories

1. As a platform engineer, I want to run one CLI command to ingest a repository, so that I can generate structural documentation consistently.
2. As a developer, I want long and short CLI options, so that command usage is fast and ergonomic.
3. As a team lead, I want one repository analyzed per run, so that identity and provenance remain unambiguous.
4. As an architect, I want repository-root modeling with solutions/projects beneath it, so that non-solution project assets are not missed.
5. As a maintainer, I want all git-tracked files included in analysis, so that operationally important non-code files are still documented.
6. As a maintainer, I want files classified (for example .NET source vs non-source), so that I can filter wiki/query views cleanly.
7. As a build engineer, I want MSBuild evaluation used as primary discovery, so that conditional/imported project and package data is accurate.
8. As a developer, I want fallback parsing when MSBuild evaluation fails, so that ingestion still produces useful partial output.
9. As a dependency owner, I want declared package dependencies captured, so that intent is visible even without restore artifacts.
10. As a dependency owner, I want resolved package versions captured when available locally, so that effective dependency reality is visible.
11. As a repository owner, I want strict ontology governance, so that query contracts remain stable over time.
12. As a consumer of generated docs, I want semantic versioning for ontology changes, so that breaking changes are explicit and managed.
13. As a graph consumer, I want stable natural-key identity, so that entities are comparable across runs.
14. As a graph consumer, I want deterministic derived IDs, so that links and references remain stable and machine-friendly.
15. As an analyst, I want literal values represented as triple nodes, so that querying remains uniform across fact types.
16. As a maintainer, I want ingestion to complete with diagnostics when partial failures occur, so that documentation flow is not blocked.
17. As a CI owner, I want partial completion to return non-zero by default, so that pipelines stay strict unless explicitly overridden.
18. As an engineer, I want a local override for partial-success acceptance, so that exploratory runs are possible during investigation.
19. As a user of generated docs, I want pages for repository, solution, project, package, and file entities, so that phase-1 context is complete.
20. As an Obsidian user, I want minimal mandatory common front matter, so that metadata remains useful without dominating pages.
21. As an LLM consumer, I want consistent common front matter across all entity pages, so that retrieval is reliable.
22. As a documentation consumer, I want richer details in page bodies rather than front matter, so that readability remains high.
23. As a developer, I want deterministic full regeneration of generated pages each run, so that output drift is eliminated.
24. As a developer, I want run-scoped output directories, so that I can compare and audit historical runs.
25. As an operator, I want atomic promotion to a `latest` pointer only on success, so that consumers never see partial artifacts.
26. As a maintainer, I want a machine-readable run manifest, so that CI and tooling can reason about status and diagnostics programmatically.
27. As a repository reader, I want each page to state branch derivation context, so that I understand source-of-truth lineage.
28. As a file-level reader, I want edit count and last-change summaries, so that I can identify potential hotspots quickly.
29. As a file-level reader, I want rename-aware file history, so that refactors do not reset historical understanding.
30. As a file-level reader, I want merge-to-mainline history entries for true merge commits, so that delivery cadence is visible.
31. As a file-level reader, I want merge history to include timestamp and author, so that accountability and chronology are clear.
32. As a file-level reader, I want source-branch commit counts for that specific file only, so that the metric stays file-relevant.
33. As a documentation reader, I want metadata-first file pages without code excerpts in phase 1, so that pages stay concise and focused.
34. As a query consumer, I want wiki generation to use a formal query service contract, so that downstream interfaces can evolve safely.
35. As a future platform architect, I want a language-agnostic ontology core with .NET extension namespace, so that Python/frontend analyzers can be added without redesign.
36. As a team member, I want generated artifact freshness enforced in CI, so that committed docs cannot drift from implementation.
37. As a QA engineer, I want golden-file integration tests for graph and wiki outputs, so that regressions are caught end-to-end.
38. As a developer, I want unit tests around modules with stable interfaces, so that behavior is validated without over-coupling to implementation details.
39. As a performance owner, I want a defined phase-1 performance envelope, so that we can detect unacceptable regressions early.
40. As a product owner, I want a vertical slice delivered end-to-end first, so that we validate architecture with working output before adding deeper analysis.
41. As an engineering manager, I want ADRs for major contracts, so that decisions and change history stay explicit and current.
42. As a future integrator, I want query compatibility with MCP-style ad hoc access in later phases, so that graph insights are reusable beyond wiki generation.
43. As an operations engineer, I want run diagnostics persisted as structured facts, so that trust boundaries are visible in outputs.
44. As an architect, I want submodules captured as opaque dependencies in phase 1, so that repository boundary rules stay consistent.
45. As a maintainability-focused team, I want module boundaries to enforce deep interfaces, so that implementation can evolve behind stable contracts.

## Implementation Decisions

1. Use a CLI-first architecture with a single ingestion verb and option aliases.
2. Use one repository per run.
3. Use `HEAD` as canonical provenance commit for phase 1.
4. Record working-tree dirtiness as diagnostics without changing provenance commit identity.
5. Model repository as root with discovered solutions and projects as children.
6. Include all git-tracked files at `HEAD`; classify files for filtering.
7. Capture package dependencies as declared plus resolved when locally available.
8. Use MSBuild evaluation as primary for .NET structure/package discovery; fallback parsing when needed.
9. Represent data as strict semantic triples with first-class literal/value nodes.
10. Back graph representation with QuikGraph and support GraphML serialization.
11. Enforce strict, versioned ontology governance with a canonical machine-readable ontology file.
12. Use language-agnostic core ontology predicates and `dotnet:*` extension namespace.
13. Use stable natural keys for entity identity; allow deterministic derived IDs.
14. Persist full per-file commit timeline plus summary fields.
15. Follow renames to preserve logical file identity across path changes.
16. Track true merge commits to mainline only; do not infer squash/rebase merges in phase 1.
17. Resolve mainline branch from default remote head first, then fallback branch names.
18. Generate five page types in phase 1: repository, solution, project, package, file.
19. Keep common front matter minimal and fixed to seven mandatory fields.
20. Keep page-specific front matter minimal and stable; place richer detail in page body sections.
21. Regenerate all generated wiki pages deterministically every run.
22. Write artifacts to run-scoped output and atomically promote a success-only `latest` view.
23. Emit a machine-readable run manifest with status, counts, timings, diagnostics, and artifact locations.
24. Expose graph read access through a formal query interface used by wiki generation.
25. Return non-zero exit code for partial-success runs by default, with explicit override option.
26. Require ADRs for major contracts: ontology, identity, query interface, front matter schema, and CLI exit semantics.
27. Target deep module seams: CLI, ingestion orchestration, .NET analyzer, graph, query, wiki, validation.

## Testing Decisions

1. A good test validates externally observable behavior and contracts, not internal implementation details.
2. Unit tests will focus on deep modules with stable public interfaces and deterministic behavior.
3. Integration tests will validate end-to-end ingestion and generation through golden outputs.
4. Golden tests will cover GraphML and wiki output determinism for representative fixture repositories.
5. Validation tests will enforce ontology schema compliance and front matter schema compliance.
6. CLI behavior tests will verify exit codes, option parsing, config override behavior, and partial-success semantics.
7. Git-history behavior tests will verify rename handling, merge-to-mainline extraction, and file-specific branch commit counts.
8. Publication tests will verify atomic promotion and no partial publication into `latest`.
9. CI will include an up-to-date generated-artifacts gate to detect stale committed outputs.
10. Prior-art guidance: no existing in-repo test suite currently exists; establish patterns from this phase as project standard.

## Out of Scope

1. Full semantic code analysis (types, inheritance, call graphs, property read/write flows).
2. Endpoint discovery and endpoint-specific metadata extraction.
3. Complexity metrics (cyclomatic, cognitive, Halstead, maintainability index, CBO, LOC analysis beyond basic structural counts).
4. Domain-term extraction and semantic lexicon linking.
5. Cross-repository/system dependency traversal and whole-estate architecture maps.
6. Non-.NET analyzers (Python, React, Vue, Angular).
7. Inference of squash/rebase merges as merge events.
8. Rendering source code excerpts within file pages.

## Further Notes

1. Human readability is primary for wiki pages; stability and determinism remain hard constraints.
2. Canonical page paths are ID-based in phase 1, with human-readable titles and navigation metadata; this may evolve later.
3. This PRD is intentionally focused on one complete vertical slice before deeper analyzer expansion.
4. Decision and change history must remain current through ADR updates and ontology/version governance.
5. Expected baseline performance target for phase 1: repositories up to approximately 500k LOC / 20k tracked files, <=10 minutes runtime, <=2 GB memory.

## Addendum 2026-04-18: Output Style and Obsidian Optimization

Approved refinements to implementation/output style:

1. Use human-readable page filenames; do not expose entity IDs in filenames.
2. Mirror repository layout for file pages to maximize navigability.
3. Keep stable IDs in front matter and in a canonical repository index page.
4. Use Obsidian wikilinks for internal cross-page linking.
5. Keep front matter scalar-only and minimal; avoid global aggregates in front matter.
6. Front matter schema (v1) is:
   - common on all pages: `entity_id`, `entity_type`, `repository_id`
   - repository: `repository_name`, `repository_path`, `head_branch`, `mainline_branch`
   - solution: `solution_name`, `solution_path`
   - project: `project_name`, `project_path`, `target_frameworks`, `discovery_method`
   - package: `package_id`, `package_key`
   - file: `file_name`, `file_path`
7. Enforce snake_case front matter keys and UTC ISO-8601 `Z` timestamps.
8. Package pages must include project-membership/version context per project.
9. File merge history is default unbounded, ordered most-recent-first, with optional configurable caps.
