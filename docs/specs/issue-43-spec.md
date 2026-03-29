# Spec: GameSession — Read, Recover, and Wait Turn Actions (§8)

**Issue:** #43
**Sprint:** 7 — RPG Rules Complete
**Depends on:** #139 (Wave 0 — `RollEngine.ResolveFixedDC`, `SessionShadowTracker`, `TrapState.HasActive`)
**Contract:** `contracts/sprint-7-read-recover-wait.md`
**Maturity:** Prototype

---

## 1. Overview

Currently `GameSession` only supports the **Speak** action (the `StartTurnAsync` → `ResolveTurnAsync` flow). Rules v3.4 §8 defines three additional turn actions: **Read**, **Recover**, and **Wait**. This feature adds three new public methods to `GameSession` that implement these actions, each consuming the player's turn and advancing game state accordingly. Two new result types (`ReadResult`, `RecoverResult`) are also introduced.

Read lets the player attempt to reveal the opponent's exact interest level. Recover lets the player attempt to clear an active trap. Wait skips the turn but lets active traps expire naturally. All three actions use a fixed DC of 12 (where applicable) and the SelfAwareness stat.

---

## 2. Function Signatures

All methods are added to the existing `Pinder.Core.Conversation.GameSession` class.

### 2.1 ReadAsync

```csharp
public Task<ReadResult> ReadAsync()
```

Performs a **Read** action: rolls SelfAwareness vs fixed DC 12 using `RollEngine.ResolveFixedDC()`. On success, reveals the current interest value. On failure, applies −1 interest and +1 Overthinking shadow growth via `SessionShadowTracker`. Consumes the player's turn.

### 2.2 RecoverAsync

```csharp
public Task<RecoverResult> RecoverAsync()
```

Performs a **Recover** action: only callable when at least one trap is active (checked via `TrapState.HasActive`). Rolls SelfAwareness vs fixed DC 12 using `RollEngine.ResolveFixedDC()`. On success, clears one active trap and returns its name. On failure, applies −1 interest. Consumes the player's turn.

### 2.3 Wait

```csharp
public void Wait()
```

Performs a **Wait** action: skips the turn. Applies −1 interest. Decrements active trap durations via `TrapState.AdvanceTurn()`. Consumes the player's turn. No roll is performed.

### 2.4 ReadResult (new type)

**File:** `src/Pinder.Core/Conversation/ReadResult.cs`

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

        /// <summary>XP earned from this action: 5 on success, 2 on failure.</summary>
        public int XpEarned { get; }

        /// <summary>Shadow growth events that occurred (e.g. "Overthinking +1 (Read failed)" on failure). Empty list on success.</summary>
        public IReadOnlyList<string> ShadowGrowthEvents { get; }

        public ReadResult(bool success, int? interestValue, RollResult roll,
            GameStateSnapshot stateAfter, int xpEarned = 0,
            IReadOnlyList<string>? shadowGrowthEvents = null);
    }
}
```

### 2.5 RecoverResult (new type)

**File:** `src/Pinder.Core/Conversation/RecoverResult.cs`

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

        /// <summary>XP earned from this action: 15 on recovery success, 2 on failure.</summary>
        public int XpEarned { get; }

        /// <summary>Shadow growth events that occurred (e.g. "Overthinking +1 (Recover failed)" on failure). Empty list on success. Added by issue #44 (O2 trigger).</summary>
        public IReadOnlyList<string> ShadowGrowthEvents { get; }

        public RecoverResult(bool success, string? clearedTrapName, RollResult roll,
            GameStateSnapshot stateAfter, int xpEarned = 0,
            IReadOnlyList<string>? shadowGrowthEvents = null);
    }
}
```

---

## 3. Input/Output Examples

### 3.1 Read — Success

**Setup:** Player has SelfAwareness base +3, Overthinking shadow 0 (effective SA = +3), level 2 (level bonus = +0). Interest is currently 12. `SessionShadowTracker` available via `GameSessionConfig.PlayerShadows`.

- `RollEngine.ResolveFixedDC(StatType.SelfAwareness, playerStats, 12, traps, level, trapRegistry, dice, hasAdv, hasDisadv)` called.
- Dice rolls 10.
- Total = 10 + 3 + 0 = 13 ≥ DC 12 → **success**.
- `ReadResult.Success` = `true`
- `ReadResult.InterestValue` = `12` (current interest revealed)
- `ReadResult.XpEarned` = `5`
- `ReadResult.ShadowGrowthEvents` = empty list
- Interest is **not** modified on success.
- Turn number increments by 1.

