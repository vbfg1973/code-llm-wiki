# Issue 056: PRD 004 Phase 2: File Declared-Method Link Rendering and Type-Method Link Regression Guard Vertical Slice

> Parent PRD: [PRD 004](./004-phase-4-wiki-navigation-link-consistency.prd.md)

- Issue: [#56](https://github.com/vbfg1973/code-llm-wiki/issues/56)
- [x] Status: completed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#54

## What to build

Implement end-to-end file page rendering behavior where `Declared Symbols -> Methods` entries are emitted as wiki links for resolvable method targets, preserving current alias readability and deterministic ordering. Include a focused regression guard asserting type method listings remain linked in representative large-surface scenarios. Reference PRD 004 sections: Solution items 2, 3, 5; Implementation Decisions 1-3, 5, 10-12.

## Acceptance criteria

- [x] File page `Declared Symbols -> Methods` entries render as wiki links for resolvable method targets.
- [x] Existing alias/readability format is preserved (`MethodAlias (kind)` with linked `MethodAlias`).
- [x] Focused tests verify linked rendering and deterministic ordering for file method entries.
- [x] Regression test explicitly guards against non-linked method listings in type method sections.

## Blocked by

- None - can start immediately.

## User stories addressed

- User story 2
- User story 3
- User story 4
- User story 5
- User story 8
- User story 9
- User story 10
- User story 11
- User story 13
- User story 14
- User story 17
- User story 21
- User story 22
- User story 25
