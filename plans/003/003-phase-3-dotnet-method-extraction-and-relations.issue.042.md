# Issue 042: PRD 003 Phase 1: Method Contracts and Ontology Expansion

> Parent PRD: [PRD 003](./003-phase-3-dotnet-method-extraction-and-relations.prd.md)

- Issue: [#42](https://github.com/vbfg1973/code-llm-wiki/issues/42)
- [x] Status: completed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#41

## What to build

Establish durable method-analysis contracts and ontology support for method declarations and method-level relationships, while preserving the existing analyzer -> triples -> query -> wiki seam. Deliver a thin end-to-end tracer bullet proving that method-capable predicates/entities flow through ingestion, projection, and rendering contracts. Reference PRD 003 sections: Solution items 1, 2, 11; Implementation Decisions 1-3, 19-22.

## Acceptance criteria

- [x] Ontology includes approved method and method-relationship predicates required by PRD 003 phase boundaries.
- [x] Query/view contracts are extended for method entities and required relationship projections without regressing existing PRD 001/002 behavior.
- [x] Deterministic method identity and ordering rules are codified and testable via contract-level tests.
- [x] Baseline end-to-end pipeline compiles/runs with method contracts enabled and no method extraction behavior yet required.

## Blocked by

- None - can start immediately

## User stories addressed

- User story 3
- User story 34
- User story 35
- User story 36
- User story 45
- User story 48
- User story 50
- User story 61
- User story 62
- User story 64
- User story 65
