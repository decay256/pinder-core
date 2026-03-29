# Spec: Shadow Growth Events — Implement §7 Growth Table in GameSession

**Issue:** #44
**Sprint:** 7 — RPG Rules Complete
**Depends on:** #139 (Wave 0 — `SessionShadowTracker`), #43 (Read/Recover actions for Overthinking triggers)
**Contract:** `contracts/sprint-7-shadow-growth.md`
**Maturity:** Prototype

---

## 1. Overview

Rules v3.4 §7 defines a table of conditions under which each shadow stat grows during a conversation. `GameSession` currently tracks shadows via `SessionShadowTracker` but never applies growth events. This feature implements all 17 shadow growth triggers (plus one offset), detects them at the appropriate moment during turn resolution or game end, mutates the session shadow state via `SessionShadowTracker.ApplyGrowth()`, and populates `TurnResult.ShadowGrowthEvents` with human-readable descriptions of what grew and why.

---

## 2. New Types

### 2.1 ShadowGrowthDetector (internal helper)

**File:** `src/Pinder.Core/Conversation/ShadowGrowthDetector.cs`
**Namespace:** `Pinder.Core.Conversation`

An internal (non-public) helper class that encapsulates shadow growth detection logic, keeping `GameSession` from being bloated with 17+ condition checks. `GameSession` delegates to this class after each roll and at game end.

```csharp
internal sealed class ShadowGrowthDetector
{
    // Constructor receives the session shadow trackers for the player
    internal ShadowGrowthDetector(SessionShadowTracker? playerShadows);

    // Called after each Speak turn's roll is resolved.
    // Returns list of human-readable growth event strings (e.g. "Dread +1 (Nat 1 on Wit)").
    // Also mutates playerShadows via ApplyGrowth().
    internal IReadOnlyList<string> DetectTurnGrowth(
        RollResult roll,
        StatType statUsed,
        int tropeTrapsThisSession,
        IReadOnlyList<StatType> recentStats,   // last 3 stats used (most recent last)
        InterestMeter interest);

    // Called when the game ends (DateSecured, Unmatched, Ghosted).
    // Returns list of end-of-game growth event strings.
    internal IReadOnlyList<string> DetectEndOfGameGrowth(
        GameOutcome outcome,
        bool hasHonestySuccess,
        HashSet<StatType> allStatsUsed,
        int saUsageCount);

    // Called when a ghost trigger fires in StartTurnAsync.
    // Returns list containing "Dread +1 (Ghosted)" or empty.
    internal IReadOnlyList<string> DetectGhostGrowth();
}
```

If `playerShadows` is `null` (no `SessionShadowTracker` configured), all `Detect*` methods return empty lists and perform no mutations.

### 2.2 Per-Session Tracking State

The following counters are added as private fields to `GameSession` (or to an internal `SessionCounters` helper class if preferred by the implementer):

```csharp
private int _tropeTrapsActivatedCount;          // int, starts at 0
private bool _hasHonestySuccess;                 // bool, starts false
private readonly List<StatType> _statsUsedHistory; // ordered list of stats used each turn
private readonly HashSet<StatType> _allStatsUsed;  // set of all distinct stats used
private int _saUsageCount;                        // int, starts at 0
```

These are **not** public. They exist solely to feed `ShadowGrowthDetector`.

---

## 3. Shadow Growth Table (Complete)

All growth events use `SessionShadowTracker.ApplyGrowth(ShadowStatType shadow, int amount, string reason)`.

### 3.1 Dread (penalizes Wit)

| # | Trigger Condition | Amount | When Detected | Reason String |
|---|---|---|---|---|
| D1 | Interest reaches 0 (unmatch) | +2 | End of game (`GameOutcome.Unmatched`) | `"Dread +2 (Interest hit 0 — unmatched)"` |
| D2 | Getting ghosted | +1 | Ghost trigger fires in `StartTurnAsync` | `"Dread +1 (Ghosted)"` |
| D3 | Catastrophic Wit fail (miss by 10+) | +1 | `ResolveTurnAsync`, after roll, when `roll.Stat == StatType.Wit && roll.Tier == FailureTier.Catastrophe` | `"Dread +1 (Catastrophic Wit fail)"` |
| D4 | Nat 1 on Wit | +1 | `ResolveTurnAsync`, after roll, when `roll.IsNatOne && roll.Stat == StatType.Wit` | `"Dread +1 (Nat 1 on Wit)"` |

