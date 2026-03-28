# Spec: Issue #50 — Tells (§15 Opponent Tell Detection and Hidden Roll Bonus)

## Overview

Tells are a hidden bonus mechanic from rules v3.4 §15. The opponent's messages contain subtle behavioural cues ("tells") that hint at which stat will work best on the next turn. When the LLM detects a tell in the opponent's response, it returns a `Tell` object indicating the stat. If the player then picks a dialogue option using that stat, they receive a hidden +2 bonus to their roll total. The bonus is invisible in the displayed success percentage but is revealed post-roll via a message on `TurnResult`.

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

## New Type: `Tell`

### Namespace
`Pinder.Core.Conversation`

### Signature
```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Represents a detected tell from the opponent's behaviour.
    /// Matching the tell stat on the next turn grants a hidden +2 roll bonus.
    /// </summary>
    public sealed class Tell
    {
        /// <summary>The stat that the tell hints at.</summary>
        public StatType Stat { get; }

        /// <summary>Human-readable description of the tell behaviour (e.g. "Flirts").</summary>
        public string Description { get; }

        public Tell(StatType stat, string description);
    }
}
```

### Constraints
- Must be a `sealed class`, NOT a `record` (netstandard2.0 / C# 8.0).
- `Description` must not be null; constructor throws `ArgumentNullException` if null.
- `Stat` is a `StatType` enum value — no null check needed (value type).

---

## New Type: `OpponentResponse`

### Namespace
`Pinder.Core.Conversation`

### Rationale
`ILlmAdapter.GetOpponentResponseAsync` currently returns `Task<string>`. To carry the optional tell, a new return type is needed that wraps both the response text and the detected tell.

### Signature
```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// The opponent's response message plus any detected tell for the next turn.
    /// </summary>
    public sealed class OpponentResponse
    {
        /// <summary>The opponent's message text.</summary>
        public string Text { get; }

        /// <summary>The tell detected in this response, or null if no tell was detected.</summary>
        public Tell? DetectedTell { get; }

        public OpponentResponse(string text, Tell? detectedTell = null);
    }
}
```

### Constraints
- `Text` must not be null; constructor throws `ArgumentNullException` if null.
- `DetectedTell` may be null (no tell detected this turn).

---

## Interface Change: `ILlmAdapter.GetOpponentResponseAsync`

### Current Signature
```csharp
Task<string> GetOpponentResponseAsync(OpponentContext context);
```

### New Signature
```csharp
Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);
```

### Impact
- **`ILlmAdapter`**: return type changes from `Task<string>` to `Task<OpponentResponse>`.
- **`NullLlmAdapter`**: must be updated to return `new OpponentResponse("...", detectedTell: null)`.
- **`GameSession`**: must unwrap `OpponentResponse.Text` for the history entry and store `OpponentResponse.DetectedTell` for the next turn.

---

## Changes to `GameSession`

### New Internal State

| Field | Type | Initial Value | Purpose |
|-------|------|---------------|---------|
| `_activeTell` | `Tell?` | `null` | The tell detected from the opponent's last response. Consumed on the next `ResolveTurnAsync`. |

### `ResolveTurnAsync` Changes

After step 1 (dice roll via `RollEngine.Resolve`), before computing interest delta:

1. **Check tell match**: If `_activeTell` is not null AND `chosenOption.Stat == _activeTell.Stat`:
   - Add +2 to `rollResult.Total` for the purpose of success/failure determination. **Important**: the +2 is applied to the roll total, NOT to the interest delta. It can turn a miss into a hit.
   - Record `tellReadBonus = 2` and `tellReadMessage = "📖 You read the moment. +2 bonus."`.
2. **Clear the active tell**: Set `_activeTell = null` regardless of whether it matched. A tell is consumed after one turn.
3. The `DialogueOption.HasTellBonus` flag is set during `StartTurnAsync` (see below) and is purely informational for the UI — it does NOT affect the roll computation in `ResolveTurnAsync`.

**Clarification on "hidden"**: The +2 bonus is NOT reflected in any success percentage shown to the player before they choose. After the roll resolves, the `TurnResult` reveals whether the tell was read correctly.

### `StartTurnAsync` Changes

When building `DialogueContext` or processing options returned from the LLM:

1. After receiving `DialogueOption[]` from `_llm.GetDialogueOptionsAsync(context)`:
   - For each option, if `_activeTell` is not null and `option.Stat == _activeTell.Stat`, reconstruct the option with `hasTellBonus = true`.
   - (Since `DialogueOption` properties are readonly, this means creating a new `DialogueOption` with the same values but `hasTellBonus = true`.)
2. Store the modified options as `_currentOptions`.

This allows the host/UI to show a hint (e.g. a 📖 icon) without revealing the actual bonus.

### Opponent Response Handling

In `ResolveTurnAsync`, after calling `_llm.GetOpponentResponseAsync(opponentContext)`:

1. Unwrap the `OpponentResponse`: extract `.Text` for the history entry.
2. Store `.DetectedTell` as `_activeTell` for the next turn.

---

## Changes to `TurnResult`

### New Properties

```csharp
/// <summary>
/// The tell bonus applied this turn: 0 if no tell matched, 2 if the player read the tell correctly.
/// </summary>
public int TellReadBonus { get; }

/// <summary>
/// Post-roll message if the tell was read correctly, e.g. "📖 You read the moment. +2 bonus."
/// Null if no tell bonus was applied.
/// </summary>
public string? TellReadMessage { get; }
```

### Constructor Change
The constructor must accept two additional parameters: `int tellReadBonus` and `string? tellReadMessage`.

---

## Roll Bonus Mechanics

The +2 tell bonus is applied to the **d20 roll total** (i.e. `d20 + statMod + levelBonus + tellBonus`), which means:
- It affects whether the roll succeeds or fails (can push a miss over the DC).
- It affects the "beat DC by" margin, which in turn affects `SuccessScale` / `FailureScale` outputs.
- It is NOT a separate interest delta addition — it modifies the roll itself.

### Integration with `RollEngine`

`RollEngine.Resolve` currently computes `total = d20Roll + modifier + levelBonus`. The tell bonus needs to be added. There are two approaches:

**Option A (preferred)**: Add an optional `int rollBonus = 0` parameter to `RollEngine.Resolve`. `GameSession` passes `2` when the tell matches, `0` otherwise. This keeps the bonus visible in `RollResult.Total`.

**Option B**: `GameSession` applies the +2 after the roll by constructing a modified `RollResult`. This is less clean because `RollResult` is immutable.

The implementer should use **Option A** — adding a `rollBonus` parameter to `RollEngine.Resolve` — since it is the simplest change and keeps all roll math in one place.

### Updated `RollEngine.Resolve` Signature
```csharp
public static RollResult Resolve(
    StatType stat,
    StatBlock attacker,
    StatBlock defender,
    TrapState attackerTraps,
    int level,
    ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage,
    bool hasDisadvantage,
    int rollBonus = 0)  // NEW — hidden bonus from tells (or future mechanics)
```

The `rollBonus` is added to `total` before comparing against DC:
```
total = d20Roll + modifier + levelBonus + rollBonus
```

All existing callers pass 0 (default), so this is backward-compatible.

---

## Input/Output Examples

### Example 1: Tell Matched — Bonus Applied
```
Turn 2 opponent response: OpponentResponse("Ha, that's actually funny", new Tell(StatType.Wit, "Makes a joke"))
  → _activeTell = Tell(Wit, "Makes a joke")

Turn 3 StartTurnAsync:
  → LLM returns options: [Charm, Honesty, Wit, Chaos]
  → Wit option gets HasTellBonus = true
  → Player sees 📖 indicator on Wit option

Turn 3 ResolveTurnAsync(optionIndex = 2):  // Player picks Wit
  → chosenOption.Stat == Wit == _activeTell.Stat → match!
  → RollEngine.Resolve(..., rollBonus: 2)
  → Total = d20(12) + witMod(3) + levelBonus(1) + tellBonus(2) = 18 vs DC 16 → success
  → _activeTell = null (consumed)
  → TurnResult.TellReadBonus = 2
  → TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."
```

### Example 2: Tell Not Matched — No Bonus
```
Turn 2 opponent response: OpponentResponse("...", new Tell(StatType.SelfAwareness, "Goes silent"))
  → _activeTell = Tell(SelfAwareness, "Goes silent")

Turn 3 ResolveTurnAsync(optionIndex = 0):  // Player picks Charm
  → chosenOption.Stat == Charm != SelfAwareness → no match
  → RollEngine.Resolve(..., rollBonus: 0)
  → _activeTell = null (consumed regardless)
  → TurnResult.TellReadBonus = 0
  → TurnResult.TellReadMessage = null
```

### Example 3: No Tell Active
```
Turn 2 opponent response: OpponentResponse("Ok.", null)
  → _activeTell = null

Turn 3 ResolveTurnAsync:
  → _activeTell is null → no tell check
  → RollEngine.Resolve(..., rollBonus: 0)
  → TurnResult.TellReadBonus = 0
  → TurnResult.TellReadMessage = null
```

### Example 4: Tell Bonus Turns Miss Into Hit
```
_activeTell = Tell(StatType.Honesty, "Shares something vulnerable")
Player picks Honesty option.

Without bonus: d20(11) + honestyMod(2) + levelBonus(0) = 13 vs DC 15 → miss by 2
With bonus:    d20(11) + honestyMod(2) + levelBonus(0) + tellBonus(2) = 15 vs DC 15 → success (beat by 0 → SuccessScale +1)

TurnResult.TellReadBonus = 2
TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."
```

---

## Acceptance Criteria

### AC1: `Tell` type defined
- `Tell` is a `sealed class` in `Pinder.Core.Conversation`.
- Has `StatType Stat` and `string Description` readonly properties.
- Constructor throws `ArgumentNullException` if `description` is null.

### AC2: `OpponentResponse` carries optional `Tell`
- `OpponentResponse` is a `sealed class` in `Pinder.Core.Conversation`.
- Has `string Text` and `Tell? DetectedTell` properties.
- `ILlmAdapter.GetOpponentResponseAsync` return type changes from `Task<string>` to `Task<OpponentResponse>`.
- `NullLlmAdapter` updated to return `new OpponentResponse("...", null)`.

### AC3: `GameSession` applies +2 on roll for matching stat
- `GameSession` stores `_activeTell` from the previous turn's `OpponentResponse.DetectedTell`.
- In `ResolveTurnAsync`, if `_activeTell?.Stat == chosenOption.Stat`, passes `rollBonus: 2` to `RollEngine.Resolve`.
- The +2 is added to the roll total (affects success/failure determination).
- `_activeTell` is set to null after consumption (whether matched or not).

### AC4: Displayed percentage excludes bonus
- The `HasTellBonus` flag on `DialogueOption` is set during `StartTurnAsync` for UI display purposes only.
- No success percentage computation includes the +2 bonus. The bonus is hidden until post-roll reveal.
- (Note: the engine does not compute success percentages — this is a constraint on any future UI/host implementation.)

### AC5: `TurnResult.TellReadBonus` and `TellReadMessage` populated correctly
- `TurnResult` has new properties: `int TellReadBonus` (0 or 2) and `string? TellReadMessage`.
- When tell matches: `TellReadBonus = 2`, `TellReadMessage = "📖 You read the moment. +2 bonus."`.
- When tell does not match or no tell active: `TellReadBonus = 0`, `TellReadMessage = null`.

### AC6: Tests cover tell bonus application
- Test: tell bonus applied when chosen stat matches active tell — verify `TurnResult.TellReadBonus == 2` and `TellReadMessage` is non-null.
- Test: tell bonus NOT applied when chosen stat differs from active tell — verify `TurnResult.TellReadBonus == 0`.
- Test: no tell active — verify `TurnResult.TellReadBonus == 0`.
- Test: tell consumed after one turn (second turn after tell set has no bonus).
- Test: tell bonus can turn a miss into a hit (integration with `RollEngine`).

### AC7: Build clean
- `dotnet build` succeeds with zero warnings/errors.
- All existing tests pass (`dotnet test`).

---

## Edge Cases

| Case | Expected Behaviour |
|------|-------------------|
| No tell active (`_activeTell == null`) | `rollBonus = 0`, `TellReadBonus = 0`, `TellReadMessage = null` |
| Tell active but player picks different stat | `rollBonus = 0`, tell consumed (set to null), `TellReadBonus = 0` |
| Tell active and player picks matching stat | `rollBonus = 2`, tell consumed, `TellReadBonus = 2` |
| Tell from turn N, player doesn't act until turn N+2 | Tells persist until consumed. One `StartTurnAsync`/`ResolveTurnAsync` cycle consumes the tell regardless of match. (Tell set in turn N's resolve is active for turn N+1's resolve.) |
| Multiple tells in sequence (opponent sends tell every turn) | Each new tell overwrites the previous one. Only the most recent tell is active. |
| First turn (no prior opponent response) | `_activeTell` is null; no tell bonus possible. |
| Nat 20 with tell bonus | `rollBonus: 2` is added to total. Nat 20 is already auto-success; the +2 increases the "beat DC by" margin, potentially yielding a higher `SuccessScale` tier. |
| Nat 1 with tell bonus | Nat 1 is auto-failure (Legendary tier). The tell bonus does not override natural 1 auto-fail. Tell is still consumed. `TellReadBonus = 2` because the player correctly read the tell (picked the matching stat); the `rollBonus: 2` was passed to `RollEngine.Resolve` but Nat 1 auto-fail takes precedence. `TellReadMessage` is set. This is consistent with the "tell matches but roll still fails" case — the tell was read, the bonus was applied, the outcome was just too bad. |
| Tell stat matches but roll still fails after +2 | `TellReadBonus = 2`, `TellReadMessage` is set. The bonus was applied but wasn't enough. (The tell was "read" even if the roll failed — the player picked the right stat.) |
| `DialogueOption.HasTellBonus` is true but player picks a different option | The non-selected option's `HasTellBonus` flag is irrelevant. Only the chosen option's stat is compared to the tell. |

---

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `Tell` constructed with null `description` | `ArgumentNullException` | `"description"` |
| `OpponentResponse` constructed with null `text` | `ArgumentNullException` | `"text"` |

No other new error conditions. The tell mechanic is additive — it introduces new fields and a bonus but does not create new failure modes in the game session flow.

---

## Dependencies

| Dependency | Type | Status |
|-----------|------|--------|
| **#27 — GameSession** | Hard (modifies `GameSession`) | Merged |
| **#63 — Architecture review** | Hard (per issue) | Merged |
| `ILlmAdapter` | Interface (return type change) | Existing — modified |
| `NullLlmAdapter` | Test adapter | Existing — modified |
| `RollEngine.Resolve` | Static method (new `rollBonus` param) | Existing — modified |
| `DialogueOption.HasTellBonus` | Existing property | Already present in codebase |
| `TurnResult` | Existing class (new properties) | Existing — modified |
| `StatType` | Existing enum | Unchanged |

---

## Files to Create

| File | Content |
|------|---------|
| `src/Pinder.Core/Conversation/Tell.cs` | `Tell` sealed class |
| `src/Pinder.Core/Conversation/OpponentResponse.cs` | `OpponentResponse` sealed class |

## Files to Modify

| File | Change |
|------|--------|
| `src/Pinder.Core/Interfaces/ILlmAdapter.cs` | `GetOpponentResponseAsync` returns `Task<OpponentResponse>` |
| `src/Pinder.Core/Conversation/NullLlmAdapter.cs` | Return `OpponentResponse` instead of `string` |
| `src/Pinder.Core/Conversation/GameSession.cs` | Add `_activeTell` field; tell matching + bonus in `ResolveTurnAsync`; flag options in `StartTurnAsync`; extract tell from `OpponentResponse` |
| `src/Pinder.Core/Conversation/TurnResult.cs` | Add `TellReadBonus` and `TellReadMessage` properties |
| `src/Pinder.Core/Rolls/RollEngine.cs` | Add `int rollBonus = 0` parameter to `Resolve` |

## Test Files

| File | Tests |
|------|-------|
| `tests/Pinder.Core.Tests/TellTests.cs` | Constructor validation, null description throws |
| `tests/Pinder.Core.Tests/OpponentResponseTests.cs` | Constructor validation, null text throws, null tell allowed |
| `tests/Pinder.Core.Tests/GameSessionTellTests.cs` | Tell bonus applied on match; not applied on mismatch; no tell active; tell consumed after one turn; tell bonus turns miss into hit; Nat 1 with tell; HasTellBonus flag set on matching options |

---

## Stacking Order in `ResolveTurnAsync` (Updated)

After this issue is implemented, the full interest delta computation in `ResolveTurnAsync` follows this order:

```
0. Tell bonus    = +2 to ROLL TOTAL if tell stat matches chosen stat (applied via rollBonus param)
1. Base delta    = SuccessScale.GetInterestDelta(roll) or FailureScale.GetInterestDelta(roll)
2. Risk bonus    = (from #42, if implemented) +1 for Hard, +2 for Bold — success only
3. Callback bonus = (from #47, if implemented) CallbackBonus.Compute() — success only
4. Momentum bonus = GetMomentumBonus(streak) — success only
───────────────────
   Total interestDelta = sum of 1–4 → InterestMeter.Apply(total)
```

The tell bonus (step 0) is fundamentally different from the other bonuses: it modifies the **roll total**, not the interest delta. This means it can change the roll from failure to success, which then unlocks the other success-only bonuses.
