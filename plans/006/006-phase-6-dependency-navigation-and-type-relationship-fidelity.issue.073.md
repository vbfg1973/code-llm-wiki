# Issue 073: PRD 006 Phase 1: Parse-Safe Link Contract and Deep Anchors

> Parent PRD: [PRD 006](./006-phase-6-dependency-navigation-and-type-relationship-fidelity.prd.md)

- Issue: [#73](https://github.com/vbfg1973/code-llm-wiki/issues/73)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#72

## What to build

Establish a parse-safe wiki link contract and stable deep-anchor policy for package external-type sections. Ensure external dependency and relationship references can route through package pages to deterministic anchors while preventing markdown table parsing failures. Reference PRD 006 Solution items 2 and 4 and Implementation Decisions 4-5, 11.

## Acceptance criteria

- [x] Table-rendered links use parse-safe markdown link syntax and avoid delimiter conflicts.
- [x] Package external-type sections publish deterministic deep anchors suitable for direct linking.
- [x] External references can route to package deep anchors from dependency/relationship contexts.
- [x] Tests validate table parse safety and deep-anchor stability.

## Blocked by

None - can start immediately.

## User stories addressed

- User story 4
- User story 5
- User story 6
- User story 7
- User story 22
- User story 23
- User story 30
- User story 35
