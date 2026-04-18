# Issue 045: PRD 003 Phase 4: Calls, External Usage, and Extension Method Vertical Slice

> Parent PRD: [PRD 003](./003-phase-3-dotnet-method-extraction-and-relations.prd.md)

- Issue: [#45](https://github.com/vbfg1973/code-llm-wiki/issues/45)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#41

## What to build

Deliver method call relationship extraction for internal callers, including internal and external callee handling, extension method call resolution, and shallow external dependency usage projection at type and assembly level. Reference PRD 003 sections: Solution items 4, 5, 7, 8; Implementation Decisions 6-8, 10-13.

## Acceptance criteria

- [ ] `calls` edges are created for internal method callers with semantically resolved targets where possible.
- [ ] External invocation usage is represented at external type and external assembly granularity (no deep external method pages).
- [ ] Extension methods are captured, flagged, resolved from extension-call syntax, and linked to extended internal types.
- [ ] Method pages render deterministic `Calls`/`Called By` sections and degradation diagnostics/provenance when semantic binding fails.

## Blocked by

- Blocked by #44

## User stories addressed

- User story 9
- User story 10
- User story 11
- User story 12
- User story 14
- User story 21
- User story 29
- User story 30
- User story 31
- User story 32
- User story 33
- User story 58
