# Spec: Madness T3 (≥18) Unhinged Option Replacement

**Issue**: #310  
**Module**: `docs/modules/conversation.md` (existing)

---

## Overview

When a player's Madness shadow stat reaches Tier 3 (raw value ≥ 18), exactly one of their dialogue options per turn should be replaced with an "unhinged" marker. This instructs the LLM adapter to generate bizarre, unhinged text for that option instead of the normal intended message. This implements the rules §7 shadow threshold: *"Madness at 18+: One option/turn replaced with unhinged text."*

Denial T3 (remove Honesty options) and Fixation T3 (force last stat) are already implemented in `GameSession.StartTurnAsync()`. This issue adds Madness T3 alongside them.

---

## Function Signatures

### DialogueOption (modified constructor)

**File**: `src/Pinder.Core/Conversation/DialogueOption.cs`

```csharp
public sealed class DialogueOption
{
    // NEW property
    public bool IsUnhingedReplacement { get; }

    // MODIFIED constructor — new optional parameter appended
    public DialogueOption(
        StatType stat,
        string intendedText,
        int? callbackTurnNumber = null,
        string? comboName = null,
        bool hasTellBonus = false,
        bool hasWeaknessWindow = false,
        bool isUnhingedReplacement = false)  // NEW — default false for backward compatibility
}
```

The new parameter `isUnhingedReplacement` defaults to `false`. All existing call sites remain unaffected. The property is read-only (set only in constructor).

### GameSession.StartTurnAsync() (modified method — no signature change)

**File**: `src/Pinder.Core/Conversation/GameSession.cs`

No public signature change. Internal logic is extended: after the existing Denial T3 block and before the Horniness T3 block, a new Madness T3 block is inserted that marks one random option as unhinged.

**Pseudocode location**: Inside `StartTurnAsync()`, after the Denial T3 block (~line 345), before the Horniness T3 block (~line 349).

---

## Input/Output Examples

### Example 1: Madness T3 active, 3 options returned by LLM

