# Spec: Horniness-Forced Rizz Option — Implement §15 🔥 Mechanic

**Issue:** #51  
**Depends on:** #27 (GameSession), #45 (Shadow thresholds — Horniness at 12/18), #54 (GameClock — time-of-day Horniness modifier)  
**Component:** `Pinder.Core.Conversation`, `Pinder.Core.Interfaces`  
**Target:** .NET Standard 2.0, C# 8.0, zero NuGet dependencies

---

## 1. Overview

When a player character's Horniness shadow stat is high, it forces Rizz-stat dialogue options into the option list — whether the player wants them or not. At Horniness ≥ 6, at least one option becomes a forced Rizz option marked with 🔥. At Horniness ≥ 12, one option is *always* Rizz regardless of what the LLM returned. At Horniness ≥ 18, *all* options become Rizz. Horniness is rolled fresh each conversation (`dice.Roll(10)`) and modified by time-of-day from `GameClock`.

---

## 2. Horniness Calculation

### Roll at Session Start

At `GameSession` construction time, the player's Horniness level for this conversation is determined:

```
horninessBase = dice.Roll(10)          // 1d10 → range 1–10
timeModifier  = gameClock.GetHorninessModifier()  // -2 / 0 / +1 / +3 / +5
horniness     = horninessBase + timeModifier
```

The resulting `horniness` value is clamped to a minimum of 0 (no upper clamp — values above 10 are valid and expected with time-of-day modifiers). This value is stored as a `private readonly int _horniness` field on `GameSession` and does **not** change during the conversation.

**Note on shadow stat Horniness:** The `ShadowStatType.Horniness` exists in the stat system as the shadow pair of `StatType.Rizz`. However, per the architecture doc and issue #44 edge cases, Horniness is *rolled fresh each conversation* (1d10 + time modifier), not grown by events. The shadow stat value from `StatBlock` is **not** used for this mechanic. The per-session rolled value is the sole input.

### Threshold Levels

| Horniness Value | Threshold Level | Effect |
|----------------|----------------|--------|
| 0–5 | 0 (None) | No forced options |
| 6–11 | 1 (Low) | At least one Rizz option present; marked `IsHorninessForced = true` |
| 12–17 | 2 (High) | At least one option is always forced Rizz |
| 18+ | 3 (Overwhelming) | ALL options become Rizz |

The distinction between threshold 1 and 2 is subtle: at threshold 1, the LLM is *asked* to include a Rizz option (via `DialogueContext.RequiresRizzOption`), and if it fails to do so, the engine replaces the lowest-priority option. At threshold 2, the engine *guarantees* at least one option is Rizz by replacing if needed — same as threshold 1 mechanically, but the `DialogueContext.HorninessLevel` tells the LLM to lean harder into Rizz content. At threshold 3, all options are replaced.

---

## 3. Function Signatures

### Modified: `GameSession` Constructor

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    IGameClock gameClock)       // NEW — required for time-of-day modifier
```

- `gameClock`: Non-null. Used at construction to compute `GetHorninessModifier()`. Stored if needed for other time features, but Horniness is computed once at construction.
- Throws `ArgumentNullException` if `gameClock` is null.

**Alternative (if IGameClock is not yet available):** Accept `int horninessModifier` as a constructor parameter instead, with the host responsible for calling `gameClock.GetHorninessModifier()` before constructing the session.

### Modified: `DialogueOption`

Add one new property:

```csharp
public bool IsHorninessForced { get; }
```

- `true` if this option was injected or replaced by the Horniness mechanic.
- `false` for all organically generated options.
- Added as an optional constructor parameter (default `false`) to preserve backward compatibility:

```csharp
public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool isHorninessForced = false)  // NEW
```

### Modified: `DialogueContext`

Add two new properties:

```csharp
/// <summary>Current Horniness level for this session (0–15+).</summary>
public int HorninessLevel { get; }

/// <summary>
/// True if the LLM should generate at least one Rizz option.
/// Set when Horniness ≥ 6.
/// </summary>
public bool RequiresRizzOption { get; }
```

Constructor gains two new parameters:

```csharp
public DialogueContext(
    string playerPrompt,
    string opponentPrompt,
    IReadOnlyList<(string Sender, string Text)> conversationHistory,
    string opponentLastMessage,
    IReadOnlyList<string> activeTraps,
    int currentInterest,
    int horninessLevel,          // NEW
    bool requiresRizzOption)     // NEW
