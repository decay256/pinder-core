# Session Runner

## Overview
The session runner orchestrates simulated playtest sessions between two `CharacterProfile` instances via `GameSession`. It handles turn execution, output formatting (interest bars, trap status, shadow events), and session summary generation including outcome and shadow delta tables.

## Key Components

- **`session-runner/Program.cs`** — Entry point. Builds character profiles, configures `GameSession` with `GameSessionConfig`, runs the turn loop, and prints per-turn status and session summary markdown. Calls `PlaytestFormatter` to render pick reasoning and score tables.
- **`session-runner/PlaytestFormatter.cs`** — Static utility class for formatting player agent reasoning blocks and option score tables as markdown. Contains `FormatReasoningBlock` and `FormatScoreTable`.
- **`session-runner/LlmPlayerAgent.cs`** — LLM-backed player agent. Sends game state and rules to Anthropic Claude, parses `PICK:` response, falls back to `ScoringPlayerAgent` on failure. Implements `IDisposable`.
- **`tests/Pinder.Core.Tests/LlmPlayerAgentTests.cs`** — Tests for `LlmPlayerAgent`: adjusted probability display (tell/momentum/callback bonuses), no-bonus raw percentage, dispose idempotency.
- **`tests/Pinder.Core.Tests/Issue350_ShadowTrackingSpecTests.cs`** — Spec tests verifying shadow tracking wiring: `SessionShadowTracker` wraps `StatBlock`, `GameSessionConfig` passes player shadows into `GameSession`, delta accumulation, edge cases (negative deltas, multiple events per turn, game-end readability).
- **`tests/Pinder.Core.Tests/Issue351_PickReasoningTests.cs`** — Tests for `PlaytestFormatter`: reasoning block formatting, score table columns/checkmarks/bold/bonuses, edge cases (null decision, NaN values, empty reasoning, score mismatch, fewer options).

## API / Public Interface

The session runner is a console application (`Program.cs`), not a library. It consumes the following public APIs:

### Session Setup
```csharp
var sableShadows = new SessionShadowTracker(sableStats);   // wraps player's StatBlock
var config = new GameSessionConfig(playerShadows: sableShadows);
var session = new GameSession(player, opponent, llm, dice, traps, config);
```

### Per-Turn Shadow Output
After each `ResolveTurnAsync()`, if `TurnResult.ShadowGrowthEvents.Count > 0`, each event is printed as:
```
⚠️ SHADOW GROWTH: {event}
```
Lines appear inside the post-roll status block, after the interest bar and before the "Active Traps" line.

### Session Summary — Shadow Delta Table
After the session outcome line, a markdown table is printed:
```markdown
## Shadow Changes This Session
| Shadow | Start | End | Delta |
|---|---|---|---|
| Madness | 0 | 0 | 0 |
| Horniness | 0 | 0 | 0 |
| Denial | 3 | 4 | +1 |
| Fixation | 2 | 2 | 0 |
| Dread | 0 | 0 | 0 |
| Overthinking | 0 | 0 | 0 |
```
- **Start**: `statBlock.GetShadow(type)` (base value)
- **End**: `shadowTracker.GetEffectiveShadow(type)` (base + session delta)
- **Delta**: `shadowTracker.GetDelta(type)` — `+N` for positive, `-N` for negative, `0` for zero
- All six `ShadowStatType` values are always listed.

### PlaytestFormatter (static class)

Formats player agent decision output for playtest markdown.

```csharp
/// Formats reasoning as a markdown blockquote. Returns "" if decision is null.
/// Empty/null reasoning renders as "> (no reasoning provided)".
public static string FormatReasoningBlock(PlayerDecision? decision, string agentTypeName);

/// Formats option scores as a markdown table with columns: Option, Stat, Pct, Expected ΔI, Score.
/// Chosen option row is marked with ✓ and bold score. Returns "" if decision is null.
/// If Scores is null, skips table and writes warning to stderr.
/// NaN/negative SuccessChance → 0%. NaN/negative Score → 0.0.
/// Missing score entries for an option render as "—".
/// BonusesApplied are concatenated without spaces (e.g. "📖🔗").
public static string FormatScoreTable(PlayerDecision? decision, DialogueOption[] options);
```

