# Issue 066: PRD 005 Phase 5: Rollup and Contract Hardening Vertical Slice

> Parent PRD: [PRD 005](./005-phase-5-dependency-usage-mapping-and-package-provenance.prd.md)

- Issue: [#66](https://github.com/vbfg1973/code-llm-wiki/issues/66)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#61

## What to build

Harden BL-011 contracts by finalizing query/wiki rollups derived from method-level evidence, enforcing deterministic ordering/count invariants, and expanding regression/golden coverage for intentional dependency-view deltas only. Keep scope constrained to BL-011 boundaries without MCP/query-surface expansion. Reference PRD 005 sections: Solution items 2, 4; Implementation Decisions 2-4, 11-14; Testing Decisions 1-7.

## Acceptance criteria

- [ ] Type-level dependency rollups are derived in query/wiki from method-level evidence without duplicating ingestion truth.
- [ ] Deterministic ordering/count invariants are enforced across dependency query and wiki outputs.
- [ ] Regression coverage validates declaration/method-body split, attribution semantics, and unresolved semantics stability.
- [ ] Golden/snapshot tests are updated only for intentional BL-011 output deltas.
- [ ] No MCP/query-surface expansion is introduced in this phase.

## Blocked by

- Blocked by #62
- Blocked by #63
- Blocked by #64
- Blocked by #65

## User stories addressed

- User story 3
- User story 4
- User story 8
- User story 23
- User story 31
- User story 32
- User story 35
- User story 36
- User story 38
