# Issue 088: PRD 007 Phase 2: Structural Rollups and Scope Filters

> Parent PRD: [PRD 007](./007-phase-7-complexity-and-maintainability-metrics.prd.md)

- Issue: [#88](https://github.com/vbfg1973/code-llm-wiki/issues/88)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#86

## What to build

Build deterministic rollups and scope semantics for BL-012 across file, namespace hierarchy, project, and repository surfaces. Implement namespace direct vs recursive cumulative rollups (including synthetic `(global)`), production-default ranking scope, generated-code filtering defaults, and explicit insufficient-data severity behavior. Reference PRD 007: Solution items 2, 5; Implementation Decisions 3, 9, 14-16, 23.

## Acceptance criteria

- [x] File, namespace, project, and repository rollups are available and deterministic.
- [x] Namespace rollups support direct-only and recursive cumulative modes including `(global)` namespace.
- [x] Production-default ranking with filterable test/generated code kinds is implemented.
- [x] Insufficient-data scopes are marked `severity: none` and excluded from default ranked lists.

## Blocked by

- Blocked by #87

## User stories addressed

- User story 8
- User story 9
- User story 10
- User story 11
- User story 12
- User story 13
- User story 14
- User story 15
- User story 16
- User story 17
- User story 24
- User story 25
- User story 26
- User story 27
