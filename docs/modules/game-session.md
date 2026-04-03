# Game Session

## Overview
`GameSession` orchestrates a single Pinder conversation from match to outcome. It owns all mutable game state — interest, traps, momentum, combo tracking, shadow growth, turn count, and XP — and sequences calls to `RollEngine`, `ILlmAdapter`, and various subsystems each turn.

## Key Components

| File / Class | Description |
|---|---|
| `src/Pinder.Core/Conversation/GameSession.cs` | Main orchestrator. Manages turn lifecycle (`StartTurnAsync` → `ResolveTurnAsync`), interest meter, momentum, combos, traps, shadows, and game-over conditions. |
| `src/Pinder.Core/Conversation/GameSessionConfig.cs` | Optional configuration (clock, shadow trackers, starting interest, previous opener). |
| `src/Pinder.Core/Conversation/ComboTracker.cs` | Tracks stat combos across turns (e.g., The Triple — 3 distinct stats in a row). |
| `src/Pinder.Core/Conversation/InterestMeter.cs` | Manages interest value, clamped to [0, 25]. Maps interest to tiers (Bored, Interested, VeryIntoIt, AlmostThere, etc.). |
| `src/Pinder.Core/Conversation/DialogueOption.cs` | Represents a player dialogue choice with associated stat. |
| `src/Pinder.Core/Conversation/TurnResult.cs` | Value object returned by `ResolveTurnAsync` with roll result, interest delta, state snapshot, combo info. |
| `src/Pinder.Core/Conversation/TurnStart.cs` | Value object returned by `StartTurnAsync` with dialogue options and state snapshot. |
| `tests/Pinder.Core.Tests/HorninessAlwaysRolledTests.cs` | Tests that horniness is rolled every session (with and without `IGameClock`), modifier application, and clamping. |
| `tests/Pinder.Core.Tests/GameSessionReadRecoverWaitTests.cs` | Tests that `ReadAsync` and `RecoverAsync` apply shadow-based SA disadvantage (Overthinking T2+). |
| `tests/Pinder.Core.Tests/ShadowReductionTests.cs` | Tests for the 4 new shadow reduction events (Dread, Denial, Madness, Overthinking). |
| `tests/Pinder.Core.Tests/ShadowReductionSpecTests.cs` | Spec-driven tests for shadow reductions — boundary values, edge cases, negative cases, null-safety. |
| `tests/Pinder.Core.Tests/TripleBonusReadRecoverTests.cs` | Tests that triple combo bonus (+1) is applied to Read and Recover rolls via `externalBonus`. |
| `tests/Pinder.Core.Tests/TripleBonusReadRecoverEdgeCaseTests.cs` | Edge case tests for triple bonus on Read/Recover: consumption on failure, advantage stacking, no-trap throws, boundary values. |
| `tests/Pinder.Core.Tests/Issue308_ShadowThresholdWiringSpecTests.cs` | Tests that `GameSession` wires player shadows → `DeliveryContext` and opponent shadows → `OpponentContext`. 13 tests covering cross-wiring, null safety, all 6 stats, zero values. |
| `tests/Pinder.Core.Tests/Issue315_VisionTierBoundaryTests.cs` | Tier boundary tests verifying Triple combo bonus (+1) correctly softens failure tiers at every boundary on `ReadAsync` and `RecoverAsync`. 10 tests covering Catastrophe→TropeTrap, Misfire→Fumble, Fumble→Success boundaries with paired control cases. |

## API / Public Interface

### GameSession

```csharp
public sealed class GameSession
{
    public GameSession(
        CharacterProfile player,
        CharacterProfile opponent,
        ILlmAdapter llm,
        IDiceRoller dice,
        ITrapRegistry trapRegistry);

    public GameSession(
        CharacterProfile player,
        CharacterProfile opponent,
        ILlmAdapter llm,
        IDiceRoller dice,
        ITrapRegistry trapRegistry,
        GameSessionConfig config);

    public Task<TurnStart> StartTurnAsync();
    public Task<TurnResult> ResolveTurnAsync(int optionIndex);
    public void Wait();  // Skip a turn (consumes combo bonus, etc.)
}
```

