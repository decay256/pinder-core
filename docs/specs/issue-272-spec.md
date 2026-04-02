# Spec: Denial +1 When Player Skips Available Honesty Option (§7)

**Issue:** #272
**Module**: docs/modules/conversation.md

---

## Overview

Rules §7 states: "Choosing a non-Honesty option when Honesty was available → Denial +1 (per turn)." This shadow growth trigger is currently missing from `GameSession.ResolveTurnAsync()`. When a player has at least one Honesty option in their dialogue lineup and selects a different stat, the player's Denial shadow stat must grow by 1.

---

## Function Signatures

No new public methods or types are introduced. The change is internal to an existing method.

### Modified Method

```csharp
// Pinder.Core.Conversation.GameSession
public async Task<TurnResult> ResolveTurnAsync(int optionIndex)
```

**Existing signature — unchanged.** The behavioral change is:
- After determining `chosenOption` from `_currentOptions[optionIndex]`, a new check evaluates whether Honesty was available but not chosen.
- If so, `_playerShadows.ApplyGrowth(ShadowStatType.Denial, 1, "Skipped Honesty option")` is called.

### Relevant Existing APIs Used

```csharp
// Pinder.Core.Stats.SessionShadowTracker
public string ApplyGrowth(ShadowStatType shadow, int amount, string reason)
// Precondition: amount > 0 (throws ArgumentOutOfRangeException otherwise)
// Returns: a human-readable growth event description string

// Pinder.Core.Stats.ShadowStatType (enum)
ShadowStatType.Denial

// Pinder.Core.Stats.StatType (enum)
StatType.Honesty

// Pinder.Core.Conversation.DialogueOption
public StatType Stat { get; }
```

### Internal Fields Referenced

```csharp
// Already exists in GameSession — set by StartTurnAsync(), consumed by ResolveTurnAsync()
private DialogueOption[]? _currentOptions;

// Already exists — injected via constructor or GameSessionConfig
private readonly SessionShadowTracker? _playerShadows;
```

---

## Input/Output Examples

### Example 1: Honesty available, player picks Charm

**Setup:**
- `StartTurnAsync()` returns 3 options: `[Charm, Honesty, Wit]`
- Player calls `ResolveTurnAsync(0)` (picks Charm)
- `_playerShadows` is non-null, Denial starts at delta 0

**Result:**
- `_playerShadows.ApplyGrowth(ShadowStatType.Denial, 1, "Skipped Honesty option")` is called
- Denial delta becomes 1
- The growth event string is included in `TurnResult.ShadowGrowthEvents`

### Example 2: Honesty available, player picks Honesty

**Setup:**
- `StartTurnAsync()` returns 3 options: `[Charm, Honesty, Wit]`
- Player calls `ResolveTurnAsync(1)` (picks Honesty)

**Result:**
- No Denial growth occurs
- Denial delta remains unchanged

### Example 3: No Honesty in lineup

**Setup:**
- `StartTurnAsync()` returns 3 options: `[Charm, Wit, Rizz]` (Honesty not present — e.g. because Denial T3 already removed it via existing Fixation/Denial threshold logic)
- Player calls `ResolveTurnAsync(0)` (picks Charm)

**Result:**
- No Denial growth occurs (Honesty was not available, so the player cannot be faulted for skipping it)

### Example 4: No shadow tracker (null guard)

**Setup:**
- `GameSession` constructed without a `SessionShadowTracker` (`_playerShadows == null`)
- Options include Honesty, player picks non-Honesty

**Result:**
- No Denial growth (null guard prevents `ApplyGrowth` call)
- No exception thrown

---

## Acceptance Criteria

### AC1: Denial +1 when Honesty option available and player chose different stat

When the dialogue options returned by `StartTurnAsync()` include at least one option with `Stat == StatType.Honesty`, and the player calls `ResolveTurnAsync()` with an index pointing to an option whose `Stat != StatType.Honesty`, then `SessionShadowTracker.ApplyGrowth(ShadowStatType.Denial, 1, ...)` must be called exactly once.