**Input state**:
- `_playerShadows.GetEffectiveShadow(ShadowStatType.Madness)` returns `20`
- `shadowThresholds[ShadowStatType.Madness]` = `20` (raw value, per #307)
- LLM returns 3 `DialogueOption` objects, all with `IsUnhingedReplacement = false`
- `_dice.Roll(3)` returns `2` (1-indexed)

**Output**: The option at index `1` (0-indexed: `Roll(3) - 1 = 1`) is replaced with a new `DialogueOption` that copies all properties from the original but sets `IsUnhingedReplacement = true`. The other two options remain unchanged.

```
Before: [Option(Charm, "Hey...", false), Option(Wit, "Nice bio", false), Option(Rizz, "You're cute", false)]
After:  [Option(Charm, "Hey...", false), Option(Wit, "Nice bio", true),  Option(Rizz, "You're cute", false)]
                                                                  ^^^^
                                                        IsUnhingedReplacement = true
```

### Example 2: Madness T2 (value = 14), no replacement

**Input state**:
- `shadowThresholds[ShadowStatType.Madness]` = `14`

**Output**: No options are marked. All `IsUnhingedReplacement` remain `false`.

### Example 3: Madness T3 active, only 1 option

**Input state**:
- `shadowThresholds[ShadowStatType.Madness]` = `18`
- Only 1 option in the array

**Output**: That single option is marked `IsUnhingedReplacement = true`. (`_dice.Roll(1)` returns `1`, index = `0`.)

### Example 4: Madness T3 active but options array is empty

**Input state**:
- `shadowThresholds[ShadowStatType.Madness]` = `22`
- `options.Length == 0` (e.g., all options were removed by Denial T3)

**Output**: No replacement occurs. The `options.Length > 0` guard prevents array index errors.

### Example 5: Interaction with other T3 effects

**Input state**:
- Fixation T3 active (all options forced to same stat)
- Madness T3 active (raw value = 19)
- 3 options, all forced to `Chaos` by Fixation T3

**Output**: After Fixation T3 forces all to `Chaos`, Madness T3 then marks one random option as unhinged. The stat remains `Chaos`. Both effects apply.

---

## Acceptance Criteria

### AC1: `DialogueOption` has `IsUnhingedReplacement` bool property

- `DialogueOption` gains a `public bool IsUnhingedReplacement { get; }` property.
- Constructor gains an optional parameter `bool isUnhingedReplacement = false`.
- Default value is `false` for backward compatibility.
- All existing call sites that construct `DialogueOption` without the new parameter continue to produce `IsUnhingedReplacement == false`.

### AC2: At Madness T3 (≥18), exactly one option is marked `IsUnhingedReplacement = true`

- In `GameSession.StartTurnAsync()`, after Denial T3 and before Horniness T3:
  - Check `shadowThresholds` dictionary for `ShadowStatType.Madness`.
  - If the raw value is `≥ 18` AND `options.Length > 0`:
    - Pick a random index: `int idx = _dice.Roll(options.Length) - 1;`
    - Replace the option at that index with a copy that has `IsUnhingedReplacement = true`.
  - Exactly one option is marked per turn (never zero, never more than one when Madness ≥ 18).
- The replacement preserves all other properties of the original option (`Stat`, `IntendedText`, `CallbackTurnNumber`, `ComboName`, `HasTellBonus`, `HasWeaknessWindow`).

**Important dependency**: This issue depends on #307 (shadow thresholds stored as raw values instead of tier integers). After #307, `shadowThresholds[ShadowStatType.Madness]` contains the raw shadow value (0–30+), so the check is `>= 18` (not `>= 3`). If #307 has NOT landed yet, the threshold check must still be `>= 18` against the raw value (the architect contract mandates raw values).

### AC3: At Madness T2 or lower, no options are marked unhinged

- When Madness raw value is `< 18`, no options have `IsUnhingedReplacement = true`.
- When `shadowThresholds` is `null` (no shadow tracking), no options are marked.
- When Madness is not present in the dictionary, no options are marked.

### AC4: The LLM prompt notes which option slot should be unhinged

- The `IsUnhingedReplacement` flag is carried on the `DialogueOption` object in the `TurnStart.Options` array returned to the host/caller.
- The LLM adapter (in `Pinder.LlmAdapters`) can read this flag when constructing prompts to instruct the LLM to generate unhinged text for that slot.
- **Scope for this issue**: The flag must be present and correctly set on the `DialogueOption`. The LLM adapter prompt integration (instructing the LLM to write unhinged text for the flagged slot) may be handled in this issue or a follow-up, but the flag itself is the minimum deliverable.

### AC5: Tests verify T3 marks one option, T2 marks none

- At least one test where Madness raw value ≥ 18: assert exactly one option has `IsUnhingedReplacement == true`.
- At least one test where Madness raw value < 18 (e.g., 12): assert no options have `IsUnhingedReplacement == true`.
- At least one test where Madness raw value = 0 or absent: assert no options are marked.
- Edge case test: single option with Madness ≥ 18 → that option is marked.

### AC6: Build clean

- Solution compiles with zero errors and zero warnings.
- All existing tests (1718+) continue to pass.

---

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| `shadowThresholds` is `null` | No options marked. The null check guard prevents NRE. |
| `ShadowStatType.Madness` not in dictionary | `TryGetValue` returns `false`, no replacement. |
| Madness raw value = 17 (just below T3) | No replacement. Threshold is strict `>= 18`. |
| Madness raw value = 18 (exactly T3) | One option marked. |
| Madness raw value = 30 (far above T3) | One option marked (same behavior as 18). |
| `options.Length == 0` | No replacement. The `options.Length > 0` guard skips the block. |
| `options.Length == 1` | That single option is marked. `_dice.Roll(1)` returns 1, index 0. |
| Fixation T3 + Madness T3 | Both apply. Fixation forces stat first, then Madness marks one option unhinged. Order matters: Madness runs after Fixation/Denial. |
| Denial T3 removes all options, fallback produces 1 | Madness T3 marks that 1 fallback option if Madness ≥ 18. |
| Horniness T3 runs after Madness T3 | Horniness T3 forces all options to Rizz. The `IsUnhingedReplacement` flag set by Madness T3 is preserved because Horniness T3 reconstructs options — **the implementer must ensure Horniness T3 copies `IsUnhingedReplacement` when reconstructing options**. Currently, the Horniness T3 block creates `new DialogueOption(StatType.Rizz, o.IntendedText, ...)` without passing `isUnhingedReplacement`. This is a latent bug to fix in this issue. |

---

## Error Conditions

| Error | Cause | Expected Behavior |
|-------|-------|-------------------|
| `ArgumentNullException` on `DialogueOption` constructor | `intendedText` is null | Existing behavior, unchanged. |
| Index out of range on `options[idx]` | `_dice.Roll()` returns value outside 1..Length | Cannot happen: `_dice.Roll(n)` is contractually 1..n inclusive. Guard `options.Length > 0` prevents Roll(0). |
| `shadowThresholds` stale from previous turn | `StartTurnAsync` recomputes `shadowThresholds` each call | Not an error — fresh computation each turn. |

No new exception types are introduced. No new error states are possible beyond what already exists.

---

## Dependencies

| Dependency | Type | Status | Impact |
|------------|------|--------|--------|
| #307 — Shadow raw values | Code (same sprint) | Wave 3 predecessor | `shadowThresholds` must contain raw values (not tier ints) for the `>= 18` check to work correctly. If #307 hasn't landed, the check `>= 18` would compare against tier values (0-3) and never trigger. **#307 must be implemented first.** |
| `IDiceRoller` (`_dice`) | Interface | Existing | Used for `_dice.Roll(options.Length)` to pick random index. Already injected into `GameSession`. |
| `SessionShadowTracker` (`_playerShadows`) | Component | Existing | Provides `GetEffectiveShadow()` for raw value. Already used in `StartTurnAsync`. |
| `ShadowThresholdEvaluator` | Component | Existing | T3 = raw value ≥ 18. Not directly called in this block (raw value comparison is sufficient). |
| Horniness T3 block (same method) | Code (existing) | Needs update | Must copy `IsUnhingedReplacement` flag when reconstructing options. See Edge Cases. |

### Ordering within StartTurnAsync

The T3 effects execute in this order:
1. **Fixation T3** — forces all options to last stat used
2. **Denial T3** — removes Honesty options
3. **Madness T3** — marks one random option as unhinged ← **NEW**
4. **Horniness T3** — forces all options to Rizz (must preserve `IsUnhingedReplacement`)

This ordering means Madness T3 operates on the already-filtered option set (after Denial may have removed some, after Fixation may have changed stats). This is correct: the unhinged marker applies to whatever options survived other T3 effects.
