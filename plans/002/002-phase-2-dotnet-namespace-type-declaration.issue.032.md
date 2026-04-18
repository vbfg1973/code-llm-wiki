# Issue 032: PRD 002 Phase 8: Publication Determinism, Front Matter Validation, and CI Gates

> Parent PRD: [PRD 002](./002-phase-2-dotnet-namespace-type-declaration.prd.md)

- Issue: [#32](https://github.com/vbfg1973/code-llm-wiki/issues/32)
- [ ] Status: open
- [ ] Completion date: 

## Notes

## Parent PRD

#24

## What to build

Harden phase-2 publication and validation behavior: enforce scalar front matter contracts, preserve body readability constraints, validate repository-boundary rules, and extend golden/behavioral tests to protect deterministic output and degraded-resolution behavior. Reference PRD 002 validation and governance decisions, and plan 'Phase 8: Publication Determinism, Front Matter Validation, and CI Gates'.

## Acceptance criteria

- [ ] Namespace/type/file page front matter validation enforces minimal scalar contracts with conditional fields.
- [ ] Visible wiki body content remains ID-light/human-readable while IDs remain queryable via front matter/index.
- [ ] HEAD + git-tracked boundary behavior is preserved, including build-artifact exclusion rules.
- [ ] Golden and behavioral test suites verify deterministic output, edge cases (partial/nested/generic), and degradation diagnostics.

## Blocked by

- Blocked by #31

## User stories addressed

- User story 37
- User story 38
- User story 40
- User story 41
- User story 42
- User story 47
- User story 48
- User story 50
- User story 51
- User story 52
- User story 53
- User story 54
