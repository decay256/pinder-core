# Spec: Issue #49 — Weakness Windows (§15 Opponent Crack Detection)

## Overview

Weakness windows are a one-turn DC reduction mechanic from rules v3.4 §15. When the LLM detects a "crack" in the opponent's response (contradiction, genuine laugh, personal overshare, flustered reply, risky joke, or personal question), it returns a `WeaknessWindow` indicating which defending stat is weakened and by how much. On the next turn, the matching dialogue option's DC is reduced and flagged with `HasWeaknessWindow = true` so the UI can display a 🔓 icon. The window expires after exactly one turn regardless of whether the player exploits it.

---

## Current Codebase State (Sprint 7 Baseline)

The following types **already exist** and do NOT need to be created:

- **`WeaknessWindow`** (`src/Pinder.Core/Conversation/WeaknessWindow.cs`) — sealed class with `StatType DefendingStat` and `int DcReduction` properties. Constructor exists but **lacks validation** (`dcReduction` can be ≤ 0).
- **`OpponentResponse`** (`src/Pinder.Core/Conversation/OpponentResponse.cs`) — sealed class with `string MessageText`, `Tell? DetectedTell`, `WeaknessWindow? WeaknessWindow`. Constructor validates `messageText != null`.
- **`ILlmAdapter.GetOpponentResponseAsync`** (`src/Pinder.Core/Interfaces/ILlmAdapter.cs`) — already returns `Task<OpponentResponse>`.

The following **need to be added or modified**:

- `WeaknessWindow` constructor: add `dcReduction > 0` validation
- `DialogueOption`: add `bool HasWeaknessWindow` property
- `RollEngine.Resolve`: add `int dcAdjustment = 0` parameter
- `GameSession`: add `_activeWeaknessWindow` field + weakness window logic in `StartTurnAsync` / `ResolveTurnAsync`
- `TurnResult`: add `WeaknessWindow? DetectedWindow` property

---

## Crack Trigger Table (from §15)

| Opponent Behaviour                      | Defending Stat   | DC Reduction |
|-----------------------------------------|------------------|-------------|
| Contradicts themselves                  | Honesty          | −2          |
| Laughs genuinely                        | Charm            | −2          |
| Shares something personal (unprompted)  | SelfAwareness    | −3          |
| Gets flustered / responds too fast      | Wit              | −2          |
| Asks YOU a personal question            | Honesty          | −2          |
| Makes a risky joke                      | Chaos            | −2          |

Note: Two behaviours map to Honesty with the same −2 reduction. They are distinct trigger reasons but mechanically identical. Detection is entirely the LLM's responsibility — `GameSession` does not detect cracks; it only stores and applies the `WeaknessWindow` returned by `ILlmAdapter.GetOpponentResponseAsync`.

---

## Function Signatures

### Modified: `WeaknessWindow` Constructor

**File**: `src/Pinder.Core/Conversation/WeaknessWindow.cs`

```csharp
public WeaknessWindow(StatType defendingStat, int dcReduction)
```

**Change**: Add validation — throw `ArgumentOutOfRangeException` if `dcReduction <= 0`.

### New Property: `DialogueOption.HasWeaknessWindow`

**File**: `src/Pinder.Core/Conversation/DialogueOption.cs`

```csharp
public bool HasWeaknessWindow { get; }
```

Constructor gains a new optional parameter (backward-compatible):

```csharp
public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool hasWeaknessWindow = false)  // NEW
```

### Modified: `RollEngine.Resolve`

**File**: `src/Pinder.Core/Rolls/RollEngine.cs`

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
    int dcAdjustment = 0)  // NEW — subtracted from DC
