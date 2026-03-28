# Spec: Issue #78 — TurnResult Expansion

## Overview

TurnResult is the sealed class returned by `GameSession.ResolveTurnAsync()` that captures everything that happened in a single turn. Sprint 3 introduces several new features (shadow growth, combo tracking, callback bonuses, tell-reads, risk tiers, XP earning) that each need observable output fields on TurnResult. This issue adds all seven new fields to TurnResult in a single coordinated change, plus a new `RiskTier` enum, so that downstream feature issues (#42, #44, #46, #47, #48, #50) can populate them without conflicting structural changes.

## Dependencies

- **Issue #63** (Architecture Review / ILlmAdapter expansion) — must be merged before implementation begins, as it may affect the contract surface that TurnResult participates in.
- **No external libraries** — the project is netstandard2.0 with zero NuGet dependencies.
- **Existing types used**: `RollResult` (Pinder.Core.Rolls), `GameStateSnapshot`, `GameOutcome` (Pinder.Core.Conversation).

## 1. New Enum: `RiskTier`

### Location
`src/Pinder.Core/Rolls/RiskTier.cs`

### Definition

```
namespace Pinder.Core.Rolls

public enum RiskTier
{
    Safe,
    Medium,
    Hard,
    Bold
}
```

Four members, in ascending risk order. This enum is referenced by issue #42 (risk tier system) and exposed on TurnResult so the host/UI can display the risk level of the player's chosen action.

## 2. Expanded `TurnResult` Class

### Location
`src/Pinder.Core/Conversation/TurnResult.cs`

### Existing Fields (unchanged)

| Property | Type | Description |
|----------|------|-------------|
| `Roll` | `RollResult` | Full roll result (required, non-null) |
| `DeliveredMessage` | `string` | Player's message text after degradation (required, non-null) |
| `OpponentMessage` | `string` | Opponent's response message (required, non-null) |
| `NarrativeBeat` | `string?` | Narrative beat text if interest threshold crossed, null otherwise |
| `InterestDelta` | `int` | Net interest delta applied this turn (includes momentum) |
| `StateAfter` | `GameStateSnapshot` | Snapshot of game state after this turn (required, non-null) |
| `IsGameOver` | `bool` | True if the game ended this turn |
| `Outcome` | `GameOutcome?` | The outcome if game ended, null otherwise |

### New Fields

| Property | Type | Default | Source Issue | Description |
|----------|------|---------|-------------|-------------|
| `ShadowGrowthEvents` | `IReadOnlyList<string>` | Empty list (`Array.Empty<string>()`) | #44 | Human-readable descriptions of shadow stat growth that occurred this turn. Empty if no shadow growth happened. Each string describes one shadow growth event (e.g., `"Horniness +1 (Rizz overuse)"`). Individual elements must not be null — callers are responsible for ensuring all list entries are non-null strings. |
| `ComboTriggered` | `string?` | `null` | #46 | Name/identifier of the combo that was triggered this turn, or null if no combo fired. |
| `CallbackBonusApplied` | `int` | `0` | #47 | The callback bonus (integer modifier) that was applied to the roll or interest delta this turn. 0 means no callback bonus was applied. |
| `TellReadBonus` | `int` | `0` | #50 | The tell-read bonus (integer modifier) applied this turn. 0 means no tell-read occurred. |
| `TellReadMessage` | `string?` | `null` | #50 | Descriptive message about the tell that was read, or null if no tell-read occurred. |
| `RiskTier` | `RiskTier` | `RiskTier.Safe` | #42 | The risk tier of the action chosen by the player this turn. |
| `XpEarned` | `int` | `0` | #48 | Amount of XP earned from this turn's outcome. 0 if no XP was earned. |

### Constructor Signature

The constructor must accept all existing parameters as required positional parameters (preserving backward compatibility of call sites), plus the seven new parameters as **optional named parameters** with default values:

```csharp
public TurnResult(
    RollResult roll,
    string deliveredMessage,
    string opponentMessage,
    string? narrativeBeat,
    int interestDelta,
    GameStateSnapshot stateAfter,
    bool isGameOver,
    GameOutcome? outcome,
    IReadOnlyList<string>? shadowGrowthEvents = null,
    string? comboTriggered = null,
    int callbackBonusApplied = 0,
    int tellReadBonus = 0,
    string? tellReadMessage = null,
    RiskTier riskTier = RiskTier.Safe,
    int xpEarned = 0)
```

**Constructor behavior**:
- `roll`, `deliveredMessage`, `opponentMessage`, `stateAfter` — throw `ArgumentNullException` if null (same as current behavior).
- `shadowGrowthEvents` — if null is passed (or default), store as `Array.Empty<string>()` (cast to `IReadOnlyList<string>`). Never expose null from the property getter. The constructor does **not** validate individual list elements for null — that responsibility lies with the caller.
- All other nullable fields (`comboTriggered`, `tellReadMessage`, `narrativeBeat`, `outcome`) — store as-is (null is a valid value).
- Numeric fields (`callbackBonusApplied`, `tellReadBonus`, `xpEarned`) — store as-is (no validation; 0 is the default).
- `riskTier` — store as-is; defaults to `RiskTier.Safe`.

### Using Directive

Add `using System.Collections.Generic;` to TurnResult.cs (for `IReadOnlyList<T>`). The `RiskTier` enum is in `Pinder.Core.Rolls`, which is already imported.

## 3. Input/Output Examples

### Example 1: Existing call site (no new fields)

Current code in `GameSession.ResolveTurnAsync()` constructs TurnResult with only the original 8 parameters. After this change, that code continues to work unmodified because all new parameters are optional:

```
new TurnResult(
    roll: rollResult,
    deliveredMessage: "Hey there cutie",
    opponentMessage: "Ew, blocked.",
    narrativeBeat: null,
    interestDelta: -2,
    stateAfter: snapshot,
    isGameOver: false,
    outcome: null)
```

Result:
- `ShadowGrowthEvents` → empty list (count 0)
- `ComboTriggered` → null
- `CallbackBonusApplied` → 0
- `TellReadBonus` → 0
- `TellReadMessage` → null
- `RiskTier` → `RiskTier.Safe`
- `XpEarned` → 0

### Example 2: Future call site with all new fields populated

```
new TurnResult(
    roll: rollResult,
    deliveredMessage: "I noticed you like long walks...",
    opponentMessage: "Omg yes! Tell me more!",
    narrativeBeat: "Things are heating up!",
    interestDelta: 3,
    stateAfter: snapshot,
    isGameOver: false,
    outcome: null,
    shadowGrowthEvents: new[] { "Horniness +1 (Rizz overuse)" },
    comboTriggered: "SmoothOperator",
    callbackBonusApplied: 1,
    tellReadBonus: 2,
    tellReadMessage: "You noticed they always mention cats — +2 bonus!",
    riskTier: RiskTier.Hard,
    xpEarned: 15)
```

Result: all properties reflect the passed values. `ShadowGrowthEvents.Count` → 1.

### Example 3: RiskTier enum usage

```
RiskTier tier = RiskTier.Bold;  // value: 3
```

Enum members are ordered: `Safe` (0), `Medium` (1), `Hard` (2), `Bold` (3).

## 4. Acceptance Criteria

### AC1: RiskTier enum defined

- File `src/Pinder.Core/Rolls/RiskTier.cs` exists.
- Namespace: `Pinder.Core.Rolls`.
- Enum name: `RiskTier`.
- Members in order: `Safe`, `Medium`, `Hard`, `Bold`.
- No explicit integer assignments needed (implicit 0, 1, 2, 3).

### AC2: All seven fields added to TurnResult with sensible defaults

- Each of the seven properties listed in §2 "New Fields" exists as a public read-only property on `TurnResult`.
- Types match exactly as specified.
- When constructed without specifying the new parameters, defaults are: empty list, null, 0, 0, null, `RiskTier.Safe`, 0.

### AC3: TurnResult constructor updated with optional parameters

- The constructor signature matches §2 "Constructor Signature".
- All new parameters are optional (have default values).
- Existing call sites that pass only the original 8 arguments compile without changes.
- The `shadowGrowthEvents` property never returns null — it returns an empty `IReadOnlyList<string>` when no events are provided.

### AC4: All 98 existing tests pass

- `dotnet test` passes with zero failures.
- No existing test file is modified.
- This is a purely additive, backward-compatible change.

### AC5: Build clean

- `dotnet build` produces zero errors and zero warnings.
- No new NuGet dependencies added.
- Target framework remains `netstandard2.0`.

## 5. Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| `shadowGrowthEvents` passed as `null` | Property returns `Array.Empty<string>()` (empty `IReadOnlyList<string>`), never null |
| `shadowGrowthEvents` passed as empty list | Property returns that empty list (count 0) |
| `shadowGrowthEvents` contains null elements | No validation — stored as-is. Callers must ensure non-null entries. |
| `xpEarned` passed as negative value | Stored as-is — no validation (downstream logic owns correctness) |
| `callbackBonusApplied` passed as negative value | Stored as-is — no validation |
| `tellReadBonus` passed as negative value | Stored as-is — no validation |
| `riskTier` passed as invalid enum value (e.g., `(RiskTier)99`) | Stored as-is — no validation (C# enums don't prevent this) |
| All new fields at defaults | Object behaves identically to pre-expansion TurnResult from the consumer's perspective |
| Multiple shadow growth events | `ShadowGrowthEvents` contains all items in order; no deduplication |

## 6. Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `roll` is null | `ArgumentNullException` | `"roll"` |
| `deliveredMessage` is null | `ArgumentNullException` | `"deliveredMessage"` |
| `opponentMessage` is null | `ArgumentNullException` | `"opponentMessage"` |
| `stateAfter` is null | `ArgumentNullException` | `"stateAfter"` |

No new error conditions are introduced. The new fields are all optional with safe defaults. No null-argument validation is added for the new fields (nullable types accept null; value types have defaults).

## 7. Files Changed

| File | Change |
|------|--------|
| `src/Pinder.Core/Rolls/RiskTier.cs` | **New file** — `RiskTier` enum |
| `src/Pinder.Core/Conversation/TurnResult.cs` | Add 7 new properties, expand constructor with optional parameters, add `using System.Collections.Generic;` |

No other files are modified. The existing `GameSession.ResolveTurnAsync()` call site continues to compile because all new parameters are optional.
