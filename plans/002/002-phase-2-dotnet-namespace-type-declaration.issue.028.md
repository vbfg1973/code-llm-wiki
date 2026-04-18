# Issue 028: PRD 002 Phase 4: Partial, Nested, and Generic Identity Hardening

> Parent PRD: [PRD 002](./002-phase-2-dotnet-namespace-type-declaration.prd.md)

- Issue: [#28](https://github.com/vbfg1973/code-llm-wiki/issues/28)
- [x] Status: complete
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#24

## What to build

Harden symbol identity behavior for partial declarations, nested declarations, and generic signatures, ensuring one canonical type representation per logical symbol with readable paths/titles and conditional nested metadata. Reference PRD 002 identity decisions and plan 'Phase 4: Partial, Nested, and Generic Identity Hardening'.

## Acceptance criteria

- [x] Partial declarations resolve to one canonical type entity/page with multiple declaration locations.
- [x] Nested types expose conditional scalar metadata (`is_nested_type`, conditional declaring type ID).
- [x] Generic identity includes deterministic arity/parameter/constraint metadata without path ambiguity.
- [x] Path/title output stays human-readable with deterministic suffixing only when required.

## Blocked by

- Blocked by #27

## User stories addressed

- User story 3
- User story 16
- User story 17
- User story 18
- User story 23
- User story 24
- User story 36