```

Inside the method, after computing `dc = defender.GetDefenceDC(stat)`:

```
dc = dc - dcAdjustment;
```

The `dcAdjustment` is a signed integer subtracted from the DC. A `WeaknessWindow` with `DcReduction = 2` passes `dcAdjustment = 2`, lowering the DC by 2. The `RollResult.DC` property must reflect the **modified** DC (i.e., the DC actually used for the roll comparison).

This parameter is also used by other Sprint 7 features (per architecture contract), so the name `dcAdjustment` is canonical — not `dcModifier`.

### New Property: `TurnResult.DetectedWindow`

**File**: `src/Pinder.Core/Conversation/TurnResult.cs`

```csharp
/// <summary>
/// Weakness window detected in the opponent's response this turn, if any.
/// The host/UI can use this to preview the next turn's opportunity.
/// </summary>
public WeaknessWindow? DetectedWindow { get; }
```

Constructor gains a new optional parameter (backward-compatible):

```csharp
public TurnResult(
    ...,  // existing parameters
    int xpEarned = 0,
    WeaknessWindow? detectedWindow = null)  // NEW — appended at end
```

### Modified: `GameSession` Internal State

**File**: `src/Pinder.Core/Conversation/GameSession.cs`

New field:

```csharp
private WeaknessWindow? _activeWeaknessWindow;  // set after opponent response, consumed next turn
```

Initial value: `null`.

---

## Data Flow (Per-Turn)

### Turn N — Opponent cracks

1. `ResolveTurnAsync` calls `_llm.GetOpponentResponseAsync(opponentContext)`
2. `OpponentResponse` comes back with `WeaknessWindow? WeaknessWindow`
3. `GameSession` stores `_activeWeaknessWindow = opponentResponse.WeaknessWindow`
4. `TurnResult.DetectedWindow` is set to `opponentResponse.WeaknessWindow` (for UI preview)

### Turn N+1 — Window is active

5. `StartTurnAsync` checks `_activeWeaknessWindow != null`
6. For each `DialogueOption`, compute: `StatBlock.DefenceTable[option.Stat]`
7. If `DefenceTable[option.Stat] == _activeWeaknessWindow.DefendingStat` → set `HasWeaknessWindow = true`
8. Return `TurnStart` with enriched options

### Turn N+1 — Player resolves

9. `ResolveTurnAsync(optionIndex)` checks if `_activeWeaknessWindow != null`
10. Compute `StatType defenceStat = StatBlock.DefenceTable[chosenOption.Stat]`
11. If `defenceStat == _activeWeaknessWindow.DefendingStat` → pass `dcAdjustment = _activeWeaknessWindow.DcReduction` to `RollEngine.Resolve`
12. If no match → pass `dcAdjustment = 0`
13. **Clear** `_activeWeaknessWindow = null` (regardless of match)
14. Store new window from this turn's opponent response (step 3 above)

---

## Input/Output Examples

### Example 1: Opponent contradicts themselves → SelfAwareness option benefits

**Turn 3 — Opponent response:**
```
OpponentResponse(
    messageText: "Wait, I said I hated pineapple pizza but... okay fine I had some last week.",
    detectedTell: null,
    weaknessWindow: WeaknessWindow(StatType.Honesty, dcReduction: 2)
)
```
`GameSession` stores `_activeWeaknessWindow = WeaknessWindow(Honesty, 2)`.

**Turn 4 — StartTurnAsync returns options:**

Defence table lookups:
| Option Stat    | Defence Stat (from DefenceTable) | Matches Honesty? | HasWeaknessWindow |
|----------------|----------------------------------|-------------------|-------------------|
| Charm          | SelfAwareness                    | No                | false             |
| Rizz           | Wit                              | No                | false             |
| SelfAwareness  | Honesty                          | **Yes**           | **true** 🔓       |
| Chaos          | Charm                            | No                | false             |

**Turn 4 — Player picks SelfAwareness (index 2):**

Normal DC: `13 + opponent.GetEffective(Honesty)` = e.g. 15
With window: `RollEngine.Resolve(..., dcAdjustment: 2)` → DC becomes 13
After roll, `_activeWeaknessWindow = null`.

### Example 2: Window not exploited — still clears

Same setup. Player picks Charm instead of SelfAwareness.
`dcAdjustment = 0` (no match). Window still clears: `_activeWeaknessWindow = null`.
Turn 5 has no active window.

### Example 3: Personal overshare → Charm option benefits with −3

```
OpponentResponse(
    messageText: "I haven't told anyone this but... I was actually born in a petri dish.",
    weaknessWindow: WeaknessWindow(StatType.SelfAwareness, dcReduction: 3)
)
```

`DefenceTable[Charm] = SelfAwareness` → the **Charm** option gets `HasWeaknessWindow = true`.
If player picks Charm: `dcAdjustment = 3`.

### Example 4: No crack detected

```
OpponentResponse(messageText: "lol ok whatever", weaknessWindow: null)
```

`_activeWeaknessWindow = null`. Next turn: all options `HasWeaknessWindow = false`, `dcAdjustment = 0`.

---

## Acceptance Criteria

### AC1: `WeaknessWindow` type validation

- `WeaknessWindow` constructor throws `ArgumentOutOfRangeException` when `dcReduction <= 0`
- Existing constructor signature unchanged; only validation logic added

### AC2: `OpponentResponse` carries optional `WeaknessWindow`

- **Already implemented.** `OpponentResponse.WeaknessWindow` property exists.
- `ILlmAdapter.GetOpponentResponseAsync` already returns `Task<OpponentResponse>`.
- Verify no regressions: all existing callers/tests still compile and pass.

### AC3: `GameSession` stores active window, applies DC reduction for one turn, clears after turn

- `GameSession` has `_activeWeaknessWindow` field of type `WeaknessWindow?`, initially `null`.
- After `GetOpponentResponseAsync` returns, `_activeWeaknessWindow` is set to `opponentResponse.WeaknessWindow`.
- In `ResolveTurnAsync`, if `_activeWeaknessWindow != null` and `StatBlock.DefenceTable[chosenOption.Stat] == _activeWeaknessWindow.DefendingStat`, call `RollEngine.Resolve` with `dcAdjustment = _activeWeaknessWindow.DcReduction`.
- If defending stat does not match, `dcAdjustment = 0`.
- After the roll, `_activeWeaknessWindow` is cleared to `null` regardless of match.
- Window lasts exactly one turn.

### AC4: `DialogueOption.HasWeaknessWindow` set correctly

- `DialogueOption` has a `bool HasWeaknessWindow` property (read-only, default `false`).
- In `StartTurnAsync`, each option is enriched: `HasWeaknessWindow = true` if `_activeWeaknessWindow != null && StatBlock.DefenceTable[option.Stat] == _activeWeaknessWindow.DefendingStat`.
- When `_activeWeaknessWindow` is null, all options have `HasWeaknessWindow = false`.

### AC5: DC displayed in option already reflects the reduction

- The DC the host/UI sees for an option must already account for the weakness window reduction.
- This is achieved via the `dcAdjustment` parameter flowing into `RollEngine.Resolve`, which computes the actual DC used; `RollResult.DC` reflects the modified value.

### AC6: Tests

Required test scenarios:
1. **Window applied for one turn then cleared**: Play turn N where LLM returns a `WeaknessWindow`. Verify turn N+1 applies the DC reduction. Verify turn N+2 has no window active.
2. **Correct stat DC reduced**: Given `WeaknessWindow(Honesty, 2)`, verify the SelfAwareness option has `HasWeaknessWindow = true` and the roll DC is reduced by 2.
3. **No window → no reduction**: When `OpponentResponse.WeaknessWindow` is null, verify all options `HasWeaknessWindow = false` and DC unmodified.
4. **Window clears even if not exploited**: Player picks non-matching stat. Verify window clears.
5. **DcReduction validation**: `new WeaknessWindow(StatType.Charm, 0)` throws `ArgumentOutOfRangeException`. Same for negative values.
6. **dcAdjustment parameter on RollEngine**: `RollEngine.Resolve` with `dcAdjustment = 2` produces a result where `RollResult.DC` is 2 lower than without.
7. **Backward compatibility**: Existing calls to `RollEngine.Resolve` without `dcAdjustment` produce identical results (default is 0).

### AC7: Build clean

- `dotnet build` succeeds with zero errors and zero warnings.
- All existing 254+ tests pass unchanged.
- New tests pass.

---

## Edge Cases

1. **Multiple cracks in sequence**: If turn N and turn N+1 both return a `WeaknessWindow`, the new one replaces the old. Only one window active at a time — latest overwrites.

2. **Window on first turn**: `_activeWeaknessWindow` starts as `null`. Turn 0's `StartTurnAsync` has no window. This is correct — no opponent response has occurred yet.

3. **Game ends on the turn a window is set**: If interest hits 0 or 25 during `ResolveTurnAsync`, the game ends. The stored window is irrelevant — no next turn exists. No special handling needed.

4. **DC goes very low or negative**: If `dcAdjustment` makes the DC < 1, the DC still applies as computed. No clamping. A d20 roll of 1 is still a Legendary Failure (Nat 1 rule), but any other roll trivially succeeds. This is intentional — the window makes the roll very easy.

5. **Same defending stat from different cracks**: Two different opponent behaviours can produce `WeaknessWindow(Honesty, 2)`. Mechanically identical — no distinction needed.

6. **LLM returns unexpected stat in window**: The engine does not validate that the window's stat matches the §15 crack table. The LLM is trusted. The engine only uses `DefendingStat` and `DcReduction`.

7. **DialogueOption enrichment vs LLM**: The LLM returns `DialogueOption[]` from `GetDialogueOptionsAsync`. These do NOT include `HasWeaknessWindow`. `GameSession` must enrich options post-LLM by checking `_activeWeaknessWindow` against each option's defending stat. The session is authoritative for this field.

8. **Interaction with `externalBonus` (callback/tell)**: The `dcAdjustment` parameter is independent of `externalBonus`. Both can apply simultaneously. `dcAdjustment` reduces the DC; `externalBonus` adds to the roll total. They are separate mechanical channels per the architecture contract.

9. **Interaction with Read/Recover/Wait (#43)**: These alternative actions use `ResolveFixedDC` (DC 12). Weakness windows do NOT apply to Read/Recover/Wait — they only apply to Speak actions via `ResolveTurnAsync`. The window should still clear if Read/Recover/Wait is chosen instead of Speak (consume the window, waste it).

---

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `WeaknessWindow(stat, dcReduction)` with `dcReduction <= 0` | `ArgumentOutOfRangeException` | `"dcReduction must be greater than zero"` |
| `RollEngine.Resolve` with existing invalid args | Unchanged — existing validation applies | — |
| `ILlmAdapter` returns null `OpponentResponse` | `NullReferenceException` at `opponentResponse.MessageText` | Guard already exists via existing code path |

No new exception types are introduced.

---

## Dependencies

| Dependency | Type | Status | Notes |
|-----------|------|--------|-------|
| `GameSession` (Issue #27) | Code | **Merged** | Weakness windows modify `StartTurnAsync` and `ResolveTurnAsync` |
| `ILlmAdapter` (Issue #26) | Interface | **Merged** | Already returns `Task<OpponentResponse>` |
| `OpponentResponse` | Type | **Exists** | Already has `WeaknessWindow?` property |
| `WeaknessWindow` | Type | **Exists** | Needs validation added |
| Architecture review (#63) | Process | **Merged** | Sprint 3 architecture context |
| `StatBlock.DefenceTable` | Data | **Exists** | Maps attacking stat → defending stat |
| `RollEngine.Resolve` | Method | **Exists** | Needs `dcAdjustment` parameter added |
| `DialogueOption` | Type | **Exists** | Needs `HasWeaknessWindow` property added |
| Sprint 7 Wave 0 (#139) | Architecture | **Merged** | Defines `dcAdjustment` as canonical param name |

**No new external/NuGet dependencies.** All changes are pure C# within the existing `netstandard2.0` project.

---

## Defence Table Quick Reference

For implementers — the mapping from attacking stat to defending stat:

| Attacking Stat   | Defending Stat   |
|------------------|------------------|
| Charm            | SelfAwareness    |
| Rizz             | Wit              |
| Honesty          | Chaos            |
| Chaos            | Charm            |
| Wit              | Rizz             |
| SelfAwareness    | Honesty          |

A `WeaknessWindow(DefendingStat = X)` benefits the attacking stat whose defence is X. For example, `WeaknessWindow(Honesty)` benefits `SelfAwareness` attacks (because `DefenceTable[SelfAwareness] = Honesty`).