### 3.2 Read — Failure

**Setup:** Player has SelfAwareness base +0, Overthinking shadow 0, level 1 (level bonus = +0). Interest is currently 8. `SessionShadowTracker` present.

- Dice rolls 5.
- Total = 5 + 0 + 0 = 5 < DC 12 → **failure**.
- `ReadResult.Success` = `false`
- `ReadResult.InterestValue` = `null`
- `ReadResult.XpEarned` = `2`
- Interest becomes 8 + (−1) = **7**.
- `SessionShadowTracker.ApplyGrowth(ShadowStatType.Overthinking, 1, "Read failed")` called → returns `"Overthinking +1 (Read failed)"`
- `ReadResult.ShadowGrowthEvents` = `["Overthinking +1 (Read failed)"]`
- Turn number increments by 1.

### 3.3 Read — Failure, no SessionShadowTracker

**Setup:** Same as 3.2 but `GameSessionConfig` is null or `PlayerShadows` is null (legacy constructor used).

- Dice rolls 5 → failure.
- Interest becomes 7.
- Overthinking growth is **skipped** (no tracker available). `ShadowGrowthEvents` = empty list.
- A warning-level note or empty event list is acceptable — the shadow growth simply does not occur.

### 3.4 Recover — Success (trap active)

**Setup:** Player has an active trap "Oversharing" on Honesty stat (`TrapState.HasActive` = true). SelfAwareness effective modifier +2, level 1 (bonus +0).

- Dice rolls 12.
- Total = 12 + 2 + 0 = 14 ≥ DC 12 → **success**.
- `RecoverResult.Success` = `true`
- `RecoverResult.ClearedTrapName` = `"Oversharing"`
- `RecoverResult.XpEarned` = `15`
- The trap is removed from `TrapState` via `_traps.Clear(stat)` where `stat` is the trapped stat.
- Interest is **not** modified on success.
- Turn number increments by 1.

### 3.5 Recover — Failure

**Setup:** Active trap present. SelfAwareness effective modifier +0.

- Dice rolls 3.
- Total = 3 < DC 12 → **failure**.
- `RecoverResult.Success` = `false`
- `RecoverResult.ClearedTrapName` = `null`
- `RecoverResult.XpEarned` = `2`
- Interest decreases by 1.
- Trap remains active.

### 3.6 Recover — No active trap (error)

**Setup:** `TrapState.HasActive` = false (no traps active).

- `RecoverAsync()` throws `InvalidOperationException` with message containing `"no active trap"`.

### 3.7 Wait

**Setup:** Interest is 10. One active trap with 2 turns remaining.

- Interest becomes 10 + (−1) = **9**.
- `TrapState.AdvanceTurn()` called → trap turns remaining decremented (2 → 1).
- Turn number increments by 1.
- No roll is performed.
- No XP is earned (Wait does not earn XP).

### 3.8 Read — Nat 20

**Setup:** Player SA effective = −2, level bonus = 0. DC 12.

- Dice rolls 20 → auto-success regardless of total (−2 + 0 + 20 = 18, but Nat20 bypasses DC check).
- `ReadResult.Success` = `true`
- `ReadResult.InterestValue` = current interest revealed.

### 3.9 Read — Nat 1

**Setup:** Player SA effective = +5, level bonus = +2. DC 12.

- Dice rolls 1 → auto-fail regardless of total.
- `ReadResult.Success` = `false`
- Interest −1, Overthinking +1 via tracker.

---

## 4. Acceptance Criteria

### AC1: All three actions implemented on `GameSession`

`GameSession` exposes three new public methods: `ReadAsync()`, `RecoverAsync()`, and `Wait()`. Each consumes one player turn by:
- Incrementing `_turnNumber`
- Clearing `_currentOptions` if set (discards any pending Speak options from `StartTurnAsync`)
- Checking end conditions before executing (game already ended → throw `GameEndedException`)
- Checking end conditions after interest changes (interest hits 0 → set `_ended`, `_outcome = Unmatched`)
- Advancing trap timers via `_traps.AdvanceTurn()`

