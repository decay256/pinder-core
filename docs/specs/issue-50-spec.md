# Spec: Issue #50 — Tells (§15 Opponent Tell Detection and Hidden Roll Bonus)

## Overview

Tells are a hidden bonus mechanic from rules v3.4 §15. The opponent's messages contain subtle behavioural cues ("tells") that hint at which stat will work best on the next turn. When the LLM detects a tell in the opponent's response, it returns a `Tell` object indicating the stat. If the player then picks a dialogue option using that stat, they receive a hidden +2 bonus to their roll via the `externalBonus` parameter on `RollEngine.Resolve`. The bonus is invisible in the displayed success percentage but is revealed post-roll via `TurnResult.TellReadBonus` and `TurnResult.TellReadMessage`.

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

## Existing Types (already in codebase)

The following types already exist and require **no changes**:

### `Tell` (`src/Pinder.Core/Conversation/Tell.cs`)

```csharp
public sealed class Tell
{
    public StatType Stat { get; }
    public string Description { get; }
    public Tell(StatType stat, string description);
    // Throws ArgumentNullException if description is null
}
```

### `OpponentResponse` (`src/Pinder.Core/Conversation/OpponentResponse.cs`)

```csharp
public sealed class OpponentResponse
{
    public string MessageText { get; }
    public Tell? DetectedTell { get; }
    public WeaknessWindow? WeaknessWindow { get; }
    public OpponentResponse(string messageText, Tell? detectedTell = null, WeaknessWindow? weaknessWindow = null);
    // Throws ArgumentNullException if messageText is null
}
```

### `DialogueOption` (`src/Pinder.Core/Conversation/DialogueOption.cs`)

```csharp
public sealed class DialogueOption
{
    public StatType Stat { get; }
    public string IntendedText { get; }
    public int? CallbackTurnNumber { get; }
    public string? ComboName { get; }
    public bool HasTellBonus { get; }
    public DialogueOption(StatType stat, string intendedText, int? callbackTurnNumber = null,
        string? comboName = null, bool hasTellBonus = false);
}
```

### `TurnResult` (`src/Pinder.Core/Conversation/TurnResult.cs`)

Already has:
```csharp
public int TellReadBonus { get; }        // 0 or 2
public string? TellReadMessage { get; }  // null or "📖 You read the moment. +2 bonus."
```

These are constructor parameters with defaults of `0` and `null`.

### `ILlmAdapter.GetOpponentResponseAsync`

Already returns `Task<OpponentResponse>` (not `Task<string>`).

### `RollResult` (`src/Pinder.Core/Rolls/RollResult.cs`)

Already has:
```csharp
public int ExternalBonus { get; private set; }         // defaults to 0
public int FinalTotal => Total + ExternalBonus;         // computed
public void AddExternalBonus(int bonus);                // DEPRECATED but available
```

