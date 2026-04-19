# Issue 073: PRD 006 Phase 1: Parse-Safe Link Contract and Deep Anchors

> Parent PRD: [PRD 006](./006-phase-6-dependency-navigation-and-type-relationship-fidelity.prd.md)

- Issue: [#73](https://github.com/vbfg1973/code-llm-wiki/issues/73)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#72

## What to build

Establish a parse-safe wiki link contract and stable deep-anchor policy for package external-type sections. Ensure external dependency and relationship references can route through package pages to deterministic anchors while preventing markdown table parsing failures. Reference PRD 006 Solution items 2 and 4 and Implementation Decisions 4-5, 11.

## Acceptance criteria

- [ ] Table-rendered links use parse-safe markdown link syntax and avoid delimiter conflicts.
- [ ] Package external-type sections publish deterministic deep anchors suitable for direct linking.
- [ ] External references can route to package deep anchors from dependency/relationship contexts.
- [ ] Tests validate table parse safety and deep-anchor stability.

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
