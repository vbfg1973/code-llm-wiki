# ADR 0006: Declaration Identity and Ordering

## Status
Accepted

## Context
PRD 002 introduces namespace/type/member declarations. Identity and ordering must be deterministic so wiki paths, indexes, and query projections remain stable across repeated runs over the same HEAD snapshot.

## Decision
Declaration contracts define deterministic identity and ordering rules:
- Natural-key builders are explicit for namespace, type, and member declarations.
- Ordering uses a deterministic sort key composed from namespace, display name, path, and stable id.
- Rules are implemented in query-level contract helpers and covered by tests.

## Consequences
Later PRD 002 phases can add declaration ingestion and rendering behavior without revisiting basic determinism guarantees.
