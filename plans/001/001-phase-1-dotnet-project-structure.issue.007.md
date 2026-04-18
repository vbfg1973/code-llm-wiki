# Issue 007: Phase 6: Deterministic Wiki + GraphML Publication

> Parent PRD: [PRD 001](./001-phase-1-dotnet-project-structure.prd.md)

- Issue: [#7](https://github.com/vbfg1973/code-llm-wiki/issues/7)
- [x] Status: closed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#1

## What to build

Complete deterministic output publication: full wiki regeneration, GraphML export, run-scoped artifact layout, run manifest, and success-gated atomic promotion to latest.

Reference sections: PRD "Solution", "Implementation Decisions", and plan "Phase 6: Deterministic Wiki + GraphML Publication".

## Acceptance criteria

- [ ] All five wiki entity page types generate deterministically from query interfaces
- [ ] GraphML export is emitted per run and aligned with graph facts
- [ ] Run manifest includes status, timings, counts, diagnostics summary, and artifact references
- [ ] latest publication updates atomically only on success

## Blocked by

- Blocked by #4
- Blocked by #6

## User stories addressed

- User story 20
- User story 21
- User story 23
- User story 24
- User story 25
- User story 26
- User story 34
