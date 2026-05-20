You are a frontend engineer subagent doing a dead-code cleanup in pinder-web.

## Workspace setup (do this FIRST)

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-669 origin/main
cd /tmp/work-669
git checkout -b chore/669-drop-outcome-classes
```

Edits in `/tmp/work-669` only.

## Role spec

Read `~/.openclaw/skills/eigentakt/agents/frontend-engineer.md` for DoD-Evidence + Research-Log discipline.

## Lessons / canonical hazards

Apply: WORKSPACE-ISOLATION, NO-SCOPE-CREEP, APPROVED-WORK-IS-IMMUTABLE. Bodies in `~/.openclaw/skills/eigentakt/references/canonical-lessons.md`.

## AGENTS.md

Read `/root/projects/pinder-web/AGENTS.md`. 6-stat system (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`).

## Ticket #669 — Drop dead `classes` field from outcomeStyle

### Scope (one-file dead-code removal)
- File: `frontend/src/components/TurnResultDisplay.tsx`
- Function: `outcomeStyle(result: TurnResult)` (around line 137).
- Remove the `classes: string` field from the return type and from each of the 5 return branches.
- Confirm no remaining consumer: `grep -rn "outcome.classes\b\|outcomeStyle.*classes" frontend/src/` should return zero matches AFTER the removal.

### Acceptance criteria
- `classes` field gone from `outcomeStyle` return type and all 5 return branches.
- `grep -rn "outcome\.classes\b" frontend/src/` returns zero matches.
- `npm test` clean.
- `npm run build` clean (tsc + vite).

### Out of scope
- Any other refactor of `outcomeStyle` (don't touch label/emoji/tone/outcome_summary).
- Touching the EventBoxTone wiring landed by #652.

### PR
- Repo: `decay256/pinder-web`.
- Branch: `chore/669-drop-outcome-classes`.
- Title: `chore(#669): drop dead 'classes' field from outcomeStyle`.
- Body: `Closes #669`, one-paragraph summary, `## DoD Evidence`, `## Research Log`.
- Open as non-draft.

### Authority
Sprint `2026-05-20-eventbox`. Self-unblock per SELF-UNBLOCK-BY-DEFAULT.

Return when PR is open with green DoD.
