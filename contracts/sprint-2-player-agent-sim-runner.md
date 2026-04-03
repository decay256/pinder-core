# Contract: Sprint 2 — Player Agent + Sim Runner Fixes

## Architecture Overview

This sprint introduces the **player agent abstraction** and fixes bugs
in the session runner and core game logic. The existing architecture
is unchanged — Pinder.Core remains a zero-dependency .NET Standard 2.0
RPG engine. `GameSession` orchestrates single-conversation turns,
delegating to `RollEngine`, `InterestMeter`, `TrapState`,
`SessionShadowTracker`, `ComboTracker`, and `XpLedger`. The session
runner (`session-runner/`) is a .NET 8 console app that depends on
both `Pinder.Core` and `Pinder.LlmAdapters`.

**New components** are confined to `session-runner/`:
- `IPlayerAgent` interface + supporting types
- `ScoringPlayerAgent` (deterministic, math-only)
- `LlmPlayerAgent` (Anthropic-backed, with fallback)

**Bug fixes** touch:
- `session-runner/Program.cs` — file counter, trap registry, shadow
  tracking, reasoning output
- `Pinder.Core/Conversation/GameSession.cs` — Fixation probability
- `Pinder.Core/Conversation/InterestChangeContext.cs` — opponent prompt
- `Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` — beat voice

Per **#355**: all player agent types live in `session-runner/`, NOT
in `Pinder.Core`. Core gains zero new public types.

### Components being extended

