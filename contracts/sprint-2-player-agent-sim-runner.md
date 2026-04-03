# Contract: Sprint 2 — Player Agent + Sim Runner Fixes

## Architecture Overview

This sprint continues the existing architecture with no structural
changes to Pinder.Core. New components are added to `session-runner/`
only. Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine.
`GameSession` orchestrates single-conversation turns, delegating to
`RollEngine` (stateless), `InterestMeter`, `TrapState`,
`SessionShadowTracker`, `ComboTracker`, and `XpLedger`.
`Pinder.LlmAdapters` implements `ILlmAdapter` via
`AnthropicLlmAdapter`. The session runner (`session-runner/`) is a
.NET 8 console app that drives `GameSession` + `AnthropicLlmAdapter`
for automated playtesting.

**Bug fixes #349, #352, #353, #354 are merged.** Remaining open
issues: #346, #347, #348, #350, #351, #386.

Per **#355**: all player agent types live in `session-runner/`, NOT
in `Pinder.Core`. Core gains zero new public types this sprint.

---

## Separation of Concerns Map

- IPlayerAgent (session-runner)
  - Responsibility:
    - Define decision-making contract for sim agents
    - Carry decision result (index, reasoning, scores)
  - Interface:
    - IPlayerAgent.DecideAsync(TurnStart, PlayerAgentContext)
    - PlayerDecision (OptionIndex, Reasoning, Scores)
    - OptionScore (Score, SuccessChance, ExpectedGain, Bonuses)
    - PlayerAgentContext (stats, interest, momentum, etc.)
  - Must NOT know:
    - GameSession internals
    - LLM transport details
    - Roll resolution math (uses computed probabilities)

