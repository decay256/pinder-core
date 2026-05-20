# Sprint kickoff — engine-cleanup

**Sprint id:** 2026-05-20-cc5607
**ISO timestamp:** 2026-05-20T19:18:00Z
**Orchestrator session:** agent:pinder:subagent:f36925d1-cfac-4623-9c59-c660b765c15a
**Skill HEAD:** ff5f69c

## Scoped tickets (from Daniel's brief)
- #976 — Engine population of Consequence
- #957 — Wait()/CheckInterestEndConditions()/CheckGhostTrigger() transactional
- #956 — GameEndedException.ShadowGrowthEffects record list
- #953 — 46 GameDefinitionYaml test failures
- #941 — Delete ArchetypeYamlLoader.LoadFromYaml
- #959 — Backfill user_sessions.outcome

## Provider preflight
- openrouter: enabled, OPENROUTER_API_KEY set
- google: enabled, GEMINI_API_KEY + GOOGLE_API_KEY set
- anthropic: enabled, ANTHROPIC_API_KEY set
- yaml_sha256: 3386f1bb464a2eb4bfbe3faca9a4c199f860f50c98cdb60705834f06caa260d5

## Orchestrator pin advisory
yaml v5 pins orchestrator to `google/gemini-3.5-flash` (2026-05-20 swap, exploratory).
This orchestrator subagent is running at `anthropic/claude-opus-4-7` per the caller spawn.
The pin advisory is logged for cost-calibration awareness but does not block the run —
the run will still pay Opus rates for orchestration coordination.

## Calibration thresholds
Using `triggers[].threshold_seed` (seed-uncalibrated) — no approved trigger-calibration.json
from a prior sprint promoted to next-run yet. TRIGGER-CONSERVATISM-UNTIL-CALIBRATED applies.

## Refiner pass
Orchestrator-default: per-ticket refiner spawn elided. All 4 implementable tickets carry
explicit AC; #957 ships an explicit "Pick A or B" decision (orchestrator default: Option A,
recommended in ticket body and uniform contract). Logged as orchestrator-default in agent.log.
