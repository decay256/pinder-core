# Architecture Vision Review — Sprint 2 (Player Agent + Sim Runner Fixes)

## Alignment: ✅

The architect's output is well-structured and strategically sound for prototype maturity. The sprint correctly prioritizes **automated playtesting infrastructure** (player agents) and **correctness fixes** (fixation, traps, file counter, beat voice) — both are high-leverage work that directly enables game balance iteration. The architecture makes no structural changes to Pinder.Core, correctly confines new abstractions to session-runner, and integrates all 5 vision concerns from the first pass.

## Maturity Fit Assessment

**Appropriate for prototype.** Key signals:
- IPlayerAgent is a simple `Task<PlayerDecision> DecideAsync(TurnStart, PlayerAgentContext)` — minimal surface area, easy to refactor
- ScoringPlayerAgent uses an EV approximation rather than Monte Carlo — acknowledged as "good enough for prototype" in the tradeoffs section
- LlmPlayerAgent falls back to ScoringPlayerAgent on any failure — correct resilience pattern for a tool that's not user-facing
- No new abstractions leak into Pinder.Core (per #355)

**No over-engineering detected.** The agent types are plain classes with no framework, no plugin system, no configuration DSL. This is exactly right.

## Coupling Analysis

### ✅ No problematic coupling introduced

1. **session-runner → Pinder.Core**: One-way. Agent types depend on `TurnStart`, `GameStateSnapshot`, `StatBlock`, `InterestState` — all stable public types. No internal access needed.
2. **session-runner → Pinder.LlmAdapters**: Only `LlmPlayerAgent` depends on `AnthropicClient` + `AnthropicOptions`. This is acceptable — both live outside Core.
3. **Pinder.Core changes are minimal**: `InterestChangeContext` gains an optional param (backward-compatible). `GameSession` gains a private helper method for Fixation. No new public API.

### ⚠️ PlayerAgentContext duplicates GameStateSnapshot

The architect's contract defines `PlayerAgentContext` with fields that largely overlap `GameStateSnapshot` (interest, state, momentum, traps, turn number) plus `StatBlock` references. The first-pass vision review flagged this. The architecture doesn't address whether `PlayerAgentContext` should simply wrap or reference `GameStateSnapshot` + stat blocks rather than flattening. This is **not blocking** at prototype — it's a convenience type that's easy to refactor — but should be noted.

## Abstraction Reversibility

All new abstractions live in `session-runner/`, which is a standalone console app. If the player agent abstraction proves wrong:
- Deleting the files and reverting to inline logic costs ~30 minutes
- No Core types need to change
- No other consumers exist

**Risk: LOW.** This is the right place to experiment at prototype maturity.

## Interface Design Evaluation

### ✅ IPlayerAgent surface is correct
- Takes `TurnStart` (the engine's output) + context → returns decision
- Does not expose GameSession internals
- Does not require the agent to understand roll math (receives computed probabilities)

### ✅ ScoringPlayerAgent formula is sound for prototype
- EV approximation captures the key tradeoffs (risk vs. reward, momentum, traps)
- Strategic adjustments (Bored → Bold bias, AlmostThere → Safe bias) model rational play
- Determinism guarantee enables reproducible test runs

### ✅ LlmPlayerAgent design is correct
- Fallback to deterministic agent on failure
- Prompt includes state but not conversation history (intentional — mechanical pick, not narrative)
- Uses existing `AnthropicClient` rather than rolling its own HTTP

## Data Flow Trace Verification

### Player Agent Decision Flow
```
GameSession.StartTurnAsync() → TurnStart(options[], snapshot)
  → Program builds PlayerAgentContext from snapshot + stat blocks
  → IPlayerAgent.DecideAsync(turnStart, context) → PlayerDecision
  → Program calls GameSession.ResolveTurnAsync(decision.OptionIndex) → TurnResult
  → Program displays reasoning + scores from PlayerDecision
```
**Fields flow correctly.** `TurnStart.State` (GameStateSnapshot) contains Interest, InterestState, MomentumStreak, ActiveTrapNames, TurnNumber. `PlayerAgentContext` adds StatBlock references and SessionHorniness. No missing fields for the scoring formula.

### Shadow Tracking Flow
```
Program creates SessionShadowTracker(sableStats)
  → passes via GameSessionConfig(playerShadows: tracker)
  → GameSession stores as _playerShadows, calls ApplyGrowth/ApplyOffset during turns
  → TurnResult.ShadowGrowthEvents populated from DrainGrowthEvents()
  → Program reads events for per-turn display
  → Program reads tracker.GetEffectiveShadow() at session end for delta table
```
**Fields flow correctly.** The architect correctly identified the constructor signature issue (#360) and documented the fix.

### Interest Beat Voice Flow
```
GameSession detects interest threshold crossing
  → builds InterestChangeContext(name, before, after, state, opponentPrompt: _opponent.AssembledSystemPrompt)
  → ILlmAdapter.GetInterestChangeBeatAsync(context)
  → AnthropicLlmAdapter reads context.OpponentPrompt
  → if non-null: builds system blocks with opponent character prompt (cached)
  → generates beat text in character voice
```
**⚠️ Minor gap:** The architect's contract says GameSession passes `_opponent.AssembledSystemPrompt` but doesn't specify what this field is or whether it exists on `CharacterProfile`. Looking at the codebase, `CharacterProfile` has no `AssembledSystemPrompt` property — the prompt is built by `PromptBuilder.BuildSystemPrompt()`. The implementer will need to determine the correct source. This is **not blocking** — it's an implementation detail the backend engineer can resolve.

## Gaps

- **None blocking.** The architect covered all vision concerns, provided correct constructor signatures, documented the wave ordering, and identified tradeoffs.
- **Minor**: `PlayerAgentContext.SessionHorniness` is initialized to `0` in the wiring example, with a comment "from shadow tracker if available." The architect should specify: horniness comes from `IGameClock.GetHorninessModifier()` + shadow-derived base, but session-runner may not have a game clock. Defaulting to 0 is fine for prototype.
- **Minor**: The `_opponent.AssembledSystemPrompt` reference in #352 wiring needs clarification (see above).

## Roadmap Alignment

The sprint correctly advances toward the product's goal of a **playable, testable RPG engine**:
1. Player agents enable automated balance testing (critical before Unity integration)
2. Bug fixes ensure the simulation produces valid game data
3. Shadow tracking output makes balance visible to designers
4. No premature Unity coupling — everything stays in the console runner

## Recommendations

1. **Implementer of #352**: Verify how to obtain the opponent's system prompt string. It's likely `PromptBuilder.BuildSystemPrompt(opponent.Fragments, ...)` — check the existing session runner code for how the opponent prompt is constructed during `GameSession` creation.
2. **No new arch-concern issues needed.** The existing #87 (GameSession god object) continues to apply but is not worsened by this sprint — the only GameSession change is a private helper method for Fixation.

## Requirements Compliance

No `REQUIREMENTS.md` exists in the repo. All changes are backward-compatible. Zero-dependency constraint on Pinder.Core is maintained.

---

**VERDICT: CLEAN** — Architecture aligns with product vision. Proceed with implementation.
