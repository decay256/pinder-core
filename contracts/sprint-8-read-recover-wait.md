# Contract: Issue #43 — Read, Recover, Wait Turn Actions

## Component
`Pinder.Core.Conversation.GameSession` (extend) + new `ReadResult`, `RecoverResult` types

## Depends on
- #139 Wave 0: `RollEngine.ResolveFixedDC`, `TrapState.HasActive`, `SessionShadowTracker`

## Maturity: Prototype

---

## New Methods on GameSession

```csharp
/// <summary>
/// Read action: SA vs DC 12. Success reveals interest. Failure: −1 interest + Overthinking +1.
/// Self-contained turn action — does NOT require StartTurnAsync() first.
/// Clears _currentOptions if set. Checks end conditions independently.
/// </summary>
public Task<ReadResult> ReadAsync();

/// <summary>
/// Recover action: SA vs DC 12. Success clears one active trap. Failure: −1 interest.
/// Throws InvalidOperationException if no traps active (TrapState.HasActive == false).
/// </summary>
public Task<RecoverResult> RecoverAsync();

/// <summary>
/// Wait action: −1 interest, advance trap timers. No roll.
/// Synchronous — no LLM calls.
/// </summary>
public void Wait();
```

## New Types

### ReadResult
**File:** `src/Pinder.Core/Conversation/ReadResult.cs`

```csharp
public sealed class ReadResult
{
    public bool Success { get; }
    public int? InterestValue { get; }      // non-null only on success
    public RollResult Roll { get; }
    public GameStateSnapshot StateAfter { get; }
    public int XpEarned { get; }            // 5 on success, 2 on failure (wired by #48)
    public IReadOnlyList<string> ShadowGrowthEvents { get; }  // "Overthinking +1 (Read failed)" on failure
}
```

### RecoverResult
**File:** `src/Pinder.Core/Conversation/RecoverResult.cs`

```csharp
public sealed class RecoverResult
{
    public bool Success { get; }
    public string? ClearedTrapName { get; }  // non-null only on success
    public RollResult Roll { get; }
    public GameStateSnapshot StateAfter { get; }
    public int XpEarned { get; }             // 15 on success, 2 on failure (wired by #48)
}
```

## Behavioral Invariants

1. All three actions increment `_turnNumber` by 1.
2. All three actions check end conditions (interest 0/25) and ghost trigger (Bored → 25%) at start.
3. `ReadAsync`/`RecoverAsync` use `RollEngine.ResolveFixedDC(StatType.SelfAwareness, player.Stats, 12, ...)`.
4. On Read failure: `SessionShadowTracker.ApplyGrowth(Overthinking, 1, "Read failed")` → event in result.
5. On Recover failure: −1 interest only (no shadow growth).
6. Wait: −1 interest + `TrapState.AdvanceTurn()`. No roll.
7. `RecoverAsync` throws `InvalidOperationException` if `!_traps.HasActive`.
8. If called after `StartTurnAsync()`, `_currentOptions` is cleared.
9. Read/Recover/Wait do NOT call any `ILlmAdapter` methods.

## Dependencies
- `RollEngine.ResolveFixedDC` (#139)
- `SessionShadowTracker` (#139)
- `TrapState.HasActive` (#139)
- `XpLedger` (#48 — XP values are wired when #48 is implemented)

## Consumers
- #44 (shadow growth — Overthinking from Read failure)
- #48 (XP from Read/Recover success/failure)
