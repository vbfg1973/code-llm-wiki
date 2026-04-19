# Issue 120: PRD 010 Phase 1: Diagnostics Taxonomy and Stage Telemetry Baseline

> Parent PRD: [PRD 010](/home/vbfg/dev/dotnet-llm-wiki/plans/010/010-phase-10-semantic-resolution-quality-and-ingestion-observability.prd.md)

- Issue: [#120](https://github.com/vbfg1973/code-llm-wiki/issues/120)
- [x] Status: closed
- [x] Completion date: 2026-04-19

## Notes

- Completed in [PR #126](https://github.com/vbfg1973/code-llm-wiki/pull/126) and merged to `develop`.

## Parent PRD

#119

## What to build

Deliver the baseline quality-observability slice: introduce cause-level method-call resolution diagnostics and live stage timing emission to stderr using stable stage identifiers, while preserving compatibility summaries. Reference PRD 010 Solution items 4 and 6, and Implementation Decisions 5, 6, 11, 12, and 13.

## Acceptance criteria

- [x] Call-resolution diagnostics are emitted with explicit cause-level subcodes.
- [x] Aggregate `method:call:resolution:failed` compatibility is preserved in summary outputs.
- [x] Stage timing events are emitted to stderr at stage boundaries using approved stage identifiers.
- [x] Stage timing output is deterministic and parse-safe across reruns.

## Blocked by

None - can start immediately.

## User stories addressed

- User story 6
- User story 7
- User story 8
- User story 13
- User story 14
- User story 15
- User story 16
- User story 25
- User story 30
- User story 41
- User story 42
- User story 43
- User story 45
