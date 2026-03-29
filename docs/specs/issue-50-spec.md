# Spec: Issue #50 — Tells (§15 Opponent Tell Detection and Hidden Roll Bonus)

## Overview

Tells are a hidden bonus mechanic from rules v3.4 §15. When an opponent's response contains a behavioural cue (e.g. "flirts", "makes a joke"), the LLM detects a `Tell` indicating which stat will work best next turn. If the player then picks a dialogue option using that stat, they receive a hidden +2 bonus to their roll total. The bonus is invisible in the displayed success percentage but is revealed post-roll via `TurnResult.TellReadBonus` and `TellReadMessage`.

This issue implements the **GameSession integration** — the `Tell` and `OpponentResponse` types already exist (merged via #63/PR #114). The `TurnResult` fields (`TellReadBonus`, `TellReadMessage`) and `DialogueOption.HasTellBonus` also already exist (merged via #78/PR #117). What remains is wiring these together inside `GameSession` and routing the +2 bonus through `RollEngine.Resolve`.

---

## Tell Table (from §15)

| Opponent Behaviour | Tell Stat (+2) |
|---|---|
| Compliments you | Honesty |
| Asks a personal question | Honesty or SelfAwareness |
| Makes a joke | Wit or Chaos |
| Shares something vulnerable | Honesty |
| Pulls back / gets guarded | SelfAwareness |
| Tests you / challenges | Wit or Chaos |
| Sends a short reply | Charm or Chaos |
| Flirts | Rizz or Charm |
| Changes subject | Chaos |
| Goes silent a while | SelfAwareness |

Note: Some behaviours map to two possible stats. The LLM chooses one per response. The engine only receives a single `Tell` per opponent response.

---

## Existing Types (No Changes Needed)

These types already exist in the codebase and require NO modification:

### `Tell` (`Pinder.Core.Conversation`)
```csharp
public sealed class Tell
{
    public StatType Stat { get; }
    public string Description { get; }
    public Tell(StatType stat, string description);  // throws ArgumentNullException if description is null
}
```

### `OpponentResponse` (`Pinder.Core.Conversation`)
```csharp
public sealed class OpponentResponse
{
    public string MessageText { get; }
    public Tell? DetectedTell { get; }
    public WeaknessWindow? WeaknessWindow { get; }
    public OpponentResponse(string messageText, Tell? detectedTell = null, WeaknessWindow? weaknessWindow = null);
}
```

### `DialogueOption.HasTellBonus` — already exists as a `bool` property (default `false`)

### `TurnResult.TellReadBonus` — already exists as `int` (default `0`)

### `TurnResult.TellReadMessage` — already exists as `string?` (default `null`)

### `ILlmAdapter.GetOpponentResponseAsync` — already returns `Task<OpponentResponse>`

---

## Changes Required

### 1. `RollEngine.Resolve` — Add `externalBonus` and `dcAdjustment` Parameters

**File:** `src/Pinder.Core/Rolls/RollEngine.cs`

**Note:** This is a Wave 0 dependency (#139). If `externalBonus` has already been added by another issue, skip this section.

#### Updated Signature
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
    int externalBonus = 0,    // NEW — added to total before success check
    int dcAdjustment = 0)     // NEW — subtracted from DC (positive = easier roll)
```

#### Behaviour
- `externalBonus` is added to `total` before the success/failure comparison: `total = usedDieRoll + statModifier + levelBonus + externalBonus`
- `dcAdjustment` is subtracted from the computed DC: `effectiveDC = dc - dcAdjustment`
- Both default to `0`, making this backward-compatible with all existing callers
- The `RollResult` constructor must receive the adjusted values so `Total`, `IsSuccess`, and `MissMargin` all reflect external bonuses
- Nat 1 remains auto-fail regardless of `externalBonus`
- Nat 20 remains auto-success regardless of DC

#### RollResult Impact
`RollResult.Total` must include `externalBonus`. Two approaches:

**Option A (preferred, per architecture §#146):** Pass `externalBonus` into `RollEngine.Resolve`, compute `total = usedDieRoll + statModifier + levelBonus + externalBonus` inside the engine, and construct `RollResult` with the full total. `RollResult.ExternalBonus` property and `AddExternalBonus()` method become unused for new code (deprecated per architecture doc).

**Option B (backward compat):** Keep `RollResult.Total` as `usedDieRoll + statModifier + levelBonus` and have the engine call `AddExternalBonus()` before returning. This preserves `Total` semantics but `FinalTotal` becomes the canonical success check.

The implementer should follow the architecture decision in §#146: external bonuses flow through `RollEngine.Resolve(externalBonus)` as the canonical path. The existing `AddExternalBonus()` is deprecated.

---

### 2. `GameSession` — Add Tell State and Tell Bonus Logic

**File:** `src/Pinder.Core/Conversation/GameSession.cs`

#### New Field
```csharp
private Tell? _activeTell;  // initialized to null
```

#### `StartTurnAsync()` Changes

After receiving `DialogueOption[]` from `_llm.GetDialogueOptionsAsync(context)`, before storing as `_currentOptions`:

1. For each option, check if `_activeTell != null && option.Stat == _activeTell.Stat`
2. If matching, reconstruct the option with `hasTellBonus: true`:
   ```csharp
   new DialogueOption(
       stat: option.Stat,
       intendedText: option.IntendedText,
       callbackTurnNumber: option.CallbackTurnNumber,
       comboName: option.ComboName,
       hasTellBonus: true)
   ```
3. Store the (possibly modified) options as `_currentOptions`

This allows the host/UI to display a 📖 icon on matching options without revealing the bonus amount.

#### `ResolveTurnAsync(int optionIndex)` Changes

**Before the roll** (between selecting `chosenOption` and calling `RollEngine.Resolve`):

1. Compute tell bonus:
   ```
   int tellBonus = 0;
   if (_activeTell != null && chosenOption.Stat == _activeTell.Stat)
       tellBonus = 2;
   ```

2. Pass `tellBonus` as part of `externalBonus` to `RollEngine.Resolve`:
   ```csharp
   var rollResult = RollEngine.Resolve(
       stat: chosenOption.Stat,
       attacker: _player.Stats,
       defender: _opponent.Stats,
       attackerTraps: _traps,
       level: _player.Level,
       trapRegistry: _trapRegistry,
       dice: _dice,
       hasAdvantage: _currentHasAdvantage,
       hasDisadvantage: _currentHasDisadvantage,
       externalBonus: tellBonus);  // +2 when tell matches, 0 otherwise
   ```

   Note: When other external bonuses (callback #47, triple combo #46) are also implemented, they should be summed: `externalBonus: tellBonus + callbackBonus + tripleComboBonus`.

3. Clear the active tell regardless of match:
   ```
   _activeTell = null;
   ```

**After the opponent response** (after calling `_llm.GetOpponentResponseAsync`):

4. Store the new tell for next turn:
   ```
   _activeTell = opponentResponse.DetectedTell;
   ```

**When constructing `TurnResult`:**

5. Pass tell fields:
   ```csharp
   tellReadBonus: tellBonus,
   tellReadMessage: tellBonus > 0 ? "📖 You read the moment. +2 bonus." : null
   ```

#### Tell Bonus Value
The tell bonus is always exactly `+2` when matched, `0` when not matched. There is no variable tell bonus amount.

#### Tell Message
The exact string is: `"📖 You read the moment. +2 bonus."` — this is the canonical post-roll reveal message.

---

## Function Signatures Summary

### Modified Functions

| Function | Change |
|----------|--------|
| `RollEngine.Resolve(...)` | Add `int externalBonus = 0, int dcAdjustment = 0` params |
| `GameSession.StartTurnAsync()` | Set `HasTellBonus` on matching dialogue options |
| `GameSession.ResolveTurnAsync(int)` | Compute tell bonus, pass to `externalBonus`, populate `TurnResult` tell fields, store new tell from opponent response |

### No New Public Functions
All tell logic is internal to `GameSession`. No new public API surfaces are needed.

---

## Input/Output Examples

### Example 1: Tell Matched — Bonus Applied
```
State: _activeTell = Tell(StatType.Wit, "Makes a joke")

StartTurnAsync():
  → LLM returns options: [DialogueOption(Charm, "..."), DialogueOption(Wit, "..."), DialogueOption(SA, "...")]
  → Wit option reconstructed with HasTellBonus = true
  → Returns TurnStart with modified options

ResolveTurnAsync(1):  // Player picks Wit (index 1)
  → chosenOption.Stat == Wit == _activeTell.Stat → tellBonus = 2
  → RollEngine.Resolve(..., externalBonus: 2)
  → d20(12) + witMod(3) + levelBonus(1) + externalBonus(2) = 18 vs DC 16 → success
  → _activeTell = null (consumed)
  → TurnResult.TellReadBonus = 2
  → TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."
```

### Example 2: Tell Not Matched — No Bonus
```
State: _activeTell = Tell(StatType.SelfAwareness, "Goes silent")

ResolveTurnAsync(0):  // Player picks Charm
  → chosenOption.Stat == Charm ≠ SelfAwareness → tellBonus = 0
  → RollEngine.Resolve(..., externalBonus: 0)
  → _activeTell = null (consumed regardless)
  → TurnResult.TellReadBonus = 0
  → TurnResult.TellReadMessage = null
```

### Example 3: No Tell Active
```
State: _activeTell = null

ResolveTurnAsync(2):
  → _activeTell is null → tellBonus = 0
  → RollEngine.Resolve(..., externalBonus: 0)
  → TurnResult.TellReadBonus = 0
  → TurnResult.TellReadMessage = null
```

### Example 4: Tell Bonus Turns Miss Into Hit
```
State: _activeTell = Tell(StatType.Honesty, "Shares something vulnerable")
Player picks Honesty option.

Without bonus: d20(11) + honestyMod(2) + levelBonus(0) = 13 vs DC 15 → miss by 2
With bonus:    d20(11) + honestyMod(2) + levelBonus(0) + externalBonus(2) = 15 vs DC 15 → success (beat by 0 → +1 interest)

TurnResult.TellReadBonus = 2
TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."
```

### Example 5: Tell Stored from Opponent Response
```
ResolveTurnAsync completes:
  → _llm.GetOpponentResponseAsync() returns OpponentResponse("Haha nice one", Tell(StatType.Charm, "Flirts"))
  → opponentMessage = "Haha nice one"
  → _activeTell = Tell(StatType.Charm, "Flirts")  // stored for next turn
```

---

## Acceptance Criteria

### AC1: GameSession stores active tell from previous turn's OpponentResponse.DetectedTell
- After `ResolveTurnAsync` calls `_llm.GetOpponentResponseAsync()`, the returned `OpponentResponse.DetectedTell` is stored as `_activeTell`
- `_activeTell` persists until the next `ResolveTurnAsync` call
- On the first turn, `_activeTell` is `null` (no prior opponent response)

### AC2: On matching stat — +2 added via externalBonus on RollEngine.Resolve
- When `_activeTell != null` and `chosenOption.Stat == _activeTell.Stat`, `externalBonus` of `2` is passed to `RollEngine.Resolve`
- The bonus affects `RollResult.Total` (or `FinalTotal`), `IsSuccess`, and `MissMargin`
- When the stat does not match, `externalBonus` is `0` (for the tell component)
- `_activeTell` is set to `null` after consumption regardless of match

### AC3: Displayed percentage excludes bonus
- The `HasTellBonus` flag on `DialogueOption` is set during `StartTurnAsync` for UI purposes only
- No success probability calculation should include the +2 — it is hidden until post-roll reveal
- The engine does not compute success percentages; this is a constraint on host/UI implementations

### AC4: TurnResult.TellReadBonus and TellReadMessage populated correctly
- When tell matches: `TellReadBonus = 2`, `TellReadMessage = "📖 You read the moment. +2 bonus."`
- When tell does not match: `TellReadBonus = 0`, `TellReadMessage = null`
- When no tell was active: `TellReadBonus = 0`, `TellReadMessage = null`

### AC5: DialogueOption.HasTellBonus set when option stat matches active tell
- During `StartTurnAsync`, each `DialogueOption` whose `Stat` matches `_activeTell.Stat` must be reconstructed with `hasTellBonus: true`
- Options with non-matching stats retain `hasTellBonus: false`
- If `_activeTell` is null, all options have `hasTellBonus: false`

### AC6: Tests cover tell bonus application
- Tell bonus applied when chosen stat matches → `TellReadBonus == 2`, `TellReadMessage` is non-null
- Tell bonus NOT applied when chosen stat differs → `TellReadBonus == 0`
- No tell active → `TellReadBonus == 0`
- Tell consumed after one turn (second turn has no bonus unless new tell detected)
- Tell bonus can turn a miss into a hit (verified via `RollResult.IsSuccess`)
- `HasTellBonus` set correctly on options during `StartTurnAsync`

### AC7: Build clean
- `dotnet build` succeeds with zero errors
- All existing tests pass (`dotnet test`)

---

## Edge Cases

| # | Case | Expected Behaviour |
|---|------|-------------------|
| 1 | No tell active (`_activeTell == null`) | `externalBonus = 0` (tell portion), `TellReadBonus = 0`, `TellReadMessage = null` |
| 2 | Tell active, player picks different stat | `externalBonus = 0`, tell consumed (set to null), `TellReadBonus = 0` |
| 3 | Tell active, player picks matching stat | `externalBonus = 2`, tell consumed, `TellReadBonus = 2` |
| 4 | Tell from turn N not consumed until turn N+1 | Tell persists in `_activeTell` across `StartTurnAsync`/`ResolveTurnAsync` boundary. It is only consumed when `ResolveTurnAsync` runs. |
| 5 | Multiple tells in sequence | Each new `OpponentResponse.DetectedTell` overwrites `_activeTell`. Only the most recent tell is active. |
| 6 | First turn (no prior opponent response) | `_activeTell` is null; no tell bonus possible. |
| 7 | Nat 20 with tell bonus | `externalBonus: 2` is passed. Nat 20 is already auto-success; the +2 increases the "beat DC by" margin, potentially yielding a higher `SuccessScale` tier. `TellReadBonus = 2`. |
| 8 | Nat 1 with tell bonus | Nat 1 is auto-failure (Legendary tier). The `externalBonus: 2` is passed to `RollEngine.Resolve` but Nat 1 auto-fail takes precedence. `TellReadBonus = 2` because the player correctly identified the matching stat — the tell was "read" even though the roll auto-failed. `TellReadMessage` is set. |
| 9 | Tell matches but roll still fails after +2 | `TellReadBonus = 2`, `TellReadMessage` is set. The bonus was applied but wasn't enough to beat DC. The tell was correctly read; the outcome was just insufficient. |
| 10 | `HasTellBonus` is true on option but player picks different option | The non-selected option's `HasTellBonus` flag is irrelevant. Only the chosen option's stat is compared to `_activeTell.Stat`. |
| 11 | Opponent response has no tell (`DetectedTell = null`) | `_activeTell = null` after this turn. Next turn has no tell bonus available. |
| 12 | Two options share the same stat that matches tell | Both get `HasTellBonus = true`. Whichever is chosen triggers the +2. |
| 13 | Read/Recover/Wait actions with active tell | These actions do NOT consume the tell. The tell persists for the next Speak turn. Only `ResolveTurnAsync` consumes the tell. |

---

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| No new error conditions | — | The tell mechanic is purely additive. It does not introduce new failure modes. Existing `ResolveTurnAsync` errors (game ended, no options, index out of range) are unchanged. |

---

## Dependencies

| Dependency | Type | Status | Notes |
|-----------|------|--------|-------|
| #27 — GameSession | Hard | ✅ Merged | Base GameSession implementation |
| #63 — Tell/OpponentResponse types | Hard | ✅ Merged (PR #114) | `Tell`, `OpponentResponse`, `ILlmAdapter` return type |
| #78 — TurnResult expansion | Hard | ✅ Merged (PR #117) | `TellReadBonus`, `TellReadMessage` fields on `TurnResult`; `HasTellBonus` on `DialogueOption` |
| #139 — Wave 0 (externalBonus param) | Hard | ⚠️ Not yet merged | `RollEngine.Resolve(externalBonus)` — must be implemented before or alongside this issue |

---

## Files to Modify

| File | Change |
|------|--------|
| `src/Pinder.Core/Rolls/RollEngine.cs` | Add `int externalBonus = 0, int dcAdjustment = 0` params to `Resolve()`; incorporate into total and DC computation. **Skip if Wave 0 already did this.** |
| `src/Pinder.Core/Conversation/GameSession.cs` | Add `_activeTell` field; tell matching + `externalBonus` in `ResolveTurnAsync`; flag options in `StartTurnAsync`; store tell from `OpponentResponse` |

## Files NOT Modified (already exist with needed shape)

| File | Reason |
|------|--------|
| `src/Pinder.Core/Conversation/Tell.cs` | Already exists with correct shape |
| `src/Pinder.Core/Conversation/OpponentResponse.cs` | Already exists, returns `DetectedTell` |
| `src/Pinder.Core/Conversation/TurnResult.cs` | Already has `TellReadBonus`, `TellReadMessage` with correct defaults |
| `src/Pinder.Core/Conversation/DialogueOption.cs` | Already has `HasTellBonus` with correct default |
| `src/Pinder.Core/Interfaces/ILlmAdapter.cs` | Already returns `Task<OpponentResponse>` |

---

## Stacking Order in `ResolveTurnAsync`

After this issue is implemented, the full interest delta computation follows this order:

```
0. Tell bonus      → +2 to ROLL TOTAL if tell stat matches chosen stat
                     (applied via externalBonus param on RollEngine.Resolve)
                     This can turn a failure into success.

1. Base delta      = SuccessScale.GetInterestDelta(roll)
                     or FailureScale.GetInterestDelta(roll)

2. Risk bonus      = RiskTierBonus.GetInterestBonus(roll) — success only

3. Callback bonus  = (from #47, if implemented) — success only

4. Momentum bonus  = GetMomentumBonus(streak) — success only
───────────────────────────
   Total interestDelta = sum of 1–4 → InterestMeter.Apply(total)
```

The tell bonus (step 0) modifies the **roll total**, not the interest delta. It can change the roll from failure to success, which then unlocks the success-only bonuses (steps 2–4).

---

## Test Guidance

Tests should use a mock `ILlmAdapter` that returns controlled `OpponentResponse` objects with specific `Tell` values, and a fixed `IDiceRoller` to control roll outcomes. Key test scenarios:

1. **Tell match → bonus applied**: Set up `OpponentResponse` with `Tell(Wit, "joke")`, next turn pick Wit option → verify `TellReadBonus == 2`
2. **Tell mismatch → no bonus**: Same tell, pick Charm → verify `TellReadBonus == 0`
3. **No tell → no bonus**: `OpponentResponse` with `null` tell → verify `TellReadBonus == 0`
4. **Tell consumed**: After one `ResolveTurnAsync`, tell is cleared. Next turn (without new tell) → `TellReadBonus == 0`
5. **Miss → hit conversion**: Set dice to roll value that misses DC by 1-2, add tell match → verify `IsSuccess == true`
6. **Nat 1 with tell**: Tell matches but Nat 1 → `IsSuccess == false`, `TellReadBonus == 2`
7. **HasTellBonus flag**: Call `StartTurnAsync` with active tell → verify matching options have `HasTellBonus == true`, non-matching have `false`
8. **Tell overwrite**: Two consecutive opponent responses with different tells → only latest is active
