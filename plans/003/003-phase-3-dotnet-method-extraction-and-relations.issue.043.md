# Issue 043: PRD 003 Phase 2: Method Declaration and Method Page Vertical Slice

> Parent PRD: [PRD 003](./003-phase-3-dotnet-method-extraction-and-relations.prd.md)

- Issue: [#43](https://github.com/vbfg1973/code-llm-wiki/issues/43)
- [x] Status: completed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#41

## What to build

Deliver the first complete method vertical slice: ingest method/constructor declarations as first-class entities using canonical identity, include declaration provenance, and publish a dedicated method page family linked from owning type pages with deterministic, human-readable contracts. Reference PRD 003 sections: Solution items 1-3, 9; Implementation Decisions 1-5, 15-16, 19.

## Acceptance criteria

- [x] Named type-level methods and constructors are ingested as first-class method entities with canonical deterministic identity.
- [x] Method entities include declarations without bodies (interface/abstract/extern) and declaration file/location provenance.
- [x] Method pages render deterministic signature-oriented summaries and are linked from owning type pages.
- [x] Method front matter remains minimal scalar and body output remains ID-light/human-readable.

## Blocked by

- Blocked by #42

## User stories addressed

- User story 1
- User story 2
- User story 4
- User story 5
- User story 13
- User story 15
- User story 16
- User story 17
- User story 18
- User story 19
- User story 20
- User story 44
