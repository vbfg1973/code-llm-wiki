# Issue 063: PRD 005 Phase 2: Method-Body Dependency Usage Vertical Slice

> Parent PRD: [PRD 005](./005-phase-5-dependency-usage-mapping-and-package-provenance.prd.md)

- Issue: [#63](https://github.com/vbfg1973/code-llm-wiki/issues/63)
- [x] Status: completed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#61

## What to build

Implement the second end-to-end dependency vertical slice for method-body provenance. Emit method-body dependency predicates for approved evidence forms, exclude `nameof`, project package-centric grouping, and render deterministic method-body usage sections in wiki parallel to declaration usage. Reference PRD 005 sections: Solution items 1, 2, 4; Implementation Decisions 1-4, 9-14.

## Acceptance criteria

- [x] Method-body provenance dependency predicate(s) are emitted from ingestion for approved operation forms.
- [x] `nameof` is excluded from method-body dependency evidence in v1.
- [x] Query projections expose method-body dependency usage grouped as `package -> namespace -> type -> method` with deterministic counts/order.
- [x] Package wiki output includes method-body dependency usage sections with navigable links and deterministic ordering.
- [x] Tests verify method-body extraction/projection/render behavior through public boundaries.

## Blocked by

- Blocked by #62

## User stories addressed

- User story 1
- User story 2
- User story 5
- User story 6
- User story 7
- User story 8
- User story 9
- User story 16
- User story 17
- User story 20
- User story 22
- User story 24
- User story 25
- User story 26
- User story 28
- User story 29
- User story 30
- User story 33
- User story 34
- User story 37
