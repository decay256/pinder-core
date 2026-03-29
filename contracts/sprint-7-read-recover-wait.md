# Contract: Issue #43 — Read, Recover, Wait Actions

## Component
Extensions to `GameSession` (Conversation/) + new result types

## Dependencies
- #139 Wave 0: `RollEngine.ResolveFixedDC`, `SessionShadowTracker`, `TrapState.HasActive`

---

## New Types

### ReadResult
**File:** `src/Pinder.Core/Conversation/ReadResult.cs`

```csharp
public sealed class ReadResult
{
    public bool Success { get; }
    public int? InterestValue { get; }       // Non-null only on success
    public RollResult Roll { get; }
    public GameStateSnapshot StateAfter { get; }
    public int XpEarned { get; }             // 5 on success (DC≤13), 2 on fail
    public IReadOnlyList<string> ShadowGrowthEvents { get; }  // Overthinking +1 on fail

    public ReadResult(bool success, int? interestValue, RollResult roll,
        GameStateSnapshot stateAfter, int xpEarned = 0,
        IReadOnlyList<string>? shadowGrowthEvents = null);
}
```

### RecoverResult
**File:** `src/Pinder.Core/Conversation/RecoverResult.cs`

```csharp
public sealed class RecoverResult
{
    public bool Success { get; }
    public string? ClearedTrapName { get; }  // Non-null only on success
    public RollResult Roll { get; }
    public GameStateSnapshot StateAfter { get; }
    public int XpEarned { get; }             // 15 on recovery success, 2 on fail

    public RecoverResult(bool success, string? clearedTrapName, RollResult roll,
        GameStateSnapshot stateAfter, int xpEarned = 0);
}
```

---

## GameSession Methods

### ReadAsync
```csharp
public Task<ReadResult> ReadAsync()
```

1. Check `_ended` → throw `GameEndedException`
2. Determine advantage/disadvantage from interest state
3. `RollEngine.ResolveFixedDC(StatType.SelfAwareness, _player.Stats, 12, _traps, _player.Level, _trapRegistry, _dice, hasAdvantage, hasDisadvantage)`
4. **Success**: return `InterestValue = _interest.Current`, no interest change
5. **Failure**: `_interest.Apply(-1)`, shadow growth: Overthinking +1 via `SessionShadowTracker` (if available)
6. Advance trap timers, increment turn, clear `_currentOptions`
7. Check end conditions (interest=0 → ended)

### RecoverAsync
```csharp
public Task<RecoverResult> RecoverAsync()
```

1. Check `_ended` → throw `GameEndedException`
2. Check `_traps.HasActive` → throw `InvalidOperationException` if false
3. Roll same as Read (SA vs DC 12)
4. **Success**: clear first active trap via `_traps.Clear(stat)`, return trap ID
5. **Failure**: `_interest.Apply(-1)`
6. Advance trap timers, increment turn, clear `_currentOptions`
7. Check end conditions

### Wait
```csharp
public void Wait()
```

1. Check `_ended` → throw `GameEndedException`
2. `_interest.Apply(-1)`
3. `_traps.AdvanceTurn()`
4. Increment turn, clear `_currentOptions`
5. Check end conditions

---

## Behavioral Invariants
- Read/Recover/Wait do **not** affect momentum streak
- Read/Recover/Wait can be called after `StartTurnAsync()` (discards options) or standalone
- Ghost trigger does NOT apply to Read/Recover/Wait (only on StartTurnAsync for Speak)
- Nat1/Nat20 on Read/Recover: standard auto-fail/auto-success rules apply
- Interest penalty is always −1 regardless of failure tier

## Consumers
- #44 (shadow growth — Overthinking +1 on Read fail)
- #48 (XP — Recovery success = 15 XP)
