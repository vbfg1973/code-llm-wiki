# Issue 057: PRD 004 Phase 3: Publication Invariant Gate for Scoped Link Rules Vertical Slice

> Parent PRD: [PRD 004](./004-phase-4-wiki-navigation-link-consistency.prd.md)

- Issue: [#57](https://github.com/vbfg1973/code-llm-wiki/issues/57)
- [ ] Status: open
- [ ] Completion date:

## Notes

## Parent PRD

#54

## What to build

Add a scoped publication-time invariant validator for the two PRD 004 sections and integrate it into artifact publication so runs fail when resolvable targets are emitted as non-link bullets in those sections. Ensure invalid runs are not promoted as `latest` and violations are reported clearly. Reference PRD 004 sections: Solution item 4; Implementation Decisions 4-7, 10-12.

## Acceptance criteria

- [ ] Publication invariant validator exists for namespace contained-type and file method sections and reports concrete violations.
- [ ] Artifact publication fails when scoped link invariants are violated.
- [ ] Invalid runs are not promoted to `latest` when scoped link invariants fail.
- [ ] Publisher/validator tests cover pass/fail paths, including failure messaging and promotion behavior.
- [ ] Golden/snapshot tests are updated only for intentional output deltas introduced by scoped link hardening.

## Blocked by

- Blocked by #55
- Blocked by #56

## User stories addressed

- User story 6
- User story 7
- User story 9
- User story 10
- User story 11
- User story 15
- User story 16
- User story 18
- User story 19
- User story 20
- User story 23
- User story 24
