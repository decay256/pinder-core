# Spec: Horniness-Forced Rizz Option — Implement §15 🔥 Mechanic

**Issue:** #51
**Sprint:** 8 (RPG Rules Complete)
**Depends on:** #45 (Shadow thresholds), #54 (IGameClock / time-of-day modifier), #130 (Wave 0 — SessionShadowTracker, GameSessionConfig)
**Component:** `Pinder.Core.Conversation.GameSession`
**Contract:** `contracts/sprint-8-horniness-forced-rizz.md`
**Target:** .NET Standard 2.0, C# 8.0, zero NuGet dependencies

---

## 1. Overview

When a player character's Horniness level is high, the engine forces Rizz-stat dialogue options into the option list — whether the player wants them or not. At Horniness ≥ 6, at least one option becomes a forced Rizz option marked with 🔥. At Horniness ≥ 12, one option is *always* Rizz regardless of what the LLM returned. At Horniness ≥ 18, *all* options become Rizz. Horniness is computed **once per conversation** at session construction.

---

## 2. Horniness Calculation

### Formula (computed once at GameSession construction)

```
horninessBase   = dice.Roll(10)                                                  // 1d10 → 1–10
timeModifier    = gameClock?.GetHorninessModifier() ?? 0                         // -2 / 0 / +1 / +3 / +5
shadowHorniness = playerShadows?.GetEffectiveShadow(ShadowStatType.Horniness)
                  ?? player.Stats.GetShadow(ShadowStatType.Horniness)           // 0+
horniness       = Math.Max(0, horninessBase + timeModifier + shadowHorniness)
```

**Sources:**
- `dice` is the `IDiceRoller` injected into `GameSession`.
- `gameClock` comes from `GameSessionConfig.Clock` (may be null; defaults modifier to 0).
- `playerShadows` comes from `GameSessionConfig.PlayerShadows` (a `SessionShadowTracker`; may be null — falls back to `player.Stats.GetShadow()`).

The result is stored as `private readonly int _horniness` on `GameSession`. It does **not** change during the conversation.

**Why all three terms are needed:** The 1d10 roll adds per-session variance so even characters with low shadow Horniness may occasionally hit threshold 1 (≥ 6). The time-of-day modifier shifts the distribution based on when the conversation takes place. The shadow stat Horniness makes threshold 3 (≥ 18) reachable — without it the natural maximum would be 15 (roll 10 + AfterTwoAm modifier +5).

### Threshold Levels

| Horniness Value | Threshold | Label         | Effect on Options                                           |
|-----------------|-----------|---------------|-------------------------------------------------------------|
| 0–5             | 0         | None          | No forced options                                           |
| 6–11            | 1         | Low           | ≥ 1 Rizz option present; forced one marked `IsHorninessForced = true` |
| 12–17           | 2         | High          | ≥ 1 option always forced Rizz                               |
| ≥ 18            | 3         | Overwhelming  | ALL options become Rizz                                     |

**Threshold 1 vs 2 distinction:** Both guarantee at least one Rizz option. At threshold 1, the LLM is *asked* to include one (via `DialogueContext.RequiresRizzOption`), and the engine replaces only if the LLM failed to comply. At threshold 2, the engine *always* forces at least one regardless. The practical difference: `DialogueContext.HorninessLevel` conveys the urgency to the LLM, which should produce more Rizz-flavored content at higher levels.

---

## 3. Function Signatures

### 3.1 Modified: `GameSession` constructor (new overload)

Per ADR #162, optional configuration flows through `GameSessionConfig`:

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config = null)    // NEW optional parameter
```

Inside the constructor, Horniness is computed:

```csharp
var gameClock = config?.Clock;           // IGameClock? — may be null
var playerShadows = config?.PlayerShadows; // SessionShadowTracker? — may be null

int horninessBase = dice.Roll(10);
int timeModifier = gameClock?.GetHorninessModifier() ?? 0;
int shadowHorniness = playerShadows != null
    ? playerShadows.GetEffectiveShadow(ShadowStatType.Horniness)
    : player.Stats.GetShadow(ShadowStatType.Horniness);