### Momentum System (rules §15)

Momentum is tracked as a streak counter (`_momentumStreak`) that increments on each consecutive success and resets to 0 on any failure.

**Bonus computation** (`GetMomentumBonus`):

| Streak | Roll Bonus |
|--------|-----------|
| 0–2 | +0 |
| 3–4 | +2 |
| 5+ | +3 |

**Timing**: The momentum bonus is computed at `StartTurnAsync` based on the current streak and stored as `_pendingMomentumBonus`. It is added to `externalBonus` in `ResolveTurnAsync`, meaning it affects the roll's `FinalTotal` (and thus can change whether a roll succeeds or fails). It does **not** affect `InterestDelta` directly.

**External bonus composition** (in `ResolveTurnAsync`):
```
externalBonus = tellBonus + callbackBonus + _pendingMomentumBonus + (tripleBonus ? 1 : 0)
```

### Horniness System

Session horniness is **always** rolled (1d10) during `GameSession` construction, regardless of whether an `IGameClock` is provided. When a clock is available, the time-of-day modifier (`IGameClock.GetHorninessModifier()`) is added; without a clock, the modifier defaults to 0. The result is clamped to a minimum of 0.

```csharp
// In constructor:
int horninessRoll = _dice.Roll(10);
int todModifier = _clock?.GetHorninessModifier() ?? 0;
_sessionHorniness = Math.Max(0, horninessRoll + todModifier);
```

This means the first dice value consumed from `IDiceRoller` is always the horniness roll.

### Interest Clamping

Interest is clamped to [0, 25] by `GameSession` / `InterestMeter`. Individual delta computations (FailureScale, SuccessScale, RiskTierBonus) do not clamp.

## Architecture Notes

