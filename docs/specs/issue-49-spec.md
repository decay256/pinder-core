# Spec: Issue #49 — Weakness Windows (§15 Opponent Crack Detection)

## Overview

Weakness windows are a one-turn DC reduction mechanic triggered when the opponent's last message reveals a "crack" — a contradiction, genuine laugh, personal overshare, flustered reply, risky joke, or personal question. When the LLM detects a crack in the opponent's response, it returns a `WeaknessWindow` indicating which defending stat is weakened and by how much. On the next turn, the matching stat's DC is reduced, and the corresponding `DialogueOption` is flagged with `HasWeaknessWindow = true` so the UI can display a 🔓 icon.

This implements rules v3.4 §15.

---

## Crack Trigger Table (from §15)

| Opponent Behaviour             | Defending Stat  | DC Reduction |
|-------------------------------|-----------------|-------------|
| Contradicts themselves         | Honesty         | −2          |
| Laughs genuinely               | Charm           | −2          |
| Shares something personal (unprompted) | SelfAwareness | −3    |
| Gets flustered / responds too fast | Wit          | −2          |
| Asks YOU a personal question   | Honesty         | −2          |
| Makes a risky joke             | Chaos           | −2          |

Note: two different behaviours (contradicts themselves, asks a personal question) map to the same stat (Honesty) with the same reduction (−2). They are distinct trigger reasons but mechanically identical.

---

## New Types

### `WeaknessWindow`

**Namespace**: `Pinder.Core.Conversation`
**File**: `src/Pinder.Core/Conversation/WeaknessWindow.cs`

```csharp
public sealed class WeaknessWindow
{
    /// <summary>The defending stat whose DC is reduced.</summary>
    public StatType DefendingStat { get; }

    /// <summary>
    /// The DC reduction amount (always a positive integer; subtracted from DC).
    /// Typically 2, except SelfAwareness overshare which is 3.
    /// </summary>
    public int DcReduction { get; }

    public WeaknessWindow(StatType defendingStat, int dcReduction);
}
```

**Constraints**:
- `dcReduction` must be > 0. If ≤ 0, throw `ArgumentOutOfRangeException`.
- `DefendingStat` is the stat used for *defence* against the attack — i.e., it's the value in `StatBlock.DefenceTable` for the attacking stat that benefits. For example, if `DefendingStat = StatType.Charm`, the attacker benefits when attacking with `Chaos` (because `DefenceTable[Chaos] = Charm`).

**Platform note**: Cannot use `record` (C# 9+). Use `sealed class` per project conventions (netstandard2.0, LangVersion 8.0).

---

### `OpponentResponse`

**Namespace**: `Pinder.Core.Conversation`
**File**: `src/Pinder.Core/Conversation/OpponentResponse.cs`

Currently, `ILlmAdapter.GetOpponentResponseAsync` returns `Task<string>`. To carry the optional weakness window alongside the opponent's message text, a new return type is needed.

```csharp
public sealed class OpponentResponse
{
    /// <summary>The opponent's reply text.</summary>
    public string Message { get; }

    /// <summary>
    /// Weakness window detected in this response, or null if no crack was detected.
    /// </summary>
    public WeaknessWindow? Window { get; }

    public OpponentResponse(string message, WeaknessWindow? window = null);
}
```

**Constraints**:
- `message` must not be null or empty. Throw `ArgumentNullException` / `ArgumentException` if violated.

---

## Modified Types

### `ILlmAdapter` — Signature Change

**File**: `src/Pinder.Core/Interfaces/ILlmAdapter.cs`

Change the return type of `GetOpponentResponseAsync`:

```csharp
// Before:
Task<string> GetOpponentResponseAsync(OpponentContext context);

// After:
Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context);
```

This is a **breaking change** to the interface. All implementations must be updated.

### `NullLlmAdapter` — Updated Implementation

**File**: `src/Pinder.Core/Conversation/NullLlmAdapter.cs`

Update `GetOpponentResponseAsync` to return `OpponentResponse` with `Window = null` (no crack detected in test adapter):

```csharp
public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
{
    return Task.FromResult(new OpponentResponse("..."));
}
```

### `DialogueOption` — New Property

**File**: `src/Pinder.Core/Conversation/DialogueOption.cs`

Add a new read-only property:

```csharp
/// <summary>
/// True if a weakness window is active for this option's defending stat.
/// UI displays a 🔓 icon when true. The DC shown already reflects the reduction.
/// </summary>
public bool HasWeaknessWindow { get; }
```

Update the constructor to accept this parameter (with default `false` for backward compatibility):

```csharp
public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool hasWeaknessWindow = false)
```

### `GameSession` — New Internal State and Turn Logic

**File**: `src/Pinder.Core/Conversation/GameSession.cs`

Add a new field:

```csharp
private WeaknessWindow? _activeWindow;  // set after opponent response, consumed on next turn
```

Initial value: `null`.

#### Changes to `ResolveTurnAsync`

After calling `_llm.GetOpponentResponseAsync(opponentContext)`:

1. Extract `opponentMessage` from the `OpponentResponse.Message` property (instead of the raw string).
2. Store `OpponentResponse.Window` as `_activeWindow` for the *next* turn.

This means `_activeWindow` is set at the **end** of a turn and consumed at the **start** of the next turn.

#### Changes to `StartTurnAsync`

When building `DialogueOption` objects (or when the LLM returns them), the session must:

1. Check if `_activeWindow` is not null.
2. For each dialogue option, determine the defending stat via `StatBlock.DefenceTable[option.Stat]`.
3. If `DefenceTable[option.Stat] == _activeWindow.DefendingStat`, that option gets `HasWeaknessWindow = true`.
4. The DC for that option's roll must be reduced by `_activeWindow.DcReduction`.

**DC Reduction Mechanism**: The DC reduction must be applied when `RollEngine.Resolve` is called in `ResolveTurnAsync`. There are two approaches:

- **Option A (preferred)**: Store the active window and, before calling `RollEngine.Resolve`, temporarily compute a modified DC. Since `RollEngine` computes DC internally from `defender.GetDefenceDC(stat)`, the cleanest approach is to pass the reduction as additional context. However, `RollEngine.Resolve` does not currently accept a DC modifier parameter. **The implementation must add an optional `int dcModifier = 0` parameter to `RollEngine.Resolve`** that is subtracted from the computed DC before comparison.
- **Option B**: Create a wrapper `StatBlock` that returns modified defence DCs. This is more complex and fragile.

After the roll is resolved (regardless of success/failure), **clear `_activeWindow = null`**. The window lasts exactly one turn.

### `RollEngine.Resolve` — New Optional Parameter

**File**: `src/Pinder.Core/Rolls/RollEngine.cs`

Add an optional parameter to the `Resolve` method:

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
    int dcModifier = 0)  // NEW: subtracted from DC (positive value = lower DC)