### Key Consumed Types
| Type | Namespace | Role |
|------|-----------|------|
| `SessionShadowTracker` | `Pinder.Core.Stats` | Wraps `StatBlock`, tracks per-session shadow deltas |
| `GameSessionConfig` | `Pinder.Core.Conversation` | Carries optional `PlayerShadows`, `OpponentShadows`, clock, etc. |
| `GameSession` | `Pinder.Core.Conversation` | Core turn-based session engine |
| `TurnResult.ShadowGrowthEvents` | `Pinder.Core.Conversation` | `IReadOnlyList<string>` populated when `PlayerShadows` is non-null |
| `ShadowStatType` | `Pinder.Core.Stats` | Enum: Madness, Horniness, Denial, Fixation, Dread, Overthinking |

## Architecture Notes

- **Shadow tracking is opt-in**: passing `GameSessionConfig` with a `SessionShadowTracker` enables shadow growth events. Without config (or with `playerShadows: null`), `TurnResult.ShadowGrowthEvents` is empty and no shadow output is produced.
- **Retained reference pattern**: the session runner keeps a reference to the `SessionShadowTracker` it passes into `GameSessionConfig`. After the game loop exits (normally or via `GameEndedException`), it reads delta/effective values from that same reference for the summary table.
- **`OpponentShadows` is optional**: the config only wires `playerShadows`; opponent shadow tracking is not used by the session runner currently.
- **Shadow growth triggers** (e.g., Nat 1 → Madness, 3 consecutive same-stat picks → Fixation) are handled inside `GameSession`; the session runner only reads and displays results.

## Player Agents

The session runner supports pluggable player agent strategies via the `IPlayerAgent` interface.

### IPlayerAgent (interface)
```csharp
Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
```
Takes a `TurnStart` (options + game state) and `PlayerAgentContext` (stats, interest, momentum, shadows), returns a `PlayerDecision` with chosen option index, reasoning text, and per-option score breakdowns.

### ScoringPlayerAgent
Deterministic expected-value scoring agent. Pure math, no LLM. Scores all options using success probability × expected gain − failure cost. Applies strategic adjustments for momentum, interest state, trap exposure. Used as the fallback for `LlmPlayerAgent` and for regression testing.

### LlmPlayerAgent
LLM-backed agent that sends full game state and rules context to Anthropic Claude, parses a `PICK: [A/B/C/D]` response. Falls back to `ScoringPlayerAgent` on any failure (API error, parse error, timeout). Implements `IDisposable` to clean up its internal `AnthropicClient`.

**Constructor:** `LlmPlayerAgent(AnthropicOptions, ScoringPlayerAgent, playerName?, opponentName?)`

**Prompt includes:** game state (interest, momentum, traps, shadows, turn), all options with stat/DC/need/%/risk tier/bonus icons, adjusted probabilities including hidden bonuses (momentum, tell, callback), and a rules reminder.

**Agent selection:** controlled via `PLAYER_AGENT` env var (`scoring` or `llm`, default `scoring`). LLM model configurable via `PLAYER_AGENT_MODEL` env var.

### Supporting Types
- **`PlayerDecision`** — result type: `OptionIndex`, `Reasoning` (string), `Scores` (OptionScore[])
- **`OptionScore`** — per-option breakdown: `Score`, `SuccessChance`, `ExpectedInterestGain`, `BonusesApplied`
- **`PlayerAgentContext`** — input context: player/opponent stats, interest, momentum, traps, shadows, turn number

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #348 | Added `LlmPlayerAgent` with Anthropic integration, adjusted probability display in prompt (base vs adjusted % when hidden bonuses apply), `IDisposable` disposal in Program.cs, player agent docs. Added tests for adjusted probability display, callback bonus display, and dispose idempotency. |
| 2026-04-04 | #350 | Initial creation — wired `SessionShadowTracker` into `GameSession` via `GameSessionConfig`, added per-turn shadow growth event output and session-end shadow delta summary table. Added spec tests in `Issue350_ShadowTrackingSpecTests.cs`. |
| 2026-04-04 | #351 | Added `PlaytestFormatter` static class with `FormatReasoningBlock` and `FormatScoreTable` methods. After each pick, playtest output now shows the agent's reasoning as a blockquote and a score table with all options' metrics. Defensive handling for null decisions, NaN values, missing scores. Tests in `Issue351_PickReasoningTests.cs`. |
