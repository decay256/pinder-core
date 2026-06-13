# issue-901: RollEngine Unification (Phase 1 — Additive)

## Problem

Four dice-check flows in `pinder-core` each re-implemented the same primitive
(d20 + modifiers vs DC → tier from miss margin) independently:

| Flow | Location | Issues |
|---|---|---|
| Main option roll | `RollEngine.Resolve` / `ResolveFixedDC` | Inline `<=2/<=5/<=9` ladder |
| Horniness | `HorninessEngine.PeekAsync` + `DetermineHorninessTier` | Duplicate ladder |
| Shadow | Inline in `GameSession.cs:~1589` | Cross-called `HorninessEngine.DetermineHorninessTier` |
| Steering | `SteeringEngine.AttemptSteeringRollAsync` | Own d20 roll, no tier |

## New Canonical Abstractions

### `RollCheckKind` (enum)

```
OptionRoll  — main attacker roll
Steering    — datee steering
Horniness   — session-horniness overlay check
Shadow      — paired-shadow check
ShadowGrowth — reserved; ShadowGrowthEvaluator uses no standalone d20
```

### `NamedModifier` (readonly struct)

```csharp
public readonly struct NamedModifier(string Key, int Value);
```

Modifier `Key` is a stable machine-readable token (`"stat"`, `"level"`,
`"steering"`, `"tell"`, `"callback"`). Zero-value entries are kept.

### `RollCheckResult` (sealed class)

| Field | Type | Notes |
|---|---|---|
| `Kind` | `RollCheckKind` | |
| `DieRoll` | `int` | Always the first die rolled |
| `SecondDieRoll` | `int?` | Populated for advantage/disadvantage |
| `UsedDieRoll` | `int` | After advantage/disadvantage selection |
| `Modifiers` | `IReadOnlyList<NamedModifier>` | As-given bag |
| `ModifierSum` | `int` | Sum of all values in bag |
| `Total` | `int` | `UsedDieRoll + ModifierSum` |
| `Dc` | `int` | DC the roll faced |
| `IsSuccess` | `bool` | `Total >= Dc` |
| `IsNatOne` | `bool` | Informational — does NOT force Legendary here |
| `IsNatTwenty` | `bool` | Informational |
| `Tier` | `FailureTier` | From `FailureTierLadder.FromMissMargin` only |
| `MissMargin` | `int` | `0` on success |

**Important:** `RollCheckResult.Tier` never contains `FailureTier.Legendary`.
The `Legendary` tier is a game-rule override (nat-1 on the main option-roll)
handled by `RollEngine.ResolveFromComponents`, not by the check result.

### `FailureTierLadder.FromMissMargin`

```
missMargin <= 0  → None         (success)
missMargin <= 2  → Fumble
missMargin <= 5  → Misfire
missMargin <= 6  → TropeTrap
missMargin <= 9  → TropeTrap
else             → Catastrophe
```

**Single source of truth.** No other file in `Pinder.Core` may contain an
inline `missMargin <= N` comparison. Audit gate: `TierLadderAuditTest`.

### `RollEngine.ResolveCheck`

```csharp
public static RollCheckResult ResolveCheck(
    RollCheckKind kind,
    IDiceRoller dice,
    IReadOnlyList<NamedModifier> modifiers,
    int dc,
    bool hasAdvantage = false,
    bool hasDisadvantage = false);
```

Single entry point for all d20 checks. Handles advantage/disadvantage,
modifier-bag summation, and tier computation via `FailureTierLadder`.

### `ShadowCheckEngine`

Extracted from the inline logic at `GameSession.cs:~1589`. Uses the same
`Random` instance as `SteeringEngine` and `HorninessEngine` (shared RNG —
no dice consumption change).

## Per-Check Wrapper Changes

Each wrapper gains a `Check: RollCheckResult?` property (null for sentinel
values like `NotPerformed` / `NotAttempted`). Bespoke fields remain unchanged.

| Wrapper | New property | Bespoke fields retained |
|---|---|---|
| `RollResult` | `Check` | `DieRoll`, `StatModifier`, `LevelBonus`, `Total`, `DC`, `IsSuccess`, `Tier`, `IsNatOne`, `IsNatTwenty` |
| `HorninessCheckResult` | `Check` | `Roll`, `Modifier`, `Total`, `DC`, `IsMiss`, `Tier`, `OverlayApplied` |
| `SteeringRollResult` | `Check` | `SteeringRoll`, `SteeringMod`, `SteeringDC`, `SteeringSucceeded` |
| `ShadowCheckResult` | `Check` | `Roll`, `DC`, `IsMiss`, `Tier`, `OverlayApplied` |

## Migration Plan

**Phase 1 (this PR):** Additive. All wire DTOs unchanged. Snapshots byte-identical.

**Phase 2 (separate ticket):** GameApi serializes `Check` on each check DTO.
Wire-phase: existing bespoke duplicate fields begin pointing to `Check.*`.

**Phase 3 (separate ticket):** Delete bespoke duplicate fields on the wrappers.
Schema bump via `asset_kind` discriminator if needed.

## Invariant

> All dice checks go through `RollEngine.ResolveCheck`.
> The tier ladder lives in `FailureTierLadder.FromMissMargin`.
> No new check kind may roll its own d20 or re-implement the ladder inline.
