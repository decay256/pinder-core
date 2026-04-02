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

### Interest Clamping

Interest is clamped to [0, 25] by `GameSession` / `InterestMeter`. Individual delta computations (FailureScale, SuccessScale, RiskTierBonus) do not clamp.

## Architecture Notes

- **Turn lifecycle**: `StartTurnAsync()` generates dialogue options, computes advantage/disadvantage, and pre-computes the pending momentum bonus. `ResolveTurnAsync(index)` executes the roll, computes interest delta, updates momentum streak, processes shadow growth, advances traps, and triggers the opponent response via LLM.
- **Momentum is a roll bonus, not an interest delta** (changed in #268 per rules §15). Previously momentum was added to `interestDelta` after the roll; now it is added to `externalBonus` before the roll, meaning it can change the outcome tier (e.g., turn a miss into a hit).
- **Nat 1 always fails** regardless of modifiers or momentum bonus. The momentum bonus still appears in `ExternalBonus` on a Nat 1 roll but does not prevent failure.
- **Combo system** (`ComboTracker`): The Triple bonus (+1 external) is consumed after the turn it's applied. `Wait()` also consumes the Triple bonus.
- **Interest tiers** drive advantage/disadvantage: VeryIntoIt (16–20) and AlmostThere (21–25) grant advantage (roll 2d20, take highest).

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-02 | #268 | Initial creation — documented momentum system change: momentum bonus is now applied as a roll bonus (`externalBonus`) in `ResolveTurnAsync` instead of being added to `interestDelta`. `_pendingMomentumBonus` field added, computed at `StartTurnAsync`. Tests updated across GameSessionTests, ComboGameSessionTests, ComboSpecTests, FullConversationIntegrationTest, Issue209_DiceQueueExhaustionTests. |
