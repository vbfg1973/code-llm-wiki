# ADR 0001: Ontology Versioning Governance

## Status
Accepted

## Context
The ontology is a strict contract for ingestion, query, and wiki generation. Breaking predicate/entity changes must be explicit.

## Decision
The ontology is canonical in machine-readable form and follows semantic versioning:
- Major: breaking contract changes
- Minor: additive non-breaking changes
- Patch: clarifications and metadata-only fixes

Breaking changes require a migration note.

## Consequences
Consumers can reason about compatibility and CI can enforce schema evolution discipline.