```

In the DC computation:

```csharp
int dc = defender.GetDefenceDC(stat) - dcModifier;
```

The `dcModifier` is a signed integer subtracted from DC. A `WeaknessWindow` with `DcReduction = 2` means `dcModifier = 2`, making the DC 2 lower.

The `RollResult.DC` property should reflect the **modified** DC (i.e., the DC the player actually had to beat), so it accurately represents what happened.

### `TurnResult` — New Property

Add to `TurnResult`:

```csharp
/// <summary>
/// Weakness window detected in the opponent's response this turn, if any.
/// The caller (UI) may use this to preview the next turn's opportunity.
/// </summary>
public WeaknessWindow? DetectedWindow { get; }
```

This lets the host/UI display "You noticed a crack!" after the opponent responds.

---

## Function Signatures (Complete Summary)

### New

| Type | Member | Signature |
|------|--------|-----------|
| `WeaknessWindow` | Constructor | `WeaknessWindow(StatType defendingStat, int dcReduction)` |
| `WeaknessWindow` | `DefendingStat` | `StatType` (read-only) |
| `WeaknessWindow` | `DcReduction` | `int` (read-only) |
| `OpponentResponse` | Constructor | `OpponentResponse(string message, WeaknessWindow? window = null)` |
| `OpponentResponse` | `Message` | `string` (read-only) |
| `OpponentResponse` | `Window` | `WeaknessWindow?` (read-only) |

### Modified

| Type | Member | Change |
|------|--------|--------|
| `ILlmAdapter` | `GetOpponentResponseAsync` | Return type: `Task<string>` → `Task<OpponentResponse>` |
| `NullLlmAdapter` | `GetOpponentResponseAsync` | Return type: `Task<string>` → `Task<OpponentResponse>` (window = null) |
| `DialogueOption` | Constructor | Add `bool hasWeaknessWindow = false` parameter |
| `DialogueOption` | `HasWeaknessWindow` | New `bool` property (read-only) |
| `RollEngine` | `Resolve` | Add `int dcModifier = 0` parameter |
| `GameSession` | `_activeWindow` | New `WeaknessWindow?` field |
| `TurnResult` | `DetectedWindow` | New `WeaknessWindow?` property |

---

## Input/Output Examples

### Example 1: Crack Detected → Window Applied Next Turn

**Turn N — Opponent response contains a crack:**

The LLM detects the opponent contradicted themselves. `GetOpponentResponseAsync` returns:

```
OpponentResponse(
    message: "Wait, I said I hated pineapple pizza but... okay fine I had some last week.",
    window: WeaknessWindow(StatType.Honesty, dcReduction: 2)
)
```

`GameSession` stores `_activeWindow = WeaknessWindow(Honesty, 2)`.

**Turn N+1 — StartTurnAsync:**

The session checks `_activeWindow`. For each dialogue option, it looks up `StatBlock.DefenceTable[option.Stat]`:
- `Charm` → defends with `SelfAwareness` → not Honesty → `HasWeaknessWindow = false`
- `Honesty` → defends with `Chaos` → not Honesty → `HasWeaknessWindow = false`
- `Chaos` → defends with `Charm` → not Honesty → `HasWeaknessWindow = false`
- `SelfAwareness` → defends with `Honesty` → **match!** → `HasWeaknessWindow = true`

So the `SelfAwareness` option gets the 🔓 icon.

**Turn N+1 — ResolveTurnAsync (player picks SelfAwareness):**

Normal DC would be `13 + opponent.GetEffective(Honesty)`. With the window: `DC = (13 + opponent.GetEffective(Honesty)) - 2`.

`RollEngine.Resolve(..., dcModifier: 2)` is called. The window is then cleared: `_activeWindow = null`.

### Example 2: Window Not Used (Player Picks a Different Stat)

Same setup as above, but the player picks `Charm` instead of `SelfAwareness`. The window still clears after this turn — it only lasts one turn regardless of whether the player exploits it.

### Example 3: No Crack Detected

`GetOpponentResponseAsync` returns `OpponentResponse("...", window: null)`. `_activeWindow` remains null (or is set to null). Next turn proceeds normally with no DC modifications and all `HasWeaknessWindow = false`.

### Example 4: SelfAwareness Overshare (DC −3)

Opponent shares something deeply personal unprompted. LLM returns:

```
OpponentResponse(
    message: "I haven't told anyone this but... I was actually born in a petri dish.",
    window: WeaknessWindow(StatType.SelfAwareness, dcReduction: 3)
)
```

On the next turn, the option attacking with the stat whose defence is `SelfAwareness` benefits. `DefenceTable[Charm] = SelfAwareness`, so the **Charm** option gets `HasWeaknessWindow = true` with a −3 DC reduction.

---

## Acceptance Criteria

### AC1: `WeaknessWindow` type defined

A `WeaknessWindow` class exists in `Pinder.Core.Conversation` with:
- `StatType DefendingStat` (read-only)
- `int DcReduction` (read-only, must be > 0)
- Constructor validates `dcReduction > 0`

### AC2: `OpponentResponse` carries optional `WeaknessWindow`

- `OpponentResponse` class exists with `string Message` and `WeaknessWindow? Window` properties.
- `ILlmAdapter.GetOpponentResponseAsync` returns `Task<OpponentResponse>` (not `Task<string>`).
- `NullLlmAdapter` returns `OpponentResponse("...", null)`.
- All other `ILlmAdapter` implementations compile with the new signature.

### AC3: `GameSession` stores active window, applies DC reduction for one turn, clears after turn

- `GameSession` has a `_activeWindow` field of type `WeaknessWindow?`.
- After `GetOpponentResponseAsync` returns, the session stores `response.Window` as `_activeWindow`.
- In the next `ResolveTurnAsync`, if `_activeWindow` is not null and the chosen option's defending stat matches `_activeWindow.DefendingStat`, `RollEngine.Resolve` is called with `dcModifier = _activeWindow.DcReduction`.
- If the chosen option's defending stat does NOT match, `dcModifier = 0` (no reduction applied).
- After the roll (regardless of which option was chosen), `_activeWindow` is set to null.
- The window lasts exactly one turn — the turn immediately after the crack message.

### AC4: `DialogueOption.HasWeaknessWindow` set correctly

- Each `DialogueOption` returned from `StartTurnAsync` has `HasWeaknessWindow = true` if `_activeWindow != null` and `StatBlock.DefenceTable[option.Stat] == _activeWindow.DefendingStat`.
- Options whose defending stat does not match have `HasWeaknessWindow = false`.
- When `_activeWindow` is null, all options have `HasWeaknessWindow = false`.

### AC5: DC displayed in option already reflects the reduction

- The DC shown to the player (in the option / UI) must already be the reduced value. The UI does not need to compute the reduction itself.
- This means `TurnStart` or the options themselves should carry the effective DC, or the host can compute it from the stat blocks and `HasWeaknessWindow`. (Implementation detail — but the spec requires the reduction to be "baked in" to whatever DC the host sees.)

### AC6: Tests

- **Window applied for one turn then cleared**: Create a `GameSession`, play one turn where the LLM returns a `WeaknessWindow`, verify the next turn applies the DC reduction, then verify the turn after that has no window active.
- **Correct stat DC reduced**: Given a `WeaknessWindow(Honesty, 2)`, verify that the option whose defending stat is Honesty (i.e., attacking stat `SelfAwareness`) has `HasWeaknessWindow = true` and the DC is reduced by 2.
- **No window → no reduction**: When `OpponentResponse.Window` is null, verify all options have `HasWeaknessWindow = false` and DC is unmodified.
- **Window clears even if not exploited**: Player picks an option that does NOT match the window's stat. Verify the window still clears on the following turn.
- **DcReduction validation**: Constructing `WeaknessWindow` with `dcReduction <= 0` throws `ArgumentOutOfRangeException`.

### AC7: Build clean

- `dotnet build` succeeds with zero errors and zero warnings.
- All existing tests pass.
- New tests pass.

---

## Edge Cases

1. **Multiple cracks in sequence**: If the opponent's response on turn N detects a crack, and the opponent's response on turn N+1 also detects a crack, the new window **replaces** the old one. There is no stacking. `_activeWindow` is simply overwritten.

2. **Same defending stat as previous window**: If a crack on turn N targets Honesty (−2) and the crack on turn N+1 also targets Honesty (−3), the turn N+2 uses the newer window (−3). The first window was already consumed/cleared on turn N+1.

3. **Window on first turn**: If `_activeWindow` is null at game start (which it always is), `StartTurnAsync` on turn 0 has no window active. This is the normal case.

4. **Game ends on the turn a window is set**: If the game ends during `ResolveTurnAsync` (interest hits 0 or 25), the window stored from the opponent's response is irrelevant — there is no next turn. No special handling needed; the session just ends.

5. **Window with dcReduction greater than DC**: If `dcReduction` is large enough that `dc - dcReduction < 1`, the DC still goes below the roll's minimum. This is valid — it makes the roll very easy. No clamping is needed. (The d20 minimum result is 1 + modifiers, which will likely beat a very low DC.)

6. **All six stats and the DefenceTable**: The defence table is:
   - `Charm` → `SelfAwareness`
   - `Rizz` → `Wit`
   - `Honesty` → `Chaos`
   - `Chaos` → `Charm`
   - `Wit` → `Rizz`
   - `SelfAwareness` → `Honesty`

   So a `WeaknessWindow(Honesty, 2)` benefits attacks with `SelfAwareness`. A `WeaknessWindow(Charm, 2)` benefits attacks with `Chaos`. Implementers must use the DefenceTable lookup, not assume stat-to-stat identity.

7. **LLM adapter returns a window with a stat not matching any known crack**: The engine does not validate that the window's stat matches the §15 table — the LLM is trusted to return valid crack detections. The engine only cares about `DefendingStat` and `DcReduction`.

8. **NullLlmAdapter and testing adapters**: Test adapters that want to simulate cracks must return `OpponentResponse` with a non-null `Window`. The `NullLlmAdapter` always returns `null` for the window (no crack detection in test mode).

9. **DialogueOption enrichment**: The LLM returns `DialogueOption[]` from `GetDialogueOptionsAsync`. These options do NOT have `HasWeaknessWindow` set by the LLM — the `GameSession` must enrich them by checking `_activeWindow` against each option's defending stat. If the LLM returns options with `HasWeaknessWindow = true`, the session should override based on its own state (session is authoritative).

---

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `WeaknessWindow` constructed with `dcReduction <= 0` | `ArgumentOutOfRangeException` | "dcReduction must be greater than zero" |
| `OpponentResponse` constructed with null/empty message | `ArgumentNullException` / `ArgumentException` | Standard null/empty message |
| `ILlmAdapter` implementation returns null `OpponentResponse` | `NullReferenceException` (or guard) | Session should guard against null response from LLM adapter |

No new exception types are introduced. Existing `GameEndedException`, `InvalidOperationException`, and `ArgumentOutOfRangeException` cover all cases.

---

## Dependencies

| Dependency | Status | Notes |
|-----------|--------|-------|
| `GameSession` (Issue #27) | **Must be merged** | Weakness windows modify `GameSession.StartTurnAsync` and `ResolveTurnAsync` |
| `ILlmAdapter` (Issue #26) | **Must be merged** | Return type of `GetOpponentResponseAsync` changes |
| Architecture review (Issue #63) | **Must be merged** | Sprint 3 architecture context |
| `StatBlock.DefenceTable` | Exists | Used to map attacking stat → defending stat for window matching |
| `RollEngine.Resolve` | Exists | Needs new `dcModifier` parameter |
| `DialogueOption` | Exists | Needs new `HasWeaknessWindow` property |
| `InterestMeter`, `TrapState` | Exist | No changes needed |

**No new external/NuGet dependencies.** All changes are pure C# within the existing project structure.
