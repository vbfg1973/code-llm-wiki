# ADR 0005: CLI Exit Semantics

## Status
Accepted

## Context
CI requires strict signaling for success, partial success, and failure.

## Decision
Exit semantics:
- Success: `0`
- Completed with diagnostics: non-zero by default
- Failure: non-zero

A dedicated allow-partial policy can map partial-success to `0` when explicitly configured.

## Consequences
Pipelines are strict by default, with explicit opt-in for exploratory or local workflows.