`IsSuccess` is currently computed as `IsNatTwenty || (!IsNatOne && Total >= dc)`. Per the Wave 0 contract (#139), this will change to use `FinalTotal` when `externalBonus` flows through `RollEngine.Resolve`. See Dependencies section.

---

## What Must Be Implemented

### Prerequisite: `RollEngine.Resolve` Must Accept `externalBonus`

Per the Sprint 8 Wave 0 contract (#139), `RollEngine.Resolve` gains:
```csharp
public static RollResult Resolve(
    StatType stat, StatBlock attacker, StatBlock defender,
    TrapState attackerTraps, int level, ITrapRegistry trapRegistry, IDiceRoller dice,
    bool hasAdvantage = false, bool hasDisadvantage = false,
    int externalBonus = 0,    // NEW — added to total before success check
    int dcAdjustment = 0);    // NEW — subtracted from DC
```

And `RollResult` `IsSuccess` changes to: `IsNatTwenty || (!IsNatOne && FinalTotal >= dc)`.

**If Wave 0 (#130) is not yet merged**, the tell bonus must still use the `externalBonus` parameter (not `AddExternalBonus()`). The implementer should either depend on Wave 0 or implement the `externalBonus` parameter as part of this issue.

#### How to Check Wave 0 Merge Status

Run the following command before starting implementation:
```bash
gh pr list --repo decay256/pinder-core --state merged --json number,headRefName | grep -i "issue-139\|issue-130"
```
If output is non-empty, Wave 0 is merged and `RollEngine.Resolve` already accepts `externalBonus`. If empty, use Option B from the Dependencies section below.

> ⚠️ Per vision concerns #68 and #129: The +2 tell bonus MUST flow through `RollEngine.Resolve(externalBonus)` so that `RollResult.FinalTotal`, `IsSuccess`, and `MissMargin` all reflect the bonus. Do NOT add it as a post-hoc adjustment via `AddExternalBonus()`.

---

### 1. New Field on `GameSession`: `_activeTell`

```csharp
private Tell? _activeTell;  // null initially
```

Stores the tell from the most recent opponent response. Consumed on the next `ResolveTurnAsync` call.

---

### 2. Changes to `GameSession.ResolveTurnAsync`

#### 2a. Compute tell bonus before calling `RollEngine.Resolve`

Before the existing call to `RollEngine.Resolve`:

1. Check if `_activeTell != null && chosenOption.Stat == _activeTell.Stat`.
2. If match: `int tellBonus = 2`. If no match or no active tell: `int tellBonus = 0`.
3. Pass `tellBonus` as part of `externalBonus` to `RollEngine.Resolve`.

The `externalBonus` parameter may also include other bonuses (callback, triple combo) accumulated in the same turn. These are summed before the call:

```
externalBonus = tellBonus + callbackBonus + tripleComboBonus
```

For this issue, only `tellBonus` is relevant. The others default to 0 until their respective issues (#47, #46) are implemented.

> **⚠️ Cross-Issue Coordination Note:** Issues #46 (triple combo bonus) and #47 (callback bonus) MUST add their bonuses to the same `externalBonus` accumulation point shown above. When implementing those issues, reference this section to ensure bonuses are summed before the single `RollEngine.Resolve` call — do NOT make separate `RollEngine.Resolve` calls or use `AddExternalBonus()` post-hoc. This prevents duplicate or missing bonus application.

#### 2b. Record tell result on `TurnResult`

After the roll:
- If `tellBonus > 0`: set `tellReadBonus: 2` and `tellReadMessage: "📖 You read the moment. +2 bonus."` on `TurnResult`.
- If `tellBonus == 0`: set `tellReadBonus: 0` and `tellReadMessage: null` on `TurnResult`.

#### 2c. Clear the active tell

After the roll (regardless of match): set `_activeTell = null`. A tell is always consumed after one turn.

#### Updated `RollEngine.Resolve` call

```csharp
// Before:
var rollResult = RollEngine.Resolve(
    stat: chosenOption.Stat,
    attacker: _player.Stats,
    defender: _opponent.Stats,
    attackerTraps: _traps,
    level: _player.Level,
    trapRegistry: _trapRegistry,
    dice: _dice,
    hasAdvantage: _currentHasAdvantage,
    hasDisadvantage: _currentHasDisadvantage);

// After:
int tellBonus = (_activeTell != null && chosenOption.Stat == _activeTell.Stat) ? 2 : 0;

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
    externalBonus: tellBonus);

_activeTell = null;  // consumed
```

---

### 3. Changes to `GameSession.StartTurnAsync`

After receiving `DialogueOption[]` from `_llm.GetDialogueOptionsAsync(context)`:

For each option, if `_activeTell != null` and `option.Stat == _activeTell.Stat`, create a new `DialogueOption` with `hasTellBonus: true`:

```csharp
new DialogueOption(
    stat: option.Stat,
    intendedText: option.IntendedText,
    callbackTurnNumber: option.CallbackTurnNumber,
    comboName: option.ComboName,
    hasTellBonus: true)
```

Options whose stat does NOT match the active tell retain `hasTellBonus: false` (no reconstruction needed unless the LLM returns them with `hasTellBonus: true`, which it should not).

This flag is purely informational — it lets the UI show a hint (e.g. 📖 icon) without revealing the actual +2 bonus value.

---

### 4. Extract and Store Tell from Opponent Response

In `ResolveTurnAsync`, after calling `_llm.GetOpponentResponseAsync(opponentContext)`:

```csharp
var opponentResponse = await _llm.GetOpponentResponseAsync(opponentContext).ConfigureAwait(false);
string opponentMessage = opponentResponse.MessageText;
_activeTell = opponentResponse.DetectedTell;  // NEW — store tell for next turn
```

This line must be added after the existing `opponentMessage` extraction (currently at line ~254 of GameSession.cs).

---

### 5. Updated `TurnResult` Construction

The existing `TurnResult` construction in `ResolveTurnAsync` must pass the tell fields:

```csharp
return new TurnResult(
    roll: rollResult,
    deliveredMessage: deliveredMessage,
    opponentMessage: opponentMessage,
    narrativeBeat: narrativeBeat,
    interestDelta: interestDelta,
    stateAfter: stateSnapshot,
    isGameOver: isGameOver,
    outcome: outcome,
    tellReadBonus: tellBonus,
    tellReadMessage: tellBonus > 0 ? "📖 You read the moment. +2 bonus." : null);
```

---

## Function Signatures (modified methods only)

### `GameSession` — No new public methods

All changes are internal to `ResolveTurnAsync` and `StartTurnAsync`. The public API is unchanged.

Internal additions:
```csharp
private Tell? _activeTell;  // new field
```

---

## Input/Output Examples

### Example 1: Tell Matched — Bonus Applied
```
State: _activeTell = Tell(StatType.Wit, "Makes a joke")

StartTurnAsync:
  → LLM returns options: [DialogueOption(Charm, ...), DialogueOption(Honesty, ...), DialogueOption(Wit, ...), DialogueOption(Chaos, ...)]
  → Wit option reconstructed with HasTellBonus = true
  → Player sees 📖 indicator on Wit option

ResolveTurnAsync(optionIndex = 2):  // Player picks Wit
  → chosenOption.Stat == Wit == _activeTell.Stat → match!
  → tellBonus = 2
  → RollEngine.Resolve(..., externalBonus: 2)
  → d20(12) + witMod(3) + levelBonus(1) = Total 16, ExternalBonus 2, FinalTotal 18 vs DC 16 → success
  → _activeTell = null (consumed)
  → TurnResult.TellReadBonus = 2
  → TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."
```

### Example 2: Tell Not Matched — No Bonus
```
State: _activeTell = Tell(StatType.SelfAwareness, "Goes silent")

ResolveTurnAsync(optionIndex = 0):  // Player picks Charm
  → chosenOption.Stat == Charm ≠ SelfAwareness → no match
  → tellBonus = 0
  → RollEngine.Resolve(..., externalBonus: 0)
  → _activeTell = null (consumed regardless)
  → TurnResult.TellReadBonus = 0
  → TurnResult.TellReadMessage = null
```

### Example 3: No Tell Active
```
State: _activeTell = null

ResolveTurnAsync:
  → _activeTell is null → tellBonus = 0
  → RollEngine.Resolve(..., externalBonus: 0)
  → TurnResult.TellReadBonus = 0
  → TurnResult.TellReadMessage = null
```

### Example 4: Tell Bonus Turns Miss Into Hit
```
State: _activeTell = Tell(StatType.Honesty, "Shares something vulnerable")
Player picks Honesty option.

Without bonus: d20(11) + honestyMod(2) + levelBonus(0) = Total 13 vs DC 15 → miss by 2
With bonus:    Total 13 + ExternalBonus 2 = FinalTotal 15 vs DC 15 → success (beat by 0 → SuccessScale +1)

TurnResult.TellReadBonus = 2
TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."
```

### Example 5: Tell Stored from Opponent Response
```
ResolveTurnAsync completes, opponent responds:
  OpponentResponse("Ha, that's actually funny", new Tell(StatType.Wit, "Makes a joke"), null)
  → opponentMessage = "Ha, that's actually funny"
  → _activeTell = Tell(StatType.Wit, "Makes a joke")
  → Available for next turn's ResolveTurnAsync
```

---

## Acceptance Criteria

### AC1: `GameSession` stores active tell from previous turn's `OpponentResponse.DetectedTell`

- After `_llm.GetOpponentResponseAsync()` in `ResolveTurnAsync`, `_activeTell` is set to `opponentResponse.DetectedTell`.
- `_activeTell` persists across the `StartTurnAsync`/`ResolveTurnAsync` boundary.
- On the first turn (before any opponent response), `_activeTell` is null.

### AC2: On matching stat — +2 added via `externalBonus` on `RollEngine.Resolve`

- When `_activeTell != null && chosenOption.Stat == _activeTell.Stat`, the value `2` is passed as (part of) `externalBonus` to `RollEngine.Resolve`.
- The +2 affects `RollResult.FinalTotal`, `IsSuccess`, and failure tier determination.
- The +2 does NOT affect the interest delta directly — it modifies the roll, which then determines the interest delta via `SuccessScale`/`FailureScale`.

### AC3: Displayed percentage excludes bonus

- `DialogueOption.HasTellBonus` is set to `true` during `StartTurnAsync` for options matching the active tell.
- This flag is informational only — it does not affect any roll or interest computation.
- No success percentage computation includes the +2 bonus. The bonus is hidden until post-roll reveal.

### AC4: `TurnResult.TellReadBonus` and `TellReadMessage` populated correctly

- When tell matches: `TellReadBonus = 2`, `TellReadMessage = "📖 You read the moment. +2 bonus."`.
- When tell does not match or no tell active: `TellReadBonus = 0`, `TellReadMessage = null`.
- The constant string `"📖 You read the moment. +2 bonus."` is exact (including emoji and period).

### AC5: `DialogueOption.HasTellBonus` set when option stat matches active tell

- During `StartTurnAsync`, each option from the LLM is checked against `_activeTell`.
- Options with `Stat == _activeTell.Stat` are reconstructed with `hasTellBonus: true`.
- Options with non-matching stat retain `hasTellBonus: false`.
- When `_activeTell` is null, all options have `hasTellBonus: false`.

### AC6: Tests cover tell bonus application

Required test cases (see Edge Cases section for additional detail):
1. Tell bonus applied when chosen stat matches active tell → `TurnResult.TellReadBonus == 2`, `TellReadMessage` is non-null.
2. Tell bonus NOT applied when chosen stat differs from active tell → `TurnResult.TellReadBonus == 0`.
3. No tell active → `TurnResult.TellReadBonus == 0`.
4. Tell consumed after one turn — second turn after tell set has no bonus.
5. Tell bonus can turn a miss into a hit (integration with `RollEngine` via `externalBonus`).
6. `HasTellBonus` flag set correctly on matching options during `StartTurnAsync`.

### AC7: Build clean

- `dotnet build` succeeds with zero warnings/errors.
- All existing tests pass (`dotnet test`).

---

## Edge Cases

| # | Case | Expected Behaviour |
|---|------|-------------------|
| 1 | No tell active (`_activeTell == null`) | `tellBonus = 0`, `TellReadBonus = 0`, `TellReadMessage = null` |
| 2 | Tell active but player picks different stat | `tellBonus = 0`, tell consumed (set to null), `TellReadBonus = 0` |
| 3 | Tell active and player picks matching stat | `tellBonus = 2`, tell consumed, `TellReadBonus = 2`, message set |
| 4 | Tell persists across StartTurnAsync/ResolveTurnAsync boundary | `_activeTell` is not cleared by `StartTurnAsync` — only by `ResolveTurnAsync` |
| 5 | Multiple tells in sequence (opponent sends tell every turn) | Each new `OpponentResponse.DetectedTell` overwrites `_activeTell`. Only the most recent tell is active. |
| 6 | First turn (no prior opponent response) | `_activeTell` is null; no tell bonus possible. |
| 7 | Nat 20 with tell bonus | `externalBonus: 2` is passed. Nat 20 is auto-success; the +2 increases `FinalTotal`, potentially yielding a higher `SuccessScale` tier. `TellReadBonus = 2`. |
| 8 | Nat 1 with tell bonus | Nat 1 is auto-failure (Legendary tier). The `externalBonus: 2` is passed but Nat 1 auto-fail takes precedence. `TellReadBonus = 2` because the player correctly read the tell (picked the matching stat). The bonus was applied to the roll; the outcome was just too bad. |
| 9 | Tell stat matches but roll still fails after +2 | `TellReadBonus = 2`, `TellReadMessage` set. The bonus was applied but wasn't enough. The tell was "read" correctly — the player picked the right stat. |
| 10 | `DialogueOption.HasTellBonus` is true but player picks a different option | The non-selected option's `HasTellBonus` flag is irrelevant. Only the chosen option's stat is compared to `_activeTell`. |
| 11 | Opponent response has no tell (`DetectedTell == null`) | `_activeTell` set to null. No bonus available next turn. |
| 12 | Game ends during the turn (DateSecured or Unmatched) | Tell is still consumed. `TellReadBonus` is still recorded on `TurnResult`. |

---

## Error Conditions

No new error conditions are introduced by this feature. The tell mechanic is additive — it introduces new fields and a bonus but does not create new failure modes in the game session flow.

The only pre-existing error conditions relevant are:
- `GameSession.ResolveTurnAsync` throws `GameEndedException` if game already ended.
- `GameSession.ResolveTurnAsync` throws `InvalidOperationException` if `StartTurnAsync` was not called first.
- `GameSession.ResolveTurnAsync` throws `ArgumentOutOfRangeException` for invalid option index.

None of these are affected by tell logic.

---

## Dependencies

| Dependency | Type | Status | Notes |
|-----------|------|--------|-------|
| **#27 — GameSession** | Hard | ✅ Merged | Base GameSession exists |
| **#63 — Tell/OpponentResponse types** | Hard | ✅ Merged | `Tell`, `OpponentResponse`, `DialogueOption.HasTellBonus`, `TurnResult.TellReadBonus/TellReadMessage` all exist |
| **#130 — Wave 0: `RollEngine.Resolve(externalBonus)`** | Hard | ⚠️ Check status | `externalBonus` parameter on `RollEngine.Resolve` must exist. Currently `RollEngine.Resolve` does NOT have this parameter. `RollResult.ExternalBonus` and `FinalTotal` exist but are set post-hoc. |
| `ILlmAdapter` | Interface | ✅ No change needed | Already returns `Task<OpponentResponse>` |
| `NullLlmAdapter` | Test adapter | ✅ No change needed | Already returns `OpponentResponse` |

### Critical Dependency: Wave 0 `externalBonus` on `RollEngine.Resolve`

The current `RollEngine.Resolve` method does **not** accept an `externalBonus` parameter. Per the Sprint 8 architecture contract (#139), Wave 0 adds this parameter. If Wave 0 (#130) is not yet merged when this issue is implemented:

**Option A (preferred):** Implement after Wave 0 merges.  
**Option B:** Add the `externalBonus` parameter to `RollEngine.Resolve` as part of this issue (matching the Wave 0 contract exactly), then Wave 0 becomes a no-op for this specific change.

The `RollResult` already has `ExternalBonus`, `FinalTotal`, and `AddExternalBonus()`. However, `IsSuccess` currently uses `Total` (not `FinalTotal`). Per the Wave 0 contract, `IsSuccess` must change to use `FinalTotal`. This is critical for the tell bonus to actually affect success/failure determination.

---

## Files to Modify

| File | Change |
|------|--------|
| `src/Pinder.Core/Conversation/GameSession.cs` | Add `_activeTell` field; compute tell bonus in `ResolveTurnAsync`; flag options in `StartTurnAsync`; store tell from `OpponentResponse` |
| `src/Pinder.Core/Rolls/RollEngine.cs` | Add `int externalBonus = 0` parameter to `Resolve` (if Wave 0 not yet merged) |
| `src/Pinder.Core/Rolls/RollResult.cs` | Change `IsSuccess` to use `FinalTotal` (if Wave 0 not yet merged) |

## Files NOT Modified (already correct)

| File | Reason |
|------|--------|
| `src/Pinder.Core/Conversation/Tell.cs` | Already exists with correct API |
| `src/Pinder.Core/Conversation/OpponentResponse.cs` | Already exists with `DetectedTell` |
| `src/Pinder.Core/Conversation/DialogueOption.cs` | Already has `HasTellBonus` |
| `src/Pinder.Core/Conversation/TurnResult.cs` | Already has `TellReadBonus`, `TellReadMessage` |
| `src/Pinder.Core/Interfaces/ILlmAdapter.cs` | Already returns `Task<OpponentResponse>` |
| `src/Pinder.Core/Conversation/NullLlmAdapter.cs` | Already returns `OpponentResponse` |

---

## Stacking Order in `ResolveTurnAsync`

After this issue is implemented, the tell bonus integrates into the roll flow as follows:

```
0. Compute externalBonus:
     tellBonus         = +2 if _activeTell.Stat == chosenOption.Stat, else 0
     callbackBonus     = (from #47, if implemented) 0 default
     tripleComboBonus  = (from #46, if implemented) 0 default
     externalBonus     = tellBonus + callbackBonus + tripleComboBonus

1. RollEngine.Resolve(..., externalBonus: externalBonus)
     → RollResult with FinalTotal = Total + ExternalBonus
     → IsSuccess based on FinalTotal

2. Interest delta:
     Base delta    = SuccessScale or FailureScale (based on roll outcome)
     Risk bonus    = (from #42) +1 for Hard, +2 for Bold — success only
     Momentum      = GetMomentumBonus(streak) — success only
     ─────────────
     Total interest delta → InterestMeter.Apply(total)
```

The tell bonus (step 0) modifies the **roll total**, not the interest delta. This means it can change a failure to a success, which then unlocks success-only bonuses (risk, momentum).

---

## Constants

| Constant | Value | Location |
|----------|-------|----------|
| Tell bonus amount | `2` (int) | `GameSession.ResolveTurnAsync` |
| Tell read message | `"📖 You read the moment. +2 bonus."` | `GameSession.ResolveTurnAsync` |
| Bonus value on no match | `0` (int) | `GameSession.ResolveTurnAsync` |
