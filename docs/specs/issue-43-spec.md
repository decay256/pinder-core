# Spec: GameSession — Read, Recover, and Wait Turn Actions (§8)

**Issue:** #43
**Depends on:** #42 (RiskTier enum on RollResult; risk tier interest bonus in ResolveTurnAsync)
**Maturity:** Prototype

---

## 1. Overview

Currently `GameSession` only supports the **Speak** action (the normal `StartTurnAsync` → `ResolveTurnAsync` flow). Rules v3.4 §8 defines three additional turn actions: **Read**, **Recover**, and **Wait**. This feature adds three new public methods to `GameSession` that implement these actions, each consuming the player's turn and advancing game state accordingly.

Read lets the player attempt to reveal the opponent's exact interest level. Recover lets the player attempt to clear an active trap. Wait skips the turn but lets active traps expire naturally.

---

## 2. Function Signatures

All methods are added to the existing `Pinder.Core.Conversation.GameSession` class.

### 2.1 ReadAsync

```csharp
public Task<ReadResult> ReadAsync()
```

Performs a **Read** action: rolls SelfAwareness vs DC 12. On success, reveals the current interest value and opponent stat modifiers. On failure, applies −1 interest and +1 Overthinking shadow growth. Consumes the player's turn.

### 2.2 RecoverAsync

```csharp
public Task<RecoverResult> RecoverAsync()
```

Performs a **Recover** action: only callable when at least one trap is active. Rolls SelfAwareness vs DC 12. On success, clears one active trap and returns its name. On failure, applies −1 interest. Consumes the player's turn.

### 2.3 Wait

```csharp
public void Wait()
```

Performs a **Wait** action: skips the turn. Applies −1 interest. Decrements active trap durations (via `TrapState.AdvanceTurn()`). Consumes the player's turn.

### 2.4 ReadResult (new type)

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ReadResult
    {
        /// <summary>True if the SA roll met or exceeded DC 12.</summary>
        public bool Success { get; }

        /// <summary>Current interest value. Non-null only on success; null on failure.</summary>
        public int? InterestValue { get; }

        /// <summary>The roll result for transparency/logging.</summary>
        public RollResult Roll { get; }

        /// <summary>Snapshot of game state after the action resolved.</summary>
        public GameStateSnapshot StateAfter { get; }

        public ReadResult(bool success, int? interestValue, RollResult roll, GameStateSnapshot stateAfter);
    }
}
```

### 2.5 RecoverResult (new type)

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class RecoverResult
    {
        /// <summary>True if the SA roll met or exceeded DC 12.</summary>
        public bool Success { get; }

        /// <summary>The ID/name of the cleared trap. Non-null only on success; null on failure.</summary>
        public string? ClearedTrapName { get; }

        /// <summary>The roll result for transparency/logging.</summary>
        public RollResult Roll { get; }

        /// <summary>Snapshot of game state after the action resolved.</summary>
        public GameStateSnapshot StateAfter { get; }

        public RecoverResult(bool success, string? clearedTrapName, RollResult roll, GameStateSnapshot stateAfter);
    }
}
```

---

## 3. Input/Output Examples

### 3.1 Read — Success

**Setup:** Player has SelfAwareness effective modifier +3, level bonus +1. Interest is currently 12.

- Dice rolls 10.
- Total = 10 + 3 + 1 = 14 ≥ DC 12 → **success**.
- `ReadResult.Success` = `true`
- `ReadResult.InterestValue` = `12`
- Interest is **not** modified on success.
- Turn number increments by 1.

### 3.2 Read — Failure

**Setup:** Player has SelfAwareness effective modifier +0, level bonus +0. Interest is currently 8.

- Dice rolls 5.
- Total = 5 + 0 + 0 = 5 < DC 12 → **failure**.
- `ReadResult.Success` = `false`
- `ReadResult.InterestValue` = `null`
- Interest becomes 8 + (−1) = **7**.
- Overthinking shadow stat grows by +1 on the player's `StatBlock`.
- Turn number increments by 1.

### 3.3 Recover — Success (trap active)

**Setup:** Player has an active trap "Oversharing" on Honesty stat. SelfAwareness effective modifier +2, level bonus +0.

- Dice rolls 12.
- Total = 12 + 2 + 0 = 14 ≥ DC 12 → **success**.
- `RecoverResult.Success` = `true`
- `RecoverResult.ClearedTrapName` = `"Oversharing"`
- The trap is removed from `TrapState`.
- Interest is **not** modified on success.
- Turn number increments by 1.

### 3.4 Recover — Failure

**Setup:** Active trap present. SelfAwareness effective modifier +0.

- Dice rolls 3.
- Total = 3 < DC 12 → **failure**.
- `RecoverResult.Success` = `false`
- `RecoverResult.ClearedTrapName` = `null`
- Interest decreases by 1.
- Trap remains active.

### 3.5 Recover — No active trap (error)

**Setup:** No traps active.

- `RecoverAsync()` throws `InvalidOperationException` with message: `"Cannot recover: no active trap."` (or similar).

### 3.6 Wait

**Setup:** Interest is 10. One active trap with 2 turns remaining.