```

### New Private Method on `GameSession`

```csharp
/// <summary>
/// Applies Horniness-forced Rizz option rules to the LLM-returned options.
/// Returns a new array with replacements applied.
/// </summary>
private DialogueOption[] ApplyHorninessOverrides(DialogueOption[] options)
```

**Behavior:**
- If `_horniness < 6`: returns `options` unchanged.
- If `_horniness >= 6 && _horniness < 18`: ensures at least one option has `Stat == StatType.Rizz` and `IsHorninessForced == true`. If no Rizz option exists in the input, replaces the **last** option (index `options.Length - 1`, treated as lowest-priority) with a forced Rizz option.
- If `_horniness >= 18`: replaces **all** options with forced Rizz options (each with `IsHorninessForced = true`).

When replacing an option, the replacement copies the original's `IntendedText` but overrides `Stat` to `StatType.Rizz` and sets `IsHorninessForced = true`. The `CallbackTurnNumber`, `ComboName`, and `HasTellBonus` are cleared (`null`, `null`, `false`) because forced Rizz options don't carry organic bonuses.

**Why replace the last option:** The LLM is instructed (via the system prompt / dialogue context) to return options in descending priority order. The last option is the lowest priority and the most expendable. This is a convention, not enforced — but it matches the existing pattern.

### Modified: `GameSession.StartTurnAsync`

After receiving options from `_llm.GetDialogueOptionsAsync(context)`, apply overrides:

```csharp
var options = await _llm.GetDialogueOptionsAsync(context).ConfigureAwait(false);
options = ApplyHorninessOverrides(options);
_currentOptions = options;
```

The `DialogueContext` passed to the LLM includes `HorninessLevel` and `RequiresRizzOption` so the LLM can proactively generate Rizz-flavored options (reducing the need for engine-side replacement).

---

## 4. Input/Output Examples

### Example 1: Horniness = 3 (No Effect)

**Setup:** `dice.Roll(10)` returns 5, `gameClock.GetHorninessModifier()` returns −2. Horniness = 3.

**LLM returns:**
```
[Charm: "Hey beautiful...", Wit: "Did it hurt when...", Honesty: "I just want...", Chaos: "YEET into DMs"]
```

**After `ApplyHorninessOverrides`:**
```
[Charm: "Hey beautiful...", Wit: "Did it hurt when...", Honesty: "I just want...", Chaos: "YEET into DMs"]
```
No changes. No `IsHorninessForced` flags set.

### Example 2: Horniness = 8, LLM Already Includes Rizz

**Setup:** Horniness = 8 (threshold 1).

**LLM returns:**
```
[Charm: "Hey...", Rizz: "You look incredible", Honesty: "Real talk...", Wit: "Clever quip"]
```

**After `ApplyHorninessOverrides`:**
```
[Charm: "Hey...", Rizz: "You look incredible", Honesty: "Real talk...", Wit: "Clever quip"]
```
No replacement needed — a Rizz option already exists. However, the existing Rizz option does NOT get `IsHorninessForced = true` because it was organically generated. Only engine-injected replacements are flagged.

### Example 3: Horniness = 8, No Rizz in LLM Response

**Setup:** Horniness = 8 (threshold 1).

**LLM returns:**
```
[Charm: "Hey...", Wit: "Funny line", Honesty: "Truth bomb", Chaos: "Wild card"]
```

**After `ApplyHorninessOverrides`:**
```
[Charm: "Hey...", Wit: "Funny line", Honesty: "Truth bomb", Rizz(forced): "Wild card"]
```
Last option replaced. `options[3].Stat == StatType.Rizz`, `options[3].IsHorninessForced == true`, `options[3].IntendedText == "Wild card"` (text preserved from original).

### Example 4: Horniness = 20 (All Rizz)

**Setup:** `dice.Roll(10)` returns 10, `gameClock.GetHorninessModifier()` returns +5 (AfterTwoAm). Horniness = 15. Wait — that's only 15. Let's say `Roll(10)` returns 10 + modifier +5 = 15? No, needs ≥18. Let's say `Roll(10)` returns 8, modifier +5 = 13. Still not 18. Use: `Roll(10)` returns 10, modifier +5 = 15. Hmm. To hit 18 with 1d10+modifier: 10+5=15 max with AfterTwoAm. The maximum natural Horniness is 15. Threshold 3 (≥18) would require additional shadow stat contribution.

**Correction:** The issue says "Horniness rolled at session start: `dice.Roll(10)` + time-of-day modifier." With max roll 10 and max modifier +5, the natural max is 15. To reach ≥18, the shadow stat Horniness from `CharacterState`/`StatBlock` must contribute. See section 6 (Edge Cases) for how to handle this. For this example, assume shadow Horniness contribution brings the total to 20.

**LLM returns:**
```
[Charm: "Hey...", Wit: "Funny line", Honesty: "Truth bomb", Chaos: "Wild card"]
```

**After `ApplyHorninessOverrides`:**
```
[Rizz(forced): "Hey...", Rizz(forced): "Funny line", Rizz(forced): "Truth bomb", Rizz(forced): "Wild card"]
```
All options replaced with `Stat = StatType.Rizz`, `IsHorninessForced = true`. Text preserved from originals.

### Example 5: DialogueContext Construction

**Setup:** Horniness = 8.

```csharp
var context = new DialogueContext(
    playerPrompt: "...",
    opponentPrompt: "...",
    conversationHistory: history,
    opponentLastMessage: "...",
    activeTraps: trapNames,
    currentInterest: 10,
    horninessLevel: 8,         // NEW
    requiresRizzOption: true); // NEW — because 8 >= 6
