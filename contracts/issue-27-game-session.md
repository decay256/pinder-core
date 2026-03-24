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
- **Nullable enabled** (`<Nullable>enable</Nullable>`)
- **No NuGet packages**

---

## FailureScale (companion to SuccessScale)

**File**: `src/Pinder.Core/Rolls/FailureScale.cs`

```csharp
namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Maps failure tier to negative interest delta. Prototype defaults per #28.
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

**Behavioural contract**: Same pattern as `SuccessScale` (see `src/Pinder.Core/Rolls/SuccessScale.cs`). Pure function. No side effects. Returns 0 for success. Returns negative int for failures. Values are prototype defaults subject to PO revision (#28).

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
        public StatBlock Stats { get; }
        public string AssembledSystemPrompt { get; }
        public string DisplayName { get; }
        public TimingProfile Timing { get; }
        public int Level { get; }

        public CharacterProfile(
            StatBlock stats,
            string assembledSystemPrompt,
            string displayName,
            TimingProfile timing,
            int level)
        {
            Stats = stats ?? throw new System.ArgumentNullException(nameof(stats));
            AssembledSystemPrompt = assembledSystemPrompt ?? throw new System.ArgumentNullException(nameof(assembledSystemPrompt));
            DisplayName = displayName ?? throw new System.ArgumentNullException(nameof(displayName));
            Timing = timing ?? throw new System.ArgumentNullException(nameof(timing));
            Level = level;
        }
    }
}
```

**Note on `Level`**: `RollEngine.Resolve` requires `int level` (see `src/Pinder.Core/Rolls/RollEngine.cs` line signature). `CharacterProfile` must carry it.

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

All parameters are stored as readonly fields. Null checks on all.

### Internal State (not publicly accessible except via snapshots)

| Field | Type | Initial Value |
|-------|------|---------------|
| `_interest` | `InterestMeter` | `new InterestMeter()` (starts at 10) |
| `_traps` | `TrapState` | `new TrapState()` |
| `_momentumStreak` | `int` | `0` |
| `_turnNumber` | `int` | `0` |
| `_history` | `List<(string Sender, string Text)>` | empty |
| `_ended` | `bool` | `false` |
| `_outcome` | `GameOutcome?` | `null` |
| `_pendingOptions` | `DialogueOption[]?` | `null` (set by StartTurnAsync, consumed by ResolveTurnAsync) |
| `_hasAdvantage` | `bool` | `false` (set per turn in StartTurnAsync) |
| `_hasDisadvantage` | `bool` | `false` (set per turn in StartTurnAsync) |

### Public API

#### `Task<TurnStart> StartTurnAsync()`

Sequence:
1. If `_ended`, throw `GameEndedException(_outcome.Value)`
2. Check end conditions:
   - `_interest.IsZero` → end with `GameOutcome.Unmatched`
   - `_interest.IsMaxed` → end with `GameOutcome.DateSecured`
   - `_interest.GetState() == InterestState.Bored` → ghost check: `_dice.Roll(4) == 1` → end with `GameOutcome.Ghosted`
3. If ended in step 2, set `_ended = true`, `_outcome = outcome`, throw `GameEndedException`
4. Determine advantage/disadvantage:
   - `_hasAdvantage = _interest.GrantsAdvantage`
   - `_hasDisadvantage = _interest.GrantsDisadvantage`
   - (Trap-based adv/disadv is handled inside `RollEngine.Resolve`, not here)
5. Build `DialogueContext`:
   - `playerPrompt` = `_player.AssembledSystemPrompt`
   - `opponentPrompt` = `_opponent.AssembledSystemPrompt`
   - `conversationHistory` = `_history` (as `IReadOnlyList<(string,string)>`)
   - `opponentLastMessage` = last opponent message from `_history`, or `""` if no history
   - `activeTraps` = names from `_traps.AllActive` (as `IReadOnlyList<string>`)
   - `currentInterest` = `_interest.Current`
6. Call `_llm.GetDialogueOptionsAsync(context)` → store as `_pendingOptions`
7. Return `new TurnStart(_pendingOptions, GetSnapshot())`

#### `Task<TurnResult> ResolveTurnAsync(int optionIndex)`

Sequence:
1. Validate: `_pendingOptions != null` (else throw `InvalidOperationException("Must call StartTurnAsync first")`). `optionIndex >= 0 && optionIndex < _pendingOptions.Length` (else throw `ArgumentOutOfRangeException`).
2. Get chosen option: `var option = _pendingOptions[optionIndex]`
3. Roll: `RollEngine.Resolve(option.Stat, _player.Stats, _opponent.Stats, _traps, _player.Level, _trapRegistry, _dice, _hasAdvantage, _hasDisadvantage)`
4. Compute interest delta:
   - If `rollResult.IsSuccess`: `delta = SuccessScale.GetInterestDelta(rollResult)`
   - If failure: `delta = FailureScale.GetInterestDelta(rollResult)`
