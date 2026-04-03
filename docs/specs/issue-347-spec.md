# Spec: ScoringPlayerAgent — Mechanical Expected-Value Scoring

**Issue:** #347
**Module:** docs/modules/session-runner.md (create new)

---

## Overview

`ScoringPlayerAgent` is a deterministic player agent that scores all dialogue options using an expected-value formula derived from the game's mechanical rules. It implements `IPlayerAgent` (defined in #346) and lives in the `session-runner/` project (per #355 — NOT in `Pinder.Core`). It uses no LLM — pure math — producing consistent, explainable decisions useful for regression testing and as a fallback for `LlmPlayerAgent` (#348).

---

## Function Signatures

All types live in the `session-runner/` project.

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

No constructor parameters. No mutable state.

### Supporting Types (from #346 — listed for completeness)

```csharp
public interface IPlayerAgent
{
    Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);
}

public sealed class PlayerDecision
{
    public int OptionIndex { get; }
    public string Reasoning { get; }     // never null
    public OptionScore[] Scores { get; } // one per option, never null
}

public sealed class OptionScore
{
    public int OptionIndex { get; }
    public float Score { get; }                  // final score after strategic adjustments
    public float SuccessChance { get; }          // 0.0–1.0 probability
    public float ExpectedInterestGain { get; }   // raw EV before strategic adjustments
    public string[] BonusesApplied { get; }      // e.g. ["momentum +2", "tell +2"]
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

For each `DialogueOption` in `TurnStart.Options`, compute the following steps:

### Step 1: Compute `need` (minimum d20 roll required to succeed)

```
attackerMod = context.PlayerStats.GetEffective(option.Stat)
defenceDC   = context.OpponentStats.GetDefenceDC(option.Stat)
              // = 13 + opponentStats.GetEffective(DefenceTable[option.Stat])
```

**Callback bonus — MUST call `CallbackBonus.Compute()` directly (per #386 ADR):**
```csharp
// MUST use the public static method — do NOT reimplement the logic
callbackBonus = option.CallbackTurnNumber.HasValue
    ? CallbackBonus.Compute(context.TurnNumber, option.CallbackTurnNumber.Value)
    : 0;
// CallbackBonus.Compute() returns: 0 (gap<2), 1 (gap 2-3 non-opener), 2 (gap>=4), 3 (opener w/ gap>=2)
```

**Momentum bonus — duplicate with SYNC comment (per #386 ADR):**
```csharp
// SYNC: GameSession.GetMomentumBonus()
// GameSession.GetMomentumBonus() is private static — cannot be called from session-runner
int momentumBonus;
if (context.MomentumStreak >= 5) momentumBonus = 3;
else if (context.MomentumStreak >= 3) momentumBonus = 2;
else momentumBonus = 0;
```

**Tell bonus — hardcode with SYNC comment (per #386 ADR):**
```csharp
// SYNC: GameSession ResolveTurnAsync tellBonus
int tellBonus = option.HasTellBonus ? 2 : 0;
```

**Compute need:**
```
need = defenceDC - (attackerMod + momentumBonus + tellBonus + callbackBonus)
```

### Step 2: Compute `successChance` and `failChance`

```
successChance = clamp((21 - need) / 20.0, 0.0, 1.0)
failChance    = 1.0 - successChance
```

This models a d20 roll: you succeed on rolling `need` or higher. A `need` of 1 means 100% success (20/20 faces work). A `need` of 21+ means 0% success.

### Step 3: Compute `riskTier` and `riskTierBonus`

Based on `need` (matching `RollResult.ComputeRiskTier` logic):

| Need range | Risk Tier | Risk Tier Bonus |
|---|---|---|
| ≤ 5 | Safe | 0 |
| 6–10 | Medium | 0 |
| 11–15 | Hard | +1 |
| ≥ 16 | Bold | +2 |

### Step 4: Compute expected interest on success

Use a weighted average of the success scale tiers based on the probability of landing in each margin bracket:

```
Success scale (from SuccessScale.GetInterestDelta):
  Beat DC by 1–4  → +1 interest
  Beat DC by 5–9  → +2 interest
  Beat DC by 10+  → +3 interest
  Nat 20          → +4 interest
