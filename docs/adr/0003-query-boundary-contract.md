# ADR 0003: Query Boundary Contract

## Status
Accepted

## Context
Wiki generation and future MCP clients must not be coupled to graph internals.

## Decision
Consumers access analysis data through a formal query/read contract, not direct graph internals.

## Consequences
Internal graph implementations can evolve without forcing consumer rewrites.