- Interest becomes 10 + (−1) = **9**.
- Trap turns remaining decremented (2 → 1).
- Turn number increments by 1.
- No roll is performed.

---

## 4. Acceptance Criteria

### AC1: All three actions implemented on `GameSession`

`GameSession` exposes three new public methods: `ReadAsync()`, `RecoverAsync()`, and `Wait()`. Each consumes one player turn (increments `_turnNumber`, clears `_currentOptions` if set). Each checks end conditions (game already ended → throw `GameEndedException`).

### AC2: Read — DC 12, SA stat, reveals interest on pass, −1 interest on fail

- **Stat used:** `StatType.SelfAwareness`
- **DC:** Fixed at 12 (not the normal opponent-derived DC). The roll uses `RollEngine.Resolve()` with `StatType.SelfAwareness`, but the DC must be 12 regardless of opponent stats.
  - **Implementation note:** Since `RollEngine.Resolve()` computes DC from `defender.GetDefenceDC(stat)`, and Read uses a fixed DC 12, the implementer must either: (a) construct a minimal `RollResult` manually using the SA modifier + dice roll vs DC 12, or (b) add a `Resolve` overload that accepts a fixed DC. The choice is left to the implementer, but the result must faithfully represent a d20 + SA effective modifier + level bonus vs DC 12.
- **On success:** Return the current `InterestMeter.Current` value in `ReadResult.InterestValue`. Do NOT modify interest.
- **On failure:** Apply −1 interest via `_interest.Apply(-1)`. Set `ReadResult.InterestValue` to `null`.

### AC3: Recover — only callable when `TrapState` has active traps, DC 12, SA stat, clears trap on pass

- **Precondition:** At least one trap must be active. Check via `_traps.AllActive` having any elements (or equivalent). If no trap is active, throw `InvalidOperationException`.
- **Stat used:** `StatType.SelfAwareness`
- **DC:** Fixed at 12 (same consideration as Read).
- **On success:** Clear one active trap. If multiple traps are active, clear the **first** one returned by `_traps.AllActive` (iteration order). Use `_traps.Clear(stat)` where `stat` is the trapped stat. Return the trap's `Definition.Id` as `ClearedTrapName`.
- **On failure:** Apply −1 interest. Trap remains.

### AC4: Wait — −1 interest, trap duration decremented

- No roll is performed.
- Apply −1 interest via `_interest.Apply(-1)`.
- Call `_traps.AdvanceTurn()` to decrement all active trap durations (and remove expired ones).
- Increment `_turnNumber`.
- Check end conditions after interest change (interest hits 0 → set `_ended`, set `_outcome`).

### AC5: Overthinking +1 on Read fail

- When a Read roll fails, the player's Overthinking shadow stat must increase by +1.
- **Design consideration:** `StatBlock` is currently immutable (no setter for shadow stats). The implementer must either:
  - (a) Add a `GrowShadow(ShadowStatType, int)` method to `StatBlock` that mutates the shadow value, or
  - (b) Introduce a `SessionShadowTracker` (referenced in architecture doc) that tracks per-session shadow growth separately from the base `StatBlock`, or
  - (c) Make `CharacterProfile` carry a mutable shadow overlay.
- **Whichever approach is chosen**, after a failed Read, the Overthinking shadow value must be +1 higher than before. This affects future SA rolls (every 3 Overthinking points = −1 to SelfAwareness effective modifier).
- The spec does NOT prescribe which approach to use — only the observable behavior: after a failed Read, the player's effective SelfAwareness modifier must reflect the increased Overthinking shadow.

### AC6: Tests for each action happy + error path

- Tests must cover:
  - Read success path (interest revealed, no interest change)
  - Read failure path (interest −1, Overthinking +1)
  - Recover success path (trap cleared)
  - Recover failure path (interest −1, trap remains)
  - Recover with no active trap (InvalidOperationException)
  - Wait (interest −1, trap duration decremented)
  - Each action on an ended game (GameEndedException)
- Build must be clean (`dotnet build` succeeds, `dotnet test` passes).

---

## 5. Edge Cases

### 5.1 Read/Recover on an ended game
If `_ended` is true, both `ReadAsync()` and `RecoverAsync()` must throw `GameEndedException` before doing anything. Same behavior as `StartTurnAsync()`.

### 5.2 Wait on an ended game
`Wait()` must also throw `GameEndedException` if the game has ended.

### 5.3 Read/Recover when interest hits 0 from the −1 penalty
If interest is 1 and the player fails a Read or Recover, interest drops to 0. The game should end (`_ended = true`, `_outcome = GameOutcome.Unmatched`). The result type should still be returned (not thrown), but subsequent calls should throw `GameEndedException`.

### 5.4 Wait when interest hits 0
Same as above: if interest is 1, Wait drops it to 0. Game ends with `Unmatched`.

### 5.5 Multiple active traps on Recover
If multiple traps are active, Recover clears **one** trap (the first in iteration order). The player must call Recover again on a subsequent turn to clear additional traps.

