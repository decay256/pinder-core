# Spec: Issue #49 — Weakness Windows (§15 Opponent Crack Detection)

## Overview

Weakness windows are a one-turn DC reduction mechanic triggered when the opponent's last message reveals a "crack" — a contradiction, genuine laugh, personal overshare, flustered reply, risky joke, or personal question. When the LLM detects a crack in the opponent's response, it returns a `WeaknessWindow` via `OpponentResponse`. On the next turn, the matching stat's DC is reduced, and the corresponding `DialogueOption` is flagged with `HasWeaknessWindow = true` so the UI can display a 🔓 icon. The window expires after exactly one turn whether exploited or not.

This implements rules v3.4 §15.

---

## Crack Trigger Table (from §15)

| Opponent Behaviour                     | Defending Stat  | DC Reduction |
|---------------------------------------|-----------------|-------------|
| Contradicts themselves                 | Honesty         | −2          |
| Laughs genuinely                       | Charm           | −2          |
| Shares something personal (unprompted) | SelfAwareness   | −3          |
| Gets flustered / responds too fast     | Wit             | −2          |
| Asks YOU a personal question           | Honesty         | −2          |
| Makes a risky joke                     | Chaos           | −2          |

Note: Two behaviours (contradicts themselves, asks a personal question) map to the same stat (Honesty) with the same reduction (−2). They are distinct trigger reasons but mechanically identical.

---

## Existing Types (Already in Codebase)

### `WeaknessWindow`

**Namespace**: `Pinder.Core.Conversation`
**File**: `src/Pinder.Core/Conversation/WeaknessWindow.cs`
**Status**: Stub exists. Needs constructor validation added.

```csharp
public sealed class WeaknessWindow
{
    public StatType DefendingStat { get; }   // read-only
    public int DcReduction { get; }          // read-only, must be > 0

    public WeaknessWindow(StatType defendingStat, int dcReduction);
}
```

**Required change**: The constructor must validate `dcReduction > 0` and throw `ArgumentOutOfRangeException` if violated. The current stub has no validation.

**Semantics**: `DefendingStat` is the stat used for *defence* by the opponent. The *attacker* benefits when attacking with the stat whose defence pairing is `DefendingStat`. For example, if `DefendingStat = StatType.Honesty`, the attacking stat that benefits is `SelfAwareness` (because `DefenceTable[SelfAwareness] = Honesty`).

### `OpponentResponse`

**Namespace**: `Pinder.Core.Conversation`
**File**: `src/Pinder.Core/Conversation/OpponentResponse.cs`
**Status**: Already exists with correct shape.

```csharp
public sealed class OpponentResponse
{
    public string MessageText { get; }
    public Tell? DetectedTell { get; }
    public WeaknessWindow? WeaknessWindow { get; }

    public OpponentResponse(
        string messageText,
        Tell? detectedTell = null,
        WeaknessWindow? weaknessWindow = null);
}
```

**No changes needed.** Constructor already validates `messageText` non-null.

### `ILlmAdapter.GetOpponentResponseAsync`

**File**: `src/Pinder.Core/Interfaces/ILlmAdapter.cs`
**Status**: Already returns `Task<OpponentResponse>`. No changes needed.

```csharp
Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);
```

---

## Types to Modify

### `DialogueOption` — Add `HasWeaknessWindow` Property

**File**: `src/Pinder.Core/Conversation/DialogueOption.cs`

Add a new read-only property and constructor parameter:

```csharp
/// <summary>
/// True if a weakness window is active for this option's defending stat.
/// UI displays a 🔓 icon when true. The DC shown already reflects the reduction.
/// </summary>
public bool HasWeaknessWindow { get; }
```

Updated constructor (new parameter must have default `false` for backward compatibility):

```csharp
public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool hasWeaknessWindow = false)
```

### `RollEngine.Resolve` — Add `dcAdjustment` Parameter

**File**: `src/Pinder.Core/Rolls/RollEngine.cs`

