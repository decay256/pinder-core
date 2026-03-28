# Contract: Issue #43 — Read, Recover, and Wait Turn Actions

## Component
`Pinder.Core.Conversation.GameSession` (modified — 3 new public methods)
`Pinder.Core.Conversation.ReadResult` (new)
`Pinder.Core.Conversation.RecoverResult` (new)
`Pinder.Core.Rolls.RollEngine` (modified — add `fixedDc` parameter per ADR-2)

## Maturity
Prototype

---

## RollEngine Modification (ADR-2)

Add optional `fixedDc` parameter to `RollEngine.Resolve`:

```csharp
public static RollResult Resolve(
    StatType stat,
    StatBlock attacker,
    StatBlock defender,
    TrapState attackerTraps,
    int level,
    ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage = false,
    bool hasDisadvantage = false,
    int? fixedDc = null,           // NEW — when set, skip DC computation
    int externalBonus = 0)         // NEW — added to total for callbacks/tells/combos
{
    // ...
    // DC computation:
    int dc = fixedDc ?? defender.GetDefenceDC(stat);
    // ...
    // Total computation:
    int total = usedRoll + statMod + levelBonus + externalBonus;
    // ...
}
```

**Backward compat**: All existing callers pass neither parameter — behavior unchanged.

---

## New Result Types

### ReadResult
**File**: `src/Pinder.Core/Conversation/ReadResult.cs`

```csharp
public sealed class ReadResult
{
    public bool Success { get; }
    public RollResult Roll { get; }
    public int? RevealedInterest { get; }  // non-null on success
    public GameStateSnapshot StateAfter { get; }

    public ReadResult(bool success, RollResult roll, int? revealedInterest, GameStateSnapshot stateAfter) { ... }
}
```

### RecoverResult
**File**: `src/Pinder.Core/Conversation/RecoverResult.cs`

```csharp
public sealed class RecoverResult
{
    public bool Success { get; }
    public RollResult Roll { get; }
    public string? ClearedTrapName { get; }  // non-null on success
    public GameStateSnapshot StateAfter { get; }

    public RecoverResult(bool success, RollResult roll, string? clearedTrapName, GameStateSnapshot stateAfter) { ... }
}
```

---

## GameSession New Methods

### `Task<ReadResult> ReadAsync()`

Sequence:
1. If `_ended`, throw `GameEndedException`
2. Roll: `RollEngine.Resolve(StatType.SelfAwareness, _player.Stats, _opponent.Stats, _traps, _player.Level, _trapRegistry, _dice, fixedDc: 12)`
3. If success:
   - Return `ReadResult(true, roll, _interest.Current, snapshot)`
   - (Host can display interest value and opponent modifiers)
4. If failure:
   - `_interest.Apply(-1)`
   - Shadow growth: Overthinking +1 (via SessionShadowTracker if available)
   - Return `ReadResult(false, roll, null, snapshot)`
5. Advance trap timers: `_traps.AdvanceTurn()`
6. Increment turn number
7. XP: record 2 XP for failed check, 5 XP for successful check

### `Task<RecoverResult> RecoverAsync()`

Sequence:
1. If `_ended`, throw `GameEndedException`
2. If no trap is active (`!_traps.AllActive.Any()`), throw `InvalidOperationException("No active trap to recover from.")`
3. Roll: `RollEngine.Resolve(StatType.SelfAwareness, _player.Stats, _opponent.Stats, _traps, _player.Level, _trapRegistry, _dice, fixedDc: 12)`
4. If success:
   - Clear the oldest active trap: `var trap = _traps.AllActive.First(); _traps.Clear(trap.Definition.Stat);`
   - XP: record 15 XP (trap recovery)
   - Return `RecoverResult(true, roll, trap.Definition.Id, snapshot)`
5. If failure:
   - `_interest.Apply(-1)`
   - Return `RecoverResult(false, roll, null, snapshot)`
6. Advance trap timers
7. Increment turn number

### `GameStateSnapshot Wait()`

Sequence:
1. If `_ended`, throw `GameEndedException`
2. `_interest.Apply(-1)`
3. `_traps.AdvanceTurn()`
4. Reset momentum: `_momentumStreak = 0`
5. Increment turn number
6. Return current snapshot

**Note**: `Wait()` is synchronous — no LLM calls, no dice rolls.

---

## Behavioural Contract
- Read and Recover always use SA vs fixed DC 12 — they do NOT use opponent's defending stat
- Recover requires an active trap — throws if none
- Wait is the simplest action: -1 interest, advance traps, no roll
- All three actions increment turn number
- All three advance trap timers
- Read failure grows Overthinking +1 (requires SessionShadowTracker from #44; if not available, skip shadow growth)
- Momentum resets on Wait (not on Read/Recover — those don't affect streak)
- `_currentOptions` is cleared after Read/Recover/Wait (prevents stale ResolveTurnAsync)

## Dependencies
- #65 resolution (fixedDc parameter on RollEngine) — **this contract resolves #65**
- #58 resolution (SessionShadowTracker for Overthinking growth) — **can be implemented without; skip shadow growth if tracker unavailable**
- #63 (for context type fields, if Read/Recover call LLM — they don't, so minimal dependency)

## Consumers
- Host (Unity game loop) calls these methods as alternative to StartTurnAsync/ResolveTurnAsync
