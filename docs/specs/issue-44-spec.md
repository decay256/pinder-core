# Spec: Shadow Growth Events — §7 Growth Table in GameSession

**Issue:** #44
**Depends on:** #43 (Read/Recover actions), #130 (Wave 0 — SessionShadowTracker)
**Component:** `Pinder.Core.Conversation.GameSession`, `Pinder.Core.Stats.SessionShadowTracker`
**Target:** .NET Standard 2.0, C# 8.0, zero NuGet dependencies
**Maturity:** Prototype

---

## 1. Overview

Shadow stats in the RPG engine grow in response to specific in-game events — bad rolls, behavioural patterns, and game outcomes. Rules v3.4 §7 defines the complete growth table with 17 distinct triggers across five shadow stats (Dread, Madness, Denial, Fixation, Overthinking). This feature implements all shadow growth triggers inside `GameSession`, using `SessionShadowTracker` (from Wave 0 prerequisite #130) to mutate shadow deltas without touching the immutable `StatBlock`. Growth events are surfaced to the host via the existing `TurnResult.ShadowGrowthEvents` field so the UI can display narrative descriptions.

---

## 2. Architectural Decisions

### ADR #161: SessionShadowTracker is canonical — no CharacterState class

The original #44 spec and architect comment (#58) proposed a `CharacterState` wrapper. Per the Sprint 8 architecture review (#139, ADR #161), `SessionShadowTracker` is the sole shadow-tracking wrapper. It wraps an immutable `StatBlock` with mutable per-session shadow deltas. The `CharacterState` class is **not created**.

`SessionShadowTracker` provides:
- `ApplyGrowth(ShadowStatType shadow, int delta, string reason)` — accumulates delta and logs reason
- `DrainGrowthEvents() → IReadOnlyList<string>` — returns accumulated growth descriptions, clears internal log
- `GetEffectiveStat(StatType stat) → int` — returns effective modifier accounting for session shadow growth

### ADR #162: previousOpener in GameSessionConfig

The `previousOpener` value (for detecting "same opener twice in a row") is read from `GameSessionConfig.PreviousOpener`, not passed as a constructor parameter.

### ADR #163: TurnResult.ShadowGrowthEvents already exists

`TurnResult` already has an `IReadOnlyList<string> ShadowGrowthEvents` property (added by PR #117). This spec only requires **populating** it — no new field needed.

---

## 3. Shadow Growth Trigger Table

All 17 triggers from §7, grouped by shadow stat. Each trigger lists the condition, growth amount, reason string format, and when GameSession checks it.

### 3.1 Dread (penalizes Wit)

| # | Trigger Condition | Amount | Reason String | Checked In |
|---|---|---|---|---|
| D1 | Interest reaches 0 (Unmatched) | +2 | `"Interest hit 0 (unmatch): +2 Dread"` | `ResolveTurnAsync` (after interest apply) |
| D2 | Getting ghosted | +1 | `"Ghosted: +1 Dread"` | `StartTurnAsync` (on ghost trigger) |
| D3 | Catastrophic Wit failure (miss by 10+, tier = `FailureTier.Catastrophe`) | +1 | `"Catastrophic Wit failure (miss by 10+): +1 Dread"` | `ResolveTurnAsync` (after roll) |
| D4 | Nat 1 on Wit | +1 | `"Nat 1 on Wit: +1 Dread"` | `ResolveTurnAsync` (after roll) |

### 3.2 Madness (penalizes Charm)

| # | Trigger Condition | Amount | Reason String | Checked In |
|---|---|---|---|---|
| M1 | Nat 1 on Charm | +1 | `"Nat 1 on Charm: +1 Madness"` | `ResolveTurnAsync` (after roll) |
| M2 | 3+ TropeTrap-tier (or worse) failures in one conversation | +1 | `"3+ trope traps in one conversation: +1 Madness"` | `ResolveTurnAsync` (after roll) |
| M3 | Same opener text as previous conversation | +1 | `"Same opener twice in a row: +1 Madness"` | `ResolveTurnAsync` (turn 0 only) |

### 3.3 Denial (penalizes Honesty)

| # | Trigger Condition | Amount | Reason String | Checked In |
|---|---|---|---|---|
| DN1 | Date secured (`GameOutcome.DateSecured`) with zero successful Honesty rolls | +1 | `"Date secured without any Honesty successes: +1 Denial"` | `ResolveTurnAsync` (end-of-game check) |
| DN2 | Nat 1 on Honesty | +1 | `"Nat 1 on Honesty: +1 Denial"` | `ResolveTurnAsync` (after roll) |

### 3.4 Fixation (penalizes Chaos)

| # | Trigger Condition | Amount | Reason String | Checked In |
|---|---|---|---|---|
| F1 | Highest-% option picked 3 turns in a row | +1 | `"Highest-% option picked 3 turns in a row: +1 Fixation"` | `ResolveTurnAsync` (after option recorded) |
| F2 | Same stat used 3 turns in a row | +1 | `"Same stat ({statName}) used 3 turns in a row: +1 Fixation"` | `ResolveTurnAsync` (after stat recorded) |
| F3 | Never picked Chaos in whole conversation (on game end) | +1 | `"Never picked Chaos in whole conversation: +1 Fixation"` | `ResolveTurnAsync` (end-of-game check) |
| F4 | Nat 1 on Chaos | +1 | `"Nat 1 on Chaos: +1 Fixation"` | `ResolveTurnAsync` (after roll) |
| F5 | 4+ different stats used in one conversation (on game end) — **offset** | −1 | `"4+ different stats used in conversation: -1 Fixation"` | `ResolveTurnAsync` (end-of-game check) |

### 3.5 Overthinking (penalizes SelfAwareness)

| # | Trigger Condition | Amount | Reason String | Checked In |
|---|---|---|---|---|
| O1 | Read action failed | +1 | `"Read action failed: +1 Overthinking"` | `ReadAsync` (from #43) |
| O2 | Recover action failed | +1 | `"Recover action failed: +1 Overthinking"` | `RecoverAsync` (from #43) |
| O3 | SelfAwareness used 3+ times in one conversation | +1 | `"SA used 3+ times in one conversation: +1 Overthinking"` | `ResolveTurnAsync` (after stat recorded) |
| O4 | Nat 1 on SelfAwareness | +1 | `"Nat 1 on SelfAwareness: +1 Overthinking"` | `ResolveTurnAsync` (after roll) |

**Note on Horniness:** Horniness is rolled fresh each conversation (1d10) and is not grown by in-session events. No triggers exist for Horniness growth.

---

## 4. Function Signatures

### 4.1 SessionShadowTracker (from Wave 0 #130 — extended)

`SessionShadowTracker` already exists per #130. It must provide:

```csharp
namespace Pinder.Core.Stats
{
    public sealed class SessionShadowTracker
    {
        // Constructor: wraps an immutable StatBlock
        public SessionShadowTracker(StatBlock stats);

        // Apply shadow growth: accumulates delta and logs reason string
        public void ApplyGrowth(ShadowStatType shadow, int delta, string reason);

        // Drain all accumulated growth event descriptions since last drain; clears internal log
        public IReadOnlyList<string> DrainGrowthEvents();

        // Get effective stat modifier accounting for base shadow + session delta
        public int GetEffectiveStat(StatType stat);

        // Get accumulated session delta for a specific shadow type (for testing)
        public int GetShadowDelta(ShadowStatType shadow);
    }
}
```

### 4.2 GameSession — New Internal Tracking Fields

```csharp
// Added to GameSession
private readonly SessionShadowTracker _playerShadows;   // from GameSessionConfig or created internally
private readonly List<StatType> _statsUsedPerTurn;       // stat chosen each Speak turn, in order
private int _honestySuccessCount;                        // count of successful Honesty rolls
private int _saUsageCount;                               // count of SA rolls
private int _tropeTrapCount;                             // count of TropeTrap-or-worse failures
private readonly List<bool> _highestPctOptionPicked;     // per-turn: was highest-% option chosen?
private bool _madnessTropeTrapTriggered;                 // one-shot: 3+ trope traps already fired
private bool _saOverthinkingTriggered;                   // one-shot: SA 3+ already fired
```

### 4.3 GameSession — Constructor Change

```csharp
// Existing constructor remains unchanged for backward compatibility.
// New overload accepts GameSessionConfig:
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config = null)
```

If `config?.PlayerShadows` is provided, use it. Otherwise, create a new `SessionShadowTracker(_player.Stats)`.

### 4.4 GameSession — Shadow Growth Evaluation (private)

```csharp
// Evaluates all per-turn shadow growth triggers after a Speak roll resolves.
// Called from ResolveTurnAsync after interest delta is applied.
private void EvaluateShadowGrowth(
    RollResult rollResult,
    DialogueOption chosenOption,
    int optionIndex,
    bool isGameOver,
    GameOutcome? outcome);
```

Returns void — growth events are accumulated in `_playerShadows` and drained later.

### 4.5 ReadAsync / RecoverAsync (from #43 — shadow integration)

```csharp
// When Read fails: call _playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Read action failed: +1 Overthinking")
// When Recover fails: call _playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Recover action failed: +1 Overthinking")
// Both methods drain growth events and include them in their return types.
```

`ReadResult` and `RecoverResult` (defined by #43) must include a `ShadowGrowthEvents` field of type `IReadOnlyList<string>`.

---

## 5. Input/Output Examples

### Example 1: Nat 1 on Charm

**Setup:** Player picks a Charm option. Die roll = 1 (Legendary failure).
**Shadow growth calls:**
```
_playerShadows.ApplyGrowth(ShadowStatType.Madness, 1, "Nat 1 on Charm: +1 Madness")
```
**TurnResult.ShadowGrowthEvents:** `["Nat 1 on Charm: +1 Madness"]`

### Example 2: Interest drops to 0

**Setup:** Interest is at 1. Roll fails with interest delta −2. After apply, interest = 0.
**Shadow growth calls:**
```
_playerShadows.ApplyGrowth(ShadowStatType.Dread, 2, "Interest hit 0 (unmatch): +2 Dread")
```
**TurnResult.ShadowGrowthEvents:** `["Interest hit 0 (unmatch): +2 Dread"]`

### Example 3: Multiple triggers in one turn

**Setup:** Player uses Wit for the 3rd consecutive turn. Die roll = 1 (Nat 1).
**Shadow growth calls:**
```
_playerShadows.ApplyGrowth(ShadowStatType.Dread, 1, "Nat 1 on Wit: +1 Dread")
_playerShadows.ApplyGrowth(ShadowStatType.Fixation, 1, "Same stat (Wit) used 3 turns in a row: +1 Fixation")
```
**TurnResult.ShadowGrowthEvents:** `["Nat 1 on Wit: +1 Dread", "Same stat (Wit) used 3 turns in a row: +1 Fixation"]`

### Example 4: End-of-game — Date secured with Fixation offset

**Setup:** Game ends (DateSecured at interest=25). Player used Charm, Wit, Honesty, Chaos across turns. Had 1 Honesty success. Never used same stat 3x in a row.
**Shadow growth calls (end-of-game only):**
```
_playerShadows.ApplyGrowth(ShadowStatType.Fixation, -1, "4+ different stats used in conversation: -1 Fixation")
```
**TurnResult.ShadowGrowthEvents** (final turn): `["4+ different stats used in conversation: -1 Fixation"]`

### Example 5: Read action failure

**Setup:** Player chooses Read action. `RollEngine.ResolveFixedDC(SelfAwareness, 12)` fails.
**Shadow growth calls:**
```
_playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Read action failed: +1 Overthinking")
```
**ReadResult.ShadowGrowthEvents:** `["Read action failed: +1 Overthinking"]`

### Example 6: Recover action failure

**Setup:** Player chooses Recover action. `RollEngine.ResolveFixedDC(SelfAwareness, 12)` fails.
**Shadow growth calls:**
```
_playerShadows.ApplyGrowth(ShadowStatType.Overthinking, 1, "Recover action failed: +1 Overthinking")
```
**RecoverResult.ShadowGrowthEvents:** `["Recover action failed: +1 Overthinking"]`

### Example 7: Same opener twice in a row

**Setup:** `GameSessionConfig.PreviousOpener` = `"Hey there cutie"`. On turn 0, player picks option with `IntendedText` = `"hey there cutie"` (different casing).
**Shadow growth calls:**
```
_playerShadows.ApplyGrowth(ShadowStatType.Madness, 1, "Same opener twice in a row: +1 Madness")
```
**TurnResult.ShadowGrowthEvents:** `["Same opener twice in a row: +1 Madness"]`

---

## 6. Acceptance Criteria

### AC1: All shadow growth events from §7 implemented

All 17 triggers listed in Section 3 are implemented:
- **Dread (4):** D1 interest=0 (+2), D2 ghosted (+1), D3 catastrophic Wit fail (+1), D4 Nat 1 Wit (+1)
- **Madness (3):** M1 Nat 1 Charm (+1), M2 3+ trope traps (+1), M3 same opener twice (+1)
- **Denial (2):** DN1 date secured without Honesty success (+1), DN2 Nat 1 Honesty (+1)
- **Fixation (5):** F1 highest-% 3 in a row (+1), F2 same stat 3 in a row (+1), F3 never Chaos (+1), F4 Nat 1 Chaos (+1), F5 offset 4+ stats (−1)
- **Overthinking (4):** O1 Read fail (+1), O2 Recover fail (+1), O3 SA 3+ times (+1), O4 Nat 1 SA (+1)

### AC2: Shadow mutations go through SessionShadowTracker, NOT StatBlock

- All shadow growth calls use `_playerShadows.ApplyGrowth(shadow, delta, reason)`.
- `StatBlock._shadow` is never accessed or mutated.
- `SessionShadowTracker.GetEffectiveStat()` is used for shadow-affected stat checks within GameSession.

### AC3: Per-session counters tracked

GameSession tracks these counters (initialized per session, never persisted):
- `_tropeTrapCount` (int) — incremented on each TropeTrap-or-worse failure
- `_honestySuccessCount` (int) — incremented on each successful Honesty roll
- `_saUsageCount` (int) — incremented each time SelfAwareness is used
- `_statsUsedPerTurn` (List\<StatType\>) — stat chosen appended each Speak turn
- `_highestPctOptionPicked` (List\<bool\>) — whether highest-% option was chosen each turn

### AC4: TurnResult.ShadowGrowthEvents populated when shadow grows

- After all per-turn triggers are evaluated, `_playerShadows.DrainGrowthEvents()` is called.
- The returned list is passed to the `TurnResult` constructor as the `shadowGrowthEvents` parameter.
- When no growth occurs, an empty list is passed (existing default behavior via `Array.Empty<string>()`).
- Each growth event appears in exactly one `TurnResult` — events are drained (cleared) after collection.

### AC5: Overthinking growth on both Read and Recover failure

- `ReadAsync` applies `+1 Overthinking` on roll failure and includes the event in `ReadResult.ShadowGrowthEvents`.
- `RecoverAsync` applies `+1 Overthinking` on roll failure and includes the event in `RecoverResult.ShadowGrowthEvents`.
- Both actions drain growth events from `_playerShadows` after applying growth.

### AC6: Tests verify key triggers

Tests must verify at minimum:
- Dread +2 when interest reaches 0 (D1)
- Fixation +1 when same stat used 3 turns in a row (F2)
- Madness +1 when Nat 1 on Charm (M1)
- Denial +1 when DateSecured with 0 Honesty successes (DN1)
- Fixation −1 offset when 4+ distinct stats used (F5)
- Overthinking +1 on Read failure (O1)
- Overthinking +1 on Recover failure (O2)

### AC7: Build clean

- `dotnet build` succeeds with zero errors.
- All existing tests continue to pass (backward compatibility maintained).

---

## 7. Detailed Trigger Evaluation Logic

### 7.1 Per-Turn Triggers (in ResolveTurnAsync, after interest apply)

Evaluated in this order after `_interest.Apply(interestDelta)`:

1. **Nat 1 triggers (D4, M1, DN2, F4, O4):** If `rollResult.IsNatOne`, apply `+1` to the shadow paired with the chosen stat via `StatBlock.ShadowPairs[chosenOption.Stat]`. This single check covers all five Nat-1 triggers.

2. **Catastrophic Wit fail (D3):** If `chosenOption.Stat == StatType.Wit` AND `!rollResult.IsSuccess` AND `rollResult.Tier == FailureTier.Catastrophe`, apply `+1 Dread`. Note: if the Wit roll is a Nat 1, tier is `Legendary` not `Catastrophe` — D3 does NOT fire (D4 fires instead).

3. **TropeTrap count (M2):** If `!rollResult.IsSuccess` AND `rollResult.Tier >= FailureTier.TropeTrap` (i.e., TropeTrap, Catastrophe, or Legendary), increment `_tropeTrapCount`. If `_tropeTrapCount == 3` AND `!_madnessTropeTrapTriggered`, apply `+1 Madness` and set `_madnessTropeTrapTriggered = true`. Fires once per session.

4. **Same stat 3 in a row (F2):** Append `chosenOption.Stat` to `_statsUsedPerTurn`. If `_statsUsedPerTurn.Count >= 3` and the last 3 entries are identical, apply `+1 Fixation`. Re-trigger rule: fires every time a new group of 3 consecutive same-stat turns is completed (i.e., on turn indices where consecutive-count mod 3 == 0). Simplest correct implementation: fire whenever the last 3 are equal AND the count of consecutive same-stat entries at the tail just became a multiple of 3.

5. **Highest-% option 3 in a row (F1):** Determine the "highest-percentage option" as the option with the highest effective stat modifier (`_playerShadows.GetEffectiveStat(option.Stat) + _player.Level` — approximates success probability without full DC calculation). Record `true` in `_highestPctOptionPicked` if `optionIndex` matches that option's index, `false` otherwise. If the last 3 entries are all `true`, apply `+1 Fixation`. Same re-trigger logic as F2.

6. **Honesty success tracking (for DN1):** If `chosenOption.Stat == StatType.Honesty` AND `rollResult.IsSuccess`, increment `_honestySuccessCount`.

7. **SA usage tracking (O3):** If `chosenOption.Stat == StatType.SelfAwareness`, increment `_saUsageCount`. If `_saUsageCount == 3` AND `!_saOverthinkingTriggered`, apply `+1 Overthinking` and set `_saOverthinkingTriggered = true`. Fires once per session.

8. **Interest hits 0 (D1):** If `_interest.Current == 0` after applying delta (equivalently `_interest.IsZero`), apply `+2 Dread`.

9. **Same opener twice (M3):** On `_turnNumber == 0` only: compare `chosenOption.IntendedText` (trimmed, case-insensitive via `StringComparison.OrdinalIgnoreCase`) against `config.PreviousOpener`. If they match, apply `+1 Madness`. Also store the opener text for the host to persist (via a new `GameSession.CurrentOpener` property or on `TurnResult`).

### 7.2 End-of-Game Triggers (in ResolveTurnAsync, when isGameOver == true)

Evaluated after per-turn triggers when the game ends:

10. **Date secured without Honesty (DN1):** If `outcome == GameOutcome.DateSecured` AND `_honestySuccessCount == 0`, apply `+1 Denial`.

11. **Never picked Chaos (F3):** If `_statsUsedPerTurn` does not contain `StatType.Chaos`, apply `+1 Fixation`.

12. **Fixation offset (F5):** If `_statsUsedPerTurn` contains 4 or more distinct `StatType` values, apply `−1 Fixation`.

### 7.3 Ghost Trigger (in StartTurnAsync)

13. **Getting ghosted (D2):** When the ghost roll triggers (`_interest.GetState() == InterestState.Bored` AND `_dice.Roll(4) == 1`): apply `+1 Dread` to `_playerShadows` BEFORE throwing `GameEndedException`. The growth events must be accessible after the exception. Two acceptable approaches:
    - **Option A:** Add `IReadOnlyList<string> ShadowGrowthEvents` property to `GameEndedException`, populated by draining `_playerShadows` before throwing.
    - **Option B:** Store a "pending ghost growth" list on GameSession that the host can retrieve after catching the exception.
    - **Recommended:** Option A (self-contained, no host-side coordination needed).

### 7.4 Read/Recover Triggers (in ReadAsync/RecoverAsync from #43)

14. **Read failure (O1):** After `RollEngine.ResolveFixedDC(StatType.SelfAwareness, 12)` returns a failure, apply `+1 Overthinking`. Drain events and include in `ReadResult.ShadowGrowthEvents`.

15. **Recover failure (O2):** After `RollEngine.ResolveFixedDC(StatType.SelfAwareness, 12)` returns a failure, apply `+1 Overthinking`. Drain events and include in `RecoverResult.ShadowGrowthEvents`.

### 7.5 Drain and Attach

After all applicable triggers for a turn/action are evaluated:
```
var events = _playerShadows.DrainGrowthEvents();
// Pass `events` to TurnResult / ReadResult / RecoverResult constructor
```

---

## 8. Edge Cases

| Scenario | Expected Behaviour |
|----------|-------------------|
| Multiple Nat 1s in same session | Each Nat 1 triggers +1 to the corresponding shadow. They accumulate. |
| Nat 1 on Wit (Legendary tier) | D4 fires (+1 Dread). D3 does NOT fire (tier is Legendary, not Catastrophe). |
| Nat 1 on Wit with TropeTrap also active | D4 fires. TropeTrap count also increments (Legendary ≥ TropeTrap). |
| Catastrophic Wit fail that is NOT Nat 1 | D3 fires (+1 Dread). D4 does not fire. |
| Exactly 3 TropeTrap failures | M2 fires once. Does NOT fire again at 4, 5, etc. |
| Same stat streak of 6 turns | F2 fires at turn 3 and again at turn 6 (every 3rd consecutive). |
| Same stat streak of 4 turns | F2 fires at turn 3 only. Turn 4 does not form a new group of 3. |
| Player uses only 3 distinct stats | F5 offset does NOT apply (requires 4+). |
| Session with 0 completed turns (ghost on turn 1 start) | Only D2 fires. No per-turn or end-of-game triggers. |
| `config.PreviousOpener` is null | M3 check skips — no comparison, no Madness growth. |
| `config.PreviousOpener` matches with different casing/whitespace | Match is case-insensitive, trimmed. `"Hello there"` matches `"  hello THERE  "`. |
| Fixation offset AND Fixation growth in same game | Both apply. E.g., +1 (F3 never Chaos) and −1 (F5 offset) net to 0 Fixation delta. |
| Shadow delta goes negative from offset | Allowed. `GetShadowDelta` can return negative values. |
| No shadow growth in a turn | `DrainGrowthEvents()` returns empty list. `TurnResult.ShadowGrowthEvents` is empty (not null). |
| Interest drops to 0 AND ghost in same concept | Cannot happen — ghost only triggers when Bored (interest 1–4), unmatch requires interest reaching 0. These are mutually exclusive paths. |
| Both D1 (interest=0) and other triggers in same turn | D1 fires alongside any roll-based triggers. E.g., Nat 1 on Charm + interest hits 0 = M1 (+1 Madness) + D1 (+2 Dread). |
| Backward compatibility: existing constructor without config | Creates internal `SessionShadowTracker` from `_player.Stats`. All shadow tracking works; PreviousOpener is null (M3 skipped). |
| Existing tests | The existing `TurnResult` constructor already accepts optional `shadowGrowthEvents` defaulting to null/empty. No breaking change to existing call sites. |

---

## 9. Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `SessionShadowTracker` constructed with null `StatBlock` | `ArgumentNullException` | `"stats"` |
| `ApplyGrowth` called with null or empty `reason` | `ArgumentException` | `"reason cannot be null or empty"` |
| `GameSession` constructor with null `player` | `ArgumentNullException` | `"player"` (existing behavior) |
| Ghost triggers shadow growth, game ends via exception | `GameEndedException` carries `ShadowGrowthEvents` property (drained before throw) |
| Read/Recover called after game ended | `GameEndedException` (existing behavior from #43) |

---

## 10. Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| #130 (Wave 0 — SessionShadowTracker) | Hard | Required before implementation | Provides `SessionShadowTracker` with `ApplyGrowth`, `DrainGrowthEvents`, `GetEffectiveStat` |
| #43 (Read/Recover/Wait actions) | Hard | Required for O1/O2 triggers | Defines `ReadAsync`, `RecoverAsync`, `ReadResult`, `RecoverResult`. Shadow growth for Read/Recover failure is owned by #44 but integrated into #43's methods |
| `GameSessionConfig` (from #139) | Soft | Expected from Wave 0 | Provides `PlayerShadows` and `PreviousOpener` fields |
| `StatBlock.ShadowPairs` | Internal | Exists | Static dictionary mapping `StatType` → `ShadowStatType` |
| `RollResult` | Internal | Exists | `IsNatOne`, `IsSuccess`, `Tier`, `Stat` properties |
| `FailureTier` | Internal | Exists | `TropeTrap`, `Catastrophe`, `Legendary` enum values |
| `TurnResult.ShadowGrowthEvents` | Internal | Exists (PR #117) | `IReadOnlyList<string>`, optional param in constructor |
| `InterestMeter` | Internal | Exists | `Current`, `IsZero`, `GetState()` |
| `GameEndedException` | Internal | Exists | Needs new `ShadowGrowthEvents` property for ghost-triggered growth |

---

## 11. Files to Create or Modify

| File | Action | Description |
|------|--------|-------------|
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modify** | Add `SessionShadowTracker` field, tracking counters, shadow growth evaluation logic, constructor overload with `GameSessionConfig` |
| `src/Pinder.Core/Conversation/GameEndedException.cs` | **Modify** | Add `IReadOnlyList<string> ShadowGrowthEvents` property for ghost-triggered Dread growth |
| `src/Pinder.Core/Conversation/ReadResult.cs` | **Modify** (from #43) | Ensure `ShadowGrowthEvents` field exists for Overthinking growth on Read failure |
| `src/Pinder.Core/Conversation/RecoverResult.cs` | **Modify** (from #43) | Ensure `ShadowGrowthEvents` field exists for Overthinking growth on Recover failure |

**Not created:** `CharacterState.cs` — per ADR #161, `SessionShadowTracker` is used instead.
