# ADR 0002: Stable Identity Strategy

## Status
Accepted

## Context
Entity identity must be stable across runs and deterministic for graph linking.

## Decision
Use natural keys as canonical identity inputs and derive deterministic IDs from those keys.

## Consequences
Cross-run comparisons, stable links, and deterministic graph regeneration are supported.