### 5.6 Read/Recover interaction with advantage/disadvantage
Read and Recover use a fixed DC 12, not the opponent-derived DC. However, advantage/disadvantage from interest state should still apply to the d20 roll (roll twice, take higher/lower). The implementer should check `_interest.GrantsAdvantage` and `_interest.GrantsDisadvantage` when resolving the roll.

### 5.7 Read/Recover interaction with traps
Active traps may impose disadvantage or stat penalties on SA rolls. These effects should still apply during Read/Recover rolls if the trap affects SelfAwareness.

### 5.8 Momentum streak
Read, Recover, and Wait do **not** affect the momentum streak. They neither increment nor reset it. Momentum only tracks consecutive Speak successes.

### 5.9 Nat 1 / Nat 20 on Read/Recover
- **Nat 20:** Auto-success regardless of modifiers vs DC 12.
- **Nat 1:** Auto-fail regardless of modifiers. Failure penalty (−1 interest) applies. For Read, Overthinking +1 also applies.
- Failure tier classification (Fumble/Misfire/etc.) can be computed but is informational only for Read/Recover — the interest penalty is always −1 regardless of tier.

### 5.10 Calling Read/Recover/Wait between StartTurnAsync and ResolveTurnAsync
If the player called `StartTurnAsync()` (which stored `_currentOptions`), then calls `ReadAsync()`, `RecoverAsync()`, or `Wait()` instead of `ResolveTurnAsync()`, the action should still work. It consumes the turn. `_currentOptions` should be cleared (set to `null`) since the Speak action was abandoned.

---

## 6. Error Conditions

| Condition | Method | Exception Type | Message |
|-----------|--------|---------------|---------|
| Game already ended | `ReadAsync()` | `GameEndedException` | `"Game has ended: {outcome}"` |
| Game already ended | `RecoverAsync()` | `GameEndedException` | `"Game has ended: {outcome}"` |
| Game already ended | `Wait()` | `GameEndedException` | `"Game has ended: {outcome}"` |
| No active trap | `RecoverAsync()` | `InvalidOperationException` | `"Cannot recover: no active trap."` |

---

## 7. Dependencies

### Code dependencies (existing, in this repo)
- `Pinder.Core.Conversation.GameSession` — the class being modified
- `Pinder.Core.Conversation.InterestMeter` — interest tracking (Apply, Current, GetState, IsZero)
- `Pinder.Core.Conversation.GameStateSnapshot` — snapshot returned in results
- `Pinder.Core.Conversation.GameEndedException` — thrown on ended game
- `Pinder.Core.Conversation.GameOutcome` — enum for end states
- `Pinder.Core.Rolls.RollEngine` — d20 resolution (or manual roll construction for fixed DC)
- `Pinder.Core.Rolls.RollResult` — roll outcome type
- `Pinder.Core.Stats.StatType` — specifically `StatType.SelfAwareness`
- `Pinder.Core.Stats.ShadowStatType` — specifically `ShadowStatType.Overthinking`
- `Pinder.Core.Stats.StatBlock` — for SA modifier lookup; may need mutation for shadow growth
- `Pinder.Core.Traps.TrapState` — trap tracking (AllActive, Clear, AdvanceTurn)
- `Pinder.Core.Interfaces.IDiceRoller` — dice rolling
- `Pinder.Core.Characters.CharacterProfile` — player/opponent profiles

### Issue dependencies
- **#42 (RiskTier)** — Must be merged first. Read/Recover rolls produce a `RollResult` which may carry a `RiskTier` after #42 is implemented. The risk tier interest bonus does NOT apply to Read/Recover (only to Speak), but the `RollResult` type must be compatible.

### External dependencies
- None. Zero NuGet packages.

### Platform constraints
- `netstandard2.0`, `LangVersion 8.0`
- No `record` types (C# 9+). Use `sealed class` with readonly properties and constructor.
- Nullable reference types enabled.

---

## 8. Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `src/Pinder.Core/Conversation/ReadResult.cs` | **Create** | Result type for ReadAsync |
| `src/Pinder.Core/Conversation/RecoverResult.cs` | **Create** | Result type for RecoverAsync |
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modify** | Add ReadAsync, RecoverAsync, Wait methods |
| `src/Pinder.Core/Stats/StatBlock.cs` | **Modify** | Add shadow growth capability (or create SessionShadowTracker) |
| `tests/Pinder.Core.Tests/GameSessionReadRecoverWaitTests.cs` | **Create** | Tests for all three actions |

---

## 9. Turn Lifecycle Summary

After this feature, a player turn can be one of four actions:

| Action | Roll? | Stat | DC | Success Effect | Failure Effect | Momentum? |
|--------|-------|------|----|---------------|---------------|-----------|
| **Speak** | Yes | Varies (chosen option) | Opponent-derived (13 + defender mod) | +interest (SuccessScale + risk bonus) | −interest (FailureScale) | Yes |
| **Read** | Yes | SelfAwareness | Fixed 12 | Reveal interest value | −1 interest, +1 Overthinking | No |
| **Recover** | Yes | SelfAwareness | Fixed 12 | Clear one active trap | −1 interest | No |
| **Wait** | No | — | — | — | −1 interest, traps tick down | No |

All four actions increment `_turnNumber` and are subject to game-ended checks.
