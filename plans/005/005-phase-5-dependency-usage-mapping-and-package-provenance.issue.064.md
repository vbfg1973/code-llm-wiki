# Issue 064: PRD 005 Phase 3: Project-Scoped Package Attribution Vertical Slice

> Parent PRD: [PRD 005](./005-phase-5-dependency-usage-mapping-and-package-provenance.prd.md)

- Issue: [#64](https://github.com/vbfg1973/code-llm-wiki/issues/64)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#61

## What to build

Implement deterministic, project-scoped package attribution for external dependency usage. Attribute dependencies using source method/type project context, validate mixed-version correctness, and propagate package attribution into query/wiki grouping while preserving deterministic behavior. Reference PRD 005 sections: Solution items 3, 4; Implementation Decisions 4-7, 12-14.

## Acceptance criteria

- [ ] External dependency package attribution uses source method/type project context.
- [ ] Deterministic mapping is applied only when attribution certainty is available.
- [ ] Query/package projections and wiki output reflect project-correct package attribution in mixed-version scenarios.
- [ ] Tests cover mixed-version multi-project attribution behavior deterministically.

## Blocked by

- Blocked by #62
- Blocked by #63

## User stories addressed

- User story 10
- User story 11
- User story 12
- User story 13
- User story 24
- User story 25
- User story 30
- User story 36
- User story 39
- User story 40
