# Issue 047: PRD 003 Phase 6: Publication Determinism, Validation, and CI Gates

> Parent PRD: [PRD 003](./003-phase-3-dotnet-method-extraction-and-relations.prd.md)

- Issue: [#47](https://github.com/vbfg1973/code-llm-wiki/issues/47)
- [x] Status: complete
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#41

## What to build

Harden PRD 003 publication and quality gates: validate method page/front matter contracts, preserve ID-light readability constraints and operational boundaries, and extend deterministic behavioral/golden regression coverage for method relationships and degraded semantic behavior. Reference PRD 003 sections: Solution item 11; Testing Decisions.

## Acceptance criteria

- [x] Method/type front matter validation enforces approved minimal scalar contracts and conditional fields.
- [x] Visible wiki body content remains ID-light/human-readable while IDs remain queryable via front matter/index.
- [x] `HEAD` + git-tracked boundary behavior remains enforced for method/data-flow publication outputs.
- [x] Golden and behavioral test suites verify deterministic method outputs, relationship rendering, and degraded semantic diagnostics/provenance.

## Blocked by

- Blocked by #46

## User stories addressed

- User story 46
- User story 47
- User story 49
- User story 51
- User story 52
- User story 53
- User story 54
- User story 55
- User story 60
- User story 63
