# Issue 122: PRD 010 Phase 3: Override Resolution and Type-Fallback Noise Reduction

> Parent PRD: [PRD 010](/home/vbfg/dev/dotnet-llm-wiki/plans/010/010-phase-10-semantic-resolution-quality-and-ingestion-observability.prd.md)

- Issue: [#122](https://github.com/vbfg1973/code-llm-wiki/issues/122)
- [ ] Status: open

## Notes

## Parent PRD

#119

## What to build

Extend project-scoped semantics to override relationship resolution and reduce fallback noise by normalizing nullable and array type forms before unresolved classification and dedupe. Reference PRD 010 Solution items 1 and 5, and Implementation Decisions 2, 9, and 10.

## Acceptance criteria

- [ ] Override relationships resolve through project-scoped semantic contexts.
- [ ] Nullable and array type references are normalized before fallback classification.
- [ ] Fallback diagnostic dedupe reduces repeated low-signal noise while preserving evidence.
- [ ] Tests verify override resolution and normalized fallback behavior stability.

## Blocked by

- Blocked by #121

## User stories addressed

- User story 5
- User story 17
- User story 18
- User story 27
- User story 44
