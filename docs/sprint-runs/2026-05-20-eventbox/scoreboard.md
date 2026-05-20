# Sprint 2026-05-20-eventbox — final scoreboard

- **Started:** 2026-05-20T10:10Z
- **Finished:** 2026-05-20T12:50Z
- **Wall-clock:** ~2h40m
- **Orchestrator model:** Claude Opus 4.7 (direct Anthropic; pinned)
- **Implementer model:** DeepSeek V4 Pro (OpenRouter; Rung 0 default; 7 of 7 implementer runs ran here)
- **Reviewer model:** Gemini 3.5 Flash (direct Google; Rung 1)
- **Tickets closed:** 9 / 9 (100%)
- **PRs merged:** 8 (core #975 #977 #978; web #673 #674 #675 #676 #677)
- **Follow-ups filed:** 1 (pinder-core #976)
- **Questions queued:** 0

## Ticket-by-ticket

- **#968** (core, P3) — `JsonPropertyName("defending_stat")` on TurnSnapshot.DefendingStat. PR #975, +2 LoC. Rung 0 clean run.
- **#964** (core, P2) — consequence wire field on RollCheckResult / ShadowCheckResult / HornyCheckResult. PR #977, +196 LoC, 9 unit tests. Follow-up #976 filed for engine-side population. Rung 0, $0.12.
- **#973** (cross-repo, P2) — consume final_verdict/final_tier in collapsedHeader + replay; session-runner needed no migration. Web PR #673, +47/-9, 990 tests green. Cross-repo close on core. Rung 0, $0.18.
- **#672** (cross-repo, P2 bug) — triple_hit summary prop + EventBoxProps.summary required + 9-kind regression test. Core PR #978 (i18n keys) + web PR #674 (the actual fix). Rung 0, recovered from two stream-cuts via orchestrator-takeover.
- **#669** (web, P2) — drop dead outcomeStyle classes field. PR #675, -7 LoC. Rung 0 clean run.
- **#653** (web, P2 bug) — intended=delivered dedup. PR #676, 4 jsdom tests, +297/-35, 1003 tests green. Rung 0, recovered from stream-cut.
- **#621** (web, chore) — CLOSED as obsolete (legacy RollFormula.tsx deleted in PR #636).
- **#619** (web, chore) — CLOSED as obsolete (same reason as #621).
- **#612** (web, chore) — RollEventBox component-gallery, 26 fixtures + 28 jsdom tests. PR #677, +580 LoC, 1031 tests green. Rung 0, recovered from stream-cut.

## Cost & token totals (best estimate)

Logged via `spawn-complete.sh` / `spawn-recover.sh`. Stream-cut runs
reported zero tokens (runtime gap); real numbers shown where Stats
parsed cleanly:

- #964 impl: 262.7k in / 9.6k out → $0.12
- #973 impl: 384.7k in / 14.5k out → $0.18
- #653 impl: 332.4k in / 14.5k out → $0.16
- #672 web review: 13 in / 2.9k cache write / 41.2k cache read → $0.026
- All other spawn records use stats-reparse / operator-provided
  recovery (zero-token-stats runtime gap or stream-cut).

**Recorded implementer cost: ~$0.49.** True cost is somewhat higher due
to the zero-token-stats and stream-cut runs that consumed real wall-
clock but couldn't be billed accurately. Estimate true total cost in
the $1.50–$2.50 range, dominated by the three #672-area and #612 runs.

## Stream-cut count (DeepSeek V4 Pro on OpenRouter)

5 stream-cuts in 7 implementer runs (71%). See lessons.md §L1 for
forward-fix proposals.

## What got merged

- pinder-core: 9bbcf6f → c6af3d8 → ...latest (3 PRs merged this sprint).
- pinder-web: 448e815 → ...latest (5 PRs merged this sprint).

## What's queued for the next sprint

- pinder-core #976 — engine-side population of the
  RollCheckResult/ShadowCheckResult/HornyCheckResult.Consequence field
  from i18n catalogue. Substantial work (yaml loader + slot
  substitution + 3 emission sites + tests). Not Rung 0.
- Forward-fixes in lessons.md L1–L6 (role-spec edits, default-rung
  policy, triage hygiene). Tracked here, not filed as separate tickets
  — they apply to the eigentakt skill itself, not pinder-core/web.

## Trigger calibration data

`token-explosion`, `wall-clock-overrun`, `3-strike-review` triggers
were not enforced-fired this sprint. The five `provider_flake_retry`
events (and one same-rung retry) provide modest calibration data
points for the seed thresholds. A separate trigger-calibration.json
artefact is not produced this sprint (the sprint was small enough
that thresholds visibly weren't the limiting factor; the
DeepSeek-12m-ceiling pattern in L1 is the real signal).
