# Vision Review — Sprint 2 (Player Agent + Sim Runner Fixes) — Attempt 1 (Re-run)

## Alignment: ✅ Strong

This sprint is correctly sequenced and high-leverage. The RPG engine rules are complete (sprints 8–12), three bugs were found during playtesting (#349, #352, #353, #354), and the player agent infrastructure (#346–#348) transforms the sim runner from a naive "pick highest modifier" tool into a meaningful game balance stress-tester. Shadow tracking (#350) and reasoning output (#351) make simulation results actionable. This is exactly what a prototype should be doing: building the tooling to exercise rules at scale.

## Sprint Progress Snapshot

| Issue | Status | Notes |
|-------|--------|-------|
| #354 | ✅ Merged (PR #371) | File counter — code review flagged test quality |
| #353 | ✅ Merged (PR #372) | Trap registry fix |
| #352 | ✅ Merged (PR #373) | Interest beat voice |
| #349 | 🔄 Open PR #374 | Fixation probability fix |
| #346 | 📝 Spec merged (PR #369) | IPlayerAgent interface — awaiting implementation |
| #347 | 📝 Spec merged (PR #366) | ScoringPlayerAgent — awaiting implementation |
| #348 | 📝 Spec merged (PR #365) | LlmPlayerAgent — awaiting implementation |
| #350 | 📝 Spec in PR #367 (open) | Shadow tracking — awaiting merge + implementation |
| #351 | 📝 Spec merged (PR #368) | Pick reasoning — awaiting implementation |

## Data Flow Traces

### Player Agent Decision Flow (#346 → #347 → #348)
- `GameSession.StartTurnAsync()` → `TurnStart { Options[], State }` → `IPlayerAgent.DecideAsync(turn, context)` → `PlayerDecision { OptionIndex, Reasoning, Scores[] }` → `session.ResolveTurnAsync(index)` → `TurnResult`
- Required fields for scoring: `StatBlock` (player + opponent), DC per option, momentum streak, active traps, tell/callback/combo flags on each `DialogueOption`
- ✅ All required fields exist in `TurnStart.State` (GameStateSnapshot) and `TurnStart.Options`
- ⚠️ `PlayerAgentContext` duplicates data from `TurnStart.State` — noted in prior review, acceptable at prototype

### Shadow Tracking Flow (#350)
- Session runner creates `SessionShadowTracker(sableStats)` → passes via `GameSessionConfig.PlayerShadows` → `GameSession` uses for shadow growth → `TurnResult.ShadowGrowthEvents` populated → session runner displays
- ✅ Data path is clean. `SessionShadowTracker` constructor confirmed to take `StatBlock` (per #360).

### Fixation Probability Fix (#349)
- `ResolveTurnAsync(optionIndex)` → `IsHighestProbabilityOption(chosen, _currentOptions)` → computes `margin = statMod + levelBonus - DC` per option → returns true if chosen has max margin
- ✅ Implementation in PR #374 looks correct — uses `_player.Stats.GetEffective()` + `LevelTable.GetBonus()` vs `_opponent.Stats.GetDefenceDC()`
- ⚠️ Minor: does not factor `externalBonus` (momentum/tell/callback) into probability — but neither did the original `optionIndex == 0` proxy. Acceptable for prototype.

### Interest Beat Voice (#352)
- `GameSession` → `InterestChangeContext(name, before, after, state, opponentPrompt)` → `ILlmAdapter.GetInterestChangeBeatAsync(ctx)` → LLM generates beat in character voice
- ✅ Already merged (PR #373). `InterestChangeContext.OpponentPrompt` field confirmed present.

## Unstated Requirements
- **ScoringPlayerAgent must produce different play patterns than LlmPlayerAgent** — otherwise two agent types are pointless. The scoring agent should be notably more conservative (pure EV), while the LLM agent should exhibit personality and risk-taking.
- **Session runner output should identify which agent type was used** — readers need to know if reasoning comes from deterministic scoring or LLM analysis.
- **File counter fix must be idempotent** — N sequential runs produce N correctly-numbered files.

## Domain Invariants
- Simulation agents must NOT mutate game state — they receive `TurnStart` (read-only) and return a decision index
- `ScoringPlayerAgent` must be deterministic: identical inputs → identical outputs, no randomness
- Shadow growth correctness: Fixation triggers on 3 consecutive highest-probability picks, not index-0 picks
- All Pinder.Core DTO changes use optional params with defaults — existing 1994 tests must pass unchanged
- Player agent types live in `session-runner/`, NOT `Pinder.Core` (per #355)

## Gaps

### Missing: Nothing critical
All previous vision concerns (#355–#360) have been filed and documented. Bug fixes for #352, #353, #354 are already merged.

### New concern filed:
- **#386**: `ScoringPlayerAgent` hardcodes engine bonus constants (tell=2, callback=1/2/3, momentum=2/3) that are defined in `CallbackBonus.Compute()` and `GameSession.GetMomentumBonus()`. The scoring agent should call `CallbackBonus.Compute()` directly rather than reimplementing the logic.

### Unnecessary: Nothing
All 9 issues serve simulation infrastructure or fix real bugs.

### Assumptions validated:
- ✅ `CallbackBonus.Compute()` is public static — scoring agent can call it directly
- ✅ `GameSession.GetMomentumBonus()` is private — must be duplicated or exposed
- ✅ `SessionShadowTracker(StatBlock)` is the correct constructor signature

## Requirements Compliance
- No `REQUIREMENTS.md` exists in the repo
- ✅ Zero NuGet dependencies on Pinder.Core maintained
- ✅ All DTO changes use backward-compatible optional params
- ✅ Player agent types confined to session-runner (per #355)
- ✅ netstandard2.0 + C# 8.0 for Pinder.Core; net8.0 for session-runner

## Vision Concerns Status

| # | Concern | Status |
|---|---------|--------|
| #355 | IPlayerAgent should NOT live in Pinder.Core | ✅ Filed, well-specified |
| #356 | JsonTrapRepository takes JSON string, not file path | ✅ Filed, resolved in PR #372 |
| #357 | InterestChangeContext backward-compatible optional param | ✅ Filed, resolved in PR #373 |
| #359 | File counter glob pattern fix | ✅ Filed, resolved in PR #371 |
| #360 | SessionShadowTracker constructor takes StatBlock | ✅ Filed, well-specified |
| **#386** | **ScoringPlayerAgent hardcodes engine bonus constants** | **NEW — advisory** |

## Wave Plan

**Wave 1**: #354, #353, #352 (already merged), #349 (PR open)
**Wave 2**: #350, #346
**Wave 3**: #347
**Wave 4**: #348, #351

## Role Assignments
All 9 issues assigned `backend-engineer` — **correct**. This is all C# backend work (game engine + console runner). No frontend, devops, or docs-only work.

## Recommendations
1. Implementer of #347 should call `CallbackBonus.Compute()` directly instead of reimplementing callback distance logic (per #386)
2. Implementer of #349 (PR #374) should address code review feedback if any remains
3. Implementer of #350 must use `new SessionShadowTracker(sableStats)` where `sableStats` is a `StatBlock` (per #360)
4. Implementer of #346 must place all types in `session-runner/` (per #355)

## VERDICT: CLEAN

One new advisory concern filed (#386). No blocking gaps. Sprint is well-scoped and correctly prioritized. 3 of 9 issues already merged, 1 has an open PR. Previous 5 vision concerns are all addressed. Proceed with implementation.