```

---

## 5. Acceptance Criteria

### AC1: Horniness Rolled at Session Start and Stored in `GameSession`

- On `GameSession` construction, Horniness is computed: `dice.Roll(10) + gameClock.GetHorninessModifier()`.
- The value is clamped to a minimum of 0.
- The value is stored as `_horniness` (private, readonly) and does not change during the session.
- Accessible for testing via `GameStateSnapshot` or a read-only property if needed.

### AC2: `DialogueContext.HorninessLevel` and `RequiresRizzOption` Set Correctly Each Turn

- `HorninessLevel` is set to the session's Horniness value every time `DialogueContext` is constructed in `StartTurnAsync`.
- `RequiresRizzOption` is `true` when `_horniness >= 6`, `false` otherwise.
- These values do not change between turns (Horniness is per-session, not per-turn).

### AC3: At Horniness ≥ 6, At Least One Rizz Option Present, Marked `IsHorninessForced = true`

- After `ApplyHorninessOverrides`, the returned options array contains at least one option with `Stat == StatType.Rizz`.
- If the LLM already returned a Rizz option, no replacement occurs (the organic option is kept without the forced flag).
- If no Rizz option was in the LLM response, the last option is replaced with a forced Rizz option (`IsHorninessForced = true`).
- The replacement option preserves the original's `IntendedText`.

### AC4: At Horniness ≥ 18, All Options Are Rizz

- When Horniness is 18 or higher, every option in the returned array has `Stat == StatType.Rizz` and `IsHorninessForced == true`.
- Original `IntendedText` values are preserved on each replaced option.
- No option retains its original stat.

### AC5: Tests — Horniness Threshold Effects on Option Composition

Tests must verify:
- Horniness < 6: options unchanged, no `IsHorninessForced` flags.
- Horniness = 6: one Rizz option present when LLM didn't provide one.
- Horniness = 6: no replacement when LLM already provided Rizz.
- Horniness = 12: same mechanical behavior as 6 (at least one Rizz).
- Horniness = 18: all options replaced with Rizz.
- Horniness = 0 (clamped): no effect.
- `DialogueContext` carries correct `HorninessLevel` and `RequiresRizzOption`.

### AC6: Build Clean

- `dotnet build` succeeds with zero errors.
- All existing tests continue to pass (may require updating call sites that construct `DialogueContext` or `DialogueOption` due to new parameters).

---

## 6. Edge Cases

| Scenario | Expected Behaviour |
|----------|-------------------|
| `dice.Roll(10)` returns 1, modifier is −2 | Horniness = max(0, 1 + (−2)) = 0. No Rizz forcing. |
| `dice.Roll(10)` returns 10, modifier is +5 | Horniness = 15. Threshold 2 (≥12). At least one forced Rizz. |
| Horniness exactly 6 | Threshold 1 active. One Rizz option ensured. |
| Horniness exactly 12 | Threshold 2 active. Same mechanical effect as threshold 1 for engine-side replacement; `HorninessLevel = 12` passed to LLM for stronger Rizz flavor. |
| Horniness exactly 18 | Threshold 3 active. All options become Rizz. |
| LLM returns fewer than 4 options | `ApplyHorninessOverrides` operates on whatever array length is returned. At threshold 3, replaces all N options. At threshold 1–2, replaces last option if no Rizz exists. If array is empty, returns empty (no crash). |
| LLM returns multiple Rizz options organically | At threshold 1–2, no replacement needed (Rizz already present). None are marked `IsHorninessForced` since they were organic. |
| Forced Rizz option is selected by player | Rolls against `StatType.Rizz` as normal. The `IsHorninessForced` flag is informational for the UI only — no mechanical difference in the roll. |
| Reaching ≥18 threshold | With 1d10 (max 10) + max modifier (+5 AfterTwoAm) = 15 max natural. To reach 18, either: (a) the shadow stat Horniness from `CharacterState` adds to the roll, or (b) other game effects increase it. **Recommended:** Include shadow Horniness in the formula: `horniness = dice.Roll(10) + timeModifier + (characterState.GetShadowDelta(ShadowStatType.Horniness) / 3)` or use the raw shadow value. The issue text says "Horniness ≥ 12 and ≥ 18 is a shadow threshold effect" (comment from PO referencing #45). **Resolution:** Defer to #45's `ShadowThresholdEvaluator.GetThresholdLevel(ShadowStatType.Horniness)` which returns 0/1/2/3 based on the shadow stat value. Use the **higher** of the two threshold levels (rolled vs. shadow-based). |
| Interaction with shadow threshold #45 | Issue #45 defines shadow thresholds at 6/12/18 for all shadow stats including Horniness. The Horniness shadow threshold effects from #45's table match exactly what #51 implements. **Integration:** `GameSession` should take the max of rolled Horniness threshold level and shadow-stat Horniness threshold level to determine the effective threshold. This ensures both freshly-rolled Horniness and accumulated shadow Horniness contribute. |
| Interaction with Denial ≥ 18 (no Honesty options) | If both Horniness ≥ 18 and Denial ≥ 18 apply, Horniness wins — all options become Rizz. Denial's "no Honesty" restriction is moot since all options are already overridden. |
| Interaction with Fixation ≥ 18 (forced same stat) | If Fixation ≥ 18 forces the same stat as last turn, and Horniness ≥ 18 forces all Rizz: if last turn was Rizz, both are satisfied. If last turn was Charm, conflict arises. **Recommended:** Horniness ≥ 18 takes priority (all Rizz). Fixation's forced-stat is overridden. |
| `GameSession` constructor backward compatibility | Adding `IGameClock` (or `int horninessModifier`) to the constructor is a **breaking change**. All existing tests and call sites must be updated. Consider an overload that defaults Horniness to 0 for backward compat during prototype phase. |

---

## 7. Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `GameSession` constructed with null `gameClock` | `ArgumentNullException` | `"gameClock"` |
| `DialogueContext` constructed with negative `horninessLevel` | No error — negative values are valid (treated as 0 threshold; clamped at `GameSession` level, not at `DialogueContext`). |
| `ApplyHorninessOverrides` receives null array | `ArgumentNullException` | `"options"` (should not happen in practice since `ILlmAdapter` contract requires non-null return). |
| `ApplyHorninessOverrides` receives empty array | Returns empty array. No crash. |

---

## 8. Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| #27 (GameSession) | Hard | Merged | `GameSession` class exists and is the modification target. |
| #45 (Shadow thresholds) | Hard | In progress | Provides `ShadowThresholdEvaluator.GetThresholdLevel()` for shadow-based Horniness thresholds. Horniness ≥ 12/18 as shadow threshold effects come from here. |
| #54 (GameClock) | Hard | In progress | Provides `IGameClock.GetHorninessModifier()` for time-of-day modifier to the Horniness roll. |
| `Pinder.Core.Conversation.DialogueOption` | Internal | Exists | Modified: new `IsHorninessForced` property. |
| `Pinder.Core.Conversation.DialogueContext` | Internal | Exists | Modified: new `HorninessLevel` and `RequiresRizzOption` properties. |
| `Pinder.Core.Conversation.GameSession` | Internal | Exists | Modified: Horniness roll at construction, `ApplyHorninessOverrides` in `StartTurnAsync`. |
| `Pinder.Core.Interfaces.IDiceRoller` | Internal | Exists | Used for `dice.Roll(10)` at session start. |
| `Pinder.Core.Interfaces.IGameClock` | Internal | From #54 | Used for `GetHorninessModifier()`. |
| `Pinder.Core.Stats.StatType` | Internal | Exists | `StatType.Rizz` used for forced options. |
| `Pinder.Core.Stats.ShadowStatType` | Internal | Exists | `ShadowStatType.Horniness` — referenced but not directly used (shadow threshold evaluation is delegated to #45). |

---

## 9. Files to Create or Modify

| File | Action | Description |
|------|--------|-------------|
| `src/Pinder.Core/Conversation/DialogueOption.cs` | **Modify** | Add `bool IsHorninessForced` property and optional constructor parameter. |
| `src/Pinder.Core/Conversation/DialogueContext.cs` | **Modify** | Add `int HorninessLevel` and `bool RequiresRizzOption` properties and constructor parameters. |
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modify** | Add `_horniness` field computed at construction. Add `IGameClock` constructor parameter. Add `ApplyHorninessOverrides` private method. Call it in `StartTurnAsync` after LLM returns options. Pass Horniness info in `DialogueContext`. |
| `src/Pinder.Core/Conversation/GameStateSnapshot.cs` | **Modify** (optional) | Add `int Horniness` property for UI/test observability. |
