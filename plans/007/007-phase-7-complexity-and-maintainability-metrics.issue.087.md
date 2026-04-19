# Issue 087: PRD 007 Phase 1: Core Metric Fact Extraction

> Parent PRD: [PRD 007](./007-phase-7-complexity-and-maintainability-metrics.prd.md)

- Issue: [#87](https://github.com/vbfg1973/code-llm-wiki/issues/87)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#86

## What to build

Implement ingestion-time metric fact extraction for BL-012 core signals. Persist method metrics (cyclomatic, cognitive, Halstead core set, LOC breakdown, maintainability index) and type CBO metrics (declaration, method-body, total) as first-class triples, with explicit coverage/completeness metadata for non-analyzable members. Reference PRD 007: Solution items 1, 6-7; Implementation Decisions 1-3, 10-13, 18, 22, 24.

## Acceptance criteria

- [x] Method metric facts are persisted as triples with stable identity and deterministic value formatting.
- [x] Type CBO facts are persisted as triples with declaration/method-body/total breakdown.
- [x] Methods without bodies are excluded from rankings but included in coverage/completeness counters.
- [x] Unit and integration tests validate formula contracts and normalization semantics for generics/wrappers.

## Blocked by

- None - can start immediately.

## User stories addressed

- User story 1
- User story 2
- User story 3
- User story 4
- User story 5
- User story 6
- User story 18
- User story 19
- User story 37
- User story 38
- User story 39
- User story 40
- User story 41
