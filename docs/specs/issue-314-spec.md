# Spec: XP Risk-Tier Multiplier

**Issue**: #314 — Missing: XP risk-tier multiplier (1x/1.5x/2x/3x) not applied  
**Module**: docs/modules/conversation-game-session.md

---

## Overview

The rules §10 defines base XP for successful checks (5/10/15 by DC tier), and the risk-reward design document defines a multiplier based on the option's risk tier (Safe=1x, Medium=1.5x, Hard=2x, Bold=3x). Currently, `GameSession.RecordRollXp` records only the flat base XP without applying any risk-tier multiplier. This means a Bold success yields the same XP as a Safe success at the same DC, removing a key incentive for risky play.

This fix applies the risk-tier multiplier to the base XP amount on successful checks only, rounding to the nearest integer via `Math.Round` (midpoint rounds to even, the .NET default for `Math.Round(double)`).

---

## Function Signatures

### Modified: `GameSession.RecordRollXp`

```csharp
// Location: src/Pinder.Core/Conversation/GameSession.cs
// Visibility: private
// Existing signature — no signature change, only body change
private void RecordRollXp(RollResult rollResult)
```

This method is private. No public API changes are required.

### No new public types or methods

The `RiskTier` enum, `RollResult.RiskTier` property, and `XpLedger.Record(string, int)` all exist and are unchanged.

---

## Input/Output Examples

### Base XP tiers (from §10, unchanged)

| DC Range | Base XP |
|----------|---------|
| DC ≤ 13  | 5       |
| DC 14–17 | 10      |
| DC ≥ 18  | 15      |

### Risk-tier multipliers

| RiskTier | Multiplier | Need range    |
|----------|------------|---------------|
| Safe     | 1.0x       | need ≤ 5      |
| Medium   | 1.5x       | need 6–10     |
| Hard     | 2.0x       | need 11–15    |
| Bold     | 3.0x       | need ≥ 16     |

("Need" = DC − statModifier − levelBonus, computed inside `RollResult.ComputeRiskTier`.)

### Concrete examples

| Scenario | DC | Base XP | RiskTier | Multiplier | Final XP | XpLedger source label |
|---|---|---|---|---|---|---|
| Safe success, low DC | 13 | 5 | Safe | 1.0 | 5 | "Success_DC_Low" |
| Medium success, low DC | 13 | 5 | Medium | 1.5 | 8 | "Success_DC_Low" |
| Hard success, mid DC | 15 | 10 | Hard | 2.0 | 20 | "Success_DC_Mid" |
| Bold success, high DC | 18 | 15 | Bold | 3.0 | 45 | "Success_DC_High" |
| Medium success, mid DC | 16 | 10 | Medium | 1.5 | 15 | "Success_DC_Mid" |
| Nat 20 (any tier) | any | N/A | any | N/A | 25 | "Nat20" |
| Nat 1 (any tier) | any | N/A | any | N/A | 10 | "Nat1" |
| Failure (any tier) | any | N/A | any | N/A | 2 | "Failure" |

### Rounding example

