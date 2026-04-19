# Issue 101: PRD 008 Phase 4: Enforce Guidance Contract Invariants and Deterministic Regression Coverage

> Parent PRD: [PRD 008](./008-phase-8-llm-and-user-guidance-documentation.prd.md)

- Issue: [#101](https://github.com/vbfg1973/code-llm-wiki/issues/101)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#97

## What to build

Harden BL-019 with guidance-specific invariant and regression coverage: required-page presence, front matter schema, mandatory anchors/sections, entry-link wiring, and deterministic rerun behavior. Integrate with existing publication/golden test patterns. Reference PRD 008 Solution item 5 and Testing Decisions 1-7.

## Acceptance criteria

- [x] Automated tests enforce guidance page existence and required front matter keys.
- [x] Automated tests enforce required sections/anchors and repository/index entry links.
- [x] Golden/snapshot coverage captures intentional guidance-output additions only.
- [x] Repeated runs remain deterministic with stable anchors and content ordering.

## Blocked by

- Blocked by #100

## User stories addressed

- User story 19
- User story 20
- User story 21
- User story 22
- User story 23
- User story 24
- User story 26
- User story 29
- User story 35
- User story 36
- User story 37
