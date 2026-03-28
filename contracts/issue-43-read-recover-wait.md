# Contract: Issue #43 — Read, Recover, and Wait Turn Actions

## Component
Extensions to `GameSession` in `Pinder.Core.Conversation`

## Maturity
Prototype

## NFR
- latency_p99_ms: N/A (in-process + async LLM calls)

---

## New Public API on GameSession

Three new async methods, parallel to the existing `ResolveTurnAsync(int optionIndex)`:

### ReadAsync

```csharp
/// <summary>
/// Read action: roll SA vs fixed DC 12.
/// Success: reveals exact interest value + opponent stat modifiers.
/// Failure: -1 Interest, Overthinking +1.
/// </summary>
/// <returns>ReadResult with outcome.</returns>
/// <exception cref="GameEndedException">If game already ended.</exception>
/// <exception cref="InvalidOperationException">If StartTurnAsync was not called first.</exception>
public Task<ReadResult> ReadAsync();
```

### RecoverAsync

```csharp
/// <summary>
/// Recover action: roll SA vs fixed DC 12.
/// Only valid when at least one trap is active (TrapState.HasActive).
/// Success: clears all active traps. Failure: -1 Interest.
/// </summary>
/// <returns>RecoverResult with outcome.</returns>
/// <exception cref="GameEndedException">If game already ended.</exception>
/// <exception cref="InvalidOperationException">If StartTurnAsync not called or no active traps.</exception>
public Task<RecoverResult> RecoverAsync();
```

### WaitAsync

```csharp
/// <summary>
/// Wait action: skip turn. All active traps decrement (via AdvanceTurn).
/// -1 Interest. No roll.
/// </summary>
/// <returns>WaitResult with outcome.</returns>
/// <exception cref="GameEndedException">If game already ended.</exception>
/// <exception cref="InvalidOperationException">If StartTurnAsync not called.</exception>
public Task<WaitResult> WaitAsync();
```

---

## Result Types (new sealed classes in `Pinder.Core.Conversation`)

### ReadResult
```csharp
public sealed class ReadResult
{
    public RollResult Roll { get; }           // SA vs DC 12
    public bool Success { get; }
    public int? RevealedInterest { get; }      // non-null on success
    public Dictionary<StatType, int>? RevealedModifiers { get; } // opponent effective mods, non-null on success
    public int InterestDelta { get; }          // 0 on success, -1 on failure
    public GameStateSnapshot StateAfter { get; }
    public IReadOnlyList<string> ShadowGrowthEvents { get; } // Overthinking +1 on failure
    public int XpEarned { get; }               // 5 XP on success (DC 12 ≤ 13 tier), 2 XP on failure
    public bool IsGameOver { get; }
    public GameOutcome? Outcome { get; }
}
```

### RecoverResult
```csharp
public sealed class RecoverResult
{
    public RollResult Roll { get; }           // SA vs DC 12
    public bool Success { get; }
    public int TrapsCleared { get; }           // count of traps cleared (0 on failure)
    public int InterestDelta { get; }          // 0 on success, -1 on failure
    public GameStateSnapshot StateAfter { get; }
    public int XpEarned { get; }               // 15 XP on success (trap recovery), 2 XP on failure
    public bool IsGameOver { get; }
    public GameOutcome? Outcome { get; }
}
```

### WaitResult
```csharp
public sealed class WaitResult
{
    public int InterestDelta { get; }          // always -1
    public GameStateSnapshot StateAfter { get; }
    public bool IsGameOver { get; }
    public GameOutcome? Outcome { get; }
}
```

---

## Behavioral Contract

- All three actions consume the current turn (increment `_turnNumber`, clear `_currentOptions`).
- After any action, `StartTurnAsync` must be called again before the next action.
- Read/Recover use `RollEngine.ResolveFixedDC(StatType.SelfAwareness, playerStats, 12, ...)` from Wave 0.
- Momentum streak resets on Read/Recover/Wait (only Speak maintains streaks).
- Wait does NOT generate LLM calls (no delivered message, no opponent response).
- Read/Recover DO generate an opponent response (the opponent still replies even if the player didn't "speak").

## Dependencies
- Wave 0 (#139): `RollEngine.ResolveFixedDC`, `TrapState.HasActive`, `SessionShadowTracker` (for Overthinking growth on Read fail)
- XP tracking (#48): `XpLedger` for recording XP events

## Consumers
- `GameSession` (internal)
- Host/Unity (calls these methods)
