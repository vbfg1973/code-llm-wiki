# Issue 017: Phase 9: Scalar Front Matter Schema v1

> Parent PRD: [PRD 001](./001-phase-1-dotnet-project-structure.prd.md)

- Issue: [#17](https://github.com/vbfg1973/code-llm-wiki/issues/17)
- [x] Status: closed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#1

## What to build

Implement scalar-only front matter schema v1 for all generated pages, including common and entity-specific keys approved in the output-style addendum, plus strict schema validation.

Reference sections: PRD Addendum 2026-04-18 and plan "Phase 9: Scalar Front Matter Schema v1".

## Acceptance criteria

- [ ] Common front matter exists on all pages: `entity_id`, `entity_type`, `repository_id`
- [ ] Entity-specific fields are emitted exactly as approved (repository, solution, project, package, file)
- [ ] Front matter keys are snake_case and emitted timestamps are UTC ISO-8601 with trailing `Z`
- [ ] Validation tests fail on missing/invalid required fields

## Blocked by

- Blocked by #16

## User stories addressed

- User story 20
- User story 21
- User story 22
- User story 27
- User story 34
