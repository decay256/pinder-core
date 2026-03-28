# Spec: RollEngine — Apply Risk Tier Interest Bonus on Success

**Issue:** #42  
**Status:** Prototype  
**Last updated:** 2026-03-28

---

## 1. Overview

Rules v3.4 §5 defines a bonus to Interest gain when a player succeeds on a "risky" roll. The risk tier is derived from the gap between the DC and the player's modifiers (the "need" value). Hard rolls (need 11–15) grant +1 bonus Interest on success; Bold rolls (need 16+) grant +2. This feature adds a `RiskTier` enum, exposes it on `RollResult`, and wires the bonus into `GameSession.ResolveTurnAsync`.

---

## 2. Function Signatures

### 2.1 New enum: `RiskTier`

**Namespace:** `Pinder.Core.Rolls`  
**File:** `src/Pinder.Core/Rolls/RiskTier.cs`

```csharp
public enum RiskTier
{
    Safe,    // need ≤ 5
    Medium,  // need 6–10
    Hard,    // need 11–15
    Bold     // need ≥ 16
}
```

### 2.2 Modified class: `RollResult`

**File:** `src/Pinder.Core/Rolls/RollResult.cs`

Add a new read-only property:

```csharp
public RiskTier RiskTier { get; }
```

The value is computed at construction time from the `need` value. The `need` value is defined as:

```
need = dc - (statModifier + levelBonus)
```

This represents the minimum d20 roll required to hit the DC (before considering nat-1/nat-20 rules). The constructor must accept or compute the `RiskTier` and store it.

**Tier thresholds (applied to `need`):**

| Condition | RiskTier |
|-----------|----------|
| need ≤ 5 | `Safe` |
| 6 ≤ need ≤ 10 | `Medium` |
| 11 ≤ need ≤ 15 | `Hard` |
| need ≥ 16 | `Bold` |

**Edge case — negative or zero need:** When modifiers exceed the DC (need ≤ 0), the tier is `Safe`.

**Edge case — need > 20:** The roll is impossible without a nat-20, but the tier is still `Bold` (need ≥ 16).

### 2.3 New static method: `RiskTierBonus.GetInterestBonus`

**Namespace:** `Pinder.Core.Rolls`  
**File:** `src/Pinder.Core/Rolls/RiskTierBonus.cs` (new file)

```csharp
public static class RiskTierBonus
{
    /// <summary>
    /// Returns the bonus Interest delta for a successful roll at the given risk tier.
    /// Returns 0 for failures or Safe/Medium tiers.
    /// </summary>
    public static int GetInterestBonus(RollResult result);
}
```

**Return values:**

| Condition | Return |
|-----------|--------|
| `result.IsSuccess == false` | `0` |
| `result.RiskTier == Safe` | `0` |
| `result.RiskTier == Medium` | `0` |
| `result.RiskTier == Hard` | `1` |
| `result.RiskTier == Bold` | `2` |

### 2.4 Modified method: `GameSession.ResolveTurnAsync`

**File:** `src/Pinder.Core/Conversation/GameSession.cs`

After computing `interestDelta` from `SuccessScale.GetInterestDelta(rollResult)` and before applying momentum, add the risk tier bonus:

```
interestDelta += RiskTierBonus.GetInterestBonus(rollResult);
```

This line executes only on the success branch (where `SuccessScale` is called). On failure, the risk tier bonus is not applied.

---

## 3. Input/Output Examples

### Example 1: Hard success

- **Stat modifier:** +2, **Level bonus:** +1, **DC:** 18
- **need** = 18 − (2 + 1) = 15 → `RiskTier.Hard`
- **d20 roll:** 16 → Total = 16 + 2 + 1 = 19 ≥ 18 → Success
- **SuccessScale delta:** margin = 19 − 18 = 1 → +1
- **RiskTierBonus:** Hard → +1
- **Total interest delta (before momentum):** +1 + 1 = **+2**