5. Momentum:
   - If success: `_momentumStreak++`
   - If failure: `_momentumStreak = 0`
   - Momentum bonus = `GetMomentumBonus(_momentumStreak)` (added to delta)
   - `totalDelta = delta + momentumBonus`
6. Record `int interestBefore = _interest.Current`
7. Record `InterestState stateBefore = _interest.GetState()`
8. Apply: `_interest.Apply(totalDelta)`
9. Advance trap timers: `_traps.AdvanceTurn()`
10. Build `DeliveryContext`:
    - `playerPrompt` = `_player.AssembledSystemPrompt`
    - `opponentPrompt` = `_opponent.AssembledSystemPrompt`
    - `conversationHistory` = `_history`
    - `opponentLastMessage` = last opponent msg or `""`
    - `chosenOption` = `option`
    - `outcome` = `rollResult.Tier`
    - `beatDcBy` = `rollResult.IsSuccess ? rollResult.Total - rollResult.DC : 0`
    - `activeTraps` = LLM instructions from `_traps.AllActive` (use `Definition.LlmInstruction`)
11. Call `_llm.DeliverMessageAsync(deliveryContext)` → `deliveredMessage`
12. Append `(_player.DisplayName, deliveredMessage)` to `_history`
13. Check interest threshold crossing: if `_interest.GetState() != stateBefore`:
    - Build `InterestChangeContext(_opponent.DisplayName, interestBefore, _interest.Current, _interest.GetState())`
    - Call `_llm.GetInterestChangeBeatAsync(context)` → `narrativeBeat`
    - else `narrativeBeat = null`
14. Compute `responseDelay` = `_opponent.Timing.ComputeDelay(_interest.Current, _dice)`
15. Build `OpponentContext`:
    - `playerPrompt`, `opponentPrompt`, `conversationHistory`, `opponentLastMessage` (same as above)
    - `activeTraps` = trap names
    - `currentInterest` = `_interest.Current`
    - `playerDeliveredMessage` = `deliveredMessage`
    - `interestBefore` = `interestBefore`
    - `interestAfter` = `_interest.Current`
    - `responseDelayMinutes` = `responseDelay`
16. Call `_llm.GetOpponentResponseAsync(opponentContext)` → `opponentMessage`
17. Append `(_opponent.DisplayName, opponentMessage)` to `_history`
18. Increment `_turnNumber`
19. Clear `_pendingOptions = null`
20. Check end: `bool isGameOver = _interest.IsZero || _interest.IsMaxed`
21. If game over: `_ended = true`, `_outcome = _interest.IsMaxed ? GameOutcome.DateSecured : GameOutcome.Unmatched`
22. Return `new TurnResult(rollResult, deliveredMessage, opponentMessage, narrativeBeat, totalDelta, GetSnapshot(), isGameOver, _outcome)`

#### `GameStateSnapshot GetSnapshot()` (private helper)

```csharp
private GameStateSnapshot GetSnapshot()
{
    var trapNames = new List<string>();
    foreach (var t in _traps.AllActive)
        trapNames.Add(t.Definition.Id);
    return new GameStateSnapshot(
        _interest.Current,
        _interest.GetState(),
        _momentumStreak,
        trapNames.ToArray(),
        _turnNumber);
}
```

### Momentum Specification

| Streak | Bonus | Notes |
|--------|-------|-------|
| 0–2 | +0 | No bonus yet |
| 3 | +2 | First momentum bonus |
| 4 | +2 | Sustained momentum |
| 5+ | +3 | Hot streak |

```csharp
private static int GetMomentumBonus(int streak)
{
    if (streak >= 5) return 3;
    if (streak >= 3) return 2;
    return 0;
}
```

**Critical detail**: Momentum bonus is computed AFTER incrementing the streak. So on the 3rd consecutive success, `_momentumStreak` becomes 3, then `GetMomentumBonus(3)` returns +2.

### Ghost Trigger Specification

- Checked in `StartTurnAsync`, NOT in `ResolveTurnAsync`
- Only triggers when `InterestState == Bored` (interest 1–4)
- Probability: 25% (`_dice.Roll(4) == 1`)
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

    public TurnStart(DialogueOption[] options, GameStateSnapshot state)
    {
        Options = options ?? throw new System.ArgumentNullException(nameof(options));
        State = state ?? throw new System.ArgumentNullException(nameof(state));
    }
}
```

### TurnResult
```csharp
public sealed class TurnResult
{
    public RollResult Roll { get; }
    public string DeliveredMessage { get; }
    public string OpponentMessage { get; }
    public string? NarrativeBeat { get; }
    public int InterestDelta { get; }
    public GameStateSnapshot StateAfter { get; }
    public bool IsGameOver { get; }
    public GameOutcome? Outcome { get; }