Per the Sprint 8 Wave 0 contract (#139), `Resolve` gains two optional parameters. The weakness windows feature uses `dcAdjustment`:

```csharp
public static RollResult Resolve(
    StatType stat, StatBlock attacker, StatBlock defender,
    TrapState attackerTraps, int level, ITrapRegistry trapRegistry, IDiceRoller dice,
    bool hasAdvantage = false, bool hasDisadvantage = false,
    int externalBonus = 0,    // added to total before success check
    int dcAdjustment = 0);    // subtracted from DC (positive = easier)
```

DC computation changes from:
```
int dc = defender.GetDefenceDC(attackingStat);
```
to:
```
int dc = defender.GetDefenceDC(attackingStat) - dcAdjustment;
```

The `RollResult.DC` property must reflect the **adjusted** DC (i.e., the DC the player actually had to beat).

**Note**: `dcAdjustment` is a positive integer that *reduces* the DC. A `WeaknessWindow` with `DcReduction = 2` maps to `dcAdjustment = 2`.

**Note**: `externalBonus` and `dcAdjustment` default to 0, preserving backward compatibility with all existing callers and tests.

**Note**: `RollEngine.Resolve` may already have these parameters if Wave 0 (#139) is implemented first. If so, no changes are needed to `RollEngine` for this issue — just use the existing `dcAdjustment` parameter.

### `TurnResult` — Add `DetectedWindow` Property

**File**: `src/Pinder.Core/Conversation/TurnResult.cs`

Add a new property to communicate the weakness window detected in the *current* turn's opponent response back to the host/UI:

```csharp
/// <summary>
/// Weakness window detected in the opponent's response this turn, if any.
/// The caller (UI) may use this to preview the next turn's opportunity.
/// </summary>
public WeaknessWindow? DetectedWindow { get; }
```

Add to constructor with default `null`:

```csharp
public TurnResult(
    ...,
    WeaknessWindow? detectedWindow = null)
```

### `GameSession` — Core Weakness Window Logic

**File**: `src/Pinder.Core/Conversation/GameSession.cs`

#### New Field

```csharp
private WeaknessWindow? _activeWeakness;  // set after opponent response, consumed on next turn
```

Initial value: `null`.

#### Changes to `ResolveTurnAsync`

At the end of `ResolveTurnAsync`, after calling `_llm.GetOpponentResponseAsync(...)`:

1. Read `OpponentResponse.WeaknessWindow` from the response.
2. Store it as `_activeWeakness` for the *next* turn.
3. Include the detected window in the returned `TurnResult.DetectedWindow`.

When resolving the chosen option's roll:

1. Check if `_activeWeakness` is not null.
2. If `StatBlock.DefenceTable[chosenOption.Stat] == _activeWeakness.DefendingStat`, set `dcAdjustment = _activeWeakness.DcReduction`.
3. Otherwise, `dcAdjustment = 0`.
4. Pass `dcAdjustment` to `RollEngine.Resolve(dcAdjustment: dcAdjustment)`.
5. **After** the roll resolves (regardless of which option was chosen, regardless of success/failure), clear `_activeWeakness = null`.

**Important ordering**: The `_activeWeakness` from the *previous* turn's opponent response is consumed during *this* turn's roll. Then the *current* turn's opponent response may set a *new* `_activeWeakness` for the *next* turn.

#### Changes to `StartTurnAsync`

When building/enriching `DialogueOption` objects:

1. Check if `_activeWeakness` is not null.
2. For each option, look up the defending stat: `StatBlock.DefenceTable[option.Stat]`.
3. If it matches `_activeWeakness.DefendingStat`, set `HasWeaknessWindow = true` on that option.
4. All non-matching options get `HasWeaknessWindow = false`.
5. When `_activeWeakness` is null, all options get `HasWeaknessWindow = false`.

The DC displayed in the option (if the option carries a displayed DC) must already reflect the reduction.

---

## Function Signatures (Complete Summary)

### New Members

| Type | Member | Signature |
|------|--------|-----------|
| `DialogueOption` | `HasWeaknessWindow` | `bool` (read-only property) |
| `TurnResult` | `DetectedWindow` | `WeaknessWindow?` (read-only property) |
| `GameSession` | `_activeWeakness` | `WeaknessWindow?` (private field) |

### Modified Members

| Type | Member | Change |
|------|--------|--------|
| `WeaknessWindow` | Constructor | Add validation: throw `ArgumentOutOfRangeException` if `dcReduction <= 0` |
| `DialogueOption` | Constructor | Add `bool hasWeaknessWindow = false` parameter |
| `RollEngine` | `Resolve` | Add `int dcAdjustment = 0` parameter (may already exist from Wave 0) |
| `TurnResult` | Constructor | Add `WeaknessWindow? detectedWindow = null` parameter |
| `GameSession` | `StartTurnAsync` | Enrich options with `HasWeaknessWindow` based on `_activeWeakness` |
| `GameSession` | `ResolveTurnAsync` | Apply `dcAdjustment` from `_activeWeakness`, clear after roll, store new window from response |

### Unchanged (Already Correct)

| Type | Member | Notes |
|------|--------|-------|
| `WeaknessWindow` | `DefendingStat`, `DcReduction` | Properties already exist |
| `OpponentResponse` | `WeaknessWindow` | Property already exists |
| `ILlmAdapter` | `GetOpponentResponseAsync` | Already returns `Task<OpponentResponse>` |

---

## Input/Output Examples

### Example 1: Crack Detected → Window Applied Next Turn

**Turn N — Opponent response contains a crack:**

The LLM detects the opponent contradicted themselves. `GetOpponentResponseAsync` returns:

```
OpponentResponse(
    messageText: "Wait, I said I hated pineapple pizza but... okay fine I had some last week.",
    detectedTell: null,
    weaknessWindow: WeaknessWindow(StatType.Honesty, dcReduction: 2)
)
```

`GameSession` stores `_activeWeakness = WeaknessWindow(Honesty, 2)`.
`TurnResult.DetectedWindow = WeaknessWindow(Honesty, 2)`.

**Turn N+1 — StartTurnAsync:**

The session checks `_activeWeakness`. For each dialogue option, it looks up `StatBlock.DefenceTable[option.Stat]`:
- Option with `Charm` → defends with `SelfAwareness` → not Honesty → `HasWeaknessWindow = false`
- Option with `Honesty` → defends with `Chaos` → not Honesty → `HasWeaknessWindow = false`
- Option with `SelfAwareness` → defends with `Honesty` → **match!** → `HasWeaknessWindow = true`
- Option with `Wit` → defends with `Rizz` → not Honesty → `HasWeaknessWindow = false`

The `SelfAwareness` option gets the 🔓 icon.

**Turn N+1 — ResolveTurnAsync (player picks SelfAwareness):**

Normal DC: `13 + opponent.GetBase(Honesty)` (e.g., if opponent Honesty modifier = 2, DC = 15).
With window: `DC = 15 - 2 = 13`.

`RollEngine.Resolve(..., dcAdjustment: 2)` is called. After the roll, `_activeWeakness = null`.

### Example 2: Window Not Used (Player Picks a Different Stat)

Same setup — `_activeWeakness = WeaknessWindow(Honesty, 2)`. Player picks `Charm` (defended by `SelfAwareness`, not `Honesty`). `dcAdjustment = 0`. The window still clears: `_activeWeakness = null`.

### Example 3: No Crack Detected

`GetOpponentResponseAsync` returns `OpponentResponse("...", weaknessWindow: null)`. `_activeWeakness` is set to `null`. Next turn: all `HasWeaknessWindow = false`, no DC modifications.

### Example 4: SelfAwareness Overshare (DC −3)

Opponent shares something personal unprompted. LLM returns:

```
OpponentResponse(
    messageText: "I haven't told anyone this but... I was actually born in a petri dish.",
    weaknessWindow: WeaknessWindow(StatType.SelfAwareness, dcReduction: 3)
)
```

On the next turn: `DefenceTable[Charm] = SelfAwareness` → **Charm** option gets `HasWeaknessWindow = true` with DC reduced by 3.

### Example 5: Consecutive Cracks (Replacement, Not Stacking)

Turn N opponent response: `WeaknessWindow(Honesty, 2)` → stored as `_activeWeakness`.
Turn N+1: `_activeWeakness` consumed (applied or not), cleared.
Turn N+1 opponent response: `WeaknessWindow(Charm, 2)` → new `_activeWeakness`.
Turn N+2: the Charm window is active, not Honesty.

There is no stacking. Each turn's opponent response replaces any prior stored window.

---

## Acceptance Criteria

### AC1: `WeaknessWindow` type defined

- `WeaknessWindow` class exists in `Pinder.Core.Conversation` (already exists).
- Has `StatType DefendingStat` (read-only) and `int DcReduction` (read-only) properties (already exist).
- Constructor validates `dcReduction > 0`; throws `ArgumentOutOfRangeException` if violated (**needs adding**).

### AC2: `OpponentResponse` carries optional `WeaknessWindow`

- `OpponentResponse` class exists with `WeaknessWindow? WeaknessWindow` property (**already exists**).
- `ILlmAdapter.GetOpponentResponseAsync` returns `Task<OpponentResponse>` (**already exists**).
- All `ILlmAdapter` implementations compile with the existing signature.

### AC3: `GameSession` stores active window, applies DC reduction for one turn, clears after turn

- `GameSession` has a `_activeWeakness` field of type `WeaknessWindow?`.
- After `GetOpponentResponseAsync` returns, the session stores `response.WeaknessWindow` as `_activeWeakness`.
- In the next `ResolveTurnAsync`, if `_activeWeakness != null` and `StatBlock.DefenceTable[chosenOption.Stat] == _activeWeakness.DefendingStat`, then `RollEngine.Resolve` is called with `dcAdjustment = _activeWeakness.DcReduction`.
- If the defending stat does NOT match, `dcAdjustment = 0`.
- After the roll (regardless of which option was chosen or the outcome), `_activeWeakness` is set to `null`.
- The window lasts exactly one turn — the turn immediately after the crack message.

### AC4: `DialogueOption.HasWeaknessWindow` set correctly

- Each `DialogueOption` returned from `StartTurnAsync` has `HasWeaknessWindow = true` if `_activeWeakness != null` and `StatBlock.DefenceTable[option.Stat] == _activeWeakness.DefendingStat`.
- Options whose defending stat does not match have `HasWeaknessWindow = false`.
- When `_activeWeakness` is null, all options have `HasWeaknessWindow = false`.

### AC5: DC displayed in option already reflects the reduction

- The DC in the roll result (`RollResult.DC`) reflects the reduced value (i.e., `defender.GetDefenceDC(stat) - dcAdjustment`).
- The UI/host does not need to compute the reduction — it is already applied.

### AC6: Tests

Required test scenarios (see Test Scenarios section below for details):
- Window applied for one turn then cleared
- Correct stat DC reduced
- No window → no reduction
- Window clears even if not exploited
- DcReduction validation (`<= 0` throws)

### AC7: Build clean

- `dotnet build` succeeds with zero errors and zero warnings.
- All existing tests continue to pass (backward compatibility via default parameter values).
- New tests pass.

---

## Test Scenarios

### T1: Window Applied for One Turn Then Cleared

1. Create a `GameSession` with a mock `ILlmAdapter`.
2. Mock `GetOpponentResponseAsync` to return `OpponentResponse("msg", weaknessWindow: new WeaknessWindow(StatType.Honesty, 2))` on turn 0.
3. Complete turn 0 (StartTurnAsync → ResolveTurnAsync).
4. On turn 1, `StartTurnAsync` should show `HasWeaknessWindow = true` for the option whose defending stat is Honesty (attacking stat = SelfAwareness).
5. Mock `GetOpponentResponseAsync` to return `OpponentResponse("msg", weaknessWindow: null)` on turn 1.
6. Complete turn 1 (ResolveTurnAsync with any option).
7. On turn 2, `StartTurnAsync` should show `HasWeaknessWindow = false` for all options.

### T2: Correct Stat DC Reduced

1. Set up opponent with known stat values (e.g., Honesty base modifier = 2, so DC = 15).
2. Set `_activeWeakness = WeaknessWindow(Honesty, 2)`.
3. Player picks `SelfAwareness` (defended by Honesty).
4. Verify `RollEngine.Resolve` is called with `dcAdjustment = 2`.
5. Verify `RollResult.DC == 13` (15 − 2).

### T3: No Window → No Reduction

1. Complete a turn where opponent returns no window.
2. Verify all options have `HasWeaknessWindow = false`.
3. Verify `RollEngine.Resolve` is called with `dcAdjustment = 0`.

### T4: Window Clears Even If Not Exploited

1. Set `_activeWeakness = WeaknessWindow(Honesty, 2)`.
2. Player picks `Charm` (defended by SelfAwareness, not Honesty).
3. Verify `dcAdjustment = 0` for this roll.
4. After the turn, verify `_activeWeakness` is null (next turn has no window).

### T5: DcReduction Validation

1. `new WeaknessWindow(StatType.Charm, 0)` throws `ArgumentOutOfRangeException`.
2. `new WeaknessWindow(StatType.Charm, -1)` throws `ArgumentOutOfRangeException`.
3. `new WeaknessWindow(StatType.Charm, 1)` succeeds.

### T6: DetectedWindow in TurnResult

1. Mock opponent response with a weakness window.
2. Complete a turn via `ResolveTurnAsync`.
3. Verify `TurnResult.DetectedWindow` equals the window from the opponent response.
4. When opponent returns no window, verify `TurnResult.DetectedWindow` is null.

### T7: Window Does Not Apply to Read/Recover/Wait

1. Set `_activeWeakness` to a non-null window.
2. Call `ReadAsync()`, `RecoverAsync()`, or `Wait()`.
3. These actions use `ResolveFixedDC` (DC 12, SA stat) — the weakness window should NOT modify their DC.
4. After the Read/Recover/Wait, `_activeWeakness` should be cleared (the turn has passed).

---

## Edge Cases

1. **Multiple cracks in sequence**: If the opponent's response on turn N detects a crack, and the opponent's response on turn N+1 also detects a crack, the new window **replaces** the old one. There is no stacking. `_activeWeakness` is simply overwritten after the old one is consumed/cleared.

2. **Window on first turn**: `_activeWeakness` is `null` at game start. Turn 0's `StartTurnAsync` has no window active. This is the normal starting case.

3. **Game ends on the turn a window is set**: If interest hits 0 or 25 during `ResolveTurnAsync`, the game ends. The window stored from the opponent's response is irrelevant — there is no next turn. No special handling needed.

4. **DC reduced below 1**: If `dcAdjustment` is large enough that the effective DC drops below 1, the roll still uses that DC. No clamping. A d20 roll of 1 (nat 1) is still auto-fail per existing rules; anything else beats the low DC trivially.

5. **Defence table mapping (critical for implementers)**:
   - `DefenceTable[Charm] = SelfAwareness` → window on SelfAwareness benefits Charm attacks
   - `DefenceTable[Rizz] = Wit` → window on Wit benefits Rizz attacks
   - `DefenceTable[Honesty] = Chaos` → window on Chaos benefits Honesty attacks
   - `DefenceTable[Chaos] = Charm` → window on Charm benefits Chaos attacks
   - `DefenceTable[Wit] = Rizz` → window on Rizz benefits Wit attacks
   - `DefenceTable[SelfAwareness] = Honesty` → window on Honesty benefits SelfAwareness attacks

6. **LLM adapter returns unexpected stat**: The engine does not validate that the window's stat matches the §15 crack table. The LLM is trusted. The engine only cares about `DefendingStat` and `DcReduction`.

7. **NullLlmAdapter**: Always returns `OpponentResponse` with `WeaknessWindow = null`. No crack detection in null/test adapter by default.

8. **DialogueOption enrichment is done by GameSession**: The LLM returns `DialogueOption[]` from `GetDialogueOptionsAsync`. These do NOT have `HasWeaknessWindow` set by the LLM. `GameSession` enriches them by checking `_activeWeakness` against each option's defending stat. GameSession is authoritative.

9. **Interaction with other bonuses**: `dcAdjustment` is independent of `externalBonus` (tell bonus, callback bonus, combo bonus). They stack orthogonally — `externalBonus` affects the roll total, `dcAdjustment` affects the DC.

---

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `WeaknessWindow` constructed with `dcReduction <= 0` | `ArgumentOutOfRangeException` | "dcReduction must be greater than zero" |
| `OpponentResponse` constructed with null `messageText` | `ArgumentNullException` | (already implemented) |
| `ILlmAdapter` implementation returns null `OpponentResponse` | `NullReferenceException` | GameSession should guard against null response; throw `InvalidOperationException` with message "LLM adapter returned null opponent response" |

No new exception types are introduced.

---

## Dependencies

| Dependency | Type | Status | Notes |
|-----------|------|--------|-------|
| Issue #27 (GameSession) | Code | **Merged** | GameSession exists with `StartTurnAsync` / `ResolveTurnAsync` |
| Issue #63 (Architecture) | Code | **Merged** | Sprint architecture context |
| Issue #139 Wave 0 | Code | **Required** | `RollEngine.Resolve(dcAdjustment)` parameter — if not yet merged, this issue must add the parameter or wait |
| `WeaknessWindow` class | Code | **Exists** (stub) | Needs validation added |
| `OpponentResponse` class | Code | **Exists** | No changes needed |
| `ILlmAdapter` interface | Code | **Exists** | Already returns `Task<OpponentResponse>` |
| `StatBlock.DefenceTable` | Code | **Exists** | Used for attack→defence stat mapping |
| `DialogueOption` class | Code | **Exists** | Needs `HasWeaknessWindow` property added |
| `TurnResult` class | Code | **Exists** | Needs `DetectedWindow` property added |

**No new external/NuGet dependencies.** All changes are pure C# within the existing project structure. Target: netstandard2.0, LangVersion 8.0. No `record` types — use `sealed class`.
