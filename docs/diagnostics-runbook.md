# Ingestion Diagnostics Runbook

Purpose: define run status meanings, diagnostic code contracts, likely causes, and first-response actions for `.NET` ingestion runs.

## Run Statuses

- `Succeeded`: run completed with no diagnostics.
- `SucceededWithDiagnostics`: run completed and published artifacts, with warnings/non-fatal diagnostics.
- `FailedQualityGate`: run completed analysis but failed the unresolved-call quality policy.
- `Failed`: run could not complete required ingestion stages (for example invalid ontology or fatal publish failure).

## Quality Gate

- Gate ID: `quality:unresolved-call-ratio`
- Metric: `unresolved_call_failures / total_call_resolution_attempts`
- Default threshold: `0.25`
- Failure behavior:
  - status: `FailedQualityGate`
  - exit code: `3`
  - CLI stderr includes measured ratio and threshold.
- Non-gating diagnostics (for this phase): `project:discovery:fallback`.

## Stage Telemetry Contract

- Emitted to `stderr` during execution.
- Line format:
  - start: `ingest_stage|event=start|stage=<stage_id>`
  - end: `ingest_stage|event=end|stage=<stage_id>|elapsed_ms=<n>|...counters`
- Stable stage IDs:
  - `project_discovery`
  - `source_snapshot`
  - `declaration_scan`
  - `semantic_call_graph`
  - `endpoint_extraction`
  - `query_projection`
  - `wiki_render`
  - `graphml_serialize`

## Diagnostic Families

### Call Resolution

- `method:call:resolution:failed`
  - Meaning: invocation target could not be resolved to a method declaration.
  - Probable causes: incomplete semantic context, unknown symbol, missing references.
  - Actions: inspect paired specific code and invocation location; verify project references/build graph.

- `method:call:resolution:failed:symbol-unresolved`
  - Meaning: Roslyn symbol lookup failed for invocation.
  - Probable causes: unresolved identifiers, generated code not available, parse/binding gaps.
  - Actions: confirm source snapshot completeness and owning-project semantic context.

- `method:call:resolution:failed:missing-containing-type`
  - Meaning: invocation symbol existed but containing type was missing.
  - Probable causes: partial symbol metadata, edge-case language constructs.
  - Actions: inspect invocation syntax and semantic model fallback path.

- `method:call:internal-target-unmatched`
  - Meaning: invocation bound to an internal type but no unique method declaration matched.
  - Probable causes: overload ambiguity, signature normalization mismatch, duplicate declarations.
  - Actions: inspect candidate methods for arity/signature conflicts; review method identity normalization.

### Type Resolution

- `type:resolution:fallback`
  - Meaning: declaration-level type link could not resolve to internal or external stub with certainty.
  - Probable causes: missing/ambiguous type names, unsupported syntax edge cases.
  - Actions: inspect normalized type text and imports/aliases; confirm source declarations are indexed.

### Project Discovery / Package Resolution

- `project:discovery:msbuild`
  - Meaning: project metadata was discovered through MSBuild evaluation.
  - Actions: informational.

- `project:discovery:fallback`
  - Meaning: MSBuild discovery degraded and fallback parsing was used.
  - Probable causes: environment-specific MSBuild issues, incomplete SDK context.
  - Actions: inspect machine SDK/tooling state; compare fallback outputs with expected project metadata.
  - Gate impact: warning-level, non-gating in this phase.

- `package:resolved:available`
  - Meaning: resolved package information was available from assets data.
  - Actions: informational.

- `package:resolved:not-available`
  - Meaning: resolved package information could not be loaded.
  - Probable causes: missing/invalid `project.assets.json`, restore not run.
  - Actions: run restore for target project and re-run ingestion.

### Method Relationship

- `method:relationship:override:unresolved`
  - Meaning: override target could not be mapped to a source declaration ID.
  - Probable causes: source location mismatch, unavailable declaration in semantic context.
  - Actions: verify project-scoped semantic context and declaration location mapping.

## Run Artifact Expectations

- `manifest.json` always includes:
  - `Status`, `ExitCode`, `DiagnosticsSummary`
- When quality gate is evaluated, manifest includes:
  - `QualityGate.GateId`
  - `QualityGate.Passed`
  - `QualityGate.UnresolvedCallFailures`
  - `QualityGate.TotalCallResolutionAttempts`
  - `QualityGate.UnresolvedCallRatio`
  - `QualityGate.Threshold`

## Operator Workflow

1. Check `Status` and `ExitCode` in `manifest.json`.
2. If `FailedQualityGate`, capture ratio/threshold from stderr and manifest.
3. Review `DiagnosticsSummary` for dominant code families.
4. Use stage telemetry to isolate slow stages before deeper tuning.
5. Re-run after fixes; compare status, gate values, and diagnostics family counts.