- Base 5 × 1.5 = 7.5 → `(int)Math.Round(7.5)` = 8 (banker's rounding; .NET default rounds 7.5 to 8)
- Base 15 × 1.5 = 22.5 → `(int)Math.Round(22.5)` = 22 (banker's rounding; .NET rounds 22.5 to 22)

Note: If the implementer prefers `MidpointRounding.AwayFromZero` for more intuitive results (7.5→8, 22.5→23), that is acceptable — the issue says "rounded to nearest int" without specifying tie-breaking. Either rounding mode is acceptable. The tests should match whichever mode is implemented.

---

## Acceptance Criteria

### AC1: Safe success → 1x base XP

When a successful roll has `RiskTier.Safe`, the XP recorded equals exactly the base XP (5, 10, or 15 depending on DC tier). No multiplication occurs.

### AC2: Medium success → 1.5x base XP (rounded)

When a successful roll has `RiskTier.Medium`, the XP recorded equals `(int)Math.Round(baseXp * 1.5)`.

- DC ≤ 13: `Round(5 * 1.5)` = 8
- DC 14–17: `Round(10 * 1.5)` = 15
- DC ≥ 18: `Round(15 * 1.5)` = 23 (or 22 with banker's rounding)

### AC3: Hard success → 2x base XP

When a successful roll has `RiskTier.Hard`, the XP recorded equals `baseXp * 2`.

- DC ≤ 13: 10
- DC 14–17: 20
- DC ≥ 18: 30

### AC4: Bold success → 3x base XP

When a successful roll has `RiskTier.Bold`, the XP recorded equals `baseXp * 3`.

- DC ≤ 13: 15
- DC 14–17: 30
- DC ≥ 18: 45

### AC5: Tests verify each risk tier multiplier

Unit tests must cover all four risk tiers with at least one DC tier each, verifying the XP amount recorded in the `XpLedger`.

### AC6: Build clean

`dotnet build` must succeed with zero errors. All existing tests must continue to pass.

---

## Edge Cases

1. **Nat 20 override**: Nat 20 awards a flat 25 XP regardless of risk tier or DC. The multiplier does NOT apply to Nat 20. This is the existing precedence rule and must remain unchanged.

2. **Nat 1 override**: Nat 1 awards a flat 10 XP regardless of risk tier. No multiplier. Unchanged.

3. **Failure**: Failure awards a flat 2 XP regardless of risk tier. No multiplier. Unchanged.

4. **Risk tier on failure**: The `RiskTier` property is always set on `RollResult` (computed from DC, statModifier, levelBonus). However, the multiplier only applies when `rollResult.IsSuccess == true` and it is not a Nat 20. On failure, the tier is irrelevant for XP.

5. **Rounding at boundary**: 1.5x on base 5 = 7.5. The choice of `Math.Round` (banker's) vs `Math.Round(..., MidpointRounding.AwayFromZero)` affects this case. Either is acceptable; tests must match the implementation. With integer multipliers (2x, 3x), no rounding is needed.

6. **XpLedger source labels**: The source labels ("Success_DC_Low", "Success_DC_Mid", "Success_DC_High") should remain the same — only the `amount` parameter changes. This preserves backward compatibility for any code that reads the ledger by source label.

---

## Error Conditions

1. **`rollResult` is null**: Not expected — `RecordRollXp` is a private method called from `ResolveTurnAsync` which validates the roll result. No new null check needed.

2. **Unknown RiskTier enum value**: If a future `RiskTier` value is added, the multiplier should default to 1.0x (same as Safe). The default/else branch must handle unknown tiers gracefully.

3. **`XpLedger.Record` rejects amount ≤ 0**: This cannot happen because the minimum base XP is 5 and the minimum multiplier is 1.0x, so the minimum product is 5. No risk of zero or negative amounts.

---

## Dependencies

- **`RollResult.RiskTier`** (Pinder.Core.Rolls) — already exists, computed from DC/statModifier/levelBonus
- **`RollResult.IsSuccess`** — already exists
- **`RollResult.IsNatTwenty` / `IsNatOne`** — already exist
- **`RollResult.DC`** — already exists
- **`XpLedger.Record(string, int)`** (Pinder.Core.Progression) — already exists, no changes needed
- **`RiskTier` enum** (Pinder.Core.Rolls) — Safe, Medium, Hard, Bold — already exists

No new dependencies, no new NuGet packages, no external services. This is a pure logic change within `GameSession.RecordRollXp`.

---

## Implementation Guidance (for implementer reference)

The change is localized to `GameSession.RecordRollXp` (lines ~675–700 of `GameSession.cs`). The success branch currently does:

```
if (rollResult.DC <= 13) _xpLedger.Record("Success_DC_Low", 5);
else if (rollResult.DC <= 17) _xpLedger.Record("Success_DC_Mid", 10);
else _xpLedger.Record("Success_DC_High", 15);
```

After the fix, the success branch should:
1. Determine base XP from DC tier (5, 10, or 15)
2. Look up multiplier from `rollResult.RiskTier` (1.0, 1.5, 2.0, or 3.0)
3. Compute `(int)Math.Round(baseXp * multiplier)` 
4. Record with the same source label and the new amount

The Nat 20, Nat 1, and Failure branches remain unchanged.

**Note on C# 8.0 constraint**: `switch` expressions are available in C# 8.0 but require exhaustive patterns. An `if/else if` chain or `Dictionary<RiskTier, float>` lookup is equally valid. No `record` types or C# 9+ features.
