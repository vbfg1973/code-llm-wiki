# Issue 123: PRD 010 Phase 4: Quality Gate Policy and Run Status Integration

> Parent PRD: [PRD 010](/home/vbfg/dev/dotnet-llm-wiki/plans/010/010-phase-10-semantic-resolution-quality-and-ingestion-observability.prd.md)

- Issue: [#123](https://github.com/vbfg1973/code-llm-wiki/issues/123)
- [ ] Status: open

## Notes

## Parent PRD

#119

## What to build

Implement unresolved-call-ratio quality gate policy with global default threshold and integrate outcomes into run status, exit behavior, and manifest evidence while keeping non-gating warning diagnostics explicit. Reference PRD 010 Solution item 3 and Implementation Decisions 7, 8, and 17.

## Acceptance criteria

- [ ] Quality policy computes unresolved-call ratio and evaluates against a global default threshold.
- [ ] Gate pass/fail outcomes are reflected in run status and CLI exit behavior.
- [ ] Failure output includes measured ratio and threshold values.
- [ ] Project discovery fallback remains warning-level and non-gating in this phase.

## Blocked by

- Blocked by #122

## User stories addressed

- User story 9
- User story 10
- User story 11
- User story 12
- User story 24
- User story 28
- User story 31
- User story 32
- User story 37
- User story 38
