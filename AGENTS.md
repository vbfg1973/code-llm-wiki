# Agent Orientation Guide

Use this file to rehydrate project context quickly for any new agent/session.

## Start Here (in order)

1. Read the feature backlog:
   - `/plans/BACKLOG.md`
2. Read planning standards:
   - `/plans/README.md`
3. Read operator diagnostics runbook:
   - `/docs/diagnostics-runbook.md`
4. Read implemented PRDs and plans:
   - `/plans/001/001-phase-1-dotnet-project-structure.prd.md`
   - `/plans/001/001-phase-1-dotnet-project-structure.plan.md`
   - `/plans/002/002-phase-2-dotnet-namespace-type-declaration.prd.md`
   - `/plans/002/002-phase-2-dotnet-namespace-type-declaration.plan.md`
   - `/plans/003/003-phase-3-dotnet-method-extraction-and-relations.prd.md`
   - `/plans/003/003-phase-3-dotnet-method-extraction-and-relations.plan.md`
5. If implementing a capability, read its linked issue file(s):
   - `/plans/{prd_id}/{prd_id}-{name}.issue.{issue_id}.md`

## Current Program State

- Source of truth for capability and delivery status is `/plans/BACKLOG.md`.
- Source of truth for PRD 010 progress and phase checklists is:
  - `/plans/010/010-phase-10-semantic-resolution-quality-and-ingestion-observability.plan.md`
- Source of truth for operator diagnostics/status behavior is:
  - `/docs/diagnostics-runbook.md`

Canonical feature backlog:
- `/plans/BACKLOG.md`

## Delivery Workflow (must follow)

- Branching: `develop` -> `feature/{plan_id}/{issue_id}`
- For each issue:
  - implement with TDD and interface-first discipline
  - run tests and ensure green
  - push branch and open PR into `develop`
  - run independent review against issue acceptance criteria
  - merge PR
  - close GitHub issue
  - refresh local `develop` before next issue

## Documentation Discipline

- Keep backlog, PRD/plan/issue checklists, and completion dates up to date as work progresses.
- Keep human readability primary in wiki outputs; keep IDs in front matter/index, not visible body content.
- When scope/priority changes, update `/plans/BACKLOG.md` first, then link to concrete PRD/issue artifacts.
