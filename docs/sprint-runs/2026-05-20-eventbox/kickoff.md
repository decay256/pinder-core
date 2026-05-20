# Sprint kickoff — eventbox-closeout

- **Sprint ID:** `2026-05-20-eventbox`
- **Started:** 2026-05-20T10:10Z (UTC)
- **Orchestrator session:** `agent:pinder:subagent:10b2c19a-07a1-45f8-95cd-d6f27770d790`
- **Routing yaml sha:** `d20027b1d4044fe09edddb4c261bb339ae73d35bed2a7d1f16d0dcafb2247733`
- **Authorization:** Daniel (decay0815) authorized via Discord #pinder; merge authority granted.

## Scope: RollEventBox / EventBox closeout

Closes the post-#592 / post-#601 unified EventBox + RollEventBox chain across
pinder-core and pinder-web. Goal: fix the one real bug, enforce the
invariant, remove dead code, add the missing tests, surface the missing wire
fields.

## Sequence (9 tickets, all implementable)

Core wire-DTO work first; consumers after.

1. pinder-core **#968** — Add `[JsonPropertyName("defending_stat")]` to `TurnSnapshot.DefendingStat` (P3, ~2 LoC)
2. pinder-core **#964** — Surface `consequence` on `RollCheckResult` / `ShadowCheckResult` / `HornyCheckResult` / `TropeTrapResult` wire DTOs (P2; unblocks downstream visibility)
3. cross-repo **#973** — Consume `RollCheckResult.final_verdict` / `final_tier` in frontend (pinder-web), replay tool (pinder-web), simulator (pinder-core). Cross-repo PR pair.
4. pinder-web **#672** — `triple_hit` EventBox missing `summary` prop + make `summary` required on `EventBoxProps` (P2 bug)
5. pinder-web **#669** — Drop dead `classes` field from `outcomeStyle` after web#652 (P2)
6. pinder-web **#653** — When delivered = intended (no overlays / no diff), show "intended = delivered" instead of duplicating text (P2 bug)
7. pinder-web **#621** — jsdom render test for `RollFormula` with real `defendingStat` (depends on #968 landing)
8. pinder-web **#619** — Drop hardcoded `option_roll` kind in `RollFormula` once `ModifierBagRollFormula` covers it
9. pinder-web **#612** — Storybook / component-gallery coverage for `RollEventBox`

## Out of scope (per brief)

prompt-yaml migration chain (#871/#587/#885/#585), game-engine cleanup
(#953/#957/#956/#941/#921), data/wire schema chores (#959/#658/#646) — all
deferred to other sprints.

## Triage classification

- **Implementable:** 9 (all tickets above)
- **Skipped:** 0

## Hygiene sweep (Phase 4.5)

Clean. No open PRs in either repo; 9 local branches in pinder-core all
<30 days old with PRs squash-merged within 2-3 days (under the
self-unblockable threshold); no stray worktrees; no `/tmp/work-*` dirs.

## Progress reporting mode

`live-per-step` — orchestrator sends one upstream event per Phase 4.x
step via the `message` tool to the parent's Discord channel.

## Trigger calibration

Uncalibrated; using seed values from `model-routing.yaml`.

## ETA caveat

Sequential mode — expect roughly 9 × (impl + review + merge + docs)
wall-clock time. Most tickets here are small (2 LoC up to ~150 LoC); the
heaviest is #973 (cross-repo consumer migration). Rough ETA: 4-6h
wall-clock.
