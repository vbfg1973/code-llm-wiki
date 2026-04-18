# Plan: Phase 4 Wiki Navigation Link Consistency Hardening

> Source PRD: `plans/004/004-phase-4-wiki-navigation-link-consistency.prd.md`

## Execution checklist

- [x] Phase 1: Namespace Contained-Type Linking Slice — completion date: 2026-04-18
- [x] Phase 2: File Declared-Method Linking Slice — completion date: 2026-04-18
- [ ] Phase 3: Publication Invariant Gate Slice — completion date:

---

## Architectural decisions

Durable decisions that apply across all phases:

- **Scope boundary**: Only two wiki sections are in scope for link hardening:
  - Namespace page `Contained Types`
  - File page `Declared Symbols -> Methods`
- **Navigation invariant (scoped)**: If a target entity is resolvable and already has a page family, the entry must render as a wiki link.
- **Alias policy**: Keep existing human-readable alias format and wrap only the navigable part as a link.
- **Unresolved policy**: Unresolved targets may remain non-link text with explicit unresolved semantics.
- **Validation policy**: Publication must fail for scoped invariant violations; invalid outputs must not be promoted as `latest`.
- **Contract boundary**: No graph/query/schema/front-matter contract changes.
- **Page-family boundary**: No new member page family in this phase.
- **Determinism policy**: Maintain deterministic ordering and stable output contracts.

---

## Phase 1: Namespace Contained-Type Linking Slice

**User stories**: 1, 3, 4, 5, 8, 9, 10, 11, 12, 17, 21, 22, 25

### What to build

Implement end-to-end namespace page rendering behavior where contained type entries are emitted as wiki links for resolvable types while preserving the current human-readable format. Validate this behavior through focused renderer-level tests and ensure output remains deterministic.

### Acceptance criteria

- [ ] Namespace page `Contained Types` entries render as wiki links for resolvable type targets.
- [ ] Existing alias/readability format is preserved (type display and kind annotation remain human-readable).
- [ ] Focused tests verify linked rendering and deterministic ordering for namespace contained-type entries.

---

## Phase 2: File Declared-Method Linking Slice

**User stories**: 2, 3, 4, 5, 8, 9, 10, 11, 13, 14, 17, 21, 22, 25

### What to build

Implement end-to-end file page rendering behavior where declared method entries are emitted as wiki links for resolvable method targets while preserving current human-readable formatting. Add targeted regression protection for method-link rendering expectations in type-context method listings.

### Acceptance criteria

- [ ] File page `Declared Symbols -> Methods` entries render as wiki links for resolvable method targets.
- [ ] Existing alias/readability format is preserved (method alias plus kind annotation remains human-readable).
- [ ] Focused tests verify linked rendering and deterministic ordering for file-method entries.
- [ ] Regression test explicitly guards against non-linked method listings in type method sections.

---

## Phase 3: Publication Invariant Gate Slice

**User stories**: 6, 7, 9, 10, 11, 15, 16, 18, 19, 20, 23, 24

### What to build

Add a scoped publication-time invariant validator that inspects rendered wiki output and fails publication if either scoped section emits non-link bullets for resolvable targets. Integrate this into artifact publication flow so invalid outputs are not promoted and failures are diagnostically actionable.

### Acceptance criteria

- [ ] Publication invariant validator exists for the two scoped sections and reports concrete violations.
- [ ] Artifact publication fails when scoped link invariants are violated.
- [ ] Invalid runs are not promoted to `latest` when scoped link invariants fail.
- [ ] Publisher/validator tests cover pass/fail paths, including failure messaging and promotion behavior.
- [ ] Golden/snapshot tests are updated only for intentional output deltas introduced by link hardening.