### Example 2: Bold success (nat 20)

- **Stat modifier:** +0, **Level bonus:** +0, **DC:** 20
- **need** = 20 − (0 + 0) = 20 → `RiskTier.Bold`
- **d20 roll:** 20 → Nat 20 auto-success
- **SuccessScale delta:** Nat 20 → +4
- **RiskTierBonus:** Bold → +2
- **Total interest delta (before momentum):** +4 + 2 = **+6**

### Example 3: Safe success — no bonus

- **Stat modifier:** +4, **Level bonus:** +2, **DC:** 10
- **need** = 10 − (4 + 2) = 4 → `RiskTier.Safe`
- **d20 roll:** 8 → Total = 8 + 4 + 2 = 14 ≥ 10 → Success
- **SuccessScale delta:** margin = 14 − 10 = 4 → +1
- **RiskTierBonus:** Safe → 0
- **Total interest delta (before momentum):** +1 + 0 = **+1**

### Example 4: Bold failure — no bonus

- **Stat modifier:** +0, **Level bonus:** +0, **DC:** 20
- **need** = 20 − (0 + 0) = 20 → `RiskTier.Bold`
- **d20 roll:** 5 → Total = 5 → miss by 15 → `FailureTier.Catastrophe`
- **FailureScale delta:** −4
- **RiskTierBonus:** failure → 0
- **Total interest delta:** **−4**

### Example 5: Medium success — no bonus

- **Stat modifier:** +3, **Level bonus:** +1, **DC:** 14
- **need** = 14 − (3 + 1) = 10 → `RiskTier.Medium`
- **d20 roll:** 12 → Total = 12 + 3 + 1 = 16 ≥ 14 → Success
- **SuccessScale delta:** margin = 16 − 14 = 2 → +1
- **RiskTierBonus:** Medium → 0
- **Total interest delta (before momentum):** **+1**

---

## 4. Acceptance Criteria

### AC-1: `RollResult` exposes `RiskTier` enum (Safe/Medium/Hard/Bold)

`RollResult` must have a public read-only property `RiskTier` of type `Pinder.Core.Rolls.RiskTier`. The value must be computed from `need = dc - (statModifier + levelBonus)` using the thresholds in §2.2. Every `RollResult` returned by `RollEngine.Resolve` must have this property populated correctly.

### AC-2: Hard success = +1 bonus Interest on top of SuccessScale delta

When `RollResult.IsSuccess == true` and `RollResult.RiskTier == RiskTier.Hard`, the total interest delta applied in `GameSession.ResolveTurnAsync` must include an additional +1 on top of the `SuccessScale` value.

### AC-3: Bold success = +2 bonus Interest on top of SuccessScale delta

When `RollResult.IsSuccess == true` and `RollResult.RiskTier == RiskTier.Bold`, the total interest delta applied in `GameSession.ResolveTurnAsync` must include an additional +2 on top of the `SuccessScale` value.

### AC-4: Safe/Medium success = no bonus

When `RollResult.RiskTier` is `Safe` or `Medium`, `RiskTierBonus.GetInterestBonus` must return 0 regardless of success/failure.

### AC-5: Tests cover all four tiers

Unit tests must verify:
- Safe success → bonus = 0
- Medium success → bonus = 0
- Hard success → bonus = 1
- Bold success → bonus = 2
- All four tiers on failure → bonus = 0
- Boundary values: need = 5 (Safe), need = 6 (Medium), need = 10 (Medium), need = 11 (Hard), need = 15 (Hard), need = 16 (Bold)
- Edge: need ≤ 0 → Safe
- Edge: need > 20 → Bold

### AC-6: Build clean, all existing tests pass

`dotnet build` and `dotnet test` must pass with zero errors and zero warnings (beyond any pre-existing warnings). No existing test may be broken.

---

## 5. Edge Cases