_horniness = Math.Max(0, horninessBase + timeModifier + shadowHorniness);
```

**Backward compatibility:** The existing 5-parameter constructor remains unchanged and results in `_horniness = dice.Roll(10) + 0 + player.Stats.GetShadow(ShadowStatType.Horniness)` (no clock, no session shadow tracker).

### 3.2 Modified: `DialogueOption`

Add one new property:

```csharp
public bool IsHorninessForced { get; }
```

Constructor gains an optional parameter (backward-compatible):

```csharp
public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool isHorninessForced = false)   // NEW — default false
```

`IsHorninessForced` is `true` only for options injected or replaced by the Horniness engine. Organically generated Rizz options remain `false`.

### 3.3 `DialogueContext` (already has fields)

`DialogueContext` already has `HorninessLevel` (int, default 0) and `RequiresRizzOption` (bool, default false) from PR #114. No structural changes needed — `GameSession.StartTurnAsync` must populate them.

### 3.4 New private method: `ApplyHorninessOverrides`

```csharp
/// <summary>
/// Applies Horniness-forced Rizz option rules to the LLM-returned options.
/// Returns a new array with replacements applied.
/// </summary>
/// <param name="options">Non-null array of dialogue options from the LLM.</param>
/// <returns>Array with forced Rizz replacements applied per threshold rules.</returns>
private DialogueOption[] ApplyHorninessOverrides(DialogueOption[] options)
```

**Behavior by threshold:**

- **`_horniness < 6`** — return `options` unchanged.
- **`_horniness >= 6 && _horniness < 18`** (threshold 1 or 2) — check if any option has `Stat == StatType.Rizz`. If not, replace the **last** option (index `options.Length - 1`) with a forced Rizz copy. If a Rizz option already exists, return unchanged.
- **`_horniness >= 18`** (threshold 3) — replace **every** option with a forced Rizz copy.

**Replacement logic** — when replacing an option at index `i`:

```csharp
new DialogueOption(
    stat: StatType.Rizz,
    intendedText: options[i].IntendedText,  // preserve original text
    callbackTurnNumber: null,               // clear organic bonuses
    comboName: null,
    hasTellBonus: false,
    isHorninessForced: true)
```

**Why replace the last option:** The LLM is instructed to return options in descending priority. The last is the lowest-priority and most expendable.

### 3.5 Modified: `GameSession.StartTurnAsync`

After receiving options from the LLM, apply overrides before returning:

```csharp
// Inside StartTurnAsync, after getting options from LLM:
var context = new DialogueContext(
    // ... existing params ...,
    horninessLevel: _horniness,
    requiresRizzOption: _horniness >= 6);

var options = await _llm.GetDialogueOptionsAsync(context).ConfigureAwait(false);
options = ApplyHorninessOverrides(options);
_currentOptions = options;
```

---

## 4. Input/Output Examples

### Example 1: Horniness = 3 (No Effect)

**Setup:** `dice.Roll(10)` → 5, `GetHorninessModifier()` → −2, shadow Horniness = 0. Total = max(0, 5 − 2 + 0) = 3.

**LLM returns 4 options:**
| Index | Stat   | Text              |
|-------|--------|-------------------|
| 0     | Charm  | "Hey beautiful…"  |
| 1     | Wit    | "Did it hurt…"    |
| 2     | Honesty| "I just want…"   |
| 3     | Chaos  | "YEET into DMs"   |

**After `ApplyHorninessOverrides`:** Unchanged. No `IsHorninessForced` flags.

### Example 2: Horniness = 8, LLM already includes Rizz

**Setup:** Total Horniness = 8 (threshold 1).

**LLM returns:**
| Index | Stat   | Text                  |
|-------|--------|-----------------------|
| 0     | Charm  | "Hey…"                |
| 1     | Rizz   | "You look incredible" |
| 2     | Honesty| "Real talk…"          |
| 3     | Wit    | "Clever quip"         |

**After `ApplyHorninessOverrides`:** Unchanged. A Rizz option already exists at index 1. It is NOT marked `IsHorninessForced` because it was organically generated.

### Example 3: Horniness = 8, no Rizz in LLM response

**Setup:** Total Horniness = 8 (threshold 1).

**LLM returns:**
| Index | Stat   | Text         |
|-------|--------|--------------|
| 0     | Charm  | "Hey…"       |
| 1     | Wit    | "Funny line" |
| 2     | Honesty| "Truth bomb" |
| 3     | Chaos  | "Wild card"  |

**After `ApplyHorninessOverrides`:**
| Index | Stat | Text        | IsHorninessForced |
|-------|------|-------------|-------------------|
| 0     | Charm| "Hey…"      | false             |
| 1     | Wit  | "Funny line"| false             |
| 2     | Honesty | "Truth bomb" | false        |
| 3     | **Rizz** | "Wild card" | **true**     |

Last option replaced. Text preserved, stat changed to Rizz, bonuses cleared.

### Example 4: Horniness = 20 (All Rizz)

**Setup:** `dice.Roll(10)` → 8, `GetHorninessModifier()` → +5 (AfterTwoAm), shadow Horniness = 7. Total = max(0, 8 + 5 + 7) = 20 (threshold 3).

**LLM returns 4 options** (any stats).

**After `ApplyHorninessOverrides`:**
| Index | Stat | Text               | IsHorninessForced |
|-------|------|--------------------|-------------------|
| 0     | Rizz | (original text[0]) | true              |
| 1     | Rizz | (original text[1]) | true              |
| 2     | Rizz | (original text[2]) | true              |
| 3     | Rizz | (original text[3]) | true              |

All options replaced. All are Rizz. All marked forced.

### Example 5: DialogueContext population

**Setup:** Horniness = 12.

```csharp
var context = new DialogueContext(
    playerPrompt: "...",
    opponentPrompt: "...",
    conversationHistory: history,
    opponentLastMessage: "...",
    activeTraps: trapNames,
    currentInterest: 14,
    shadowThresholds: thresholds,
    callbackOpportunities: callbacks,
    horninessLevel: 12,           // from _horniness
    requiresRizzOption: true);    // true because 12 >= 6