Read/Recover/Wait can be called **after** `StartTurnAsync()` (discards the pending Speak turn) or **standalone** without calling `StartTurnAsync()` first. Per vision concern #147, these are self-contained turn actions.

Ghost trigger does NOT apply to Read/Recover/Wait — only on `StartTurnAsync` for the Speak action.

### AC2: Read — DC 12, SA stat, reveals interest on pass, −1 interest on fail

- **Roll mechanism:** `RollEngine.ResolveFixedDC(StatType.SelfAwareness, playerStats, 12, traps, level, trapRegistry, dice, hasAdvantage, hasDisadvantage)` — the Wave 0 prerequisite `ResolveFixedDC` method.
- **Advantage/disadvantage:** Determined from `_interest.GrantsAdvantage` and `_interest.GrantsDisadvantage` (same as Speak turns). Active traps affecting SA also apply normally (handled by `ResolveFixedDC` internally).
- **On success:** Set `ReadResult.InterestValue = _interest.Current`. Do NOT modify interest.
- **On failure:** `_interest.Apply(-1)`. Set `ReadResult.InterestValue = null`.
- **Nat 20:** Auto-success (standard rule, handled by `ResolveFixedDC`).
- **Nat 1:** Auto-fail (standard rule). −1 interest and +1 Overthinking apply.
- **Interest penalty is always −1 regardless of failure tier** — failure tier is informational only for Read.

### AC3: Recover — only callable when traps active, DC 12, SA stat, clears trap on pass

- **Precondition:** `_traps.HasActive` must be `true`. If false, throw `InvalidOperationException` with message `"Cannot recover: no active trap."` (or similar wording containing "no active trap").
- **Roll mechanism:** Same as Read — `RollEngine.ResolveFixedDC(StatType.SelfAwareness, playerStats, 12, ...)`.
- **On success:** Clear one active trap. If multiple traps are active, clear the **first** one from `_traps.AllActive` iteration order. Use `_traps.Clear(stat)` where `stat` is the trapped stat (from `ActiveTrap.Definition.Stat`). Return the trap's `Definition.Id` as `ClearedTrapName`.
- **On failure:** `_interest.Apply(-1)`. Trap remains active. `ClearedTrapName = null`.
- **Nat 20 / Nat 1:** Standard auto-success/auto-fail rules.
- **Interest penalty is always −1 regardless of failure tier.**

### AC4: Wait — −1 interest, trap duration decremented

- No roll is performed.
- `_interest.Apply(-1)`.
- `_traps.AdvanceTurn()` — decrements all active trap durations, removes expired ones.
- Increment `_turnNumber`.
- Clear `_currentOptions`.
- Check end conditions after interest change (interest hits 0 → `_ended = true`, `_outcome = Unmatched`).

### AC5: Overthinking +1 on Read fail via `SessionShadowTracker`

When a Read roll **fails**:
1. If `GameSessionConfig.PlayerShadows` (a `SessionShadowTracker`) is available on the session:
   - Call `_playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Read failed")`.
   - The returned string (e.g. `"Overthinking +1 (Read failed)"`) is included in `ReadResult.ShadowGrowthEvents`.
2. If no `SessionShadowTracker` is available (legacy constructor, `config` is null):
   - Shadow growth is **not applied**. `ShadowGrowthEvents` is empty.
   - This preserves backward compatibility with the existing constructor.

**Do NOT mutate `StatBlock._shadow` directly** — `StatBlock` is immutable. Shadow mutation goes through `SessionShadowTracker` exclusively.

The Overthinking growth affects future SA rolls within the same session: every 3 Overthinking points = −1 to SelfAwareness effective modifier (computed by `SessionShadowTracker.GetEffectiveStat()`).

### AC6: XP recording

When `XpLedger` is available on the session (via `GameSessionConfig` or direct field):
- **Read success:** Record 5 XP (standard DC≤13 success award).
- **Read failure:** Record 2 XP (failure XP).
- **Recover success:** Record 15 XP (Recovery XP per §10).
- **Recover failure:** Record 2 XP (failure XP).
- **Wait:** No XP.

If `XpLedger` is not available, XP recording is silently skipped. The `XpEarned` field on result types should still report the earned amount (for display purposes even if not persisted).

### AC7: Tests for each action — happy + error paths

