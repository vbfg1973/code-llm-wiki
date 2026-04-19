# Issue 031: PRD 002 Phase 7: File Backlink and Traceability Vertical Slice

> Parent PRD: [PRD 002](./002-phase-2-dotnet-namespace-type-declaration.prd.md)

- Issue: [#31](https://github.com/vbfg1973/code-llm-wiki/issues/31)
- [x] Status: complete
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#24

## What to build

Complete bidirectional declaration traceability by enriching file pages with grouped declared-symbol backlinks and ensuring type pages provide deterministic declaration location sections. Include deterministic primary context selection for scalar type front matter. Reference PRD 002 file-linkage decisions and plan 'Phase 7: File Backlink and Traceability Vertical Slice'.

## Acceptance criteria

- [x] Type declarations link back to all declaration files/locations deterministically.
- [x] File pages include grouped declaration backlinks by kind (namespace/type/member).
- [x] Backlink ordering is deterministic by file path/source location/symbol identity tie-break.
- [x] Primary scalar project/assembly context is chosen deterministically by first declaration.

## Blocked by

- Blocked by #30

## User stories addressed

- User story 11
- User story 12
- User story 13
- User story 39
- User story 44
- User story 45