- ScoringPlayerAgent (session-runner)
  - Responsibility:
    - Score options using expected-value formula
    - Apply strategic adjustments (momentum, interest, traps)
    - Return deterministic decisions with full reasoning
    - Call CallbackBonus.Compute() for callback values (#386)
  - Interface:
    - Implements IPlayerAgent.DecideAsync
    - Pure function: same input → same output
  - Must NOT know:
    - LLM APIs
    - Session file output format
    - GameSession internal state

- LlmPlayerAgent (session-runner)
  - Responsibility:
    - Format game state into LLM prompt
    - Parse LLM pick response (PICK: A/B/C/D)
    - Fall back to ScoringPlayerAgent on failure
  - Interface:
    - Implements IPlayerAgent.DecideAsync
    - Constructor takes AnthropicOptions + ScoringPlayerAgent
  - Must NOT know:
    - Session file output format
    - GameSession internals
    - Roll resolution math (receives computed data)

- SessionRunner (session-runner/Program.cs)
  - Responsibility:
    - Wire up GameSession + IPlayerAgent + output
    - Format playtest markdown output
    - File counter and naming
    - Shadow tracking display
    - Pick reasoning display (#351)
  - Interface:
    - CLI entry point: `dotnet run` with env vars
    - Writes markdown to stdout + playtest directory
  - Must NOT know:
    - Roll resolution internals
    - LLM prompt assembly details
    - Interest meter internals

- GameSession (Pinder.Core)
  - Responsibility:
    - Orchestrate single-conversation turns
    - Shadow growth detection
  - Interface:
    - StartTurnAsync() → TurnStart
    - ResolveTurnAsync(int) → TurnResult
    - Unchanged public API
  - Must NOT know:
    - Player agent decision logic
    - Session file output
    - LLM transport

---

## Implicit Assumptions for Implementers

1. **netstandard2.0 + LangVersion 8.0** in Pinder.Core.
   Session runner is net8.0 but uses LangVersion 8.0 per csproj.
2. **Zero NuGet dependencies in Pinder.Core** — no new packages.
3. **`SessionShadowTracker` constructor takes `StatBlock`**, not
   `Dictionary` (#360). Use `new SessionShadowTracker(sableStats)`.
4. **All existing tests must pass** unchanged.
5. **Player agent types go in session-runner, not Pinder.Core** (#355).
6. **`CallbackBonus.Compute()` is public static** in
   `Pinder.Core.Conversation` — scoring agent must call it (#386).
7. **`GameSession.GetMomentumBonus()` is private static** —
   scoring agent duplicates with `// SYNC:` comment (#386).
8. **Tell bonus = literal 2** — no public constant exists.
   Hardcode with `// SYNC: GameSession ResolveTurnAsync tellBonus`.

---

## Per-Issue Interface Definitions

### #346 + #355 — IPlayerAgent Interface

**Status:** OPEN. Spec at `docs/specs/issue-346-spec.md`.

**Files created (all in `session-runner/`):**
- `session-runner/IPlayerAgent.cs`
- `session-runner/PlayerDecision.cs`
- `session-runner/OptionScore.cs`
- `session-runner/PlayerAgentContext.cs`

**IPlayerAgent contract:**
```csharp
// Namespace: up to implementer (suggested Pinder.SessionRunner)
public interface IPlayerAgent
{
    /// Given a TurnStart and additional context, return a decision.
    Task<PlayerDecision> DecideAsync(
        TurnStart turn,
        PlayerAgentContext context);
}
```

**PlayerDecision contract:**
```csharp
public sealed class PlayerDecision
{
    public int OptionIndex { get; }     // 0-based into TurnStart.Options
    public string Reasoning { get; }    // never null, may be empty
    public OptionScore[] Scores { get; } // one per option, never null

    public PlayerDecision(
        int optionIndex, string reasoning, OptionScore[] scores);
}
```

**Invariants:**
- `OptionIndex` in range `[0, Scores.Length)`
- `Scores.Length == TurnStart.Options.Length`
- `Reasoning` never null

**OptionScore contract:**
```csharp
public sealed class OptionScore
{
    public int OptionIndex { get; }
    public float Score { get; }                  // composite (higher = better)
    public float SuccessChance { get; }          // 0.0–1.0 probability
    public float ExpectedInterestGain { get; }   // raw EV
    public string[] BonusesApplied { get; }      // e.g. ["callback +2", "tell +2"]

    public OptionScore(
        int optionIndex, float score, float successChance,
        float expectedInterestGain, string[] bonusesApplied);
}
```

**PlayerAgentContext contract:**
```csharp
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

    public PlayerAgentContext(
        StatBlock playerStats, StatBlock opponentStats,
        int currentInterest, InterestState interestState,
        int momentumStreak, string[] activeTrapNames,
        int sessionHorniness,
        Dictionary<ShadowStatType, int>? shadowValues,
        int turnNumber);
}
```

**Session runner wiring (replaces `BestOption()`):**
```csharp
var agentContext = new PlayerAgentContext(
    playerStats: sableStats,
    opponentStats: brickStats,
    currentInterest: snap.Interest,
    interestState: snap.State,
    momentumStreak: snap.MomentumStreak,
    activeTrapNames: snap.ActiveTrapNames,
    sessionHorniness: 0,
    shadowValues: null,
    turnNumber: snap.TurnNumber);

var decision = await agent.DecideAsync(turnStart, agentContext);
int pick = decision.OptionIndex;
```

**Dependencies:**
- `Pinder.Core.Conversation.TurnStart`
- `Pinder.Core.Conversation.GameStateSnapshot`
- `Pinder.Core.Conversation.DialogueOption`
- `Pinder.Core.Conversation.InterestState`
- `Pinder.Core.Stats.StatBlock`
- `Pinder.Core.Stats.ShadowStatType`

---

### #347 + #386 — ScoringPlayerAgent

**Status:** OPEN. Spec at `docs/specs/issue-347-spec.md`.
**#386** modifies how bonus constants are sourced.

**File created:** `session-runner/ScoringPlayerAgent.cs`

**Contract:**
```csharp
public sealed class ScoringPlayerAgent : IPlayerAgent
{
    public Task<PlayerDecision> DecideAsync(
        TurnStart turn, PlayerAgentContext context);
}
```

**Scoring formula per option:**
```
attackerMod = context.PlayerStats.GetEffective(option.Stat)
defenceDC   = context.OpponentStats.GetDefenceDC(option.Stat)

// MUST call engine method (#386):
callbackBonus = option.CallbackTurnNumber.HasValue
    ? CallbackBonus.Compute(context.TurnNumber, option.CallbackTurnNumber.Value)
    : 0

// Duplicate with sync comment (#386):
// SYNC: GameSession.GetMomentumBonus()
momentumBonus = streak >= 5 ? 3 : streak >= 3 ? 2 : 0

// Hardcode with sync comment (#386):
// SYNC: GameSession ResolveTurnAsync tellBonus
tellBonus = option.HasTellBonus ? 2 : 0

need = defenceDC - (attackerMod + momentumBonus + tellBonus + callbackBonus)
successChance = clamp((21 - need) / 20.0, 0.0, 1.0)
failChance = 1.0 - successChance

expectedGain = successChance × (baseGain + riskTierBonus + comboBonus)
             - failChance × failCost
```

Where:
- `riskTierBonus`: 0 (need ≤ 10), +1 (need 11–15), +2 (need ≥ 16)
- `baseGain`: weighted average success interest (1 for Safe/Med, 2 for Hard, 3 for Bold)
- `comboBonus`: +1 if `option.ComboName != null`
- `failCost`: 1 (Fumble) to 3+ (TropeTrap+ with trap penalty estimate)

**Strategic adjustments:**
- momentum == 2: +1.0 bias to high-success options (reach streak)
- interest 19–24: prefer Safe/Medium (close the deal)
- interest 1–4 (Bored): prefer Bold (nothing to lose)
- active trap on stat: −2.0 penalty

**Determinism:** Same inputs MUST produce same outputs. No randomness.

**Dependencies:**
- `Pinder.Core.Conversation.CallbackBonus` (public static)
- `Pinder.Core.Conversation.TurnStart`
- `Pinder.Core.Stats.StatBlock`
- All #346 types

---

### #348 — LlmPlayerAgent

**Status:** OPEN.

**File created:** `session-runner/LlmPlayerAgent.cs`

**Contract:**
```csharp
public sealed class LlmPlayerAgent : IPlayerAgent
{
    private readonly AnthropicClient _client;
    private readonly ScoringPlayerAgent _fallback;

    public LlmPlayerAgent(
        AnthropicOptions options,
        ScoringPlayerAgent fallback);

    public async Task<PlayerDecision> DecideAsync(
        TurnStart turn, PlayerAgentContext context);
}
```

**Behavioral contract:**
- Builds prompt with full game state + all 4 options + rules reminder
- Sends to Anthropic via `AnthropicClient.SendMessagesAsync()`
- Parses `PICK: [A/B/C/D]` from response (case-insensitive)
- On ANY failure (API error, parse error, timeout):
  falls back to `_fallback.DecideAsync()`
- `Reasoning` = full LLM response text
- `Scores` = computed by ScoringPlayerAgent (for comparison table)

**Prompt includes:**
- Character identity
- Current state (interest, momentum, traps, shadows, turn)
- All 4 options with DC, %, risk tier, bonuses
- Condensed rules reminder (success/fail tiers, momentum, combos)
- Instruction: explain reasoning, then `PICK: [A/B/C/D]`

**Dependencies:**
- `Pinder.LlmAdapters.Anthropic.AnthropicClient`
- `Pinder.LlmAdapters.Anthropic.AnthropicOptions`
- `ScoringPlayerAgent` (fallback)
- All #346 types

---

### #350 + #360 — Shadow Tracking in Session Runner

**Status:** OPEN.

**Files changed:** `session-runner/Program.cs`

**Required change:**
```csharp
// SessionShadowTracker takes StatBlock, NOT Dictionary (#360)
var sableShadows = new SessionShadowTracker(sableStats);
var config = new GameSessionConfig(playerShadows: sableShadows);
var session = new GameSession(sable, brick, llm, dice, trapRegistry, config);
```

**Output contract — per turn (when shadow events fire):**
```
⚠️ SHADOW GROWTH: {event description from TurnResult.ShadowGrowthEvents}
```

**Output contract — session summary:**
```markdown
## Shadow Changes This Session
| Shadow | Start | End | Delta |
|---|---|---|---|
| Denial | 3 | 4 | +1 |
| Fixation | 2 | 2 | 0 |
```

**Behavioral contract:**
- Session runner holds reference to `sableShadows`
- Shadow events from `TurnResult.ShadowGrowthEvents` (already populated)
- Final shadow: `sableShadows.GetEffectiveShadow(type)` per type
- Starting shadow: `sableStats.GetShadow(type)` per type
- All `ShadowStatType` enum values should be iterated

**Dependencies:**
- `Pinder.Core.Stats.SessionShadowTracker`
- `Pinder.Core.Conversation.GameSessionConfig`
- `Pinder.Core.Stats.ShadowStatType`

---

### #351 — Pick Reasoning Output

**Status:** OPEN. Spec at `docs/specs/issue-351-spec.md`.

**Files changed:** `session-runner/Program.cs`

**Output contract — after the pick line:**
```markdown
**► Player picks: A (CHARM)**

**Player reasoning ({AgentTypeName}):**
> {decision.Reasoning — each line prefixed with > }

| Option | Stat | Pct | Expected ΔI | Score |
|---|---|---|---|---|
| A ✓ | CHARM | 45% | +1.8 | **8.3** |
| B | RIZZ | 5% | +0.9 | 1.2 |
| C | HONESTY | 30% | +1.4 | 6.1 |
| D | CHAOS | 0% | +0.0 | 0.0 |
```

**Behavioral contract:**
- ✓ marks the chosen option
- Chosen option's score is **bold**
- AgentTypeName = class name (e.g. "ScoringPlayerAgent")
- Reasoning is blockquoted (each line prefixed with `> `)
- Score table shows all scores from `PlayerDecision.Scores`
- SuccessChance displayed as percentage (multiply by 100)

**Dependencies:**
- `PlayerDecision` from #346
- `OptionScore` from #346
- `DialogueOption` from Pinder.Core

---

### #386 — ScoringPlayerAgent Bonus Constant Sync

**Status:** OPEN (vision concern).

**This is NOT a separate implementation issue** — it modifies #347's
implementation. The contract for #347 above already incorporates
#386's requirements:

1. `CallbackBonus.Compute()` called directly (not reimplemented)
2. Momentum bonus duplicated with `// SYNC:` comment
3. Tell bonus hardcoded `2` with `// SYNC:` comment

No separate deliverable — this is a constraint on #347's implementation.

---

## Implementation Strategy

### Wave 1 — No dependencies

1. **#346** — IPlayerAgent interface + supporting types
   - New files in session-runner/. No dependencies on other issues.
   - Test: types construct, invariants hold.

### Wave 2 — Depends on Wave 1

2. **#347 + #386** — ScoringPlayerAgent
   - Depends on #346 for IPlayerAgent.
   - Must call `CallbackBonus.Compute()` per #386.
   - Test: deterministic scoring, strategic adjustments.

3. **#350** — Shadow tracking in session runner
   - Independent of #346 but logically Wave 2.
   - Test: SessionShadowTracker wired, events display.

### Wave 3 — Depends on Wave 2

4. **#348** — LlmPlayerAgent
   - Depends on #346 + #347 for interface + fallback.
   - Test: prompt construction, parse logic, fallback behavior.

5. **#351** — Pick reasoning output
   - Depends on #346 for PlayerDecision type.
   - Can start after Wave 1 but full testing needs Wave 2/3.
   - Test: output format matches spec.

### Tradeoffs

- **Player agent in session-runner vs shared project:**
  Correct for prototype. Extract to `Pinder.Simulation` if
  multiple consumers emerge later.
- **EV formula approximates:** Discrete tier distribution
  is simplified. Monte Carlo would be more accurate.
  Good enough for prototype.
- **LlmPlayerAgent prompt omits conversation history:**
  Intentional — agent picks mechanically, not narratively.

### Risk Mitigation

- **LlmPlayerAgent API failure:** Falls back to ScoringPlayerAgent.
- **Shadow tracking breaks flow:** All behind null checks
  (`_playerShadows != null`). Zero risk.
- **Bonus constant drift (#386):** Mitigated by direct
  `CallbackBonus.Compute()` call + SYNC comments.

---

## NFR (Prototype Maturity)

| Component | Latency target |
|---|---|
| ScoringPlayerAgent.DecideAsync | < 1ms (pure math) |
| LlmPlayerAgent.DecideAsync | < 10s (API call + fallback) |
| SessionRunner file counter | < 100ms |
| GameSession.ResolveTurnAsync | unchanged |

---

## VERDICT: PROCEED

Architecture is solid. No structural changes to Pinder.Core.
All new types correctly scoped to session-runner.
Vision concerns (#355–#360, #386) are documented and addressed.
Bug fixes (#349, #352, #353, #354) already merged.
Implementation order is clear with minimal cross-dependencies.