Tests must cover:
- Read success path (interest revealed, no interest change, XP = 5)
- Read failure path (interest −1, Overthinking +1 via tracker, XP = 2)
- Read failure without `SessionShadowTracker` (interest −1, no shadow growth, no crash)
- Read Nat 20 (auto-success)
- Read Nat 1 (auto-fail, −1 interest, Overthinking +1)
- Recover success path (trap cleared, XP = 15)
- Recover failure path (interest −1, trap remains, XP = 2)
- Recover with no active trap (`InvalidOperationException`)
- Recover with multiple traps (only first cleared)
- Wait (interest −1, trap duration decremented)
- Wait causes interest to hit 0 (game ends with `Unmatched`)
- Each action on an ended game (`GameEndedException`)
- Read/Recover/Wait called after `StartTurnAsync` (options discarded, turn consumed)
- Build must be clean (`dotnet build` succeeds, `dotnet test` passes, all 254+ existing tests still pass).

---

## 5. Edge Cases

### 5.1 Read/Recover/Wait on an ended game
If `_ended` is true, all three methods must throw `GameEndedException` before doing anything else. Same behavior as `StartTurnAsync()`.

### 5.2 Read/Recover when interest hits 0 from −1 penalty
If interest is 1 and the player fails a Read or Recover, interest drops to 0. The game should end (`_ended = true`, `_outcome = GameOutcome.Unmatched`). The result type is still returned (not thrown as exception), but subsequent calls should throw `GameEndedException`.

### 5.3 Wait when interest hits 0
Same as above: if interest is 1, Wait drops it to 0. Game ends with `Unmatched`. `Wait()` is void, so the caller detects this via the next call throwing `GameEndedException`, or by inspecting `StateAfter` (if Wait is changed to return a result — per contract it is void).

### 5.4 Multiple active traps on Recover
If multiple traps are active, Recover clears **one** trap: the first in `_traps.AllActive` iteration order (which is `Dictionary<StatType, ActiveTrap>.Values` order — effectively insertion order in .NET but not guaranteed; the spec requires clearing the first iterated trap). The player must call Recover again on a subsequent turn to clear additional traps.

### 5.5 Read/Recover interaction with advantage/disadvantage
Advantage/disadvantage from interest state applies to the d20 roll (roll twice, take higher/lower via `hasAdvantage`/`hasDisadvantage` params on `ResolveFixedDC`).

### 5.6 Read/Recover interaction with active traps
Active traps that affect SelfAwareness impose disadvantage or stat penalties on SA rolls. These effects apply during Read/Recover rolls — `ResolveFixedDC` handles trap effects internally (same as `Resolve`).

### 5.7 Momentum streak
Read, Recover, and Wait do **not** affect the momentum streak. They neither increment nor reset it. Momentum only tracks consecutive Speak successes.

