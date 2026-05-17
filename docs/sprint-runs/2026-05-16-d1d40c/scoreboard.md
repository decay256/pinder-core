# Sprint 2026-05-16-d1d40c — Final Scoreboard

**Closed:** 2026-05-17 11:48 UTC
**Duration:** ~31 hours wall (kickoff 2026-05-16 04:00 → final merge 2026-05-17 11:47).
**Mode:** sequential, full swarm-drain (impl → no-context review → merge), with one #603 implementer cascade salvaged inline.

## Headline

**14/14 kickoff tickets merged. 0 open PRs from sprint scope. 0 unresolved questions. 0 ANTI-DEATH violations across all orchestrator handoffs.**

## Merged tickets

| Ticket | Repo | PR | Title |
|--------|------|----|-------|
| #909 | core | #928 | repair Pinder.LlmAdapters.Tests fixtures |
| #916 | core | #930 | add [JsonPropertyName] to OpponentDefenseSnapshot |
| #919 | core | #931 | delete dead SteeringEngine.RollD20 |
| #932 | core | #933 | expose AttackerGroup/DefenderGroup/DcBase on SteeringRollResult |
| #596 | core | #934 | drop turn_result.annotations_heading (i18n) |
| #613 | web  | #626 | re-export RollEventBox + helpers from index.ts |
| #617 | web  | #627 | plumb GhostProbabilityPerTurn through GameApi DTO |
| #610 | web  | #628 | Phase 3: wire RollEventBox into TurnResultDisplay per-kind |
| #603 | web  | #630 | frontend mirror of FailureTierDisplay.Label per-kind |
| #629 | web  | #632 | SteeringRoll wire DTO carries attacker_group/defender_group/dc math |
| #596 | web  | #633 | drop Annotations frame; migrate Triple to EventBox |
| #597 | web  | #634 | unify text-modifying events into one pipeline-ordered stack |
| #599 | web  | #635 | unify ActiveTrapBadge + add GhostRiskBanner |
| #611 | web  | #636 | Phase 4: delete legacy RollFormula / inline steering+shadow templates |

| #625 | — | — | closed as superseded by #629 (steering wire-DTO scope absorbed it; see questions.md Q1 resolution) |

## Net code change

- **pinder-core:** 5 PRs merged, additive DTO surface + dead-code removal. No breaking changes to wire contract.
- **pinder-web:** 9 PRs merged. The #592 RollFormula/RollEventBox epic landed end-to-end (Phase 1+2 → Phase 3 → Phase 4 cleanup). Net deletion in Phase 4 alone: −442 lines.
- Bundle: TurnResultDisplay chunk shrank after Phase 4 dead-code removal; 992/992 frontend tests pass; deploy build green.

## Questions queue

**Q1 (#625): RESOLVED 2026-05-16.** Daniel chose option E (no single `leveraged_stat`; carry both stat-group averages + DC math instead). Resolution drove core#932 + web#629. Final outcome merged.

Empty queue at close.

## Lessons captured

Three eigentakt/orchestrator lessons documented in `lessons.md` (immediately during the run, per LESSONS-MUST-BE-WRITTEN-IMMEDIATELY):

- **L1 — EIGENTAKT-CHECK-TRACKED-BEFORE-GITIGNORE**: don't tell a subagent to .gitignore a file without first verifying it isn't tracked in origin/main.
- **L2 — EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS**: implementer task templates must mandate explicit `git add path/...` and forbid `git add .` / `-A` / `-u`.
- **L3 — EIGENTAKT-INLINE-SALVAGE-WHEN-MECHANICAL**: when an escalated implementer produces complete file changes but stops on a mechanical gate (guardrail test, missing PR open), orchestrator may salvage inline iff the change can be reviewed end-to-end first; otherwise respawn.

These are orchestration-layer lessons; intentionally NOT promoted into pinder-{core,web}/LESSONS_LEARNED.md (which are game-mechanic/architecture files).

## Cost & timing notes

- Two ANTI-DEATH-style mid-run respawns happened cleanly (per AGENTS.md respawn discipline) — orchestrator died once between #635 and #611, the pinder agent audited, respawned for "finish," and the resume drained the final ticket cleanly.
- One implementer cascade on #603 (Rung-0 token-underrun → Rung-0 rate-limit → Rung-1 partial → inline salvage). Documented as L3.
- `runtime metering gap` on DeepSeek-v4-pro means several `attempt-end` cost rows are `cost_usd: null`; pricing-snapshot.jsonl is therefore incomplete for those rows. Forward-fix tracked outside this sprint.

## Phase 7 hygiene

- `lessons.md` promoted from draft and finalised in this directory.
- No stale `/tmp/work-*` worktrees remain (audit verified).
- No open PRs in either repo from sprint scope.
- pinder-web canonical clone has uncommitted local working-tree noise (unrelated to this sprint); flagged for Daniel to clean up at convenience — does not gate sprint closure.

## Phase 8

This scoreboard IS the Phase 8 artifact. Sprint closed.
