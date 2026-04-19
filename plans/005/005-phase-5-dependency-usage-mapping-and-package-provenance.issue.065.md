# Issue 065: PRD 005 Phase 4: Unknown and Unresolved Dependency Semantics Vertical Slice

> Parent PRD: [PRD 005](./005-phase-5-dependency-usage-mapping-and-package-provenance.prd.md)

- Issue: [#65](https://github.com/vbfg1973/code-llm-wiki/issues/65)
- [x] Status: completed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#61

## What to build

Implement explicit unknown and unresolved dependency semantics across ingestion, query, and wiki dependency views. Emit unresolved dependency entities with reason codes, retain unknown package attribution when deterministic mapping is unavailable, and surface these semantics in package-centric usage output. Reference PRD 005 sections: Solution items 3, 5; Implementation Decisions 6-7, 12-14.

## Acceptance criteria

- [x] Unknown package attribution is emitted explicitly when deterministic mapping is unavailable.
- [x] Unresolved dependency entities with reason codes are emitted and queryable.
- [x] Package/wiki dependency views surface unknown/unresolved semantics explicitly.
- [x] Tests cover ambiguity and unresolved scenarios deterministically.

## Blocked by

- Blocked by #63
- Blocked by #64

## User stories addressed

- User story 18
- User story 19
- User story 27
- User story 30
- User story 36
