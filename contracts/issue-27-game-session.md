# Contract: Issue #27 — GameSession + CharacterProfile + FailureScale

## Component
`Pinder.Core.Conversation.GameSession` (new)
`Pinder.Core.Characters.CharacterProfile` (new)
`Pinder.Core.Rolls.FailureScale` (new)
`Pinder.Core.Conversation.GameOutcome` (new enum)
`Pinder.Core.Conversation.GameEndedException` (new exception)
Result types: `TurnStart`, `TurnResult`, `GameStateSnapshot` (new)

## Maturity
Prototype

## NFR
- Latency: dominated by ILlmAdapter calls (not engine's concern). Engine logic < 1ms per turn.

## Platform Constraints
- **Target**: netstandard2.0, LangVersion 8.0
- **No `record` types**. Use `sealed class` with readonly properties + constructor.
- **Nullable enabled**
- **No NuGet packages**

---

## FailureScale (companion to SuccessScale)

**File**: `src/Pinder.Core/Rolls/FailureScale.cs`

```csharp
namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps failure tier to negative interest delta. Prototype defaults.
    /// Fumble → -1, Misfire → -2, TropeTrap → -3, Catastrophe → -4, Legendary → -5.
    /// Returns 0 for success (FailureTier.None).
    /// </summary>
    public static class FailureScale
    {
        public static int GetInterestDelta(RollResult result)
        {
            if (result.IsSuccess) return 0;

            switch (result.Tier)
            {
                case FailureTier.Fumble:      return -1;
                case FailureTier.Misfire:     return -2;
                case FailureTier.TropeTrap:   return -3;
                case FailureTier.Catastrophe: return -4;
                case FailureTier.Legendary:   return -5;
                default:                      return 0;
            }
        }
    }
}
```

**Behavioural contract**: Same pattern as `SuccessScale`. Pure function. No side effects. Returns 0 for success. Returns negative int for failures. Values are prototype defaults subject to PO revision (#28).

---

## CharacterProfile

**File**: `src/Pinder.Core/Characters/CharacterProfile.cs`

```csharp
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Runtime character data needed by GameSession.
    /// Assembled once before the session starts.
    /// </summary>
    public sealed class CharacterProfile
    {
        /// <summary>Character's stat block (with shadow penalties applied).</summary>
        public StatBlock Stats { get; }

        /// <summary>Pre-assembled system prompt string (from PromptBuilder).</summary>
        public string AssembledSystemPrompt { get; }

        /// <summary>Display name for history entries and narrative beats.</summary>
        public string DisplayName { get; }

        /// <summary>Timing profile for reply delay calculation.</summary>
        public TimingProfile Timing { get; }

        /// <summary>Player's current level (1-based). Used for level bonus in rolls.</summary>
        public int Level { get; }

        // Constructor: (StatBlock stats, string assembledSystemPrompt, string displayName, TimingProfile timing, int level)
    }
}
```

**Note on `Level`**: The issue doesn't mention it, but `RollEngine.Resolve` requires `int level`. `CharacterProfile` must carry it. Without it, `GameSession` can't call `RollEngine`.

---

## GameSession

**File**: `src/Pinder.Core/Conversation/GameSession.cs`

### Constructor

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry)
```

### Internal State (not publicly accessible except via snapshots)

| Field | Type | Initial Value |
|-------|------|---------------|
| `_interest` | `InterestMeter` | `new InterestMeter()` (starts at 10) |
| `_traps` | `TrapState` | `new TrapState()` |
| `_momentumStreak` | `int` | `0` |
| `_turnNumber` | `int` | `0` |
| `_history` | `List<(string Sender, string Text)>` | empty |
| `_ended` | `bool` | `false` |
| `_pendingOptions` | `DialogueOption[]?` | `null` (set by StartTurnAsync, consumed by ResolveTurnAsync) |

### Public API

#### `Task<TurnStart> StartTurnAsync()`

Sequence:
1. If `_ended`, throw `GameEndedException` with the outcome
2. Check end conditions:
   - `_interest.IsZero` → end with `GameOutcome.Unmatched`
   - `_interest.IsMaxed` → end with `GameOutcome.DateSecured`
   - `_interest.GetState() == InterestState.Bored` → ghost check: `dice.Roll(4) == 1` → end with `GameOutcome.Ghosted`
3. If ended in step 2, set `_ended = true`, throw `GameEndedException`
4. Determine advantage/disadvantage:
   - `hasAdvantage = _interest.GrantsAdvantage`
   - `hasDisadvantage = _interest.GrantsDisadvantage`
   - (Trap-based adv/disadv is handled inside `RollEngine.Resolve`, not here)
5. Build `DialogueContext` from current state
6. Call `_llm.GetDialogueOptionsAsync(context)` → store as `_pendingOptions`
7. Return `TurnStart` with options and current `GameStateSnapshot`

#### `Task<TurnResult> ResolveTurnAsync(int optionIndex)`

Sequence:
1. Validate: `_pendingOptions` must not be null (must call `StartTurnAsync` first). `optionIndex` must be 0–3. Throw `InvalidOperationException` otherwise.
2. Get chosen option: `var option = _pendingOptions[optionIndex]`
3. Roll: `RollEngine.Resolve(option.Stat, _player.Stats, _opponent.Stats, _traps, _player.Level, _trapRegistry, _dice, _hasAdvantage, _hasDisadvantage)`
4. Compute interest delta:
   - If success: `SuccessScale.GetInterestDelta(rollResult)`
   - If failure: `FailureScale.GetInterestDelta(rollResult)`
5. Momentum:
   - If success: `_momentumStreak++`
   - If failure: `_momentumStreak = 0`
   - Momentum bonus (added to interest delta):
     - 3 consecutive successes: +2
     - 4 consecutive: +2
     - 5+ consecutive: +3
     - (Bonus applies on the turn that reaches the streak threshold)
6. Record interest before: `int interestBefore = _interest.Current`
7. Record state before: `InterestState stateBefore = _interest.GetState()`
8. Apply interest delta: `_interest.Apply(totalDelta)`
9. Advance trap timers: `_traps.AdvanceTurn()`
10. Build `DeliveryContext` → call `_llm.DeliverMessageAsync(context)` → `deliveredMessage`
11. Append `(player.DisplayName, deliveredMessage)` to `_history`
12. Check interest threshold crossing: if `_interest.GetState() != stateBefore`:
    - Build `InterestChangeContext` → call `_llm.GetInterestChangeBeatAsync(context)` → `narrativeBeat`
13. Build `OpponentContext` → call `_llm.GetOpponentResponseAsync(context)` → `opponentMessage`
14. Append `(opponent.DisplayName, opponentMessage)` to `_history`
15. Increment `_turnNumber`
16. Clear `_pendingOptions = null`
17. Check end conditions (interest 0 or 25): set `_ended` if so
18. Return `TurnResult`

### Momentum Specification

The streak count represents consecutive successful rolls. The bonus is an **additional** interest delta on the turn that reaches the threshold:

| Streak | Bonus | Notes |
|--------|-------|-------|
| 1–2 | +0 | No bonus yet |
| 3 | +2 | First momentum bonus |
| 4 | +2 | Sustained momentum |
| 5+ | +3 | Hot streak |

Implementation: `GetMomentumBonus(int streak)` returns 0 for streak<3, 2 for streak 3–4, 3 for streak≥5.

### Ghost Trigger Specification

- Checked in `StartTurnAsync`, NOT in `ResolveTurnAsync`
- Only triggers when `InterestState == Bored` (interest 1–4)
- Probability: 25% (dice.Roll(4) == 1)
- If triggered: game ends with `GameOutcome.Ghosted`
- If NOT triggered (75%): turn proceeds normally

---

## Result Types

**File**: `src/Pinder.Core/Conversation/GameSessionTypes.cs`

### TurnStart
```csharp
public sealed class TurnStart
{
    public DialogueOption[] Options { get; }
    public GameStateSnapshot State { get; }
}
```

### TurnResult
```csharp
public sealed class TurnResult
{
    public RollResult Roll { get; }
    public string DeliveredMessage { get; }
    public string OpponentMessage { get; }
    public string? NarrativeBeat { get; }       // null if no threshold crossed
    public int InterestDelta { get; }
    public GameStateSnapshot StateAfter { get; }
    public bool IsGameOver { get; }
    public GameOutcome? Outcome { get; }         // null if game continues
}
```

### GameStateSnapshot
```csharp
public sealed class GameStateSnapshot
{
    public int Interest { get; }
    public InterestState State { get; }
    public int MomentumStreak { get; }
    public string[] ActiveTrapNames { get; }
    public int TurnNumber { get; }
}
```

### GameOutcome
```csharp
public enum GameOutcome
{
    DateSecured,    // Interest reached 25
    Unmatched,      // Interest reached 0
    Ghosted         // Random ghost trigger during Bored state
}
```

### GameEndedException
```csharp
public sealed class GameEndedException : InvalidOperationException
{
    public GameOutcome Outcome { get; }
    public GameEndedException(GameOutcome outcome)
        : base($"Game has ended: {outcome}")
    {
        Outcome = outcome;
    }
}
```

---

## Behavioural Contract

1. `StartTurnAsync` and `ResolveTurnAsync` must be called alternately. Calling `ResolveTurnAsync` without a prior `StartTurnAsync` throws `InvalidOperationException`.
2. Calling `StartTurnAsync` after game has ended throws `GameEndedException`.
3. `GameSession` does NOT own `CharacterProfile` construction — that's the caller's responsibility.
4. `GameSession` DOES own `InterestMeter` and `TrapState` — callers must not share these instances.
5. `GameSession` DOES call `TrapState.AdvanceTurn()` once per `ResolveTurnAsync`.
6. `GameSession` reads `_interest.GrantsAdvantage`/`GrantsDisadvantage` for the RollEngine call.
7. **No shadow growth logic** — explicitly descoped (#29).
8. **No Hard/Bold risk bonus** — explicitly descoped (#30).
9. Interest delta = `SuccessScale.GetInterestDelta(roll)` or `FailureScale.GetInterestDelta(roll)` + momentum bonus. Nothing else.

## Files to Create
1. `src/Pinder.Core/Rolls/FailureScale.cs`
2. `src/Pinder.Core/Characters/CharacterProfile.cs`
3. `src/Pinder.Core/Conversation/GameSession.cs`
4. `src/Pinder.Core/Conversation/GameSessionTypes.cs` (TurnStart, TurnResult, GameStateSnapshot, GameOutcome, GameEndedException)
5. `tests/Pinder.Core.Tests/FailureScaleTests.cs`
6. `tests/Pinder.Core.Tests/GameSessionTests.cs`

## Test Requirements

### FailureScale
- Returns 0 for success (FailureTier.None)
- Returns -1 for Fumble, -2 for Misfire, -3 for TropeTrap, -4 for Catastrophe, -5 for Legendary
- Symmetric test: for each tier, verify the delta matches the spec

### GameSession
- **3-turn integration test**: Using `NullLlmAdapter` + `FixedDice` (a test IDiceRoller that returns a fixed sequence), run 3 turns, assert:
  - History has 6 entries (3 player + 3 opponent)
  - Turn number is 3
  - Interest changed from starting value
- **End condition — DateSecured**: Force interest to 25, assert `GameEndedException` with `DateSecured`
- **End condition — Unmatched**: Force interest to 0, assert `GameEndedException` with `Unmatched`
- **Ghost trigger**: Set interest to Bored range, provide dice that returns 1 on Roll(4), assert `GameEndedException` with `Ghosted`
- **Momentum streak**: Run 3 consecutive successes, verify +2 bonus on 3rd turn's delta
- **Invalid call order**: Call `ResolveTurnAsync` without `StartTurnAsync`, assert `InvalidOperationException`

### FixedDice (test helper)
```csharp
// Test utility — IDiceRoller that returns from a predetermined sequence
public sealed class FixedDice : IDiceRoller
{
    private readonly int[] _rolls;
    private int _index;

    public FixedDice(params int[] rolls) { _rolls = rolls; _index = 0; }

    public int Roll(int sides)
    {
        if (_index >= _rolls.Length) return sides; // safe fallback
        return _rolls[_index++];
    }
}
```

**Note**: This may already exist in the test project. Check before creating.

## Dependencies
- Issue #26 (ILlmAdapter, context types, NullLlmAdapter) — **must be merged first**
- `RollEngine` (existing)
- `SuccessScale` (existing)
- `InterestMeter` with `GetState()`, `GrantsAdvantage`, `GrantsDisadvantage` (existing, from #6)
- `TrapState` (existing)
- `IDiceRoller`, `ITrapRegistry` (existing interfaces)

## Consumers
- Unity game loop / CLI test harness (creates GameSession, calls StartTurnAsync/ResolveTurnAsync)
- Future: save/load system (serialize GameStateSnapshot)
