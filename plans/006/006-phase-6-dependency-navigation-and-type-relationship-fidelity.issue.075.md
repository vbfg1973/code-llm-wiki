# Issue 075: PRD 006 Phase 2B: Target-First Package Method-Body Dependencies

> Parent PRD: [PRD 006](./006-phase-6-dependency-navigation-and-type-relationship-fidelity.prd.md)

- Issue: [#75](https://github.com/vbfg1973/code-llm-wiki/issues/75)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#72

## What to build

Deliver target-first package method-body dependency navigation so package pages present behavioral coupling as external type to internal method with unambiguous method naming. Remove redundant caller-first method-body view and preserve deterministic ordering. Reference PRD 006 Solution item 1 and Implementation Decisions 2-3, 11-12.

## Acceptance criteria

- [ ] Package pages render method-body dependencies as `External Type -> Internal Method`.
- [ ] Method aliases in this section are unambiguous for quick navigation.
- [ ] Caller-first method-body dependency presentation is removed from package pages.
- [ ] Deterministic ordering and regression tests cover method-body target-first behavior.

## Blocked by

- Blocked by #73
- Blocked by #74

## User stories addressed

- User story 1
- User story 3
- User story 8
- User story 17
- User story 18
- User story 28
- User story 33
