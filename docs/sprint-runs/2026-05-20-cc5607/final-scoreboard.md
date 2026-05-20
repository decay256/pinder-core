# Sprint 2026-05-20-cc5607 — final scoreboard (engine-cleanup)

## Merged (4/4 implementable)
- **#953** — PR #979 → `a5357f2` — `Pinder.Rules.Tests` yaml-drift fix (244/244 pass; only test files touched, no yaml mutation)
- **#956** — PR #980 → `93ec81a` — `ShadowGrowthEffects` typed list on `GameEndedException` (LangVersion 8.0 forced sealed class + IEquatable instead of `record`)
- **#957** — PR #981 → `a203b9a` — Wait() / CheckInterestEndConditions() / CheckGhostTrigger() / step-8 all transactional (Option A, uniform contract)
- **#976** — PR #982 → `e409e8e` — Engine-side Consequence population via `IConsequenceCatalog` (Pinder.Core/I18n) + `ConsequenceCatalog` adapter (Pinder.LlmAdapters); 3 commits incl. fix-pass that restored #957 contract after first attempt reverted it

## Skipped (2/6)
- **#941** — `ArchetypeYamlLoader.LoadFromYaml` deletion. Depends on `Pinder.GameApi/Program.cs` PromptCatalog migration which is part of the #871 chain explicitly out of scope per kickoff. Recommend reopening once #871 lands.
- **#959** — `user_sessions.outcome` / `ended_at` backfill. Persistence layer lives in pinder-web; no core changes possible from pinder-core. Recommend handling as an operator DB migration (or filing a paired pinder-web ticket).

## Test counts at HEAD (`e409e8e`)
- `Pinder.Rules.Tests` — 244 / 244
- `Pinder.Core.Tests` — 2850 / 2850 (18 skipped, unchanged)
- `Pinder.LlmAdapters.Tests` — 1080 / 1080 (9 skipped, unchanged)
- **Aggregate: 4174 pass, 0 fail, 27 skipped** (vs 4128 baseline → +46 net passing from #953 reactivation + 21 new #976 + 3 new #957 + 1 new #956)

## Spawn ledger
- 8 implementer/reviewer/fix spawns (Rung 0 DeepSeek V4 Pro × 5, Rung 1 Gemini 3.5 Flash × 3)
- Implementer attempts: 6 (1 ticket-1 retry, 1 ticket-4 retry, 1 ticket-4 finisher, 1 ticket-4 fix-pass)
- Reviewer attempts: 4 (one per ticket, two-pass on #976)
- Zero-token stream-cuts: 5 of 8 (see lesson L1). All recovered via `spawn-recover.sh --source stats-reparse`.

## Cost (lower bound; cache-read discount not included)
- Rung 0 (DeepSeek V4 Pro, OpenRouter): ~$0.19 total across 5 attempts (3 zero-billed cuts + 2 billed runs at 76k and 250k tokens)
- Rung 1 (Gemini 3.5 Flash, Google direct): ~$0.26 total across 3 reviewer attempts
- **Sprint total: ~$0.45**

## Lessons captured
4 lessons written to `lessons.md`: ZERO-TOKENS-STREAM-CUTS-CHECK-DISK-FIRST (refinement of FLAKE-RETRY policy), IMPLEMENTER-COMMIT-CHECKPOINTS-IN-PROMPT (template addition), REGRESSION-FROM-LARGE-FILE-REWRITE (reviewer focus area), FIX-PASS-FASTER-THAN-FORMAL-REVIEW (provisional orchestrator-default).

## Questions queue
Empty.

## Phase 4.5 / Phase 7 hygiene
Elided this sprint (`orchestrator-default` logged in agent.log). The eventbox sprint earlier today completed clean state; deferring full sweep to the next sprint's start-of-run.

## Orchestrator pin advisory
Sprint ran orchestrator at `anthropic/claude-opus-4-7` (caller-spawned) despite yaml v5 pinning `google/gemini-3.5-flash`. Cost-tracking note: orchestration tokens billed at Opus rates this sprint. No coordination quality issues observed; the Flash pin advisory in `model-routing.yaml` ladder_reset_note stands as exploratory.