```

For prototype maturity, the implementer may use either:

**Option A — Simple midpoint approximation:**
```
If successChance > 0:
  avgMargin ≈ (21 - need) / 2   // midpoint of the success range
  if avgMargin >= 10: baseInterestGain = 3
  else if avgMargin >= 5: baseInterestGain = 2
  else: baseInterestGain = 1
else:
  baseInterestGain = 0
```

**Option B — Exact weighted EV across all 20 die faces (more accurate):**
```
For each die face f in 1..20:
  total = f + attackerMod + momentumBonus + tellBonus + callbackBonus
  if total >= defenceDC:
    margin = total - defenceDC
    if f == 20: interestDelta = 4
    else if margin >= 10: interestDelta = 3
    else if margin >= 5: interestDelta = 2
    else: interestDelta = 1
    weightedGain += interestDelta / 20.0
baseInterestGain = weightedGain / successChance  // average gain given success
```

Either approach is acceptable. The tests should validate the agent's decision-making (strategic adjustments), not the exact EV precision.

```
comboBonus = option.ComboName != null ? 1 : 0
expectedGainOnSuccess = baseInterestGain + riskTierBonus + comboBonus
```

### Step 5: Compute expected cost on failure

```
Failure scale (from FailureScale.GetInterestDelta):
  Fumble (miss by 1-2)      → -1
  Misfire (miss by 3-5)     → -1
  TropeTrap (miss by 6-9)   → -2  (plus trap duration penalty)
  Catastrophe (miss by 10+) → -3
  Legendary (nat 1)         → -4

Approximate failCost = 1.5  (weighted baseline across failure tiers)

TropeTrap adds extra penalty: if a trap activates (miss by 6-9 range),
estimate ~-2 interest per turn × ~2 turns duration = -4 additional.
Weight by probability of landing in that miss range.
```

The implementer MAY use a simple constant `failCost ≈ 1.5` for prototype, or compute exact weighted failure cost across all 20 die faces. Either is acceptable.

### Step 6: Compute raw expected value (EV)

```
expectedInterestGain = successChance × expectedGainOnSuccess
                     - failChance × failCost

score = expectedInterestGain   // before strategic adjustments
```

The `expectedInterestGain` value is stored on the `OptionScore` and represents the raw EV.

### Step 7: Apply strategic adjustments to `score`

These modify the final `score` (not the raw EV stored in `ExpectedInterestGain`):

| Condition | Adjustment | Rationale |
|---|---|---|
| `context.MomentumStreak == 2` AND `successChance >= 0.5` | `score += 1.0` | One more success activates momentum (+2 roll bonus). Bias toward reliable options. |
| `context.CurrentInterest` in `[19, 24]` AND risk tier is Safe or Medium | `score += 2.0` | Close to winning — prefer low-variance options to seal the deal. |
| `context.InterestState == InterestState.Bored` AND risk tier is Hard or Bold | `score += 1.0` | Interest 1–4. Nothing to lose — swing for the fences. |
| Active trap on this option's stat | `score -= 2.0` | Rolling on a trapped stat risks compounding. Heavy penalty. |

**Active trap detection:** Match `option.Stat` to trap names in `context.ActiveTrapNames`. The mapping from `StatType` to shadow stat name:

| StatType | Shadow Stat (Trap Name) |
|---|---|
| Charm | Madness |
| Rizz | Horniness |
| Honesty | Denial |
| Chaos | Dread |
| Wit | Fixation |
| SA | Insecurity |

If `context.ActiveTrapNames` contains the shadow stat name for `option.Stat`, apply the -2.0 penalty.

### Step 8: Select option and build output

1. Pick the option with the highest `score`.
2. On ties, pick the first (lowest index).
3. Build `BonusesApplied` string array for each option, listing all non-zero bonuses (e.g., `"momentum +2"`, `"tell +2"`, `"callback +1"`, `"combo: The Switcheroo"`).
4. Build `Reasoning` string explaining the pick (see AC4).

---

## Bonus Constant Sync Requirements (per #386 ADR)

The following rules are **mandatory** for how bonus values are sourced:

| Bonus | Source | Rationale |
|---|---|---|
| Callback bonus | **Call `CallbackBonus.Compute()` directly** — public static in `Pinder.Core.Conversation` | Guaranteed in sync with game engine |
| Momentum bonus | **Duplicate** `GameSession.GetMomentumBonus()` logic with `// SYNC: GameSession.GetMomentumBonus()` comment | `GetMomentumBonus()` is private static — cannot call from session-runner |
| Tell bonus | **Hardcode `2`** with `// SYNC: GameSession ResolveTurnAsync tellBonus` comment | No public constant exists |

