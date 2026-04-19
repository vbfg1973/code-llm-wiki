# Issue 006: Phase 5: Git History and Merge-to-Mainline Vertical Slice

> Parent PRD: [PRD 001](./001-phase-1-dotnet-project-structure.prd.md)

- Issue: [#6](https://github.com/vbfg1973/code-llm-wiki/issues/6)
- [x] Status: closed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#1

## What to build

Integrate full rename-aware git file history, summary metrics, and true merge-commit-to-mainline events with file-specific source-branch commit counts and branch derivation context.

Reference sections: PRD "Solution", "Implementation Decisions", and plan "Phase 5: Git History and Merge-to-Mainline Vertical Slice".

## Acceptance criteria

- [ ] Full per-file commit history is ingested with rename continuity
- [ ] File summaries include edit count and last-change provenance
- [ ] Merge-to-mainline entries include timestamp, author, target branch context, and file-specific source-branch commit count
- [ ] Submodule presence is represented as opaque dependency facts without recursive ingestion

## Blocked by

- Blocked by #5

## User stories addressed

- User story 27
- User story 28
- User story 29
- User story 30
- User story 31
- User story 32
- User story 43
- User story 44
