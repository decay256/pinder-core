# Contract: Issue #43 — Read, Recover, and Wait Turn Actions

## Component
Three new public methods on `Pinder.Core.Conversation.GameSession`

## Dependencies
- #130 (`RollEngine.ResolveFixedDC`, `SessionShadowTracker`, `TrapState.HasActive`)

## Files modified
- `Conversation/GameSession.cs` — add 3 methods + result types
- `Conversation/ReadResult.cs` — new file
- `Conversation/RecoverResult.cs` — new file

## Interface

### ReadAsync

```csharp
/// <summary>
/// Read action: roll SA vs DC 12. On success, reveals exact interest + opponent modifiers.
/// On failure, -1 interest and Overthinking +1 shadow growth.
/// Consumes the player's turn (increments turn counter, advances traps).
/// </summary>
/// <exception cref="GameEndedException">If game has already ended.</exception>
public async Task<ReadResult> ReadAsync();
```

### ReadResult

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ReadResult
    {
        /// <summary>The roll result (SA vs DC 12).</summary>
        public RollResult Roll { get; }

        /// <summary>True if the roll succeeded.</summary>
        public bool Success { get; }

        /// <summary>Exact interest value (only meaningful on success; always populated).</summary>
        public int InterestValue { get; }

        /// <summary>XP earned from this action.</summary>
        public int XpEarned { get; }

        /// <summary>Shadow growth events triggered (Overthinking +1 on fail).</summary>
        public IReadOnlyList<string> ShadowGrowthEvents { get; }

        /// <summary>Game state after this action.</summary>
        public GameStateSnapshot StateAfter { get; }
    }
}
```

**Behavior**:
1. Check game not ended
2. Roll SA vs fixed DC 12 via `RollEngine.ResolveFixedDC(StatType.SelfAwareness, _player.Stats, 12, ...)`
3. On success: reveal interest (return current value). XP = 5 (DC ≤13 tier).
4. On failure: Apply -1 interest. Apply Overthinking +1 via `SessionShadowTracker.ApplyGrowth(ShadowStatType.Overthinking, 1, "Read failed")`. XP = 2.
5. Advance trap timers. Increment turn.
6. Reset momentum streak (non-Speak actions break streak).

### RecoverAsync

```csharp
/// <summary>
/// Recover action: roll SA vs DC 12. Only callable when at least one trap is active.
/// On success, clears the longest-active trap. On failure, -1 interest.
/// </summary>
/// <exception cref="GameEndedException">If game has ended.</exception>
/// <exception cref="InvalidOperationException">If no traps are active.</exception>
public async Task<RecoverResult> RecoverAsync();
```

### RecoverResult

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class RecoverResult
    {
        public RollResult Roll { get; }
        public bool Success { get; }
        /// <summary>Name of cleared trap on success, null on failure.</summary>
        public string? ClearedTrapName { get; }
        public int XpEarned { get; }
        public GameStateSnapshot StateAfter { get; }
    }
}
```

**Behavior**:
1. Check game not ended
2. Check `_traps.HasActive` — throw `InvalidOperationException` if false
3. Roll SA vs fixed DC 12
4. On success: clear the first active trap (`_traps.AllActive.First()` → `_traps.Clear(stat)`). XP = 15 (trap recovery).
5. On failure: Apply -1 interest. XP = 2.
6. Advance trap timers. Increment turn. Reset momentum.

### Wait

```csharp
/// <summary>
/// Wait action: skip turn. -1 interest. Traps decrement duration.
/// </summary>
/// <exception cref="GameEndedException">If game has ended.</exception>
public GameStateSnapshot Wait();
```

**Behavior**:
1. Check game not ended
2. Apply -1 interest
3. Advance trap timers
4. Increment turn. Reset momentum.
5. Return snapshot

## Invariants
- All three methods check game-ended first (same as StartTurnAsync)
- All three advance turn counter and trap timers
- All three reset momentum streak (only Speak maintains it)
- ReadAsync/RecoverAsync are async because they roll dice (consistent API, even though dice.Roll is sync)
- Wait is sync — no dice, no LLM calls

## Consumers
GameSession callers (host/UI), #44 (Overthinking growth trigger), #48 (XP: trap recovery = 15)