The SYNC comments flag potential drift during code review. This is acceptable at prototype maturity.

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
  [0] Charm   (no bonuses)
  [1] Rizz    (no bonuses)
  [2] Honesty (HasTellBonus=true)
  [3] Chaos   (ComboName="The Switcheroo")
```

**Computation for Option 0 (Charm):**
```
attackerMod = 4
defenceDC = 13 + 2 = 15  (opponent SA=2, Charm→SA defence)
momentumBonus = 0, tellBonus = 0, callbackBonus = 0
need = 15 - 4 = 11
successChance = (21 - 11) / 20.0 = 0.50
riskTier = Hard (need 11-15), riskTierBonus = 1
baseInterestGain ≈ 1 (avgMargin = 5, borderline 1-2)
expectedGainOnSuccess = 1 + 1 + 0 = 2
failCost ≈ 1.5
EV = 0.50 × 2 - 0.50 × 1.5 = 0.25
No strategic adjustments apply.
score = 0.25
```

**Computation for Option 2 (Honesty, with Tell):**
```
attackerMod = 1
defenceDC = 13 + 0 = 13  (opponent Chaos=0, Honesty→Chaos defence)
tellBonus = 2
need = 13 - (1 + 0 + 2 + 0) = 10
successChance = (21 - 10) / 20.0 = 0.55
riskTier = Medium (need 6-10), riskTierBonus = 0
baseInterestGain ≈ 1
expectedGainOnSuccess = 1 + 0 + 0 = 1
failCost ≈ 1.5
EV = 0.55 × 1 - 0.45 × 1.5 = -0.125
score = -0.125
```

**Output:**
```
PlayerDecision {
  OptionIndex: 0,
  Reasoning: "Charm at 50% with Hard tier (+1 bonus) gives best EV at 0.25.
              Honesty has tell bonus but lower expected gain.",
  Scores: [
    { OptionIndex: 0, Score: 0.25, SuccessChance: 0.50, ExpectedInterestGain: 0.25,
      BonusesApplied: [] },
    { OptionIndex: 1, Score: ..., ... },
    { OptionIndex: 2, Score: -0.125, SuccessChance: 0.55, ExpectedInterestGain: -0.125,
      BonusesApplied: ["tell +2"] },
    { OptionIndex: 3, Score: ..., ...,
      BonusesApplied: ["combo: The Switcheroo"] }
  ]
}
```

### Example 2: High momentum (streak = 2) — bias toward safe success

**Input:**
```
MomentumStreak: 2
Option A: successChance=0.70 (Safe), raw EV=0.40
Option B: successChance=0.30 (Bold), raw EV=0.45
```

**Output:** Option A wins.
- A: `score = 0.40 + 1.0 = 1.40` (momentum-2 bias: streak==2 AND successChance≥0.5)
- B: `score = 0.45` (successChance < 0.5 — no momentum bias)

### Example 3: Nearly won (interest = 20) — prefer safe

**Input:**
```
CurrentInterest: 20
Option A: riskTier=Safe, raw EV=0.30
Option B: riskTier=Bold, raw EV=0.50
```

**Output:** Option A wins.
- A: `score = 0.30 + 2.0 = 2.30` (near-win bias: interest in [19,24] AND Safe tier)
- B: `score = 0.50` (Bold — no near-win bias)

### Example 4: Bored state — prefer bold

**Input:**
```
CurrentInterest: 3, InterestState: Bored
Option A: riskTier=Safe, raw EV=0.10
Option B: riskTier=Bold, raw EV=0.05
```

**Output:** Option B wins.
- A: `score = 0.10` (Safe — no Bored bias)
- B: `score = 0.05 + 1.0 = 1.05` (Bored bias: InterestState==Bored AND Bold tier)

### Example 5: Active trap penalty

**Input:**
```
ActiveTrapNames: ["Madness"]  (Madness = Charm's shadow)
Option A: Charm, raw EV=0.25
Option B: Wit, raw EV=0.20
```

**Output:** Option B wins.
- A: `score = 0.25 - 2.0 = -1.75` (Charm has active Madness trap)
- B: `score = 0.20`

---

## Acceptance Criteria

### AC1: Implements `IPlayerAgent`

`ScoringPlayerAgent` must implement the `IPlayerAgent` interface from #346. The method signature:

```csharp
public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
```

Since no async work is performed, return `Task.FromResult(result)`.

### AC2: Scores all options per the formula

For every option in `TurnStart.Options`, compute:
- `need` from `defenceDC`, `attackerMod`, and all applicable bonuses
- `successChance` from `need`
- `expectedInterestGain` from success/fail EVs
- A final `score` combining EV with strategic adjustments

Each option's computation populates an `OptionScore` in `PlayerDecision.Scores`. `Scores.Length` must equal `TurnStart.Options.Length`.

### AC3: Applies momentum, interest state, and trap adjustments

All four strategic adjustments must be implemented:
1. **Momentum streak == 2:** +1.0 bias to options with `successChance >= 0.5`
2. **Interest 19–24 (near win):** +2.0 bias to Safe/Medium risk tier options
3. **Bored state (interest 1–4):** +1.0 bias to Hard/Bold risk tier options
4. **Active trap on stat:** -2.0 penalty to options using a stat with an active trap

### AC4: `PlayerDecision.Reasoning` explains the pick

The `Reasoning` string must include:
- Which option was chosen and why (stat name, percentage, key advantage)
- The dominant strategic factor if one applied (e.g., "Momentum at 2 — prioritizing success")
- At minimum one sentence of human-readable justification

Example:
```
"Charm at 50% beats Honesty at 55% — Hard tier +1 bonus outweighs raw success chance. Momentum at 2 — prioritizing reliable success."
```

### AC5: Deterministic

Given identical `TurnStart` and `PlayerAgentContext` inputs, `DecideAsync` must always return the same `PlayerDecision` — same `OptionIndex`, same `Scores` (within float equality), same `Reasoning`. No `Random`, no `DateTime.Now`, no external state.

### AC6: Unit tests

Required test scenarios:
1. **High-momentum state prefers safe option:** Streak=2, a safe option with lower raw EV beats a bold option because of the +1.0 momentum bias (given successChance ≥ 0.5).
2. **Bored state prefers bold:** Interest in 1–4 range, a bold option gets +1.0 bias.
3. **Active trap penalizes that stat:** An option using a trapped stat gets −2.0 and loses to a lower-EV untapped option.
4. **Near-win prefers safe:** Interest 19–24, Safe/Medium options get +2.0 bias.
5. **Tell bonus factored into need:** `HasTellBonus=true` lowers `need` by 2.
6. **Combo bonus adds interest:** Non-null `ComboName` gives +1 to expected interest on success.
7. **Callback bonus lowers need:** `CallbackTurnNumber` set → `CallbackBonus.Compute()` returns non-zero bonus.
8. **Basic EV ordering:** Without strategic adjustments, highest-EV option is chosen.

### AC7: Build clean

Zero warnings, zero errors. All existing tests (1977+) continue to pass.

---

## Edge Cases

| Case | Expected Behavior |
|---|---|
| All options have `successChance = 0.0` (need ≥ 21 for all) | All EVs are negative (−failCost). Pick the least negative. Strategic adjustments still apply (Bored bias may help Bold). |
| All options have `successChance = 1.0` (need ≤ 1 for all) | Pick highest `expectedGainOnSuccess`. Bold options benefit from risk tier bonus. |
| `TurnStart.Options` has fewer than 4 options | Score whatever is present. No assumption of exactly 4 options. |
| `TurnStart.Options` has exactly 1 option | Score it and return it. (Horniness-forced Rizz can produce a single option.) |
| `TurnStart.Options` is empty (length 0) | Return `PlayerDecision` with `OptionIndex = -1`, `Reasoning = "No options available"`, empty `Scores` array. |
| Multiple options tied for highest score | Pick the first (lowest index) among ties. |
| `PlayerAgentContext.ActiveTrapNames` is null | Treat as empty array — no trap penalties applied. |
| `PlayerAgentContext.MomentumStreak` is negative | Treat as 0 — no momentum bonus. |
| `CurrentInterest` at boundaries | 19 triggers near-win bias (range is [19, 24]). 4 triggers Bored bias (`InterestState.Bored` = 1–4). 18 does NOT trigger near-win. 5 does NOT trigger Bored (that's Lukewarm). |
| `CallbackTurnNumber` is 0 (opener callback) | `CallbackBonus.Compute(turnNumber, 0)` returns 3 (opener reference) when `turnNumber >= 2`. |
| Very high `defenceDC` makes `need > 20` | `successChance` clamps to 0.0. Only fail cost matters. |
| Very low `defenceDC` makes `need < 1` | `successChance` clamps to 1.0. Only success gain matters. |

---

## Error Conditions

| Condition | Expected Behavior |
|---|---|
| `turn` is null | Throw `ArgumentNullException("turn")` |
| `context` is null | Throw `ArgumentNullException("context")` |
| `turn.Options` is null | Throw `ArgumentNullException` (defensive — `TurnStart` constructor already prevents this) |
| `context.PlayerStats` is null | Throw `ArgumentNullException` (defensive) |
| `context.OpponentStats` is null | Throw `ArgumentNullException` (defensive) |
| Float overflow in score computation | Not expected given value ranges (interest 0–25, bonuses ±5). No special handling needed. |

---

## Dependencies

| Dependency | Location | Purpose |
|---|---|---|
| `IPlayerAgent` | `session-runner/IPlayerAgent.cs` (#346) | Interface this class implements |
| `PlayerDecision` | `session-runner/PlayerDecision.cs` (#346) | Return type |
| `OptionScore` | `session-runner/OptionScore.cs` (#346) | Per-option score breakdown |
| `PlayerAgentContext` | `session-runner/PlayerAgentContext.cs` (#346) | Input context carrying game state |
| `CallbackBonus` | `Pinder.Core.Conversation.CallbackBonus` | **Must call `Compute()` directly** per #386 ADR |
| `TurnStart` | `Pinder.Core.Conversation` | Input: options + game state snapshot |
| `DialogueOption` | `Pinder.Core.Conversation` | Individual option with stat, bonuses |
| `StatBlock` | `Pinder.Core.Stats` | `GetEffective()`, `GetDefenceDC()` |
| `StatType` | `Pinder.Core.Stats` | Stat enum (Charm, Rizz, Honesty, Chaos, Wit, SA) |
| `ShadowStatType` | `Pinder.Core.Stats` | Shadow stat enum (for trap name matching) |
| `InterestState` | `Pinder.Core.Conversation` | Interest state enum for strategic adjustments |

**Runtime dependencies:** None. Pure computation — no I/O, no network, no file access.

**Build dependency:** #346 must be implemented first (provides `IPlayerAgent`, `PlayerDecision`, `OptionScore`, `PlayerAgentContext`).
