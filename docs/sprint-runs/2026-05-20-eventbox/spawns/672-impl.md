You are a frontend engineer subagent fixing a P2 bug in pinder-web.

## Workspace setup (do this FIRST)

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-672 origin/main
cd /tmp/work-672
git checkout -b fix/672-triple-hit-summary
```

Edits in `/tmp/work-672` only.

## Role spec

Read `~/.openclaw/skills/eigentakt/agents/frontend-engineer.md` for DoD-Evidence + Research-Log discipline.

## Lessons / canonical hazards

Apply: WORKSPACE-ISOLATION, REGRESSION-TESTS-ON-BUGS, DOCS-FOLLOW-CODE, NO-SCOPE-CREEP, APPROVED-WORK-IS-IMMUTABLE. Bodies in `~/.openclaw/skills/eigentakt/references/canonical-lessons.md`.

## AGENTS.md

Read `/root/projects/pinder-web/AGENTS.md`. 6-stat system (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`).

## Ticket #672 — triple_hit EventBox missing summary prop + invariant enforcement

### Scope (this is a bug + invariant enforcement, both in scope per the ticket body)

**Fix (primary):**
- `frontend/src/components/TurnResultDisplay.tsx` (~lines 668–684): add `summary={tVariant('triple_hit', turnNumber)}` to the `triple_hit` `EventBox` call site, matching the pattern used by other annotation `EventBox` invocations.
- If the i18n key for `triple_hit` summary doesn't exist under `tVariant`, add it in the appropriate i18n yaml (likely `data/i18n/en/events.yaml` or `ui-turn-result.yaml` — pick whichever the other event-kind summaries live in; do NOT split locations).

**Invariant enforcement (follow-up recommended in ticket body — DO this in same PR per the orchestrator's call):**
- `EventBoxProps`: change `summary?: string` → `summary: string` (required).
- TypeScript build will then surface any other call sites missing `summary`. Fix them by passing a sensible value or, if intentional empty, explicitly pass `summary=""` (but only if the design genuinely supports it — prefer giving it a real string).
- Add a jsdom render regression test that:
  1. Renders `EventBox` across all `kind` values it supports (enumerate from the union type).
  2. Asserts each rendered output has a non-empty summary node visible.
  3. Use the existing `@testing-library/react` setup.
  Place under `frontend/src/components/eventbox/EventBox.regression.test.tsx` (new file) or the existing `EventBox.test.tsx` if present.

### Acceptance criteria
- `triple_hit` row shows two-row layout (title + summary) like every other annotation event — verified visually via the new regression test.
- `EventBoxProps.summary` is required at the type level.
- New regression test covers every `kind` in the union.
- `npm test` clean.
- `npm run build` clean (TypeScript surfaces all violations).
- No unrelated refactors of `EventBox` internals.

### Out of scope
- Restyling of `EventBox` or `RollEventBox`.
- Other event-kind summary content changes beyond what's needed to keep `tsc` green.

### PR
- Repo: `decay256/pinder-web`.
- Branch: `fix/672-triple-hit-summary`.
- Title: `fix(#672): add triple_hit summary prop + make EventBoxProps.summary required`.
- Body: `Closes #672`, one-paragraph summary, list of files changed, `## DoD Evidence` (test + build output), `## Research Log`.
- Open as non-draft.

### Authority
Sprint `2026-05-20-eventbox`. Self-unblock per SELF-UNBLOCK-BY-DEFAULT.

Return when PR is open with green DoD.
