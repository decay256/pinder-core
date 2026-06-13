# Sprint analysis — eigentakt 2026-06-13-e33d7d (FINAL)

**Pinder two-session-GM refactor backlog drain (decay256/pinder-core #1121–#1130 + #1133).**
DRAIN COMPLETE. All 11 tickets closed and merged. main HEAD @ `056328c`.

## Per-rung performance
Flat-gemini ladder, escalation OFF. Every implementer / QA / reviewer ran **rung 0
(`gemini-3.1-pro-preview`, provider google, BARE slug)**; technical-writer ran its
pinned `gemini-3.1-pro-preview`; orchestrator pinned `claude-opus-4-8`.

- 19 recorded `pre_spawn_estimate` rows across the sprint, **100% model_requested=gemini-3.1-pro-preview**.
- Outcomes: 16 success / 1 partial / 4 killed_no_progress. The 4 killed_no_progress are
  auto-heal synthetic attempt-ends from prior-segment crash recovery (logging-loop closures),
  not real worker failures. Real worker success rate this segment = 100%.
- **0 enforced triggers fired** the entire sprint (escalation off → no rung ever changed).
- Every ticket closed **single-pass**: reviewer APPROVE, 0 blockers, on the first review for
  all of #1127/#1133/#1128/#1130 (and per earlier-segment handoff, #1121–#1126/#1129 likewise).

## Segment-5 (final) ticket detail
| Ticket | PR | Merge SHA | Reviewer | Local build/tests (reviewer-reverified) |
|---|---|---|---|---|
| #1127 apiVersion contract | #1145 | 62a35f6 | APPROVE 0blk | build 0 err, 4348–4349 pass / 0 fail / 27 skip (+13 new) |
| #1133 yaml-key align + back-compat | #1146 | fc5f412 | APPROVE 0blk | build 0 err, 4337 pass / 0 fail / 27 skip |
| #1128 Unity integration doc (v1) | #1148 | f14c3a9 | APPROVE 0blk | docs-only; version=1; rename table matches code |
| #1130 docs sweep + prompt-graph | #1149 | 056328c | APPROVE 0blk | docs-only; horniness-last preserved; links resolve |

## Calibration
0 triggers fired; escalation off → no estimate-vs-actual rung deltas to learn from.
All trigger thresholds remain at seed values (see `trigger-calibration.json`,
`approved_by_human: false`). No data supports raising `default_rung` off rung 0 — the
whole two-session-GM refactor (including a wire-contract ticket and a 6-file docs sweep)
landed green at rung 0.

## Named observations / lessons-worthy
1. **Rung-0 gemini-3.1-pro-preview was sufficient for the entire refactor backlog** — including
   wire-contract regression tests and a multi-file architecture docs sweep — with single-pass
   review approval throughout. Strong signal that flat rung-0 is the right default for bounded
   refactor/rename/docs backlogs.
2. **One known parallel-test flake surfaced** (`Issue1129_SchemaRenameGuardTests.LiveEmitters_RecordTracesUnderNewKeyNames_AndRoundTrip`)
   — passes 3/3 in isolation, races on the `InMemoryPromptTraceService` singleton under
   concurrent collections. Filed as follow-up **#1147** (same family as the pre-existing #1141).
   Did not block any merge.
3. **spawn-with-routing.sh awk parser does not strip inline YAML comments** from `pinned_model`,
   so the technical-writer spawn envelope's `model` field carried the trailing
   `# FLAT-GEMINI ...` comment. Routing intent was correct (pinned gemini-3.1-pro-preview) and the
   orchestrator passed the clean slug to delegate_task. Minor cosmetic parser fix recommended in
   `trigger-calibration.json` recommendations. NOT a routing failure.

## Cleanup
Phase 7 end-of-run hygiene: all sprint worktrees (`/tmp/work-1125/1126/1127/1128/1129/1130/1133/1137/1138`,
`/tmp/review-1145/1146/1148/1149`) pruned; sprint branches deleted locally; merged remote branches
deleted on PR merge (`--delete-branch`). `/tmp/work-843` (unrelated `feat/843-narrative-harness`
sediment) left intact per PRESERVE-SEDIMENT.

## Cleanup gate
No `model-attempt/*` branches, no draft PRs, no stashes named `rung-*-escalated-*` were created this
sprint (escalation off). There is no preserved sediment requiring a signoff gate. Nothing pending.
