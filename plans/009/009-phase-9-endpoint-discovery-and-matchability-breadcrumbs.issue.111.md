# Issue 111: PRD 009 Phase 5: Matchability Fingerprints and Outbound Breadcrumbs

> Parent PRD: [PRD 009](/home/vbfg/dev/dotnet-llm-wiki/plans/009/009-phase-9-endpoint-discovery-and-matchability-breadcrumbs.prd.md)

- Issue: [#111](https://github.com/vbfg1973/code-llm-wiki/issues/111)
- [ ] Status: open

## Notes

## Parent PRD

#106

## What to build

Emit deterministic endpoint matchability fingerprints and bounded outbound call breadcrumbs from endpoint method context, including declaration-vs-method-body context tagging and package-provenance linkability where relevant, to prepare future cross-system matching. Reference PRD 009 Solution items 5 and Implementation Decisions 15-17.

## Acceptance criteria

- [ ] Endpoint fingerprint payload is emitted with stable, deterministic fields for later matching.
- [ ] Outbound call breadcrumbs are emitted from endpoint method contexts with bounded scope.
- [ ] Dependency-like breadcrumbs include declaration-context vs method-body-context tagging.
- [ ] External dependency breadcrumbs support navigation to package-level provenance views when applicable.

## Blocked by

- Blocked by #108
- Blocked by #109
- Blocked by #110

## User stories addressed

- User story 30
- User story 31
- User story 32
- User story 33
- User story 36
- User story 39
- User story 40
- User story 43
- User story 45
- User story 47