**Note on D3 vs D4:** A Nat 1 on Wit that also has miss margin ≥10 triggers **both** D3 and D4 (they are independent conditions). The reason string clearly distinguishes them.

### 3.2 Madness (penalizes Charm)

| # | Trigger Condition | Amount | When Detected | Reason String |
|---|---|---|---|---|
| M1 | Nat 1 on Charm | +1 | `ResolveTurnAsync`, after roll, when `roll.IsNatOne && roll.Stat == StatType.Charm` | `"Madness +1 (Nat 1 on Charm)"` |
| M2 | 3rd trope trap activated in this conversation | +1 | `ResolveTurnAsync`, after roll, when `_tropeTrapsActivatedCount` reaches exactly 3 | `"Madness +1 (3rd trope trap in conversation)"` |
| M3 | Same opener twice in a row | +1 | `ResolveTurnAsync`, on turn 0 (first turn), when the session detects the same opener stat was used in the immediately prior conversation. **Implementation note:** This requires cross-session state that is outside the scope of a single `GameSession`. At prototype maturity, this trigger is **deferred** — the detection hook exists but always returns false unless the host provides prior-opener context via `GameSessionConfig`. |

### 3.3 Denial (penalizes Honesty)

| # | Trigger Condition | Amount | When Detected | Reason String |
|---|---|---|---|---|
| N1 | Date secured without any Honesty successes | +1 | End of game (`GameOutcome.DateSecured` and `_hasHonestySuccess == false`) | `"Denial +1 (Date secured without Honesty)"` |
| N2 | Nat 1 on Honesty | +1 | `ResolveTurnAsync`, after roll, when `roll.IsNatOne && roll.Stat == StatType.Honesty` | `"Denial +1 (Nat 1 on Honesty)"` |

### 3.4 Fixation (penalizes Chaos)

| # | Trigger Condition | Amount | When Detected | Reason String |
|---|---|---|---|---|
| F1 | Same stat used 3 turns in a row | +1 | `ResolveTurnAsync`, after recording the stat to `_statsUsedHistory`. Check: last 3 entries are all the same `StatType`. | `"Fixation +1 (Same stat 3 turns in a row)"` |
| F2 | Never picked Chaos in whole conversation | +1 | End of game, when `!_allStatsUsed.Contains(StatType.Chaos)` | `"Fixation +1 (Never used Chaos)"` |
| F3 | Nat 1 on Chaos | +1 | `ResolveTurnAsync`, after roll, when `roll.IsNatOne && roll.Stat == StatType.Chaos` | `"Fixation +1 (Nat 1 on Chaos)"` |
| F4 | **Offset:** 4+ different stats used in conversation | −1 | End of game, when `_allStatsUsed.Count >= 4` | `"Fixation -1 (4+ different stats used)"` |

**Note on F1:** The "highest-% option picked 3 turns in a row" trigger from the issue body is simplified to "same stat 3 turns in a row" per vision concern #66. "Highest-%" is defined as "highest stat modifier + level bonus" but at prototype, the simpler same-stat-3x check is the implementation target. The issue AC lists "same stat 3 turns" specifically.

**Note on F4:** Fixation cannot go below 0 total (base + delta). If the offset would reduce the session delta below the negation of the base shadow value, clamp at zero effective shadow.

### 3.5 Overthinking (penalizes SelfAwareness)