- **Dice consumption order**: The constructor consumes the first dice value for the horniness roll (1d10). Subsequent dice values are consumed by `StartTurnAsync` / `ResolveTurnAsync` for d20 rolls, d100 timing delays, ghost checks, etc.
- **Turn lifecycle**: `StartTurnAsync()` generates dialogue options, computes advantage/disadvantage, and pre-computes the pending momentum bonus. `ResolveTurnAsync(index)` executes the roll, computes interest delta, updates momentum streak, processes shadow growth, advances traps, and triggers the opponent response via LLM.
- **Momentum is a roll bonus, not an interest delta** (changed in #268 per rules §15). Previously momentum was added to `interestDelta` after the roll; now it is added to `externalBonus` before the roll, meaning it can change the outcome tier (e.g., turn a miss into a hit).
- **Nat 1 always fails** regardless of modifiers or momentum bonus. The momentum bonus still appears in `ExternalBonus` on a Nat 1 roll but does not prevent failure.
- **Combo system** (`ComboTracker`): The Triple bonus (+1 external) is consumed after the turn it's applied. `Wait()` also consumes the Triple bonus. As of #312, the triple bonus is also passed as `externalBonus` to `RollEngine.ResolveFixedDC` in both `ReadAsync` and `RecoverAsync` — previously it was consumed but never applied to those rolls.
- **Interest tiers** drive advantage/disadvantage: VeryIntoIt (16–20) and AlmostThere (21–25) grant advantage (roll 2d20, take highest).
- **Shadow-based SA disadvantage**: When the player's Overthinking shadow reaches Tier 2+ (≥12), SA rolls get disadvantage. This is checked in `ResolveTurnAsync` (Speak), `ReadAsync`, and `RecoverAsync`. The check uses `ShadowThresholdEvaluator.GetThresholdLevel()` and is guarded by a null check on `_playerShadows`.
- **Shadow reduction events (§7)**: Five conditions reduce shadow stats by −1 via `SessionShadowTracker.ApplyOffset()` (not `ApplyGrowth`, which rejects negative values):
  1. **Dread −1** on `DateSecured` outcome — in `EvaluateEndOfGameShadowGrowth()`.
  2. **Denial −1** on Honesty success at interest ≥ 15 — in `EvaluatePerTurnShadowGrowth()`, within the trigger 6 block.
  3. **Madness −1** on successful `RecoverAsync()` — in the success branch after trap is cleared and XP recorded.
  4. **Overthinking −1** on success despite shadow-based disadvantage — in `ResolveTurnAsync()`, after roll resolution. Checks `_shadowDisadvantagedStats.Contains(chosenOption.Stat)` AND `StatBlock.ShadowPairs[chosenOption.Stat] == ShadowStatType.Overthinking`.
  5. **Fixation −1** on 4+ different stats used — already existed in `EvaluateEndOfGameShadowGrowth()` (trigger 13).
  All reduction code null-checks `_playerShadows`. Deltas can go negative (valid per `ApplyOffset` semantics). Reductions stack across turns with no per-session cap.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-02 | #268 | Initial creation — documented momentum system change: momentum bonus is now applied as a roll bonus (`externalBonus`) in `ResolveTurnAsync` instead of being added to `interestDelta`. `_pendingMomentumBonus` field added, computed at `StartTurnAsync`. Tests updated across GameSessionTests, ComboGameSessionTests, ComboSpecTests, FullConversationIntegrationTest, Issue209_DiceQueueExhaustionTests. |
| 2026-04-03 | #269 | Horniness now always rolled (1d10) at construction, even without `IGameClock`. Previously the roll was skipped when `_clock == null`. Time-of-day modifier uses null-coalescing (`_clock?.GetHorninessModifier() ?? 0`). All test `FixedDice` constructors updated to prepend the horniness roll value. New test file `HorninessAlwaysRolledTests.cs` added. |
| 2026-04-03 | #260 | `ReadAsync` and `RecoverAsync` now apply shadow-based SA disadvantage when Overthinking ≥ T2 (≥12), matching existing behavior in `ResolveTurnAsync` for Speak actions. New test file `GameSessionReadRecoverWaitTests.cs` (143 lines). |
| 2026-04-03 | #270 | Added 4 missing shadow reduction events from §7: Dread −1 on DateSecured, Denial −1 on Honesty success at interest ≥15, Madness −1 on successful recovery, Overthinking −1 on success despite shadow disadvantage. All use `ApplyOffset()` with null-checks. Overthinking reduction adds extra guard via `StatBlock.ShadowPairs` check. Two new test files (1202 lines total). |
| 2026-04-03 | #312 | Bug fix: Triple combo bonus (+1) now actually applied to Read/Recover rolls. Previously `ConsumeTripleBonus()` was called but the bonus value was never passed to `RollEngine.ResolveFixedDC`. Now `HasTripleBonus` is captured before consumption and passed as `externalBonus`. Two new test files: `TripleBonusReadRecoverTests.cs` and `TripleBonusReadRecoverEdgeCaseTests.cs` (486 lines total). |
| 2026-04-03 | #308 | Shadow threshold wiring: confirmed `GameSession` routes player shadows to `DeliveryContext` and opponent shadows to `OpponentContext`. `GameSessionConfig` accepts `opponentShadows` parameter. New test file `Issue308_ShadowThresholdWiringSpecTests.cs` (443 lines, 13 tests). |
| 2026-04-03 | #315 | Vision concern — added tier boundary tests for Triple combo bonus on Read/Recover. Tests verify +1 external bonus softens every failure tier boundary: Catastrophe→TropeTrap (miss 10→9), Misfire→Fumble (miss 3→2), Fumble→Success (miss 1→0), plus Recover path equivalents. New test file `Issue315_VisionTierBoundaryTests.cs` (242 lines, 10 tests). Test-only change; no production code modified. |
