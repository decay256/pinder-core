You are a frontend engineer subagent migrating consumers of `RollCheckResult.final_verdict` / `final_tier` (engine fields landed in pinder-core PR #972 / issue #927).

## Workspace setup (do this FIRST)

```bash
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-973 origin/main
cd /tmp/work-973
git checkout -b chore/973-final-verdict-consumer-migration
```

Edits in `/tmp/work-973` only. Also have read access to `/root/projects/pinder-core` for grep.

## Role spec

Read `~/.openclaw/skills/eigentakt/agents/frontend-engineer.md` for DoD-Evidence + Research-Log discipline.

## Lessons / canonical hazards

Apply: WORKSPACE-ISOLATION, AGENT-LOG-EVERYTHING, DOCS-FOLLOW-CODE, NO-SCOPE-CREEP, REGRESSION-TESTS-ON-BUGS, APPROVED-WORK-IS-IMMUTABLE. Project lessons in `/root/projects/pinder-web/LESSONS_LEARNED.md` and `/root/projects/pinder-core/LESSONS_LEARNED.md`. Canonical bodies in `~/.openclaw/skills/eigentakt/references/canonical-lessons.md`.

## AGENTS.md

Read `/root/projects/pinder-web/AGENTS.md` and `/root/projects/pinder-core/AGENTS.md`. 6-stat system (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SelfAwareness`).

## Ticket #973 — Consume RollCheckResult.final_verdict / final_tier in consumers

### Background
pinder-core PR #972 surfaced `final_verdict` (`"success"` | `"miss"`) and `final_tier` (FailureTier enum-as-string) on the `RollCheckResult` wire DTO, serialised snake-case. These are the post-shadow-corruption single-source-of-truth fields. Frontend / replay / simulator currently re-derive demotion via the predicate `shadow_check.is_miss && shadow_check.overlay_applied && roll.is_success` (with `shadow_check.tier` used as the demoted tier). That derivation is what this ticket replaces.

### In-scope migration sites

**Primary (pinder-web frontend):**
- `frontend/src/types.ts` — add `final_verdict: 'success' | 'miss'` and `final_tier: FailureTier` (required, not optional — pinder-core always emits them) to the `RollResult` interface, near the existing `failure_tier` field. Document with a one-line comment referencing #927/#973.
- `frontend/src/components/eventbox/collapsedHeader.ts :: deriveOptionRollHeader` — replace the compound `shadowCheck && shadowCheck.is_miss && shadowCheck.overlay_applied && roll.is_success` derivation with reads of `roll.final_verdict` (to detect demotion via `final_verdict === 'miss' && is_success === true`) and `roll.final_tier` (as the demoted tier). The `ShadowCheckSummary` parameter stays but is no longer the source of truth — strip the derivation predicate.
- Update the `OptionRollSummary` interface in the same file to include `final_verdict: 'success' | 'miss'` and `final_tier: FailureTier`.
- `frontend/src/components/eventbox/collapsedHeader.test.ts` — update existing tests to set `final_verdict` / `final_tier` on the fixture rolls. Add ONE new test case verifying the Nat-20-demoted-by-shadow-Catastrophe scenario reads from `final_verdict` / `final_tier` (NOT from the shadow_check derivation).

**Secondary (pinder-web replay tool):**
- `frontend/src/lib/replayProjection.ts` — grep for any derivation that AND-combines `shadow_check.is_miss && shadow_check.overlay_applied`. If found, replace with `roll.final_verdict` / `roll.final_tier` reads. If not found (the file appears to just pass through the shadow_check shape), simply ensure `final_verdict` / `final_tier` are surfaced in the projected output. Note the outcome in your Research Log.

**Tertiary (pinder-core session-runner simulator):**
- Grep `/root/projects/pinder-core/session-runner/` for derivations of post-shadow-corruption demotion that use `IsSuccess` AND `OverlayApplied`. If you find none, the session-runner already displays per-check states independently (no migration needed). Note "no migration needed in session-runner" in the PR body. If you DO find a derivation, file a follow-up issue rather than extending this PR to cross-repo (keep scope to pinder-web).

### Acceptance criteria
- `final_verdict` / `final_tier` typed on `RollResult` and `OptionRollSummary` interfaces.
- `deriveOptionRollHeader` reads `final_verdict` / `final_tier` instead of deriving from `shadow_check.is_miss && shadow_check.overlay_applied && roll.is_success`.
- `grep -rn "is_miss.*overlay_applied\|overlay_applied.*is_miss" frontend/src/components/eventbox/` returns ZERO derivation-style uses (test fixture data setting both fields is fine, but no compound predicate combining them as a demotion signal).
- All existing tests pass; new Nat-20-demoted test passes.
- `npm test` clean in `pinder-web/frontend/`.
- `npm run build` clean in `pinder-web/frontend/`.

### Out of scope
- Engine-side changes (PR #972 already shipped them).
- session-runner migration (see "Tertiary" above — file a follow-up if real work is found there).
- The trap-overlay derivation in `orderedTurnEvents.ts` (different concern — that's about emitting `trap_overlay_applied` event tags, not about demotion).

### PR
- Repo: `decay256/pinder-web`.
- Branch: `chore/973-final-verdict-consumer-migration`.
- Title: `chore(#973): consume RollCheckResult.final_verdict / final_tier in collapsedHeader + replay`.
- Body: `Closes #973`, one-paragraph summary, list of sites changed (collapsedHeader.ts + tests + types.ts + replayProjection.ts conditional), the session-runner finding ("no migration needed" or follow-up filed), then `## DoD Evidence` (test + build output), then `## Research Log`.
- Open as non-draft.

### Authority
Authorized for sprint `2026-05-20-eventbox`. Self-unblock per SELF-UNBLOCK-BY-DEFAULT.

Return when PR is open with green DoD and the session-runner finding noted.
