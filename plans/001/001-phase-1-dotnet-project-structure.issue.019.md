# Issue 019: Phase 11: File History Presentation Controls

> Parent PRD: [PRD 001](./001-phase-1-dotnet-project-structure.prd.md)

- Issue: [#19](https://github.com/vbfg1973/code-llm-wiki/issues/19)
- [x] Status: closed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#1

## What to build

Align file-history presentation for human readability: most-recent-first merge history, unbounded by default, optional configurable cap per file, and deterministic rendering behavior.

Reference sections: PRD Addendum 2026-04-18 and plan "Phase 11: File History Presentation Controls".

## Acceptance criteria

- [ ] File page titles use repository-relative file paths
- [ ] Merge-to-mainline entries are rendered most-recent-first and default to unbounded output
- [ ] CLI/config supports optional `max_merge_entries_per_file` without changing default behavior
- [ ] Golden/behavioral tests verify ordering, cap behavior, and unchanged default output

## Blocked by

- Blocked by #16
- Blocked by #17

## User stories addressed

- User story 27
- User story 28
- User story 30
- User story 31
- User story 32
- User story 33
