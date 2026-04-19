# Issue 110: PRD 009 Phase 4: gRPC Endpoint Slice and Partial-Resolution Semantics

> Parent PRD: [PRD 009](/home/vbfg/dev/dotnet-llm-wiki/plans/009/009-phase-9-endpoint-discovery-and-matchability-breadcrumbs.prd.md)

- Issue: [#110](https://github.com/vbfg1973/code-llm-wiki/issues/110)
- [ ] Status: open

## Notes

## Parent PRD

#106

## What to build

Add gRPC endpoint discovery from registration/service semantics and implement shared partial/unresolved fallback semantics with confidence and reason-coded diagnostics that are retained in output instead of dropped. Reference PRD 009 Solution items 1, 3, 6 and Implementation Decisions 8, 13, 18-20.

## Acceptance criteria

- [ ] gRPC endpoints are discovered from service registration semantics and projected into standard endpoint contracts.
- [ ] Partial/unresolved detections are retained and rendered with explicit reason codes.
- [ ] Confidence values are assigned consistently across resolved and unresolved states.
- [ ] Diagnostics are queryable/countable by endpoint family and reason.

## Blocked by

- Blocked by #107

## User stories addressed

- User story 6
- User story 7
- User story 14
- User story 34
- User story 37
- User story 38
