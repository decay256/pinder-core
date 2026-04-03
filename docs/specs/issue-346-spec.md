# Spec: IPlayerAgent Interface and Scoring Model

**Issue:** #346 — Player agent: IPlayerAgent interface and scoring model for sim decision-making  
**Module:** docs/modules/session-runner.md (create new)

---

## Overview

The simulation runner currently selects dialogue options using a naive `BestOption` function that picks whichever option has the highest effective stat modifier. This issue introduces an `IPlayerAgent` abstraction and supporting data types so that the session runner can delegate turn decisions to pluggable agents — a deterministic scoring agent (#347) and an LLM-backed agent (#348). All new types live in `session-runner/`, NOT in `Pinder.Core`, per vision concern #355.

---

## Function Signatures

All types are in the `session-runner/` project (namespace TBD by implementer; suggested `Pinder.SessionRunner`). The project targets `net8.0` but uses **LangVersion 8.0** per the existing `.csproj`.

### IPlayerAgent

```csharp
public interface IPlayerAgent
{
    /// <summary>
    /// Given a TurnStart (options + game state snapshot) and additional agent context,
    /// returns a decision: which option to pick, why, and score breakdowns for all options.
    /// </summary>
    Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);
}
```

- `TurnStart` is `Pinder.Core.Conversation.TurnStart` (already exists). It contains `Options` (`DialogueOption[]`) and `State` (`GameStateSnapshot`).
- `PlayerAgentContext` carries additional data not available on `TurnStart` (see below).
- Returns `Task<PlayerDecision>` because LLM-backed agents need async I/O. Synchronous agents (like `ScoringPlayerAgent`) return `Task.FromResult(...)`.

### PlayerDecision

```csharp
public sealed class PlayerDecision
{
    /// <summary>Index into TurnStart.Options (0-based).</summary>
    public int OptionIndex { get; }

    /// <summary>Human-readable explanation of why this option was chosen.</summary>
    public string Reasoning { get; }

    /// <summary>Score breakdown for every option in the TurnStart. Length == TurnStart.Options.Length.</summary>
    public OptionScore[] Scores { get; }

    public PlayerDecision(int optionIndex, string reasoning, OptionScore[] scores);
}
```

**Invariants:**
- `OptionIndex` is in range `[0, Scores.Length)`.
- `Scores` is never null and has one entry per dialogue option.
- `Reasoning` is never null (may be empty string for deterministic agents).

### OptionScore

```csharp
public sealed class OptionScore
{
    /// <summary>Index of the option this score corresponds to.</summary>
    public int OptionIndex { get; }

    /// <summary>Composite score (higher = better pick). Implementation-defined scale.</summary>
    public float Score { get; }

    /// <summary>Estimated probability of beating the DC, as a percentage 0–100.</summary>
    public float SuccessChance { get; }

    /// <summary>Expected interest gain (positive or negative), weighting success and failure outcomes.</summary>
    public float ExpectedInterestGain { get; }

    /// <summary>Human-readable list of bonuses factored into the score, e.g. ["callback +2", "tell +2"].</summary>
    public string[] BonusesApplied { get; }

    public OptionScore(
        int optionIndex,
        float score,
        float successChance,
        float expectedInterestGain,
        string[] bonusesApplied);
}
```

**Invariants:**
- `SuccessChance` is clamped to `[0, 100]`.
- `BonusesApplied` is never null (may be empty array).

### PlayerAgentContext

```csharp
public sealed class PlayerAgentContext
{
    /// <summary>The player character's stat block (immutable).</summary>
    public StatBlock PlayerStats { get; }

    /// <summary>The opponent character's stat block (immutable).</summary>
    public StatBlock OpponentStats { get; }

    /// <summary>Current interest meter value (0-25).</summary>
    public int CurrentInterest { get; }

    /// <summary>Current interest state (derived from CurrentInterest).</summary>
    public InterestState InterestState { get; }

    /// <summary>Number of consecutive successful rolls (0 = no streak).</summary>
    public int MomentumStreak { get; }

    /// <summary>Names of currently active traps.</summary>
    public string[] ActiveTrapNames { get; }

    /// <summary>Current session horniness value (from SessionShadowTracker, 0 if unavailable).</summary>
    public int SessionHorniness { get; }

    /// <summary>Current shadow stat values (from SessionShadowTracker). Null if shadow tracking disabled.</summary>
    public Dictionary<ShadowStatType, int>? ShadowValues { get; }

    /// <summary>Current turn number (from GameStateSnapshot.TurnNumber).</summary>
    public int TurnNumber { get; }

    public PlayerAgentContext(
        StatBlock playerStats,
        StatBlock opponentStats,
        int currentInterest,
        InterestState interestState,
        int momentumStreak,
        string[] activeTrapNames,
        int sessionHorniness,
        Dictionary<ShadowStatType, int>? shadowValues,
        int turnNumber);
}
```

**Invariants:**
- `PlayerStats` and `OpponentStats` are never null.
- `ActiveTrapNames` is never null (may be empty array).
- `CurrentInterest` is in `[0, 25]`.
- `MomentumStreak` is `>= 0`.

---

## Input/Output Examples

### Example 1: Basic decision with 4 options

**Input — TurnStart.Options:**

| Index | Stat    | CallbackTurnNumber | ComboName    | HasTellBonus | HasWeaknessWindow |
|-------|---------|--------------------|--------------|--------------|-------------------|
| 0     | Charm   | null               | null         | false        | false             |
| 1     | Rizz    | null               | null         | false        | false             |
| 2     | Honesty | 3                  | null         | false        | false             |
| 3     | Chaos   | null               | "WitChaosSA" | true         | false             |

**Input — PlayerAgentContext:**

- PlayerStats: Charm +4, Rizz +1, Honesty +3, Chaos +2, Wit +2, SA +3 (shadows all 0)
- OpponentStats: Charm +2, Rizz +3, Honesty +1, Chaos +2, Wit +1, SA +2 (shadows all 0)
- CurrentInterest: 12
- InterestState: Interested
- MomentumStreak: 2
- ActiveTrapNames: `[]`
- SessionHorniness: 4
- ShadowValues: null
- TurnNumber: 5

**DC calculations (for reference):**
- Charm attacks, opponent defends with SA: DC = 13 + 2 = 15. Player mod = +4. Need = 11. Success% = 50%
- Rizz attacks, opponent defends with Wit: DC = 13 + 1 = 14. Player mod = +1. Need = 13. Success% = 40%
- Honesty attacks, opponent defends with Chaos: DC = 13 + 2 = 15. Player mod = +3. Need = 12. Success% = 45%
- Chaos attacks, opponent defends with Charm: DC = 13 + 2 = 15. Player mod = +2. Need = 13. Success% = 40%

**Expected output — PlayerDecision:**

```
OptionIndex: 0
Reasoning: "Charm has highest success chance at 50%. Momentum streak at 2, one more success triggers +2 bonus."
Scores: [
  { OptionIndex: 0, Score: ~6.5,  SuccessChance: 50.0, ExpectedInterestGain: ~0.8, BonusesApplied: [] },
  { OptionIndex: 1, Score: ~3.2,  SuccessChance: 40.0, ExpectedInterestGain: ~0.3, BonusesApplied: [] },
  { OptionIndex: 2, Score: ~5.0,  SuccessChance: 45.0, ExpectedInterestGain: ~0.6, BonusesApplied: ["callback"] },
  { OptionIndex: 3, Score: ~5.8,  SuccessChance: 40.0, ExpectedInterestGain: ~0.5, BonusesApplied: ["tell +2", "combo"] }
]
```

(Exact score values depend on the scoring formula implementation in #347. The `~` prefix indicates approximate illustrative values.)

### Example 2: Bored state — prefer bold options

**Input — PlayerAgentContext (partial):**
- CurrentInterest: 3
- InterestState: Bored
- MomentumStreak: 0

**Expected behavior:** The agent should weight Bold/Hard risk-tier options higher because there is little to lose (already near Unmatched) and the risk tier bonus (+1/+2 interest) provides needed uplift.

### Example 3: AlmostThere — prefer safe options

**Input — PlayerAgentContext (partial):**
- CurrentInterest: 22
- InterestState: AlmostThere
- MomentumStreak: 4

**Expected behavior:** The agent should prefer options with highest success probability regardless of risk tier bonus, because a single success at +1 interest may be enough to reach DateSecured.

---

## Acceptance Criteria

### AC1: IPlayerAgent interface defined

The `IPlayerAgent` interface must be defined in `session-runner/` with the exact signature shown above. It must:
- Accept `TurnStart` (from `Pinder.Core.Conversation`) and `PlayerAgentContext` as parameters.
- Return `Task<PlayerDecision>`.
- Be a public interface so it can be referenced by any code in the session-runner project.

**Location:** Per vision concern #355, this interface lives in `session-runner/`, NOT `Pinder.Core/Interfaces/`. The issue body suggests `Pinder.Core/Interfaces/` but #355 overrides this.

### AC2: PlayerDecision and OptionScore types defined

Both must be `sealed class` types (not records — LangVersion 8.0) with:
- Read-only properties (get-only, set in constructor).
- Constructor validation: null checks on `Reasoning`, `Scores`, `BonusesApplied`.
- `PlayerDecision.Scores.Length` must equal the number of options in the `TurnStart` that produced it.
- `PlayerDecision.OptionIndex` must be in `[0, Scores.Length)`.

### AC3: PlayerAgentContext type defined

Must be a `sealed class` with:
- All properties shown above, with types exactly as specified.
- Constructor that validates non-null for `PlayerStats`, `OpponentStats`, `ActiveTrapNames`.
- `ShadowValues` is nullable (null when shadow tracking is not wired).

**Referenced types from Pinder.Core:**
- `Pinder.Core.Stats.StatBlock`
- `Pinder.Core.Stats.ShadowStatType`
- `Pinder.Core.Conversation.InterestState`

### AC4: Session runner updated to use IPlayerAgent

The `BestOption()` static method in `session-runner/Program.cs` must be replaced with a call to `IPlayerAgent.DecideAsync()`. The wiring looks like:

```csharp
var snapshot = turnStart.State;
var agentContext = new PlayerAgentContext(
    playerStats: sableStats,
    opponentStats: brickStats,
    currentInterest: snapshot.Interest,
    interestState: snapshot.State,
    momentumStreak: snapshot.MomentumStreak,
    activeTrapNames: snapshot.ActiveTrapNames,
    sessionHorniness: 0,
    shadowValues: null,
    turnNumber: snapshot.TurnNumber);

var decision = await agent.DecideAsync(turnStart, agentContext);
int pick = decision.OptionIndex;
```

Where `agent` is an `IPlayerAgent` instance created during session runner setup. For this issue, the concrete agent can be a minimal wrapper replicating the current `BestOption` logic (pick highest `context.PlayerStats.GetEffective(option.Stat)`) or the `ScoringPlayerAgent` from #347 if implemented together.

The `BestOption()` static method should be removed once the agent is wired.

### AC5: Build clean

The solution must compile with zero errors. All 1977+ existing tests must pass unchanged. No new NuGet packages.

---

## Edge Cases

### Zero options
If `TurnStart.Options` is empty (length 0), `DecideAsync` should throw `InvalidOperationException("No options available")`. This should never occur in normal gameplay but the agent must not crash with an index-out-of-range.

### Single option
If only one option is provided (e.g., all replaced by Horniness-forced Rizz), the agent must return `OptionIndex = 0`. Score computation still runs to populate reasoning and scores.

### All options have identical stats
When all four options use the same `StatType` (possible under Horniness T3 >= 18), all `OptionScore.SuccessChance` values will be identical. The agent should pick index 0 (deterministic tiebreak: lowest index wins).

### Null or missing shadow values
`PlayerAgentContext.ShadowValues` may be null. Agents must treat missing shadows as zero for any scoring that factors in shadow state.

### Extreme interest values
- `CurrentInterest = 0` (Unmatched): Game should already be over. Agent still returns a valid decision.
- `CurrentInterest = 25` (DateSecured): Same — agent still returns valid output.

### Momentum streak edge values
- `MomentumStreak = 0`: No momentum bonus.
- `MomentumStreak = 2`: Close to triggering +2 bonus. Strategic agents may favor high-success options.
- `MomentumStreak >= 5`: Maximum momentum bonus (+3).

### All bonuses stacked
An option can have `HasTellBonus = true`, `CallbackTurnNumber != null`, `ComboName != null`, and `HasWeaknessWindow = true` simultaneously. All bonuses stack.

### PlayerDecision construction validation
If `OptionIndex >= scores.Length` or `scores` is null, the constructor must throw immediately, catching agent bugs early.

---

## Error Conditions

| Condition | Expected behavior |
|---|---|
| `turn` is null | `DecideAsync` throws `ArgumentNullException("turn")` |
| `context` is null | `DecideAsync` throws `ArgumentNullException("context")` |
| `turn.Options` is empty (length 0) | `DecideAsync` throws `InvalidOperationException("No options available")` |
| `PlayerDecision` optionIndex < 0 or >= scores.Length | Constructor throws `ArgumentOutOfRangeException("optionIndex")` |
| `PlayerDecision` reasoning is null | Constructor throws `ArgumentNullException("reasoning")` |
| `PlayerDecision` scores is null | Constructor throws `ArgumentNullException("scores")` |
| `OptionScore` bonusesApplied is null | Constructor throws `ArgumentNullException("bonusesApplied")` |
| `PlayerAgentContext` playerStats is null | Constructor throws `ArgumentNullException("playerStats")` |
| `PlayerAgentContext` opponentStats is null | Constructor throws `ArgumentNullException("opponentStats")` |
| `PlayerAgentContext` activeTrapNames is null | Constructor throws `ArgumentNullException("activeTrapNames")` |

---

## Dependencies

### Pinder.Core types consumed (read-only — no changes to Core)

| Type | Namespace | Usage |
|---|---|---|
| `TurnStart` | `Pinder.Core.Conversation` | Input to `DecideAsync` |
| `DialogueOption` | `Pinder.Core.Conversation` | Via `TurnStart.Options` — provides `Stat`, `HasTellBonus`, `CallbackTurnNumber`, `ComboName`, `HasWeaknessWindow`, `IsUnhingedReplacement` |
| `GameStateSnapshot` | `Pinder.Core.Conversation` | Via `TurnStart.State` — provides `Interest`, `State`, `MomentumStreak`, `ActiveTrapNames`, `TurnNumber`, `TripleBonusActive` |
| `InterestState` | `Pinder.Core.Conversation` | Enum field on `PlayerAgentContext` |
| `StatBlock` | `Pinder.Core.Stats` | On `PlayerAgentContext` — provides `GetEffective(StatType)`, `GetDefenceDC(StatType)` |
| `StatType` | `Pinder.Core.Stats` | Enum — accessed via `DialogueOption.Stat` |
| `ShadowStatType` | `Pinder.Core.Stats` | Enum — key type for `ShadowValues` dictionary |

### Downstream consumers

| Consumer | Usage |
|---|---|
| `session-runner/Program.cs` | Creates agent, calls `DecideAsync` per turn, uses `OptionIndex` for `ResolveTurnAsync` |
| `ScoringPlayerAgent` (#347) | Implements `IPlayerAgent` with deterministic EV scoring |
| `LlmPlayerAgent` (#348) | Implements `IPlayerAgent` via Anthropic API + ScoringPlayerAgent fallback |
| Pick reasoning output (#351) | Reads `Reasoning` and `Scores` for markdown display |

### External dependencies

None. All types in `session-runner/` (net8.0) referencing `Pinder.Core` (netstandard2.0). No new NuGet packages.

---

## Notes for Implementers

1. **Location override:** Issue body says `Pinder.Core/Interfaces/`. Vision concern #355 overrides this — all player agent types go in `session-runner/`. Core gains zero new public types.

2. **LangVersion 8.0:** No C# 9+ features (records, init-only setters). Use `sealed class` with constructor + get-only properties.

3. **Temporary agent for AC4:** If `ScoringPlayerAgent` (#347) is not yet available, create a minimal agent replicating `BestOption` logic (pick highest `context.PlayerStats.GetEffective(option.Stat)`) wrapped in `IPlayerAgent`. This satisfies AC4 independently.

4. **Snapshot sourcing:** `GameStateSnapshot` comes from `TurnStart.State`. The session runner has `sableStats` and `brickStats` as local variables. `PlayerAgentContext` bridges both.

5. **Dictionary<ShadowStatType, int>:** `ShadowStatType` has 6 values. When shadow tracking is not wired (#350), pass `null`.

6. **Success probability formula:** `need = DC - modifier`, `successChance = max(0, min(100, (21 - need) * 5))`. Natural 20 always succeeds, natural 1 always fails — captured by the clamp.
