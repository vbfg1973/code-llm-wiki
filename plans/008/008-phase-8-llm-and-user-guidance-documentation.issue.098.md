# Issue 098: PRD 008 Phase 1: Publish Guidance Pages and Entry Links

> Parent PRD: [PRD 008](./008-phase-8-llm-and-user-guidance-documentation.prd.md)

- Issue: [#98](https://github.com/vbfg1973/code-llm-wiki/issues/98)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#97

## What to build

Publish first-class snapshot guidance pages for human and LLM consumers at stable paths (`guidance/human.md`, `guidance/llm-contract.md`) and wire compact guidance links from repository and repository-index entry pages. Keep minimal scalar front matter with guidance classification and branch context in front matter only. Reference PRD 008 Solution items 1, 4, 7 and Implementation Decisions 1-5.

## Acceptance criteria

- [x] Both guidance pages are emitted in every generated wiki snapshot at fixed paths.
- [x] Required minimal front matter is present, including `guidance_kind` and branch context fields.
- [x] Repository and repository-index pages each include a compact `Guidance` section linking both guidance pages.
- [x] Deterministic output ordering and rerun stability are preserved.

## Blocked by

None - can start immediately.

## User stories addressed

- User story 1
- User story 4
- User story 5
- User story 6
- User story 7
- User story 18
- User story 25
- User story 30
- User story 40
