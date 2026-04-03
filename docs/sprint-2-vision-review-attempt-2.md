# Vision Review — Sprint 2 (Player Agent + Sim Runner Fixes) — Attempt 2

## Alignment: ✅
This sprint serves the product vision well. The player agent abstraction (#346-#348) is critical infrastructure for automated playtesting — it transforms the sim runner from a "pick highest modifier" toy into a meaningful game balance tool. The bug fixes (#349, #352-#354) address real correctness issues found during simulation runs. Shadow tracking (#350) and reasoning output (#351) make the simulation output actionable for game design decisions. This is high-leverage prototype work.

## Vision Concerns — Status Review

### #355 — IPlayerAgent should NOT live in Pinder.Core ✅ Well-specified
- Clear AC: types must live in session-runner, not Core
- Directly contradicts #346's AC ("IPlayerAgent interface defined in Pinder.Core") — implementer must follow #355
- No changes needed

### #356 — JsonTrapRepository takes JSON string, not file path ✅ Well-specified
- Clear AC with exact code fix
- No changes needed

### #357 — InterestChangeContext backward-compatible optional parameter ✅ Well-specified
- Clear AC: optional param with null default, existing tests unchanged
- No changes needed

### NEW #359 — File counter glob doesn't match actual filenames
- #354's proposed fix addresses parsing but not the glob pattern
- `session-???.md` matches `session-NNN.md` but actual files are `session-NNN-name-vs-name.md`
- Glob returns zero files, so parsing fix never runs
- **Created issue #359** with fix: change glob to `session-*.md`

### NEW #360 — SessionShadowTracker constructor takes StatBlock, not Dictionary
- #350's spec shows wrong constructor signature
- `SessionShadowTracker(Dictionary<ShadowStatType, int>)` does not exist
- Correct: `new SessionShadowTracker(sableStats)` where sableStats is the existing StatBlock
- **Created issue #360** with correct usage

## Data Flow Traces

### Player Agent Decision Flow
- `GameSession.StartTurnAsync()` → `TurnStart` (options, state snapshot)
- `TurnStart` → `IPlayerAgent.DecideAsync(turnStart, context)` → `PlayerDecision` (index, reasoning, scores)
- `PlayerDecision.OptionIndex` → `GameSession.ResolveTurnAsync(index)` → `TurnResult`
- Required fields in `PlayerAgentContext`: PlayerStats, OpponentStats, CurrentInterest, InterestState, MomentumStreak, ActiveTrapNames, SessionHorniness, ShadowValues, TurnNumber
- ⚠️ `PlayerAgentContext` duplicates `TurnStart.State` (GameStateSnapshot) — per #355, consider using TurnStart directly

### Shadow Tracking Flow (#350)
- Session runner creates `SessionShadowTracker(sableStats)` → passes via `GameSessionConfig`
- `GameSession` stores as `_playerShadows`
- Growth triggers in `ProcessShadowGrowth()` call `_playerShadows.ApplyGrowth()`
- `TurnResult.ShadowGrowthEvents` populated from `_playerShadows.DrainGrowthEvents()`
- Session runner reads events from `TurnResult` for display
- ⚠️ Shadow delta table at session end requires `SessionShadowTracker.GetDelta()` per shadow — session runner must hold reference to tracker

### Fixation Probability Fix (#349)
- `ResolveTurnAsync` has `_currentOptions` (all 4 options) and `_opponent.Stats`
- For each option: `need = opponent.GetDefenceDC(opt.Stat) - player.GetEffective(opt.Stat)`
- `successPct = max(0, min(100, (21 - need) * 5))`
- Compare chosen option's pct against all options' pcts
- ⚠️ Must account for `externalBonus` (momentum, tell, callback) in probability calc — the "need" includes bonuses that modify the roll

## Unstated Requirements
- Users running the LlmPlayerAgent (#348) expect it to produce **different** play patterns than ScoringPlayerAgent — otherwise why have two agents? The output should visibly show strategic personality differences.
- If shadow tracking is enabled (#350), users expect shadow growth to actually **affect gameplay** within the session (not just be reported). Since GameSession already handles threshold effects, this should work — but the session output should make the effects visible (e.g., "⚠️ Dread T1: disadvantage applied").
- Users expect the file counter fix (#354) to be idempotent — running the session runner N times should produce N correctly-numbered files.

## Domain Invariants
- `ScoringPlayerAgent` must be deterministic: same `TurnStart` + `PlayerAgentContext` → same `PlayerDecision` always
- `LlmPlayerAgent` must fall back to `ScoringPlayerAgent` on any API failure — no crashes
- Session file numbering must be monotonically increasing and gap-free within a run
- `SessionShadowTracker` wraps `StatBlock` — shadow starting values come from the `StatBlock`, not a separate dictionary
- `InterestChangeContext` changes must be backward-compatible (optional params with defaults)

## Gaps
- **Missing (in #354)**: Glob pattern fix — addressed by new concern #359
- **Missing (in #350)**: Constructor mismatch — addressed by new concern #360
- **Assumption**: #349 probability computation only considers base stat + opponent DC, not external bonuses (momentum, tell, callback). If the "highest probability" should account for known bonuses, the fix is more complex. The issue's current scope (base probability comparison) is likely sufficient for prototype.
- **Assumption**: #348 LlmPlayerAgent will use `AnthropicClient` from `Pinder.LlmAdapters` rather than rolling its own HTTP client. Session-runner already references LlmAdapters, so this should work.

## Requirements Compliance
- No `REQUIREMENTS.md` found in repo — no formal FR/NFR/DC checks possible.
- All changes are backward-compatible (optional params, new types, session-runner-only changes).
- Zero-dependency constraint on Pinder.Core is maintained (per #355, player agent types stay in session-runner).

## Recommendations
1. Implementer of #354 **must also fix the glob** per #359 — the two issues should be addressed together
2. Implementer of #350 **must use StatBlock constructor** per #360 — do not attempt `new SessionShadowTracker(Dictionary<>)`
3. Implementer of #346 **must follow #355** and place types in session-runner, overriding the AC that says "in Pinder.Core"
4. Implementer of #353 **must follow #356** and use `File.ReadAllText()` before passing to `JsonTrapRepository`

## VERDICT: CLEAN

All 5 vision concerns (3 existing + 2 new) are now well-specified with actionable acceptance criteria. The sprint scope is appropriate for prototype maturity. No blocking gaps remain — all concerns are advisory corrections to spec code that implementers can follow.
