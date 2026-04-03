# Spec: ScoringPlayerAgent — Mechanical Expected-Value Scoring

**Issue:** #347  
**Module:** docs/modules/session-runner.md (create new)

---

## Overview

`ScoringPlayerAgent` is a deterministic player agent that scores all dialogue options using an expected-value formula derived from the game's mechanical rules. It implements `IPlayerAgent` (defined in #346) and lives in the `session-runner/` project. It uses no LLM — pure math — producing consistent, explainable decisions useful for regression testing and as a fallback for `LlmPlayerAgent` (#348).

---

## Function Signatures

All types live in the `session-runner/` project (per #355 — NOT in `Pinder.Core`).

### ScoringPlayerAgent

```csharp
// File: session-runner/ScoringPlayerAgent.cs

public sealed class ScoringPlayerAgent : IPlayerAgent
{
    /// <summary>
    /// Scores all options in the TurnStart and picks the highest-scoring one.
    /// Deterministic: same inputs always produce the same output.
    /// Returns a completed Task (no async work needed).
    /// </summary>
    public Task<PlayerDecision> DecideAsync(
        TurnStart turn,
        PlayerAgentContext context);
}
```

### Supporting Types (from #346 — listed here for completeness)

```csharp
public interface IPlayerAgent
{
    Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);
}

public sealed class PlayerDecision
{
    public int OptionIndex { get; }
    public string Reasoning { get; }
    public OptionScore[] Scores { get; }
}

public sealed class OptionScore
{
    public int OptionIndex { get; }
    public float Score { get; }              // final score after strategic adjustments
    public float SuccessChance { get; }      // 0.0–1.0 probability
    public float ExpectedInterestGain { get; } // raw EV before strategic adjustments
    public string[] BonusesApplied { get; }  // e.g. ["momentum +2", "tell +2"]
}

public sealed class PlayerAgentContext
{
    public StatBlock PlayerStats { get; }
    public StatBlock OpponentStats { get; }
    public int CurrentInterest { get; }
    public InterestState InterestState { get; }
    public int MomentumStreak { get; }
    public string[] ActiveTrapNames { get; }
    public int SessionHorniness { get; }
    public Dictionary<ShadowStatType, int>? ShadowValues { get; }
    public int TurnNumber { get; }
}
```

---

## Scoring Formula

For each `DialogueOption` in `TurnStart.Options`, compute:

### Step 1: Compute `need`

```
attackerMod = context.PlayerStats.GetEffective(option.Stat)
defenceDC   = context.OpponentStats.GetDefenceDC(option.Stat)
              // = 13 + opponentStats.GetEffective(DefenceTable[option.Stat])

momentumBonus = 0
  if context.MomentumStreak >= 5: momentumBonus = 3
  else if context.MomentumStreak >= 3: momentumBonus = 2

tellBonus = option.HasTellBonus ? 2 : 0

callbackBonus = 0
  if option.CallbackTurnNumber.HasValue:
    gap = context.TurnNumber - option.CallbackTurnNumber.Value
    if gap >= 4: callbackBonus = 2
    else if gap >= 2: callbackBonus = 1
    // gap < 2 or gap for opener (CallbackTurnNumber == 0): callbackBonus = 3
    // Opener callback: if CallbackTurnNumber == 0, callbackBonus = 3

need = defenceDC - (attackerMod + momentumBonus + tellBonus + callbackBonus)
```

### Step 2: Compute `successChance` and `failChance`

```
successChance = clamp((21 - need) / 20.0, 0.0, 1.0)
failChance    = 1.0 - successChance
```

This models a d20 roll: you succeed on rolling `need` or higher. A need of 1 means you always succeed (20/20), a need of 21 means you never succeed (0/20).

### Step 3: Compute `riskTier` and `riskTierBonus`

```
if need <= 5:  riskTier = Safe,   riskTierBonus = 0
if need 6-10:  riskTier = Medium, riskTierBonus = 0
if need 11-15: riskTier = Hard,   riskTierBonus = 1
if need >= 16: riskTier = Bold,   riskTierBonus = 2
```

### Step 4: Compute expected interest on success

Use a weighted average of the success scale tiers based on the probability of landing in each margin bracket:

```
baseInterestGain:
  Approximate using the midpoint assumption:
    margin 1-4  → +1 interest
    margin 5-9  → +2 interest
    margin 10+  → +3 interest

  For simplicity (and the prototype), use a single weighted estimate:
    If successChance > 0:
      Estimate the average margin given success.
      avgMargin ≈ (21 - need) / 2  (midpoint of the success range on d20)
      if avgMargin >= 10: baseInterestGain = 3
      else if avgMargin >= 5: baseInterestGain = 2
      else: baseInterestGain = 1
    else:
      baseInterestGain = 0
```

Alternatively, the implementer MAY compute exact weighted EV across all 20 die faces for greater accuracy. Either approach is acceptable at prototype maturity.

```
comboBonus = option.ComboName != null ? 1 : 0

expectedGainOnSuccess = baseInterestGain + riskTierBonus + comboBonus
```

### Step 5: Compute expected interest on failure

```
Failure cost approximation by tier:
  need <= 2 above DC (miss by 1-2):   Fumble    → -1
  need 3-5 above DC (miss by 3-5):    Misfire   → -1
  need 6-9 above DC (miss by 6-9):    TropeTrap → -2  (plus ~-2 per turn for trap duration)
  need 10+ above DC (miss by 10+):    Catastrophe → -3
  nat 1 (always possible):            Legendary → -4

Approximate failCost as weighted average across failure tiers,
or use a simpler estimate:
  failCost = 1.5  (base estimate — accounts for mix of Fumble/Misfire/TropeTrap)

If a trap would fire (TropeTrap tier, miss by 6-9), add estimated penalty:
  trapPenaltyEstimate ≈ -2 interest per turn × estimated trap duration (~2 turns) = -4
  Weight this by the probability of landing in the 6-9 miss range.
```

### Step 6: Compute raw expected value (EV)

```
expectedInterestGain = successChance × expectedGainOnSuccess
                     - failChance × failCost

score = expectedInterestGain  // before strategic adjustments
```

### Step 7: Apply strategic adjustments

These modify the final `score` (not the EV):

| Condition | Adjustment | Rationale |
|---|---|---|
| `context.MomentumStreak == 2` | `score += 1.0` if `successChance >= 0.5` | Bias toward safe success to activate momentum (+2 bonus next roll) |
| `context.CurrentInterest >= 19 && context.CurrentInterest <= 24` | `score += 2.0` if `riskTier == Safe or Medium` | Close to winning — prefer low-variance options to seal the deal |
| `context.InterestState == InterestState.Bored` (interest 1–4) | `score += 1.0` if `riskTier == Hard or Bold` | Nothing to lose — swing for the fences, trap risk is secondary |
| Active trap on this option's stat | `score -= 2.0` | Heavy penalty: rolling on a trapped stat re-triggers or compounds |

**Active trap check:** An option's stat has an active trap if `context.ActiveTrapNames` contains the stat's trap name. The mapping from `StatType` to trap name uses the `StatBlock.ShadowPairs` mapping to derive the shadow stat, then the trap's name corresponds to the shadow stat name (e.g., `Charm` → `Madness`, `Rizz` → `Horniness`). The implementer should match `option.Stat`'s shadow pair name against `context.ActiveTrapNames`.

### Step 8: Select option and build output

Pick the option with the highest `score`. On ties, pick the first (lowest index).

---

## Input/Output Examples

### Example 1: Standard 4-option turn

**Input:**

```
PlayerStats: Charm=4, Rizz=2, Honesty=1, Chaos=3, Wit=2, SA=1 (all shadows 0)
OpponentStats: SA=2, Wit=1, Chaos=0, Charm=1, Rizz=2, Honesty=3 (all shadows 0)
CurrentInterest: 12
InterestState: Interested
MomentumStreak: 0
ActiveTrapNames: []
TurnNumber: 3

Options:
  [0] Charm  (no bonuses)
  [1] Rizz   (no bonuses)
  [2] Honesty (HasTellBonus=true)
  [3] Chaos  (ComboName="The Switcheroo")
```

**Computation for Option 0 (Charm):**
```
attackerMod = 4 (Charm effective)
defenceDC = 13 + 2 (opponent SA effective) = 15
momentumBonus = 0, tellBonus = 0, callbackBonus = 0
need = 15 - 4 = 11
successChance = (21 - 11) / 20.0 = 0.50
riskTier = Hard (need 11-15), riskTierBonus = 1
baseInterestGain = 1 (avgMargin ≈ 5 → marginal, could be 1 or 2)
expectedGainOnSuccess = 1 + 1 + 0 = 2
failCost ≈ 1.5
EV = 0.50 × 2 - 0.50 × 1.5 = 0.25
No strategic adjustments apply.
score = 0.25
```

**Computation for Option 2 (Honesty, with Tell):**
```
attackerMod = 1 (Honesty effective)
defenceDC = 13 + 0 (opponent Chaos effective) = 13
tellBonus = 2
need = 13 - (1 + 0 + 2 + 0) = 10
successChance = (21 - 10) / 20.0 = 0.55
riskTier = Medium (need 6-10), riskTierBonus = 0
baseInterestGain = 1
expectedGainOnSuccess = 1 + 0 + 0 = 1
failCost ≈ 1.5
EV = 0.55 × 1 - 0.45 × 1.5 = -0.125
score = -0.125
```

**Output:**
```
PlayerDecision {
  OptionIndex: 0,
  Reasoning: "Charm at 50% with Hard risk tier (+1 bonus) is the best EV at 0.25. 
              Honesty has tell bonus but only +1 interest gain on success.",
  Scores: [
    { OptionIndex: 0, Score: 0.25, SuccessChance: 0.50, ExpectedInterestGain: 0.25, BonusesApplied: [] },
    { OptionIndex: 1, Score: ..., SuccessChance: ..., ... },
    { OptionIndex: 2, Score: -0.125, SuccessChance: 0.55, ExpectedInterestGain: -0.125, BonusesApplied: ["tell +2"] },
    { OptionIndex: 3, Score: ..., SuccessChance: ..., ExpectedInterestGain: ..., BonusesApplied: ["combo: The Switcheroo"] }
  ]
}
```

### Example 2: High momentum (streak = 2) — bias toward safe success

**Input:**
```
MomentumStreak: 2
Option A: Charm, successChance=0.70 (Safe), EV=0.40
Option B: Chaos, successChance=0.30 (Bold), EV=0.45
```

**Output:** Option A is chosen because `score = 0.40 + 1.0 = 1.40` (momentum-2 bias) beats Option B's `score = 0.45` (successChance < 0.5, so no momentum bias).

### Example 3: Nearly won (interest = 20) — prefer safe

**Input:**
```
CurrentInterest: 20
Option A: Wit, riskTier=Safe, EV=0.30
Option B: Rizz, riskTier=Bold, EV=0.50
```

**Output:** Option A: `score = 0.30 + 2.0 = 2.30`. Option B: `score = 0.50`. Option A wins despite lower EV because closing the deal safely is paramount.

### Example 4: Active trap penalty

**Input:**
```
ActiveTrapNames: ["Madness"]  // Madness is Charm's shadow
Option A: Charm (trapped stat), EV=0.25
Option B: Wit, EV=0.20
```

**Output:** Option A: `score = 0.25 - 2.0 = -1.75`. Option B: `score = 0.20`. Option B wins because Charm has an active trap.

---

## Acceptance Criteria

### AC1: Implements `IPlayerAgent`

`ScoringPlayerAgent` must implement the `IPlayerAgent` interface defined in #346. The `DecideAsync` method signature must match exactly:

```csharp
public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
```

Since no async work is performed, the implementation should return `Task.FromResult(result)`.

### AC2: Scores all 4 options per the formula

For every option in `TurnStart.Options`, the agent must compute:
- `successChance` from `need`, `attackerMod`, and `defenceDC`
- `expectedInterestGain` from success scale, risk tier bonus, combo bonus, and fail cost
- A final `score` that combines EV with strategic adjustments

Each option's computation is stored in an `OptionScore` within `PlayerDecision.Scores`.

### AC3: Applies momentum, interest state, and trap adjustments

The four strategic adjustments must all be implemented:
1. **Momentum streak = 2:** +1.0 bias to options with `successChance >= 0.5`
2. **Interest 19–24 (near win):** +2.0 bias to Safe/Medium risk tier options
3. **Bored state (interest 1–4):** +1.0 bias to Hard/Bold risk tier options
4. **Active trap on stat:** -2.0 penalty to options using a stat with an active trap

### AC4: `PlayerDecision.Reasoning` explains the pick

The `Reasoning` string must include:
- Which option was chosen and why (stat name, percentage, key advantage)
- The dominant strategic factor if one applied (e.g., "Momentum at 2 — prioritizing success")
- At minimum one sentence of human-readable justification

Example format:
```
"Charm at 50% beats Honesty at 30% — 20pp advantage outweighs DC risk. Momentum at 2 — prioritizing success to reach +2 bonus."
```

### AC5: Deterministic

Given identical `TurnStart` and `PlayerAgentContext` inputs, `DecideAsync` must always return the same `PlayerDecision` — same `OptionIndex`, same `Scores` (within floating-point equality), same `Reasoning`. No `Random`, no `DateTime.Now`, no external state.

### AC6: Unit tests

The following test scenarios must be covered:
1. **High-momentum state prefers safe option:** Streak = 2, a safe option with lower EV beats a bold option because of the +1.0 momentum bias (given its success chance ≥ 0.5).
2. **Bored state prefers bold:** Interest in 1–4 range, a bold option gets +1.0 bias.
3. **Active trap penalizes that stat:** An option using a trapped stat gets −2.0 and loses to an option with lower base EV but no trap.
4. **Near-win prefers safe:** Interest 19–24, Safe/Medium options get +2.0 bias.
5. **Tell bonus is factored into need:** An option with `HasTellBonus=true` gets +2 to its effective modifier, lowering `need`.
6. **Combo bonus adds interest:** An option with a non-null `ComboName` gets +1 to expected interest on success.
7. **Callback bonus lowers need:** An option with `CallbackTurnNumber` set gets a callback bonus.
8. **Basic EV ordering:** Without strategic adjustments, the option with the highest EV is chosen.

### AC7: Build clean

The project must compile with zero warnings and zero errors. All existing tests (1977+) must continue to pass.

---

## Edge Cases

| Case | Expected Behavior |
|---|---|
| All options have `successChance = 0.0` (need ≥ 21 for all) | All EVs are negative (−failCost). Pick the least negative. Bored bias may still apply to Bold options. |
| All options have `successChance = 1.0` (need ≤ 1 for all) | All succeed always. Pick highest `expectedGainOnSuccess`. Bold options get risk tier bonus. |
| `TurnStart.Options` has fewer than 4 options | Score whatever is present. No assumption of exactly 4. |
| `TurnStart.Options` has exactly 1 option | Score it and return it (Horniness-forced Rizz can produce a single option). |
| `TurnStart.Options` is empty (length 0) | Return `PlayerDecision` with `OptionIndex = -1`, `Reasoning = "No options available"`, empty `Scores`. |
| Multiple options tied for highest score | Pick the first (lowest index) among ties. |
| `PlayerAgentContext.ActiveTrapNames` is null | Treat as empty — no trap penalties. |
| `PlayerAgentContext.MomentumStreak` is negative | Treat as 0 — no momentum bonus. |
| `PlayerAgentContext.CurrentInterest` is at boundary (exactly 19, exactly 4) | 19 triggers near-win bias. 4 triggers Bored bias (InterestState.Bored is 1–4). |
| `CallbackTurnNumber` is 0 (opener callback) | `callbackBonus = 3` (opener reference). |
| Shadow penalty reduces `attackerMod` below 0 | `need` increases, `successChance` decreases. The formula handles this naturally. |

---

## Error Conditions

| Condition | Expected Behavior |
|---|---|
| `turn` is null | Throw `ArgumentNullException("turn")` |
| `context` is null | Throw `ArgumentNullException("context")` |
| `turn.Options` is null | Throw `ArgumentNullException` (defensive — TurnStart constructor already prevents this) |
| `context.PlayerStats` is null | Throw `ArgumentNullException` (defensive) |
| `context.OpponentStats` is null | Throw `ArgumentNullException` (defensive) |
| Float overflow in score computation | Not expected given value ranges (interest 0–25, bonuses ±5). No special handling needed. |

---

## Dependencies

| Dependency | Location | Purpose |
|---|---|---|
| `IPlayerAgent` | `session-runner/IPlayerAgent.cs` (from #346) | Interface this class implements |
| `PlayerDecision` | `session-runner/PlayerDecision.cs` (from #346) | Return type |
| `OptionScore` | `session-runner/OptionScore.cs` (from #346) | Per-option score breakdown |
| `PlayerAgentContext` | `session-runner/PlayerAgentContext.cs` (from #346) | Input context carrying game state |
| `TurnStart` | `Pinder.Core.Conversation` | Input: options + game state snapshot |
| `DialogueOption` | `Pinder.Core.Conversation` | Individual option with stat, bonuses |
| `StatBlock` | `Pinder.Core.Stats` | Player/opponent stats, `GetEffective()`, `GetDefenceDC()` |
| `StatType` | `Pinder.Core.Stats` | Stat enum (Charm, Rizz, etc.) |
| `ShadowStatType` | `Pinder.Core.Stats` | Shadow stat enum (for trap name matching) |
| `InterestState` | `Pinder.Core.Conversation` | Interest state enum (Bored, Interested, etc.) |
| `RiskTier` | `Pinder.Core.Rolls` | Risk tier enum (Safe, Medium, Hard, Bold) — may be used for classification |

**Runtime dependencies:** None. Pure computation, no I/O, no network, no file access.

**Build dependency:** #346 must be implemented first (provides `IPlayerAgent`, `PlayerDecision`, `OptionScore`, `PlayerAgentContext`).