    public TurnResult(
        RollResult roll,
        string deliveredMessage,
        string opponentMessage,
        string? narrativeBeat,
        int interestDelta,
        GameStateSnapshot stateAfter,
        bool isGameOver,
        GameOutcome? outcome)
    {
        Roll = roll ?? throw new System.ArgumentNullException(nameof(roll));
        DeliveredMessage = deliveredMessage ?? throw new System.ArgumentNullException(nameof(deliveredMessage));
        OpponentMessage = opponentMessage ?? throw new System.ArgumentNullException(nameof(opponentMessage));
        NarrativeBeat = narrativeBeat;
        InterestDelta = interestDelta;
        StateAfter = stateAfter ?? throw new System.ArgumentNullException(nameof(stateAfter));
        IsGameOver = isGameOver;
        Outcome = outcome;
    }
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

    public GameStateSnapshot(
        int interest,
        InterestState state,
        int momentumStreak,
        string[] activeTrapNames,
        int turnNumber)
    {
        Interest = interest;
        State = state;
        MomentumStreak = momentumStreak;
        ActiveTrapNames = activeTrapNames ?? throw new System.ArgumentNullException(nameof(activeTrapNames));
        TurnNumber = turnNumber;
    }
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
public sealed class GameEndedException : System.InvalidOperationException
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

1. **Call order**: `StartTurnAsync` and `ResolveTurnAsync` must be called alternately. Calling `ResolveTurnAsync` without a prior `StartTurnAsync` throws `InvalidOperationException`.
2. **Game end**: Calling `StartTurnAsync` after game has ended throws `GameEndedException`.
3. **Ownership**: `GameSession` does NOT own `CharacterProfile` construction — that's the caller's responsibility.
4. **State isolation**: `GameSession` creates and owns its own `InterestMeter` and `TrapState` instances. Callers must not share or access these.
5. **Trap advancement**: `GameSession` calls `TrapState.AdvanceTurn()` once per `ResolveTurnAsync` (step 9, after interest apply).
6. **Advantage source**: `GameSession` reads `_interest.GrantsAdvantage`/`GrantsDisadvantage` and passes them to `RollEngine.Resolve`. Additional trap-based disadvantage is handled internally by `RollEngine`.
7. **No shadow growth** — explicitly descoped (#29). Do not implement. No stub, no TODO.
8. **No Hard/Bold risk bonus** — explicitly descoped (#30). Interest delta = scale output + momentum only.
9. **Interest delta formula**: `totalDelta = (success ? SuccessScale : FailureScale).GetInterestDelta(roll) + GetMomentumBonus(streak)`.
10. **DeliveryContext.ActiveTraps**: Should contain `LlmInstruction` strings from active traps (not just names). `DialogueContext.ActiveTraps` and `OpponentContext.ActiveTraps` contain trap names/IDs only.

## Verified Source Dependencies

These are the actual signatures verified against source code (not from docs):

| Component | Method/Property | File | Verified |
|-----------|----------------|------|----------|
| `RollEngine.Resolve` | `(StatType, StatBlock, StatBlock, TrapState, int, ITrapRegistry, IDiceRoller, bool, bool) → RollResult` | `src/Pinder.Core/Rolls/RollEngine.cs` | ✅ |
| `SuccessScale.GetInterestDelta` | `(RollResult) → int` | `src/Pinder.Core/Rolls/SuccessScale.cs` | ✅ |
| `InterestMeter.Apply` | `(int) → void` | `src/Pinder.Core/Conversation/InterestMeter.cs` | ✅ |
| `InterestMeter.Current` | `int` (getter) | same | ✅ |
| `InterestMeter.IsZero` | `bool` (Current <= Min) | same | ✅ |
| `InterestMeter.IsMaxed` | `bool` (Current >= Max) | same | ✅ |
| `InterestMeter.GetState` | `() → InterestState` | same | ✅ |
| `InterestMeter.GrantsAdvantage` | `bool` (VeryIntoIt or AlmostThere) | same | ✅ |
| `InterestMeter.GrantsDisadvantage` | `bool` (Bored) | same | ✅ |
| `TrapState.AdvanceTurn` | `() → void` | `src/Pinder.Core/Traps/TrapState.cs` | ✅ |
| `TrapState.AllActive` | `IEnumerable<ActiveTrap>` | same | ✅ |
| `ActiveTrap.Definition` | `TrapDefinition` | same | ✅ |
| `TrapDefinition.LlmInstruction` | `string` | `src/Pinder.Core/Traps/TrapDefinition.cs` | ✅ |
| `TrapDefinition.Id` | `string` | same | ✅ |
| `DialogueContext` constructor | `(string, string, IReadOnlyList<(string,string)>, string, IReadOnlyList<string>, int)` | `src/Pinder.Core/Conversation/DialogueContext.cs` | ✅ |
| `DeliveryContext` constructor | `(string, string, IReadOnlyList<(string,string)>, string, DialogueOption, FailureTier, int, IReadOnlyList<string>)` | `src/Pinder.Core/Conversation/DeliveryContext.cs` | ✅ |
| `OpponentContext` constructor | `(string, string, IReadOnlyList<(string,string)>, string, IReadOnlyList<string>, int, string, int, int, double)` | `src/Pinder.Core/Conversation/OpponentContext.cs` | ✅ |
| `InterestChangeContext` constructor | `(string, int, int, InterestState)` | `src/Pinder.Core/Conversation/InterestChangeContext.cs` | ✅ |
| `NullLlmAdapter` | implements `ILlmAdapter` | `src/Pinder.Core/Conversation/NullLlmAdapter.cs` | ✅ |
| `TimingProfile.ComputeDelay` | `(int, IDiceRoller) → int` | `src/Pinder.Core/Conversation/TimingProfile.cs` | ✅ |
| `RollResult.IsSuccess` | `bool` | `src/Pinder.Core/Rolls/RollResult.cs` | ✅ |
| `RollResult.Tier` | `FailureTier` | same | ✅ |
| `RollResult.Total` | `int` (UsedDieRoll + StatModifier + LevelBonus) | same | ✅ |
| `RollResult.DC` | `int` | same | ✅ |

## Files to Create

1. `src/Pinder.Core/Rolls/FailureScale.cs`
2. `src/Pinder.Core/Characters/CharacterProfile.cs`
3. `src/Pinder.Core/Conversation/GameSession.cs`
4. `src/Pinder.Core/Conversation/GameSessionTypes.cs` (TurnStart, TurnResult, GameStateSnapshot, GameOutcome, GameEndedException)
5. `tests/Pinder.Core.Tests/FailureScaleTests.cs`
6. `tests/Pinder.Core.Tests/GameSessionTests.cs`

## Test Helpers

`FixedDice` exists as a private class in both `RollEngineTests.cs` and `CharacterSystemTests.cs`. The implementer should either:
- Create a shared `tests/Pinder.Core.Tests/TestHelpers/FixedDice.cs` (preferred), or
- Create another private copy in `GameSessionTests.cs` (acceptable for prototype)

Similarly, `EmptyTrapRegistry` is private in `RollEngineTests.cs`. The implementer needs an `ITrapRegistry` implementation for tests — can use the same pattern or create a shared helper.

## Test Requirements

### FailureScale
- Returns 0 for success (FailureTier.None)
- Returns -1 for Fumble, -2 for Misfire, -3 for TropeTrap, -4 for Catastrophe, -5 for Legendary

### GameSession
- **3-turn integration test**: Using `NullLlmAdapter` + `FixedDice`, run 3 turns via StartTurnAsync/ResolveTurnAsync pairs. Assert:
  - History has 6 entries (3 player + 3 opponent)
  - Turn number is 3
  - Interest changed from starting value (10)
- **End condition — DateSecured**: Set up dice so interest reaches 25, assert next `StartTurnAsync` throws `GameEndedException` with `DateSecured`
- **End condition — Unmatched**: Set up dice so interest reaches 0, assert `GameEndedException` with `Unmatched`
- **Ghost trigger**: Start with interest in Bored range (need initial failures to lower interest to 1–4), then provide dice that returns 1 on `Roll(4)`, assert `GameEndedException` with `Ghosted`
- **Momentum streak**: Run 3 consecutive successes (high dice rolls), verify the 3rd turn has +2 momentum bonus reflected in the delta
- **Invalid call order**: Call `ResolveTurnAsync(0)` without `StartTurnAsync`, assert `InvalidOperationException`
- **Double StartTurnAsync**: Call `StartTurnAsync` twice without `ResolveTurnAsync` — should work (overwrites pending options)

## Dependencies (verified)
- Issue #26 (ILlmAdapter, context types, NullLlmAdapter) — **merged** ✅
- `RollEngine` — exists ✅
- `SuccessScale` — exists ✅
- `InterestMeter` with full API — exists ✅
- `TrapState` — exists ✅
- `IDiceRoller`, `ITrapRegistry` — exist ✅
- `TimingProfile` — exists ✅

## Consumers
- Unity game loop / CLI test harness (creates GameSession, calls StartTurnAsync/ResolveTurnAsync)
- Future: save/load system (serialize GameStateSnapshot)
