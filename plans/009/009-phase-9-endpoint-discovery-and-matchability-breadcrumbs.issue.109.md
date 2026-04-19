# Issue 109: PRD 009 Phase 3: Message Handler and CLI Endpoint Slice

> Parent PRD: [PRD 009](/home/vbfg/dev/dotnet-llm-wiki/plans/009/009-phase-9-endpoint-discovery-and-matchability-breadcrumbs.prd.md)

- Issue: [#109](https://github.com/vbfg1973/code-llm-wiki/issues/109)
- [ ] Status: open

## Notes

## Parent PRD

#106

## What to build

Implement endpoint discovery for interface-pattern message handlers and semantic CLI command registration, including rule-catalog-driven custom handler interfaces, and publish both families through the same endpoint graph/wiki contracts. Reference PRD 009 Solution items 1, 2, 6, 7 and Implementation Decisions 3, 12, 14, 18-20.

## Acceptance criteria

- [ ] Message handlers are detected through interface-pattern rules (including custom interfaces).
- [ ] CLI endpoints are detected from semantic registration patterns without generic `Main` heuristics.
- [ ] Both families publish endpoint pages and are linked from declaring methods/types.
- [ ] Tests verify custom handler-interface pattern behavior and deterministic output.

## Blocked by

- Blocked by #107

## User stories addressed

- User story 12
- User story 13
- User story 15
- User story 24
- User story 25
- User story 44
- User story 48
