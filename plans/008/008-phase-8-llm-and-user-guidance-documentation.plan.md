# Plan: Phase 8 LLM and User Guidance Documentation

> Source PRD: [PRD 008](/home/vbfg/dev/dotnet-llm-wiki/plans/008/008-phase-8-llm-and-user-guidance-documentation.prd.md)

## Architectural decisions

Durable decisions that apply across all phases:

- **Guidance artifact topology**: publish exactly two guidance pages per snapshot: `guidance/human.md` and `guidance/llm-contract.md`.
- **Entry-point navigation**: repository and repository-index pages each include a compact `Guidance` section linking both guidance pages.
- **Front matter contract**: guidance pages use minimal scalar front matter with `entity_id`, `entity_type`, `repository_id`, `guidance_kind`, plus snapshot branch context in front matter only.
- **LLM contract style**: normative policy language (`MUST`/`SHOULD`) with explicit guardrails and non-goals.
- **Recipe contract**: include a bounded set of named query recipes with explicit stable anchors and standardized output sections (`Summary`, `Evidence Links`, `Gaps/Risks`, `Next Queries`).
- **Evidence policy**: material claims require wiki-link evidence; uncited statements are allowed only in `Gaps/Risks` and must be marked uncertain.
- **Link-style policy**: use wiki links for internal prose references; use markdown links for deep anchors and table-cell links.
- **Capability boundary disclosure**: include a compact capability matrix in LLM guidance with explicit available/deferred status linked to backlog truth.
- **Validation posture**: guidance contracts are test-enforced and deterministic across reruns.

---

## Phase 1: Publish Guidance Pages and Entry Links

**User stories**: 1, 4, 5, 6, 7, 18, 25, 30, 40

### What to build

Publish human and LLM guidance pages as first-class wiki artifacts in each snapshot and wire discoverability from repository and repository-index entry points. Ensure page identity and basic metadata contracts are in place.

### Acceptance criteria

- [ ] Both guidance pages are generated in every wiki snapshot at fixed paths.
- [ ] Guidance pages carry required minimal front matter and branch context in front matter only.
- [ ] Repository and repository-index pages each include a compact `Guidance` section linking both pages.
- [ ] Deterministic output behavior is preserved across reruns.

---

## Phase 2: Implement Normative LLM Contract and Guardrails

**User stories**: 2, 3, 10, 11, 12, 13, 14, 15, 27, 28, 33, 34, 39

### What to build

Populate the LLM guidance page with normative operating rules, explicit link/evidence policies, response template requirements, and non-goal guardrails to constrain behavior and avoid unsupported claims.

### Acceptance criteria

- [ ] LLM page contains normative policy sections using explicit `MUST`/`SHOULD` language.
- [ ] Required response-template policy is present and unambiguous.
- [ ] Link-style and evidence policies are explicit and consistent with existing parse-safety decisions.
- [ ] Guardrails and prohibited behaviors are explicitly documented.

---

## Phase 3: Add Named Recipes and Capability Matrix

**User stories**: 8, 9, 16, 17, 31, 32, 38

### What to build

Add a bounded set of named query recipes with stable anchors and concise step guidance, alongside a compact capability matrix that distinguishes currently available vs deferred capabilities and links to backlog records.

### Acceptance criteria

- [ ] Named recipes are present, bounded, and task-oriented.
- [ ] Recipe entry points have explicit stable anchors suitable for deep linking.
- [ ] Capability matrix clearly distinguishes available and deferred capabilities.
- [ ] Capability matrix references backlog truth for traceability.

---

## Phase 4: Harden Contract Invariants and Determinism

**User stories**: 19, 20, 21, 22, 23, 24, 26, 29, 35, 36, 37

### What to build

Introduce and enforce guidance-specific validation and regression tests that lock required sections, anchors, front matter schema, and entry-link wiring while preserving deterministic rendering and concise human readability.

### Acceptance criteria

- [ ] Automated tests enforce guidance page presence and required front matter keys.
- [ ] Automated tests enforce required anchors/sections and entry links from repository/index pages.
- [ ] Golden/snapshot coverage reflects intentional guidance additions only.
- [ ] Repeated runs produce stable guidance outputs with no nondeterministic drift.