### 5.8 Nat 1 / Nat 20 on Read/Recover
- **Nat 20:** Auto-success regardless of modifiers vs DC 12.
- **Nat 1:** Auto-fail regardless of modifiers. Failure penalty (−1 interest) applies. For Read, Overthinking +1 also applies.
- Failure tier classification (Fumble/Misfire/TropeTrap/Catastrophe) is computed by `ResolveFixedDC` and included in the `RollResult`, but the interest penalty is always −1 regardless of tier for Read/Recover.
- **TropeTrap tier on Read/Recover:** If the failure tier is TropeTrap, `ResolveFixedDC` will attempt to activate a trap on the SA stat (via `trapRegistry.GetTrap(StatType.SelfAwareness)`). This is a side effect of using the shared roll engine. The implementer should decide if this is desired (§8 doesn't mention trap activation on Read/Recover failures). If not desired, pass an empty `TrapState` or disable trap activation. **Recommendation:** Allow it — if the roll engine activates a trap on TropeTrap tier, that's consistent game mechanics.

### 5.9 Calling Read/Recover/Wait between StartTurnAsync and ResolveTurnAsync
If the player called `StartTurnAsync()` (which stored `_currentOptions`), then calls `ReadAsync()`, `RecoverAsync()`, or `Wait()` instead of `ResolveTurnAsync()`, the action should still work. It consumes the turn. `_currentOptions` should be cleared (set to `null`) since the Speak action was abandoned.

### 5.10 Interest at max (25) before Read/Recover/Wait
If interest is already 25, the game should already be ended (`DateSecured`). These methods would throw `GameEndedException`. If for some reason it isn't ended, Read success would reveal 25; Read/Recover failure would drop to 24; Wait would drop to 24.

### 5.11 Read success reveals exact interest — no rounding
`ReadResult.InterestValue` returns the exact integer value from `InterestMeter.Current`. No rounding, no range — the exact number.

---

## 6. Error Conditions

| Condition | Method | Exception Type | Message Pattern |
|-----------|--------|---------------|-----------------|
| Game already ended | `ReadAsync()` | `GameEndedException` | `"Game has ended with outcome: {outcome}"` |
| Game already ended | `RecoverAsync()` | `GameEndedException` | `"Game has ended with outcome: {outcome}"` |
| Game already ended | `Wait()` | `GameEndedException` | `"Game has ended with outcome: {outcome}"` |
| No active trap | `RecoverAsync()` | `InvalidOperationException` | Contains `"no active trap"` |

Note: `GameEndedException` already exists with the message format `"Game has ended with outcome: {outcome}"` (from the existing constructor in `GameEndedException.cs`).

---

## 7. Dependencies

### Code dependencies (Wave 0 prerequisites — Issue #139)

These MUST be available before implementation can begin:

| Component | File | What it provides |
|-----------|------|-----------------|
| `RollEngine.ResolveFixedDC()` | `src/Pinder.Core/Rolls/RollEngine.cs` | Fixed-DC roll resolution (DC 12 for Read/Recover) |
| `SessionShadowTracker` | `src/Pinder.Core/Stats/SessionShadowTracker.cs` | Mutable shadow tracking (`ApplyGrowth` for Overthinking +1) |
| `TrapState.HasActive` | `src/Pinder.Core/Traps/TrapState.cs` | Boolean check for Recover precondition |
| `GameSessionConfig` | `src/Pinder.Core/Conversation/GameSessionConfig.cs` | Config carrier for clock, shadow trackers, starting interest |

### Code dependencies (existing, in this repo)

| Component | Usage |
|-----------|-------|
| `GameSession` | Class being modified — add `ReadAsync`, `RecoverAsync`, `Wait` |
| `InterestMeter` | `Apply(-1)`, `Current`, `GetState()`, `IsZero`, `GrantsAdvantage`, `GrantsDisadvantage` |
| `GameStateSnapshot` | Snapshot returned in result types |
| `GameEndedException` | Thrown on ended game |
| `GameOutcome` | Enum for end states (`Unmatched`, `DateSecured`, `Ghosted`) |
| `RollResult` | Roll outcome from `ResolveFixedDC` |
| `StatType.SelfAwareness` | Stat used for Read/Recover rolls |
| `ShadowStatType.Overthinking` | Shadow stat grown on Read failure |
| `TrapState` | Trap tracking (`HasActive`, `AllActive`, `Clear`, `AdvanceTurn`) |
| `IDiceRoller` | Injected dice roller |
| `ITrapRegistry` | Injected trap definitions |
| `CharacterProfile` | Player/opponent profiles (`.Stats`, `.Level`) |

### Optional dependencies (Sprint 7, may not be present yet)

| Component | Usage | Behavior if absent |
|-----------|-------|--------------------|
| `XpLedger` | XP recording | XP not recorded; `XpEarned` still populated on result |

### Issue dependencies

- **#139 (Wave 0)** — MUST be merged first. Provides `ResolveFixedDC`, `SessionShadowTracker`, `TrapState.HasActive`, `GameSessionConfig`.
- **#42 (RiskTier)** — Already merged (PR #119). `RollResult` carries `RiskTier` field. Risk tier bonus does NOT apply to Read/Recover (only Speak), but the type is compatible.

### External dependencies
- None. Zero NuGet packages.

### Platform constraints
- `netstandard2.0`, `LangVersion 8.0`
- No `record` types (C# 9+). Use `sealed class` with readonly properties and constructor.
- Nullable reference types enabled.
- `IReadOnlyList<string>` available via `System.Collections.Generic`.

---

## 8. Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `src/Pinder.Core/Conversation/ReadResult.cs` | **Create** | Result type for `ReadAsync` |
| `src/Pinder.Core/Conversation/RecoverResult.cs` | **Create** | Result type for `RecoverAsync` |
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modify** | Add `ReadAsync`, `RecoverAsync`, `Wait` methods; add constructor overload accepting `GameSessionConfig`; store `SessionShadowTracker` reference |
| `tests/Pinder.Core.Tests/GameSessionReadRecoverWaitTests.cs` | **Create** | Tests for all three actions |

---

## 9. Turn Lifecycle Summary

After this feature, a player turn can be one of four actions:

| Action | Roll? | Stat | DC | Success Effect | Failure Effect | Momentum? | XP |
|--------|-------|------|----|---------------|---------------|-----------|-----|
| **Speak** | Yes | Varies (chosen option) | Opponent-derived (13 + defender mod) | +interest (SuccessScale + risk bonus) | −interest (FailureScale) | Yes | 5/10/15 by scale |
| **Read** | Yes | SelfAwareness | Fixed 12 | Reveal interest value | −1 interest, +1 Overthinking | No | 5 success / 2 fail |
| **Recover** | Yes | SelfAwareness | Fixed 12 | Clear one active trap | −1 interest | No | 15 success / 2 fail |
| **Wait** | No | — | — | — | −1 interest, traps tick down | No | 0 |

All four actions increment `_turnNumber`, clear `_currentOptions`, advance trap timers, and are subject to game-ended checks.

---

## 10. Method Pseudocode (behavioral specification)

### ReadAsync pseudocode

```
1. if _ended → throw GameEndedException(_outcome)
2. hasAdvantage = _interest.GrantsAdvantage
3. hasDisadvantage = _interest.GrantsDisadvantage
4. roll = RollEngine.ResolveFixedDC(SelfAwareness, _player.Stats, 12, _traps, _player.Level, _trapRegistry, _dice, hasAdvantage, hasDisadvantage)
5. shadowEvents = new List<string>()
6. xp = 0
7. if roll.IsSuccess:
     interestValue = _interest.Current
     xp = 5
   else:
     interestValue = null
     _interest.Apply(-1)
     xp = 2
     if _playerShadows != null:
       event = _playerShadows.ApplyGrowth(Overthinking, 1, "Read failed")
       shadowEvents.Add(event)
8. _traps.AdvanceTurn()
9. _turnNumber++
10. _currentOptions = null
11. check end conditions (interest == 0 → _ended = true, _outcome = Unmatched)
12. if _xpLedger != null: record xp
13. snapshot = CreateSnapshot()
14. return new ReadResult(roll.IsSuccess, interestValue, roll, snapshot, xp, shadowEvents)
```

### RecoverAsync pseudocode

```
1. if _ended → throw GameEndedException(_outcome)
2. if !_traps.HasActive → throw InvalidOperationException("Cannot recover: no active trap.")
3. hasAdvantage = _interest.GrantsAdvantage
4. hasDisadvantage = _interest.GrantsDisadvantage
5. roll = RollEngine.ResolveFixedDC(SelfAwareness, _player.Stats, 12, _traps, _player.Level, _trapRegistry, _dice, hasAdvantage, hasDisadvantage)
6. clearedTrapName = null
7. shadowEvents = new List<string>()
8. xp = 0
9. if roll.IsSuccess:
     firstTrap = _traps.AllActive.First()
     clearedTrapName = firstTrap.Definition.Id
     _traps.Clear(firstTrap.Definition.Stat)
     xp = 15
   else:
     _interest.Apply(-1)
     xp = 2
     if _playerShadows != null:
       event = _playerShadows.ApplyGrowth(Overthinking, 1, "Recover failed")
       shadowEvents.Add(event)                           // O2 trigger — owned by issue #44
10. _traps.AdvanceTurn()
11. _turnNumber++
12. _currentOptions = null
13. check end conditions (interest == 0 → _ended = true, _outcome = Unmatched)
14. if _xpLedger != null: record xp
15. snapshot = CreateSnapshot()
16. return new RecoverResult(roll.IsSuccess, clearedTrapName, roll, snapshot, xp, shadowEvents)
```

### Wait pseudocode

```
1. if _ended → throw GameEndedException(_outcome)
2. _interest.Apply(-1)
3. _traps.AdvanceTurn()
4. _turnNumber++
5. _currentOptions = null
6. check end conditions (interest == 0 → _ended = true, _outcome = Unmatched)
```