- `session-runner/Program.cs` — 5 issues (#354, #353, #350, #351, #348)
- `Pinder.Core/Conversation/GameSession.cs` — 1 issue (#349)
- `Pinder.Core/Conversation/InterestChangeContext.cs` — 1 issue (#352)
- `Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` — 1 issue (#352)

### Implicit assumptions for implementers

1. **netstandard2.0 + LangVersion 8.0** in Pinder.Core. Session runner
   is net8.0 and may use modern C# features (records, top-level, etc.)
   but should stay at LangVersion 8.0 per project file.
2. **Zero NuGet deps in Pinder.Core** — no new packages.
3. **`JsonTrapRepository` takes a JSON string**, not a file path (#356).
   Use `File.ReadAllText(path)` then pass to constructor.
4. **`SessionShadowTracker` takes `StatBlock`**, not `Dictionary` (#360).
   Use `new SessionShadowTracker(sableStats)`.
5. **All 1977 existing tests must pass** unchanged.
6. **Context DTO changes use optional params with defaults** (#357).
7. **File counter glob must be `session-*.md`** not `session-???.md` (#359).

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
    - Shadow growth detection (including Fixation fix)
  - Interface:
    - StartTurnAsync() → TurnStart
    - ResolveTurnAsync(int) → TurnResult
    - Unchanged public API
  - Must NOT know:
    - Player agent decision logic
    - Session file output
    - LLM transport

- InterestChangeContext (Pinder.Core)
  - Responsibility:
    - Carry context for interest threshold beats
  - Interface:
    - Constructor gains optional `opponentPrompt`
    - Backward-compatible (null default)
  - Must NOT know:
    - LLM prompt assembly
    - How the beat is generated

- AnthropicLlmAdapter (Pinder.LlmAdapters)
  - Responsibility:
    - Use opponent prompt in interest beat generation
  - Interface:
    - GetInterestChangeBeatAsync reads OpponentPrompt
    - Includes opponent system blocks when available
  - Must NOT know:
    - GameSession state
    - Player agent decisions

---

## Per-Issue Interface Definitions

### #354 + #359 — File counter fix

**Files changed:** `session-runner/Program.cs` (WritePlaytestLog method)

**Current (broken):**
```csharp
foreach (var f in Directory.GetFiles(dir, "session-???.md"))
{
    var n = Path.GetFileNameWithoutExtension(f);
    if (n.Length >= 11 && int.TryParse(n.Substring(8, 3), out int num))
        nextNum = Math.Max(nextNum, num + 1);
}
```

**Required change:**
```csharp
foreach (var f in Directory.GetFiles(dir, "session-*.md"))
{
    var name = Path.GetFileNameWithoutExtension(f);
    var parts = name.Split('-');
    if (parts.Length >= 2 && int.TryParse(parts[1], out int num))
        nextNum = Math.Max(nextNum, num + 1);
}
```

**Behavioral contract:**
- Pre: directory may contain `session-NNN-name-vs-name.md` files
- Post: `nextNum` = max(existing numbers) + 1
- Post: next file slug = `session-{nextNum:D3}-{p1}-vs-{p2}.md`
- Edge: empty directory → nextNum = 1
- Edge: non-numeric parts after `session-` → skip gracefully

---

### #353 + #356 — Real trap registry

**Files changed:** `session-runner/Program.cs`

**Current (broken):**
```csharp
class NullTrapRegistry : ITrapRegistry { ... }
var session = new GameSession(sable, brick, llm, dice, new NullTrapRegistry());
```

**Required change:**
```csharp
// Remove NullTrapRegistry class
string trapsJson = File.ReadAllText(trapsJsonPath);
ITrapRegistry trapRegistry = new JsonTrapRepository(trapsJson);
var session = new GameSession(sable, brick, llm, dice, trapRegistry);
```

**Behavioral contract:**
- `JsonTrapRepository(string json)` — takes JSON content, NOT path
- If file not found: print warning to stderr, fall back to
  inline `NullTrapRegistry` (keep as private fallback)
- Traps path: configurable via constant or env var
  (default: `/root/.openclaw/agents-extra/pinder/data/traps/traps.json`)

---

### #352 + #357 — Interest beat character voice

**Files changed:**
- `src/Pinder.Core/Conversation/InterestChangeContext.cs`
- `src/Pinder.Core/Conversation/GameSession.cs` (where context is built)
- `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs`

**InterestChangeContext contract:**
```csharp
public sealed class InterestChangeContext
{
    public string OpponentName { get; }
    public int InterestBefore { get; }
    public int InterestAfter { get; }
    public InterestState NewState { get; }
    public string? OpponentPrompt { get; }  // NEW — optional

    public InterestChangeContext(
        string opponentName,
        int interestBefore,
        int interestAfter,
        InterestState newState,
        string? opponentPrompt = null)  // NEW — backward-compatible
    {
        ...
        OpponentPrompt = opponentPrompt;
    }
}
```

**GameSession wiring:** Where `InterestChangeContext` is constructed,
pass `_opponent.AssembledSystemPrompt`.

**AnthropicLlmAdapter contract:**
```
GetInterestChangeBeatAsync(InterestChangeContext context):
  IF context.OpponentPrompt is not null:
    Build system blocks with opponent prompt (cached)
    Include in request
  ELSE:
    Use empty system blocks (existing behavior)
```

---

### #349 — Fixation probability fix

**Files changed:** `src/Pinder.Core/Conversation/GameSession.cs`

**Current (broken):**
```csharp
_highestPctOptionPicked.Add(optionIndex == 0);  // assumes idx 0 is highest
```

**Required change:**
```csharp
bool isHighestPct = IsHighestProbabilityOption(
    chosenOption, _currentOptions, _opponent.Stats);
_highestPctOptionPicked.Add(isHighestPct);
```

**Helper contract:**
```csharp
/// <summary>
/// Returns true if chosenOption has the highest (or tied for highest)
/// success probability among all options.
/// Probability = max(0, min(100, (21 - need) * 5))
/// where need = opponent.GetDefenceDC(opt.Stat) - player.GetEffective(opt.Stat)
/// </summary>
private bool IsHighestProbabilityOption(
    DialogueOption chosen,
    DialogueOption[] allOptions,
    StatBlock opponentStats)
```

**Behavioral contract:**
- Probability computed from base stat + DC only (no external bonuses)
- Ties: if chosen option ties for max, it counts as "highest"
- All options have 0% probability: all count as highest (edge case)
- Uses `_player.Stats.GetEffective(stat)` for attacker modifier
- Uses `opponentStats.GetDefenceDC(stat)` for DC

---

### #346 + #355 — IPlayerAgent interface

**Files created (all in `session-runner/`):**
- `session-runner/IPlayerAgent.cs`
- `session-runner/PlayerDecision.cs`
- `session-runner/OptionScore.cs`
- `session-runner/PlayerAgentContext.cs`

**IPlayerAgent contract:**
```csharp
public interface IPlayerAgent
{
    Task<PlayerDecision> DecideAsync(
        TurnStart turn,
        PlayerAgentContext context);
}
```

**PlayerDecision contract:**
```csharp
public sealed class PlayerDecision
{
    public int OptionIndex { get; }
    public string Reasoning { get; }
    public OptionScore[] Scores { get; }

    public PlayerDecision(
        int optionIndex,
        string reasoning,
        OptionScore[] scores) { ... }
}
```

**OptionScore contract:**
```csharp
public sealed class OptionScore
{
    public int OptionIndex { get; }
    public float Score { get; }
    public float SuccessChance { get; }
    public float ExpectedInterestGain { get; }
    public string[] BonusesApplied { get; }

    public OptionScore(
        int optionIndex,
        float score,
        float successChance,
        float expectedInterestGain,
        string[] bonusesApplied) { ... }
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
}
```

**Session runner wiring:** Replace `BestOption()` call with:
```csharp
var agentContext = new PlayerAgentContext(
    playerStats: sableStats,
    opponentStats: brickStats,
    currentInterest: snap.Interest,
    interestState: snap.State,
    momentumStreak: snap.MomentumStreak,
    activeTrapNames: snap.ActiveTrapNames,
    sessionHorniness: 0,  // from shadow tracker if available
    shadowValues: null,    // from shadow tracker if available
    turnNumber: snap.TurnNumber);

var decision = await agent.DecideAsync(turnStart, agentContext);
int pick = decision.OptionIndex;
```

---

### #347 — ScoringPlayerAgent

**File created:** `session-runner/ScoringPlayerAgent.cs`

**Contract:**
```csharp
public sealed class ScoringPlayerAgent : IPlayerAgent
{
    public Task<PlayerDecision> DecideAsync(
        TurnStart turn,
        PlayerAgentContext context);
}
```

**Scoring formula per option:**
```
need = opponentDC - (statMod + momentumBonus + tellBonus + callbackBonus)
successChance = clamp((21 - need) / 20.0, 0, 1)
failChance = 1 - successChance

expectedGain = successChance × (baseGain + riskTierBonus + comboBonusInterest)
             - failChance × failCost

where:
  momentumBonus = momentum >= 3 ? (momentum >= 5 ? 3 : 2) : 0
  tellBonus = opt.HasTellBonus ? 2 : 0
  callbackBonus = opt.CallbackTurnNumber.HasValue
    ? CallbackBonus.Compute(turn, callbackTurn) : 0
  baseGain = 1 (Safe/Medium), 2 (Hard), 3 (Bold) — weighted avg
  riskTierBonus = 0 (Safe/Medium), 1 (Hard), 2 (Bold)
  failCost = 1 (Fumble-ish) to 3+ (TropeTrap+)
  comboBonusInterest = opt.ComboName != null ? 1 : 0
```

**Strategic adjustments:**
- momentum == 2: +1.0 bias toward high-success options
- interest 19-24: prefer Safe/Medium (close the deal)
- interest 1-4 (Bored): prefer Bold (nothing to lose)
- active trap on stat: -2.0 penalty

**Determinism:** Same inputs MUST produce same outputs. No randomness.

---

### #348 — LlmPlayerAgent

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
        TurnStart turn,
        PlayerAgentContext context);
}
```

**Behavioral contract:**
- Builds a prompt with full game state and option details
- Sends to Anthropic via AnthropicClient
- Parses `PICK: [A/B/C/D]` from response (case-insensitive)
- On ANY failure (API error, parse error, timeout):
  falls back to `_fallback.DecideAsync()`
- Reasoning = full LLM response text
- Scores = computed by ScoringPlayerAgent (for comparison)

**LLM prompt includes:**
- Character identity (name, level)
- Current state (interest, momentum, traps, shadows)
- All 4 options with DC, %, risk tier, bonuses
- Condensed rules reminder
- Instruction: "Explain your reasoning, then state PICK: [A/B/C/D]"

**Dependencies:**
- `AnthropicClient` (from Pinder.LlmAdapters) — for HTTP transport
- `ScoringPlayerAgent` — for fallback + score comparison

---

### #350 + #360 — Shadow tracking in session runner

**Files changed:** `session-runner/Program.cs`

**Required change:**
```csharp
var sableShadows = new SessionShadowTracker(sableStats);
var config = new GameSessionConfig(playerShadows: sableShadows);
var session = new GameSession(sable, brick, llm, dice, trapRegistry, config);
```

**Output contract — per turn (when shadow events fire):**
```
⚠️ SHADOW GROWTH: {event description}
```

**Output contract — session summary:**
```markdown
## Shadow Changes This Session
| Shadow | Start | End | Delta |
|---|---|---|---|
| Denial | 3 | 4 | +1 |
| Fixation | 2 | 2 | 0 |
| ... | ... | ... | ... |
```

**Behavioral contract:**
- Session runner holds reference to `sableShadows` for end-of-session delta
- Shadow events come from `TurnResult.ShadowGrowthEvents`
- Final shadow = `sableShadows.GetEffectiveShadow(type)` for each type
- Starting shadow = `sableStats.GetShadow(type)` for each type

---

### #351 — Pick reasoning output

**Files changed:** `session-runner/Program.cs`

**Output contract — after pick line:**
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
- AgentTypeName = class name (e.g., "ScoringPlayerAgent")
- Reasoning is word-wrapped at ~76 chars for readability
- Score table shows all scores from `PlayerDecision.Scores`

---

## Implementation Strategy

### Wave 1 — No dependencies, can be parallel

1. **#354 + #359** — File counter fix (glob + parsing)
   - Trivial. ~15 lines changed in WritePlaytestLog.
   - Test: create temp dir with session files, verify counter.

2. **#349** — Fixation probability fix
   - Self-contained in GameSession.cs.
   - Add `IsHighestProbabilityOption()` private method.
   - Test: mock 4 options with known stats, verify tracking.

3. **#352 + #357** — Interest beat voice
   - Touches Core DTO + LlmAdapters + GameSession wiring.
   - Test: verify optional param default, verify adapter uses prompt.

### Wave 2 — Sequential, depends on Wave 1

4. **#353 + #356** — Real trap registry
   - Changes session runner setup. Must read traps.json.
   - Test: verify JsonTrapRepository loads, fallback works.

5. **#346 + #355** — IPlayerAgent interface + types
   - New files in session-runner/.
   - No dependencies on other issues.
   - Test: interface compiles, types construct correctly.

6. **#350 + #360** — Shadow tracking
   - Needs trap registry (#353) for realistic shadow triggers.
   - Test: verify SessionShadowTracker wired, events display.

### Wave 3 — Sequential, depends on Wave 2

7. **#347** — ScoringPlayerAgent
   - Depends on #346 for IPlayerAgent.
   - Test: deterministic scoring, strategic adjustments.

8. **#348** — LlmPlayerAgent
   - Depends on #346 + #347 for interface + fallback.
   - Test: prompt construction, parse logic, fallback behavior.

9. **#351** — Reasoning output
   - Depends on #347 + #348 for PlayerDecision data.
   - Test: output format matches spec.

### Tradeoffs

- **Player agent in session-runner vs Pinder.Simulation project:**
  Keeping in session-runner is correct for prototype. If we need
  multiple consumers, extract later.
- **ScoringPlayerAgent formula approximates EV:** The actual
  interest delta distribution is discrete (nat 20, success tiers,
  failure tiers). A Monte Carlo sim would be more accurate. EV
  formula is good enough for prototype.
- **LlmPlayerAgent prompt doesn't include conversation history:**
  It only sees current options + state. This is intentional —
  the agent picks mechanically, not narratively.

### Risk mitigation

- **LlmPlayerAgent API failure:** Falls back to ScoringPlayerAgent.
  No single point of failure.
- **traps.json missing:** Session runner prints warning, falls back
  to NullTrapRegistry (degraded but functional).
- **Shadow tracking breaks existing flow:** All shadow changes are
  behind null checks (`_playerShadows != null`). No risk.

---

## Sprint Plan Changes

**SPRINT PLAN CHANGES:**

The sprint issues are well-structured. Two adjustments needed:

1. **#354 must include #359's glob fix** — they are the same bug.
   The implementer must fix both the glob pattern AND the parsing.
   No new issue needed; #359 is already created as a concern.

2. **#350 must follow #360's constructor guidance** — already
   documented. No new issue needed.

3. **#346 must follow #355's location guidance** — types go in
   session-runner, not Pinder.Core. Already documented.

4. **#353 must follow #356's constructor guidance** — use
   `File.ReadAllText()` then pass string. Already documented.

No issues need to be dropped, split, or reordered.

---

## NFR (Prototype Maturity)

| Component | Latency target |
|---|---|
| ScoringPlayerAgent.DecideAsync | < 1ms (pure math) |
| LlmPlayerAgent.DecideAsync | < 10s (API call) |
| File counter (WritePlaytestLog) | < 100ms |
| GameSession.ResolveTurnAsync | unchanged |

---

## VERDICT: PROCEED

Architecture is solid. No structural changes to Pinder.Core.
All new types are correctly scoped to session-runner.
Vision concerns (#355-#360) are well-documented and implementable.
Implementation order is clear with minimal cross-dependencies.