| # | Trigger Condition | Amount | When Detected | Reason String |
|---|---|---|---|---|
| O1 | Read action failed | +1 | `ReadAsync()` (issue #43), on roll failure | `"Overthinking +1 (Read failed)"` |
| O2 | Recover action failed | +1 | `RecoverAsync()` (issue #43), on roll failure | `"Overthinking +1 (Recover failed)"` |
| O3 | SA used 3+ times in one conversation | +1 | End of game, when `_saUsageCount >= 3` | `"Overthinking +1 (SA used 3+ times)"` |
| O4 | Nat 1 on SA | +1 | `ResolveTurnAsync`, after roll, when `roll.IsNatOne && roll.Stat == StatType.SelfAwareness` | `"Overthinking +1 (Nat 1 on SA)"` |

**Cross-spec ownership note (O1 vs O2):**

- **O1 (Read failed → Overthinking +1)** is fully owned by issue #43. The issue-43 spec's `ReadResult` type includes a `ShadowGrowthEvents` field, and its `ReadAsync` pseudocode includes the `ApplyGrowth` call on failure. No action needed here.
- **O2 (Recover failed → Overthinking +1) is owned by this issue (#44)**, NOT by #43. The issue-43 spec's `RecoverResult` type does **not** include a `ShadowGrowthEvents` field, and the `RecoverAsync` pseudocode in issue-43 does **not** apply Overthinking growth on failure. Therefore, the implementer of issue #44 MUST:
  1. Add an `IReadOnlyList<string> ShadowGrowthEvents` property to `RecoverResult` (matching the pattern already present on `ReadResult`).
  2. Update `RecoverAsync` to call `SessionShadowTracker.ApplyGrowth(ShadowStatType.Overthinking, 1, "Recover failed")` on failure (when a tracker is available) and populate the new `ShadowGrowthEvents` field.
  3. Pass a default of `null` / empty list for backward compatibility when no tracker is configured.

This ensures the Overthinking growth on Recover failure is not missed by either implementer.

### 3.6 Horniness (penalizes Rizz)

No growth triggers defined in §7. Horniness is controlled by time-of-day modifier (issue #51/#54). No implementation needed here.

---

## 4. Integration Points

### 4.1 ResolveTurnAsync (per-turn growth)

After the roll is resolved and interest delta is applied, but **before** constructing the `TurnResult`:

1. Record the stat used: append `roll.Stat` to `_statsUsedHistory` and add to `_allStatsUsed`.
2. If `roll.Stat == StatType.SelfAwareness`, increment `_saUsageCount`.
3. If `roll.Stat == StatType.Honesty && roll.IsSuccess`, set `_hasHonestySuccess = true`.
4. If `roll.Tier == FailureTier.TropeTrap`, increment `_tropeTrapsActivatedCount`.
5. Call `ShadowGrowthDetector.DetectTurnGrowth(roll, stat, _tropeTrapsActivatedCount, recentStats, _interest)`.
6. Collect returned growth event strings and pass them to the `TurnResult` constructor as `shadowGrowthEvents`.

### 4.2 StartTurnAsync (ghost growth)

When a ghost trigger fires (the player is Ghosted):

1. Call `ShadowGrowthDetector.DetectGhostGrowth()` to apply Dread +1.
2. The growth event string is not returned to the caller via `TurnStart` (since the game ends via `GameEndedException`). However, the growth is applied to `SessionShadowTracker` so the host can read final shadow state.

### 4.3 Game End (end-of-game growth)

When the game ends (any `GameOutcome`), before throwing `GameEndedException` or returning the final `TurnResult`:

1. Call `ShadowGrowthDetector.DetectEndOfGameGrowth(outcome, _hasHonestySuccess, _allStatsUsed, _saUsageCount)`.
2. For `GameOutcome.Unmatched`: applies Dread +2.
3. For `GameOutcome.DateSecured` without Honesty successes: applies Denial +1.
4. For any outcome: checks Fixation (never-Chaos +1, 4+-stats offset -1) and Overthinking (SA 3+ times +1).
5. End-of-game growth events are included in the final `TurnResult.ShadowGrowthEvents` (merged with any per-turn events from the same final turn).
6. If the game ends via `GameEndedException` (ghost, unmatch detected at start of turn), the growth events are applied to `SessionShadowTracker` but not returned in a `TurnResult`. The host reads final shadow state from the tracker.

### 4.4 ReadAsync / RecoverAsync (Overthinking growth)

- **ReadAsync (O1):** Owned by issue #43. The `ReadAsync` method already applies Overthinking +1 on failure via `SessionShadowTracker.ApplyGrowth()` per the issue-43 spec. The growth event string is included in `ReadResult.ShadowGrowthEvents` (defined in issue #43 spec). No action needed from this issue.
- **RecoverAsync (O2):** **Owned by this issue (#44).** The issue-43 spec's `RecoverResult` does not include a `ShadowGrowthEvents` field, and `RecoverAsync` does not apply Overthinking growth on failure. The implementer of this issue must:
  1. Add `IReadOnlyList<string> ShadowGrowthEvents { get; }` to `RecoverResult` (with a constructor parameter defaulting to `null`).
  2. In `RecoverAsync`, on failure, call `_playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Recover failed")` (when tracker is available) and include the returned event string in `RecoverResult.ShadowGrowthEvents`.
  3. On success, or when no tracker is configured, `ShadowGrowthEvents` is an empty list.

---

## 5. Input/Output Examples

### Example 1: Nat 1 on Wit (Dread growth)

**Setup:** Player uses Wit stat. Dice roll returns 1 (Nat 1). DC is 15. Miss margin is 14 (≥10 → Catastrophe).

**Expected `TurnResult.ShadowGrowthEvents`:**
```
["Dread +1 (Catastrophic Wit fail)", "Dread +1 (Nat 1 on Wit)"]
```

**Expected `SessionShadowTracker` delta:** Dread increased by +2 total.

### Example 2: Same stat 3 turns in a row (Fixation growth)

**Setup:** Turns 1–3 all use `StatType.Charm`. Turn 3 roll is a normal success.

**Turn 3 `TurnResult.ShadowGrowthEvents`:**
```
["Fixation +1 (Same stat 3 turns in a row)"]
```

### Example 3: Third trope trap activates (Madness growth)

**Setup:** Two trope traps already activated this session. Turn 5 roll is a TropeTrap failure on any stat.

**Turn 5 `TurnResult.ShadowGrowthEvents`:**
```
["Madness +1 (3rd trope trap in conversation)"]
```

### Example 4: End-of-game — DateSecured, no Honesty, never used Chaos, used 4+ stats

**Setup:** Game ends with `DateSecured`. `_hasHonestySuccess == false`. `_allStatsUsed = {Charm, Wit, Rizz, SA}` (4 distinct, no Chaos). `_saUsageCount == 4`.

**Final `TurnResult.ShadowGrowthEvents` (end-of-game portion):**
```
["Denial +1 (Date secured without Honesty)", "Fixation +1 (Never used Chaos)", "Fixation -1 (4+ different stats used)", "Overthinking +1 (SA used 3+ times)"]
```

Net Fixation delta from end-of-game: 0 (+1 for never-Chaos, −1 for variety offset).

### Example 5: Unmatch (Interest hits 0)

**Setup:** Interest drops to 0 on this turn's resolution.

**Final `TurnResult.ShadowGrowthEvents`:**
```
["Dread +2 (Interest hit 0 — unmatched)"]
```

Plus any per-turn growth events from the roll that caused the unmatch.

### Example 6: Recover failure triggers Overthinking (O2)

**Setup:** Player has an active trap. `SessionShadowTracker` is available. Player calls `RecoverAsync()`. Dice roll returns 3 (total below DC 12). Roll fails.

**Expected `RecoverResult.ShadowGrowthEvents`:**
```
["Overthinking +1 (Recover failed)"]
```

**Expected `SessionShadowTracker` delta:** Overthinking increased by +1.
**Expected `RecoverResult.Success`:** `false`

### Example 7: No shadow tracker configured

**Setup:** `GameSession` constructed without `SessionShadowTracker` (e.g., `GameSessionConfig` is null or has null shadow tracker). Player rolls Nat 1 on Charm.

**Expected `TurnResult.ShadowGrowthEvents`:** `[]` (empty list). No error thrown.

---

## 6. Acceptance Criteria

### AC1: All shadow growth events from §7 implemented

All 17 triggers enumerated in Section 3 (D1–D4, M1–M3, N1–N2, F1–F4, O1–O4) plus the F4 offset are implemented. Each trigger is detectable at the correct moment (per-turn, ghost, or end-of-game).

### AC2: Shadow mutations go through SessionShadowTracker, NOT StatBlock._shadow

All shadow growth calls use `SessionShadowTracker.ApplyGrowth(ShadowStatType, int, string)`. No code reads or writes `StatBlock._shadow` directly. If `SessionShadowTracker` is null, growth is silently skipped.

### AC3: Per-session counters tracked

The following counters are maintained within `GameSession`:
- `_tropeTrapsActivatedCount` (int): incremented each time `roll.Tier == FailureTier.TropeTrap`
- `_hasHonestySuccess` (bool): set to `true` when any Honesty roll succeeds
- `_statsUsedHistory` (List<StatType>): appended each Speak turn
- `_allStatsUsed` (HashSet<StatType>): all distinct stats ever used in the session
- `_saUsageCount` (int): incremented when `roll.Stat == StatType.SelfAwareness`

Additionally, `RecoverAsync` must apply Overthinking +1 on failure (trigger O2) and populate `RecoverResult.ShadowGrowthEvents` with the resulting event string. See §4.4 for ownership details.

### AC4: TurnResult.ShadowGrowthEvents populated when shadow grows

Every `TurnResult` returned from `ResolveTurnAsync` includes a non-null `IReadOnlyList<string>` of growth event descriptions. The list is empty when no growth occurs and contains one entry per growth trigger that fired.

### AC5: Tests verify key triggers

Tests must verify at minimum:
- Dread +2 when interest reaches 0 (unmatch)
- Fixation +1 when same stat used 3 turns in a row
- Madness +1 on Nat 1 on Charm
- Multiple growth events in a single turn (e.g., Nat 1 on Wit triggering both D3 and D4)
- End-of-game triggers fire correctly (Denial on DateSecured without Honesty)
- No growth events when SessionShadowTracker is null
- Overthinking +1 on Recover failure (O2) — `RecoverResult.ShadowGrowthEvents` contains `"Overthinking +1 (Recover failed)"`

### AC6: Build clean

`dotnet build` succeeds with zero errors and zero warnings. All existing 254+ tests continue to pass.

---

## 7. Edge Cases

### 7.1 Multiple triggers on the same turn
A single roll can fire multiple growth events. Example: Nat 1 on Wit with miss margin ≥10 fires both D3 (Catastrophe) and D4 (Nat 1). All fired events are collected and returned in `TurnResult.ShadowGrowthEvents`.

### 7.2 Same-stat-3x at turns 3, 4, 5...
If a player uses Charm for turns 1–5, Fixation +1 fires on turn 3 (first time three consecutive same-stat turns are detected). It fires **again** on turn 4 (turns 2–4 are same) and turn 5 (turns 3–5 are same). Each occurrence is a separate +1 growth event. The trigger checks the last 3 entries in `_statsUsedHistory` each turn.

### 7.3 TropeTrap count at exactly 3
Madness +1 fires when `_tropeTrapsActivatedCount` reaches exactly 3. It does **not** fire again at 4, 5, etc. The trigger is: `_tropeTrapsActivatedCount == 3` after incrementing.

### 7.4 Fixation offset cannot reduce below zero
If the player's base Fixation shadow is 0 and the only end-of-game event is the −1 offset (4+ stats used), the delta is clamped so effective Fixation does not go below 0. `SessionShadowTracker.ApplyGrowth(Fixation, -1, ...)` is still called, but the tracker should clamp the effective value.

### 7.5 No SessionShadowTracker configured
When `GameSession` is constructed without a `SessionShadowTracker` (null tracker), all shadow growth detection is skipped. No errors are thrown. `TurnResult.ShadowGrowthEvents` is an empty list. This ensures backward compatibility with existing tests and configurations.

### 7.6 Game ends on the same turn as per-turn growth
When a turn's interest delta causes the game to end (e.g., interest drops to 0), both per-turn growth events AND end-of-game growth events are collected and merged into the final `TurnResult.ShadowGrowthEvents`.

### 7.7 Ghost at start of turn (no TurnResult)
When ghosting occurs in `StartTurnAsync`, Dread +1 is applied to `SessionShadowTracker` but there is no `TurnResult` to attach the event to (since `GameEndedException` is thrown). The host must read the shadow state from the tracker after catching the exception.

### 7.8 Nat 1 is always a failure
A Nat 1 always triggers the corresponding shadow growth regardless of modifiers. Even if modifiers would make the total exceed the DC, `IsNatOne` is true and the roll is a failure, so the Nat 1 shadow trigger fires.

### 7.9 Empty conversation (0 turns played)
If the game ends before any turns are played (e.g., immediate ghost), end-of-game checks run with: `_hasHonestySuccess == false`, `_allStatsUsed` is empty, `_saUsageCount == 0`. This means:
- Fixation +1 (never used Chaos) fires — correct, Chaos was never used.
- Denial +1 fires only if outcome is `DateSecured` — cannot happen with 0 turns normally.
- Overthinking does not fire (SA count < 3).

### 7.10 Highest-% option tracking (deferred)
The issue body mentions "highest-% option picked 3 turns in a row" as a Fixation trigger. Per vision concern #66, at prototype maturity this is simplified to same-stat-3x (F1). Full highest-% tracking is deferred to a future issue.

---

## 8. Error Conditions

### 8.1 SessionShadowTracker is null
**Behavior:** All growth detection is silently skipped. No exceptions thrown. This is the normal backward-compatible path.

### 8.2 Invalid ShadowStatType
Should not occur since all triggers use hardcoded `ShadowStatType` enum values. If somehow an invalid value is passed to `ApplyGrowth`, `SessionShadowTracker` should throw `ArgumentOutOfRangeException`.

### 8.3 Growth on already-ended game
If `ResolveTurnAsync` is called after the game has ended, `GameSession` should throw `InvalidOperationException` (existing behavior). Shadow growth detection does not need to guard against this separately.

### 8.4 Concurrent access
`GameSession` is not thread-safe (existing design). Shadow growth detection inherits this constraint. No locking is required.

---

## 9. Dependencies

| Dependency | Issue | What's needed |
|---|---|---|
| `SessionShadowTracker` | #139 (Wave 0) | `ApplyGrowth(ShadowStatType, int, string)` method, `GetDelta(ShadowStatType)` for reading |
| `RollEngine.ResolveFixedDC` | #139 (Wave 0) | Used by Read/Recover (issue #43), not directly by #44 |
| Read/Recover actions | #43 | `ReadAsync` and `RecoverAsync` apply Overthinking +1 on failure (O1, O2) |
| `TurnResult.ShadowGrowthEvents` | PR #117 (merged) | Already exists as `IReadOnlyList<string>` — no changes needed |
| `RecoverResult` | #43 | Created by #43 — this issue (#44) adds `ShadowGrowthEvents` property and populates it with O2 trigger |
| `RollResult.Stat` | Existing | `StatType` property on `RollResult` — used to detect which stat was rolled |
| `RollResult.IsNatOne` | Existing | Boolean — used to detect Nat 1 triggers |
| `RollResult.Tier` | Existing | `FailureTier` — used to detect Catastrophe and TropeTrap |
| `InterestMeter` | Existing | `Current` property and `GetState()` — used to detect unmatch |
| `GameOutcome` | Existing | Enum — used to determine which end-of-game triggers apply |
| `StatBlock.ShadowPairs` | Existing | Maps `StatType → ShadowStatType` — used to determine which shadow grows for Nat 1 triggers |

---

## 10. Ordering of Operations in ResolveTurnAsync

For clarity, the exact position of shadow growth detection in the `ResolveTurnAsync` flow:

```
1. Validate option index
2. Compute external bonuses (callback, tell, triple combo) — issues #47, #49, #46
3. Compute DC adjustment (weakness window) — issue #50
4. RollEngine.Resolve() → RollResult
5. Compute interest delta from SuccessScale/FailureScale
6. Add RiskTierBonus, momentum, combo interest bonus
7. Apply interest delta → InterestMeter.Apply()
8. ── SHADOW GROWTH DETECTION (this issue) ──
   a. Record stat in _statsUsedHistory, _allStatsUsed
   b. Update _saUsageCount, _hasHonestySuccess, _tropeTrapsActivatedCount
   c. Detect per-turn growth events → collect strings
   d. If game just ended → detect end-of-game growth events → merge
9. Advance trap timers
10. LLM calls (deliver message, get response)
11. Construct and return TurnResult (with shadowGrowthEvents from step 8)
```

Shadow growth does **not** affect the current turn's roll or interest delta. It is a post-resolution side effect that records corruption for future turns.

### Ordering of Operations in RecoverAsync (O2 trigger)

```
1. Validate active trap exists
2. Check end conditions
3. RollEngine.ResolveFixedDC(SA, 12) → RollResult
4. If success: clear trap, xp = 15
5. If failure:
   a. Apply −1 interest
   b. xp = 2
   c. If _playerShadows != null:
      → _playerShadows.ApplyGrowth(Overthinking, 1, "Recover failed")
      → collect event string into shadowEvents list
6. Advance trap timers
7. _turnNumber++
8. _currentOptions = null
9. Check end conditions (interest == 0 → Unmatched)
10. Record XP
11. Return RecoverResult(success, clearedTrapName, roll, snapshot, xp, shadowEvents)
```

### Ordering of Operations in Wait

Wait does **not** involve any shadow growth detection (no roll, no stat usage). For completeness:

```
1. If _ended → throw GameEndedException
2. Apply −1 interest
3. Advance trap timers
4. _turnNumber++
5. _currentOptions = null
6. Check end conditions (interest == 0 → _ended = true, _outcome = Unmatched)
```
