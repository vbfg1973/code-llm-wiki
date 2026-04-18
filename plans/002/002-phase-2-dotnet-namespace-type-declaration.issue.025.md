# Issue 025: PRD 002 Phase 1: Contracts and Ontology Expansion

> Parent PRD: [PRD 002](./002-phase-2-dotnet-namespace-type-declaration.prd.md)

- Issue: [#25](https://github.com/vbfg1973/code-llm-wiki/issues/25)
- [x] Status: complete
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#24

## What to build

Expand durable graph/query contracts and ontology vocabulary for namespace/type/member declarations without changing the established analyzer -> graph -> query -> wiki seam. Reference PRD 002 'Solution' and implementation decisions on namespace/type/member modeling, and plan 'Phase 1: Contracts and Ontology Expansion'.

## Acceptance criteria

- [x] Ontology includes approved namespace/type/member declaration predicates with validation passing.
- [x] Core query/view contracts are extended for namespace/type/member entities while preserving existing phase-1 behavior.
- [x] Deterministic identity and ordering rules for new entities are documented and testable.
- [x] Baseline integration path (analyzer -> graph -> query -> wiki) compiles and runs with new contracts enabled.

## Blocked by

None - can start immediately

## User stories addressed

- User story 21
- User story 23
- User story 24
- User story 34
- User story 46
- User story 49
- User story 55
