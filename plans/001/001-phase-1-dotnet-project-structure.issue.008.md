# Issue 008: Phase 7: Validation, Golden Tests, and CI Gates

> Parent PRD: [PRD 001](./001-phase-1-dotnet-project-structure.prd.md)

- Issue: [#8](https://github.com/vbfg1973/code-llm-wiki/issues/8)
- [x] Status: closed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#1

## What to build

Harden delivery with behavioral tests and CI gates: unit tests for stable interfaces, golden end-to-end snapshots for wiki/GraphML, exit semantics verification, and stale-artifact validation.

Reference sections: PRD "Testing Decisions" and plan "Phase 7: Validation, Golden Tests, and CI Gates".

## Acceptance criteria

- [ ] Unit tests verify external behavior for core modules
- [ ] Golden integration tests validate deterministic wiki and GraphML outputs
- [ ] CLI exit behavior is verified for success, partial-success, and override modes
- [ ] CI fails when generated artifacts are stale or ontology/front matter validation fails

## Blocked by

- Blocked by #3
- Blocked by #4
- Blocked by #5
- Blocked by #6
- Blocked by #7

## User stories addressed

- User story 17
- User story 18
- User story 36
- User story 37
- User story 38
- User story 39