```

---

## 5. Acceptance Criteria

### AC1: Horniness level computed as shadow stat + time-of-day modifier + dice roll each session

- On `GameSession` construction (when `GameSessionConfig` is provided), Horniness is computed: `Math.Max(0, dice.Roll(10) + (config?.Clock?.GetHorninessModifier() ?? 0) + shadowHorniness)`.
- `shadowHorniness` comes from `config.PlayerShadows.GetEffectiveShadow(ShadowStatType.Horniness)` if available, else `player.Stats.GetShadow(ShadowStatType.Horniness)`.
- The value is stored as `_horniness` (private, readonly) and does not change during the session.
- When no `GameSessionConfig` is provided (backward-compat constructor), Horniness still computed with `dice.Roll(10) + 0 + player.Stats.GetShadow(Horniness)`.

### AC2: `DialogueContext.HorninessLevel` and `RequiresRizzOption` set correctly each turn

- In `StartTurnAsync`, the `DialogueContext` is constructed with `horninessLevel: _horniness` and `requiresRizzOption: _horniness >= 6`.
- These values are the same every turn (Horniness is per-session).

### AC3: At Horniness ≥ 6, at least one Rizz option present, marked `IsHorninessForced = true`

- After `ApplyHorninessOverrides`, the options array contains at least one option with `Stat == StatType.Rizz`.
- If the LLM already returned a Rizz option, no replacement occurs (organic option kept, NOT flagged).
- If no Rizz option existed, the **last** option is replaced with a forced Rizz copy (`IsHorninessForced = true`, text preserved, bonuses cleared).

### AC4: At Horniness ≥ 12, one option ALWAYS unwanted Rizz

- Mechanically identical to AC3 for the engine-side replacement. The difference is that `DialogueContext.HorninessLevel = 12+` signals the LLM to lean harder into Rizz content.
- The engine guarantee is the same: if no Rizz exists after LLM response, force one.

### AC5: At Horniness ≥ 18, ALL options are Rizz

- Every option in the returned array has `Stat == StatType.Rizz` and `IsHorninessForced == true`.
- Original `IntendedText` values are preserved.
- `CallbackTurnNumber`, `ComboName`, and `HasTellBonus` are cleared on all replaced options.

### AC6: Tests — Horniness threshold effects on option composition

Tests must cover:
- Horniness < 6: options unchanged, no `IsHorninessForced` flags.
- Horniness = 6: one Rizz option forced when LLM provides none.
- Horniness = 6: no replacement when LLM already includes a Rizz option.
- Horniness = 12: forced Rizz present (same behavior as 6, stronger LLM hint).
- Horniness = 18: all options replaced with Rizz.
- Horniness = 0 (clamped from negative): no effect.
- `DialogueContext` carries correct `HorninessLevel` and `RequiresRizzOption`.
- Forced option preserves `IntendedText`, clears bonuses.
- Empty options array: no crash, returns empty.

### AC7: Build clean

- `dotnet build` succeeds with zero errors and zero warnings.
- All 254+ existing tests continue to pass. Backward-compatible defaults on new constructor parameters ensure this.

---

## 6. Edge Cases

| Scenario | Expected Behavior |
|----------|-------------------|
| `dice.Roll(10)` returns 1, modifier = −2, shadow = 0 | Horniness = max(0, 1 − 2 + 0) = 0. No Rizz forcing. |
| `dice.Roll(10)` returns 10, modifier = +5, shadow = 0 | Horniness = 15. Threshold 2 (≥ 12). At least one forced Rizz. |
| Horniness exactly 6 | Threshold 1 active. One Rizz option ensured. |
| Horniness exactly 12 | Threshold 2 active. Same engine-side replacement as threshold 1; `HorninessLevel = 12` passed to LLM. |
| Horniness exactly 18 | Threshold 3 active. All options become Rizz. |
| LLM returns fewer than 4 options | `ApplyHorninessOverrides` operates on whatever array length is returned. Threshold 3 replaces all N. Threshold 1–2 replaces last if needed. |
| LLM returns empty array | Returns empty array. No crash. |
| LLM returns multiple organic Rizz options | Threshold 1–2: no replacement (Rizz already present). None flagged `IsHorninessForced`. |
| Player selects a forced Rizz option | Rolls against `StatType.Rizz` as normal. `IsHorninessForced` is informational for the UI only — no mechanical roll difference. |
| `GameSessionConfig` is null | Falls back: `dice.Roll(10) + 0 + player.Stats.GetShadow(Horniness)`. |
| `config.Clock` is null | Time modifier defaults to 0. |
| `config.PlayerShadows` is null | Shadow Horniness read from `player.Stats.GetShadow()`. |
| Interaction with Denial ≥ 18 (no Honesty) | If both apply, Horniness ≥ 18 wins — all options Rizz. Denial restriction is moot. |
| Interaction with Fixation ≥ 18 (forced same stat) | Horniness ≥ 18 takes priority — all options become Rizz. Fixation's forced-stat is overridden. |
| Shadow Horniness grows mid-session | Does not affect `_horniness` — it was computed once at construction. Growth affects future sessions only (via `SessionShadowTracker` persistence). |

---

## 7. Error Conditions

| Condition | Error Type | Message / Behavior |
|-----------|-----------|-------------------|
| `ApplyHorninessOverrides` receives null array | `ArgumentNullException` | `"options"` — should never happen (LLM contract requires non-null). |
| `ApplyHorninessOverrides` receives empty array | No error | Returns empty array. |
| Negative Horniness (before clamp) | No error | `Math.Max(0, ...)` clamps to 0. |
| `DialogueContext.HorninessLevel` negative | No error | Allowed at context level; clamped at `GameSession` level before it reaches here. |
| `dice.Roll(10)` throws | Propagates | Caller-provided dice roller; `GameSession` does not catch. |

---

## 8. Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| #130 (Wave 0 — SessionShadowTracker, GameSessionConfig) | Hard | Required | Provides `SessionShadowTracker.GetEffectiveShadow()` and `GameSessionConfig` as the config carrier. |
| #45 (Shadow thresholds) | Hard | Required | Establishes Horniness as a shadow stat with thresholds at 6/12/18. Shadow Horniness value is an additive term in the formula. |
| #54 (IGameClock) | Hard | Required | Provides `IGameClock.GetHorninessModifier()` returning time-of-day modifier (−2 to +5). |
| `IDiceRoller` | Internal | Exists | `dice.Roll(10)` for the per-session 1d10 base roll. |
| `DialogueOption` | Internal | Exists | Modified: new `IsHorninessForced` property. |
| `DialogueContext` | Internal | Exists | Already has `HorninessLevel` and `RequiresRizzOption` (from #63 / PR #114). |
| `StatType.Rizz` | Internal | Exists | Used for forced option stat. |
| `ShadowStatType.Horniness` | Internal | Exists | Used to read shadow Horniness value. |

---

## 9. Files to Create or Modify

| File | Action | Description |
|------|--------|-------------|
| `src/Pinder.Core/Conversation/DialogueOption.cs` | **Modify** | Add `bool IsHorninessForced` property and optional constructor parameter (default `false`). |
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modify** | Add `_horniness` field computed at construction. Add `GameSessionConfig?` constructor overload. Add `ApplyHorninessOverrides()` private method. Call it in `StartTurnAsync` after LLM options. Populate `HorninessLevel` and `RequiresRizzOption` in `DialogueContext`. |
| `src/Pinder.Core/Conversation/GameStateSnapshot.cs` | **Modify** (optional) | Add `int Horniness` property for UI/test observability. |