The growth event description must be included in the returned `TurnResult.ShadowGrowthEvents` list (via the existing `DrainGrowthEvents()` call that already occurs later in `ResolveTurnAsync`).

### AC2: No Denial growth when no Honesty option was in the lineup

When none of the options in `_currentOptions` have `Stat == StatType.Honesty`, no Denial growth occurs regardless of which option the player picks.

### AC3: No Denial growth when player chose Honesty

When the player selects the Honesty option (`chosenOption.Stat == StatType.Honesty`), no Denial growth occurs.

### AC4: Tests verify all 3 cases

Unit tests must cover:
1. Honesty available + non-Honesty chosen → Denial +1
2. No Honesty in options → no Denial change
3. Honesty chosen → no Denial change

### AC5: Build clean

All existing tests (1146+) continue to pass. The solution compiles with zero warnings/errors on .NET Standard 2.0.

---

## Edge Cases

### Denial T3 removes Honesty from lineup, then no Denial growth for skipping

When Denial ≥18 (T3), the existing threshold logic in `StartTurnAsync()` already filters out Honesty options. In that case, `_currentOptions` will not contain Honesty, so this trigger does not fire. This is correct — the player cannot skip what was not offered.

### Multiple Honesty options in lineup

If the LLM returns multiple options with `Stat == StatType.Honesty` and the player picks a non-Honesty option, Denial should still grow by exactly +1 (not +N). The check is "was Honesty available" (boolean), not "how many Honesty options existed."

### Shadow tracker is null

When `_playerShadows` is null (sessions constructed without shadow tracking), the check must be guarded. No exception should be thrown.

### Repeated turns

Each turn that includes Honesty in the lineup and the player skips it results in +1 Denial. Over multiple turns, Denial accumulates (e.g., 3 skipped turns → Denial +3 total). This is per the rules: "per turn."

### Interaction with Denial T2 disadvantage

If skipping Honesty pushes Denial to ≥12 (T2), the disadvantage effect takes hold on subsequent turns (computed in `StartTurnAsync()`). This is handled by existing shadow threshold evaluation — no special logic needed in this feature.

### Horniness-forced Rizz options

When Horniness ≥18 forces all options to Rizz, Honesty will not be in the lineup. No Denial growth occurs. This is correct — the player had no choice.

---

## Error Conditions

### InvalidOperationException — StartTurnAsync not called

If `ResolveTurnAsync()` is called without a prior `StartTurnAsync()`, the existing guard (`_currentOptions == null`) throws `InvalidOperationException`. This feature does not change that behavior.

### ArgumentOutOfRangeException — from ApplyGrowth

`ApplyGrowth` throws if `amount <= 0`. Since this feature always passes `amount: 1`, this exception will not occur under normal operation. If a future refactor passes 0 or negative, the existing `SessionShadowTracker` guard catches it.

---

## Dependencies

- **`SessionShadowTracker`** (`Pinder.Core.Stats`) — provides `ApplyGrowth()` method. Already exists and tested.
- **`ShadowStatType.Denial`** (`Pinder.Core.Stats`) — enum value. Already exists.
- **`StatType.Honesty`** (`Pinder.Core.Stats`) — enum value. Already exists.
- **`DialogueOption.Stat`** (`Pinder.Core.Conversation`) — property on option objects. Already exists.
- **`_currentOptions` field** (`GameSession`) — stores options from `StartTurnAsync()`. Already exists.
- **`TurnResult.ShadowGrowthEvents`** — already populated by `DrainGrowthEvents()` later in `ResolveTurnAsync`. No change needed to the drain mechanism.
- **No external dependencies.** Pure C# logic within `Pinder.Core`.

### Implementation Order Note

This is issue #272 in the Sprint 11 sequence. Per the sprint contract (`contracts/sprint-11-rules-compliance.md`), it is in Wave 3 and should be implemented after #268 (momentum), #260 (Read/Recover shadow disadvantage), and before #270 (shadow reductions) and #273 (Madness T3). All 7 GameSession issues must be implemented sequentially to avoid merge conflicts.
