# Issue 108: PRD 009 Phase 2: Minimal API Endpoint Slice

> Parent PRD: [PRD 009](/home/vbfg/dev/dotnet-llm-wiki/plans/009/009-phase-9-endpoint-discovery-and-matchability-breadcrumbs.prd.md)

- Issue: [#108](https://github.com/vbfg1973/code-llm-wiki/issues/108)
- [ ] Status: open

## Notes

## Parent PRD

#106

## What to build

Add the minimal API vertical slice end-to-end: detect `Map*` semantics, compose route-group prefixes, apply shared endpoint identity/route normalization contracts, and publish minimal API endpoints in the standard family-grouped wiki output. Reference PRD 009 Solution items 1, 2, 3, 6 and Implementation Decisions 4, 5, 11, 18-20.

## Acceptance criteria

- [ ] Minimal API endpoint detections are ingested with canonical endpoint identity and route normalization.
- [ ] Group-prefix route composition is reflected in published endpoint route values.
- [ ] Minimal API endpoints appear in family-grouped index/navigation output.
- [ ] Fixture-based tests cover grouped mappings and deterministic rendering.

## Blocked by

- Blocked by #107

## User stories addressed

- User story 11
- User story 16
- User story 17
- User story 18
- User story 23
- User story 25
- User story 41
- User story 42
- User story 46
