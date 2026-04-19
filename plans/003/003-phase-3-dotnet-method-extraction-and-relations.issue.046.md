# Issue 046: PRD 003 Phase 5: Read-Write Data-Flow and Type Count Scalars Vertical Slice

> Parent PRD: [PRD 003](./003-phase-3-dotnet-method-extraction-and-relations.prd.md)

- Issue: [#46](https://github.com/vbfg1973/code-llm-wiki/issues/46)
- [x] Status: complete
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#41

## What to build

Add internal property/field read-write data-flow extraction from methods and publish POCO-oriented summary outputs: explicit per-property read/write counts and method backlink lists on owning type pages, plus type-level structural count scalars for Dataview usage. Reference PRD 003 sections: Solution items 6, 10; Implementation Decisions 9, 17-18.

## Acceptance criteria

- [x] Internal data-flow edges are captured for `reads_property`, `writes_property`, `reads_field`, and `writes_field`.
- [x] Type pages render per-property read/write counts with deterministic reader/writer method link lists, including explicit zero counts.
- [x] Type front matter publishes approved structural count scalars (`constructor_count`, `method_count`, `property_count`, `field_count`, `enum_member_count`, `record_parameter_count`, `behavioral_method_count`).
- [x] Method pages render deterministic `Reads`/`Writes` sections for internal targets.

## Blocked by

- Blocked by #45

## User stories addressed

- User story 23
- User story 24
- User story 25
- User story 26
- User story 27
- User story 28
- User story 42
- User story 43
- User story 59
