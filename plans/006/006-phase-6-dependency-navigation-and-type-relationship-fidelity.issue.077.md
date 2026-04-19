# Issue 077: PRD 006 Phase 4: Conditional Rare and Edge Dependency Sections

> Parent PRD: [PRD 006](./006-phase-6-dependency-navigation-and-type-relationship-fidelity.prd.md)

- Issue: [#77](https://github.com/vbfg1973/code-llm-wiki/issues/77)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#72

## What to build

Add conditional rare/edge dependency sections for package-owned inherited types and unresolved terminal buckets. These sections must appear only when needed and preserve readable, non-noisy navigation. Reference PRD 006 Solution item 5 and Implementation Decisions 9-10, 13.

## Acceptance criteria

- [x] `Inherited Package Types` appears only when package-owned inheritance evidence exists.
- [x] Unresolved external targets are grouped into explicit terminal buckets with concise reasons.
- [x] Unknown/unresolved entries preserve navigation consistency without broken links.
- [x] Tests validate conditional rendering and reason-label consistency.

## Blocked by

- Blocked by #74
- Blocked by #75
- Blocked by #76

## User stories addressed

- User story 15
- User story 16
- User story 19
- User story 20
- User story 27
