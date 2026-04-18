# Issue 062: PRD 005 Phase 1: Declaration Dependency Usage Vertical Slice

> Parent PRD: [PRD 005](./005-phase-5-dependency-usage-mapping-and-package-provenance.prd.md)

- Issue: [#62](https://github.com/vbfg1973/code-llm-wiki/issues/62)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#61

## What to build

Implement the first end-to-end dependency vertical slice for declaration provenance. Emit additive declaration dependency predicates from ingestion, project package-centric usage grouping (`package -> namespace -> type -> method`), and render declaration usage sections in wiki with deterministic ordering and links. Reference PRD 005 sections: Solution items 1, 2, 4; Implementation Decisions 1-4, 8, 12-14.

## Acceptance criteria

- [ ] Declaration provenance dependency predicate(s) are emitted from ingestion for declaration dependency evidence.
- [ ] Query projections expose declaration dependency usage grouped as `package -> namespace -> type -> method` with deterministic counts/order.
- [ ] Package wiki output includes declaration dependency usage sections with navigable links and deterministic ordering.
- [ ] Tests verify declaration extraction behavior through public boundaries and deterministic rendering/output.

## Blocked by

- None - can start immediately.

## User stories addressed

- User story 1
- User story 2
- User story 5
- User story 6
- User story 7
- User story 8
- User story 9
- User story 13
- User story 14
- User story 15
- User story 20
- User story 22
- User story 24
- User story 25
- User story 26
- User story 33
- User story 34
- User story 37