| Scenario | `need` value | Expected `RiskTier` | Notes |
|----------|-------------|---------------------|-------|
| Modifiers exceed DC | −3 | `Safe` | Treat all need ≤ 5 as Safe |
| need exactly 0 | 0 | `Safe` | Auto-success territory |
| need exactly 5 | 5 | `Safe` | Upper boundary of Safe |
| need exactly 6 | 6 | `Medium` | Lower boundary of Medium |
| need exactly 10 | 10 | `Medium` | Upper boundary of Medium |
| need exactly 11 | 11 | `Hard` | Lower boundary of Hard |
| need exactly 15 | 15 | `Hard` | Upper boundary of Hard |
| need exactly 16 | 16 | `Bold` | Lower boundary of Bold |
| need exactly 20 | 20 | `Bold` | Only nat-20 succeeds |
| need > 20 (impossible without nat-20) | 25 | `Bold` | Still Bold; nat-20 auto-succeeds |
| Nat-20 with Bold tier | 20 | `Bold` + success | Bonus applies: +2 added to SuccessScale's +4 = +6 total |
| Nat-1 with Safe tier | 2 | `Safe` + failure (auto-fail) | No bonus on failure; `RiskTier` is still `Safe` |
| Advantage/disadvantage | any | unchanged | Risk tier is based on DC and modifiers, not dice results |
| Trap-modified stat penalty | varies | Depends on post-trap modifier | `need` must use the effective stat modifier (after trap penalties), which is what `RollResult.StatModifier` already stores |

---

## 6. Error Conditions

| Condition | Expected behavior |
|-----------|------------------|
| `RollResult` constructed with invalid `RiskTier` enum value | Not applicable — `RiskTier` is computed internally, not user-supplied |
| `RiskTierBonus.GetInterestBonus(null)` | Throw `ArgumentNullException` |
| Negative DC passed to `RollEngine.Resolve` | Existing behavior unchanged; `need` may be very negative → `Safe` |

No new exception types are introduced. The feature is purely additive and does not alter existing error paths.

---

## 7. Dependencies

### Internal (same repo)

| Component | How it's used |
|-----------|--------------|
| `Pinder.Core.Rolls.RollResult` | Modified: add `RiskTier` property |
| `Pinder.Core.Rolls.SuccessScale` | Read-only: unchanged, called before risk bonus |
| `Pinder.Core.Rolls.FailureScale` | Read-only: unchanged |
| `Pinder.Core.Rolls.RollEngine` | Modified: must pass correct data so `RollResult` can compute `RiskTier` |
| `Pinder.Core.Conversation.GameSession` | Modified: `ResolveTurnAsync` adds risk tier bonus |

### External

None. Zero NuGet dependencies. Target: `netstandard2.0`, `LangVersion 8.0`.

---

## 8. Integration with Existing Systems

### Order of interest delta computation in `ResolveTurnAsync`

After this feature, the success-path interest delta computation is:

```
interestDelta = SuccessScale.GetInterestDelta(rollResult)   // +1/+2/+3/+4
              + RiskTierBonus.GetInterestBonus(rollResult)   // +0/+1/+2
              + GetMomentumBonus(streak)                     // +0/+2/+3
```

The failure path is unchanged:

```
interestDelta = FailureScale.GetInterestDelta(rollResult)    // -1 to -5
```

### Rules-to-Code Sync Table additions

| Rules Section | Rule Value | C# Location | C# Constant/Expression |
|---|---|---|---|
| §5 Risk tier thresholds | Need ≤5=Safe, 6–10=Medium, 11–15=Hard, ≥16=Bold | `Rolls/RiskTier.cs` or `Rolls/RollResult.cs` | Boundary checks computing `RiskTier` |
| §5 Risk tier bonus | Hard=+1, Bold=+2 | `Rolls/RiskTierBonus.cs` | `RiskTierBonus.GetInterestBonus()` |
