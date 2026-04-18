# Issue 030: PRD 002 Phase 6: Type Resolution Fallback and External Stub References

> Parent PRD: [PRD 002](./002-phase-2-dotnet-namespace-type-declaration.prd.md)

- Issue: [#30](https://github.com/vbfg1973/code-llm-wiki/issues/30)
- [ ] Status: open
- [ ] Completion date: 

## Notes

## Parent PRD

#24

## What to build

Implement robust type-resolution behavior for member declared types and declaration relationships: prefer resolved symbol identity, fallback to source type text when unresolved, and surface explicit resolution status. Capture external referenced types as dependency stubs without dedicated page families. Reference PRD 002 degradation and external-type decisions, and plan 'Phase 6: Type Resolution Fallback and External Stub References'.

## Acceptance criteria

- [ ] Declared type links use resolved symbol identity when available.
- [ ] Unresolved declared types retain source-text fallback with explicit resolution status.
- [ ] External referenced types are captured and rendered as dependency stubs without external-type pages.
- [ ] Partial semantic failure still produces usable output with explicit diagnostics/provenance indicators.

## Blocked by

- Blocked by #29

## User stories addressed

- User story 30
- User story 31
- User story 32
- User story 33
- User story 43
