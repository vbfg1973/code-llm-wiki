# Issue 016: Phase 8: Human-Readable Obsidian Page Contracts

> Parent PRD: [PRD 001](./001-phase-1-dotnet-project-structure.prd.md)

- Issue: [#16](https://github.com/vbfg1973/code-llm-wiki/issues/16)
- [x] Status: closed
- [x] Completion date: 2026-04-18

## Notes

## Parent PRD

#1

## What to build

Implement the approved human-readable Obsidian page contract for generated wiki artifacts: no IDs in filenames, mirrored file layout for file pages, Obsidian wikilinks for internal references, and a canonical repository index page for ID-to-page lookup.

Reference sections: PRD Addendum 2026-04-18 and plan "Phase 8: Human-Readable Obsidian Page Contracts".

## Acceptance criteria

- [ ] File pages are generated under mirrored repository-relative paths (for example `files/src/App/Program.cs.md`)
- [ ] Solution/project/package pages use idiomatic human-readable names, with deterministic non-ID disambiguation only when collisions occur
- [ ] Internal links are emitted as Obsidian wikilinks
- [ ] `index/repository-index.md` is generated with per-entity tables and `entity_id -> page_link` mappings

## Blocked by

None - can start immediately

## User stories addressed

- User story 19
- User story 20
- User story 21
- User story 22
- User story 23
- User story 34
