# Issue 074: PRD 006 Phase 2A: Target-First Package Declaration Dependencies

> Parent PRD: [PRD 006](./006-phase-6-dependency-navigation-and-type-relationship-fidelity.prd.md)

- Issue: [#74](https://github.com/vbfg1973/code-llm-wiki/issues/74)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#72

## What to build

Deliver target-first package declaration dependency navigation so package pages present declaration coupling as external type to internal type. Remove redundant caller-first declaration views in favor of the approved canonical structure. Reference PRD 006 Solution item 1 and Implementation Decisions 2-3, 11-12.

## Acceptance criteria

- [ ] Package pages render declaration dependencies as `External Type -> Internal Type`.
- [ ] Caller-first declaration dependency presentation is removed from package pages.
- [ ] External type entries support package deep-link navigation.
- [ ] Deterministic ordering and regression tests cover declaration target-first behavior.

## Blocked by

- Blocked by #73

## User stories addressed

- User story 1
- User story 2
- User story 8
- User story 18
- User story 28
- User story 33
