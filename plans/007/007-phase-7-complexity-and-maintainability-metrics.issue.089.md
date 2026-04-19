# Issue 089: PRD 007 Phase 3: Hotspot Ranking and Severity Engine

> Parent PRD: [PRD 007](./007-phase-7-complexity-and-maintainability-metrics.prd.md)

- Issue: [#89](https://github.com/vbfg1973/code-llm-wiki/issues/89)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

## Parent PRD

#86

## What to build

Implement hotspot ranking and severity contracts for BL-012 with per-metric primary rankings and composite secondary ranking. Apply repository-relative normalization, configurable weights/thresholds with stable defaults, deterministic tie-break ordering, and row-budget controls. Reference PRD 007: Solution items 3-4; Implementation Decisions 4, 7-9, 16-17, 20.

## Acceptance criteria

- [x] Per-metric rankings and composite rankings are both available with deterministic ordering.
- [x] Composite weighting and threshold configuration supports defaults and override values.
- [x] Effective weights/thresholds are exposed for auditability.
- [x] Default top-N budgeting and explicit unbounded override behavior are implemented and tested.

## Blocked by

- Blocked by #88

## User stories addressed

- User story 7
- User story 25
- User story 26
- User story 27
- User story 28
- User story 29
- User story 43
- User story 44
