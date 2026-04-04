# Session Runner

## Overview
The session runner orchestrates simulated playtest sessions between two `CharacterProfile` instances via `GameSession`. It handles turn execution, output formatting (interest bars, trap status, shadow events), and session summary generation including outcome and shadow delta tables.

## Key Components

- **`session-runner/Program.cs`** — Entry point. Builds character profiles, configures `GameSession` with `GameSessionConfig`, runs the turn loop, and prints per-turn status and session summary markdown. Calls `PlaytestFormatter` to render pick reasoning and score tables. Uses `SessionFileCounter.ResolvePlaytestDirectory()` to locate the playtest output directory.
- **`session-runner/PlaytestFormatter.cs`** — Static utility class for formatting player agent reasoning blocks and option score tables as markdown. Contains `FormatReasoningBlock` and `FormatScoreTable`.
- **`session-runner/LlmPlayerAgent.cs`** — LLM-backed player agent. Sends game state and rules to Anthropic Claude, parses `PICK:` response, falls back to `ScoringPlayerAgent` on failure. Implements `IDisposable`.
- **`tests/Pinder.Core.Tests/LlmPlayerAgentTests.cs`** — Tests for `LlmPlayerAgent`: adjusted probability display (tell/momentum/callback bonuses), no-bonus raw percentage, dispose idempotency.
- **`tests/Pinder.Core.Tests/Issue350_ShadowTrackingSpecTests.cs`** — Spec tests verifying shadow tracking wiring: `SessionShadowTracker` wraps `StatBlock`, `GameSessionConfig` passes player shadows into `GameSession`, delta accumulation, edge cases (negative deltas, multiple events per turn, game-end readability).
- **`tests/Pinder.Core.Tests/Issue351_PickReasoningTests.cs`** — Tests for `PlaytestFormatter`: reasoning block formatting, score table columns/checkmarks/bold/bonuses, edge cases (null decision, NaN values, empty reasoning, score mismatch, fewer options).
- **`session-runner/SessionFileCounter.cs`** — Static utility class that scans a directory for `session-*.md` files and returns the next available session number (`max + 1`). Also provides `ResolvePlaytestDirectory()` with 3-tier directory resolution: env var override → walk-up search for `design/playtests/` → hardcoded fallback path.
- **`tests/Pinder.Core.Tests/SessionFileCounterTests.cs`** — Tests for `SessionFileCounter`: number extraction, gaps, character names with digits, production write-read flow, large numbers, non-numeric parts, `ResolvePlaytestDirectory` env var/walk-up/null-fallback behavior.
- **`tests/Pinder.Core.Tests/SessionFileCounterSpecTests.cs`** — Spec-driven tests for issue #418: AC1–AC4 coverage, path resolution with trailing slashes and `..` segments, integration test combining `ResolvePlaytestDirectory` + `GetNextSessionNumber`.
- **`session-runner/OutcomeProjector.cs`** — Pure static class that projects likely game outcome when a session hits the turn cap without a natural ending. Uses `InterestState`-based heuristic with momentum and interest level to produce a human-readable projection string.
- **`tests/Pinder.Core.Tests/OutcomeProjectorTests.cs`** — Tests for `OutcomeProjector.Project`: decision table coverage for all five tiers (AlmostThere/VeryIntoIt/Interested/Lukewarm/Bored/Unmatched), boundary values, momentum display, degenerate cases (maxTurns=0/1), out-of-range interest, pure function guarantees (non-null, deterministic).
- **`tests/Pinder.Core.Tests/ScoringPlayerAgentTests.cs`** (engine-sync tests) — Verifies `ScoringPlayerAgent` uses engine constants correctly: `CallbackBonus.Compute()` for opener (+3) and mid-distance (+1) bonuses, momentum thresholds matching GameSession rules §15 (0–2→+0, 3–4→+2, 5+→+3), and tell bonus (+2) producing exactly 0.1 success chance delta.
- **`tests/Pinder.Core.Tests/ScoringPlayerAgentShadowRiskTests.cs`** — Tests for shadow growth risk scoring adjustments (issue #416): fixation growth penalty (AC1), denial growth penalty (AC2), fixation threshold EV reduction at T0/T1/T2/T3 (AC3), stat variety bonus (AC4), backward compatibility, and edge cases (null history, missing fixation key, combined adjustments).

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

### OutcomeProjector (internal static class)

Projects likely game outcome when a session ends due to the turn cap (no natural `GameOutcome` reached).

```csharp
internal static class OutcomeProjector
{
    /// Projects the likely outcome when the session ends due to turn cap.
    /// Pure function — no I/O, no exceptions, always returns a non-empty string.
    /// Uses InterestState-based branching with momentum bonus estimation.
    internal static string Project(
        int interest,
        int momentum,
        int turnNumber,
        int maxTurns,
        InterestState interestState);
}
```

**Projection logic by `InterestState`:**

| InterestState | Projection |
|---|---|
| `DateSecured` | `"DateSecured already achieved."` |
| `Unmatched` | `"Unmatched — interest hit 0."` |
| `AlmostThere` | `"Likely DateSecured..."` with interest/momentum details and estimated turns |
| `VeryIntoIt` | `"Probable DateSecured..."` with advantage note |
| `Interested` / `Lukewarm` | `"Possible DateSecured"` (interest ≥ 12) or `"Uncertain outcome"` (interest < 12) |
| `Bored` | `"Likely Unmatched..."` with disadvantage note |

**Notes:**
- Momentum bonus displayed when streak ≥ 3 (3–4 → +2, 5+ → +3).
- Estimated turns to close calculated from `(25 - interest) / avgGainPerTurn`.
- The spec's original design used pure numeric thresholds; the implementation dispatches on `InterestState` enum instead, which aligns with the same interest ranges but adds state-aware messaging.

### Session Summary — Cutoff Projection

When the game loop exits because `turn >= maxTurns` and no natural `GameOutcome` was reached, the session summary includes:

```
## Session Summary
**⏸️ Incomplete ({turnsPlayed}/{maxTurns} turns) | Interest: {n}/25 | Total XP: {xp}**

Projected: {OutcomeProjector.Project(...)}
```

When the game ends naturally (DateSecured, Unmatched, Ghost), the existing summary format is unchanged.

### SessionFileCounter (static class)

Manages session file numbering and playtest directory resolution.

```csharp
internal static class SessionFileCounter
{
    /// Environment variable that overrides default playtest directory search paths.
    internal const string EnvVarName = "PINDER_PLAYTESTS_PATH";

    /// Scans the given directory for session-*.md files and returns the next
    /// available session number (highest existing + 1, or 1 if none exist).
    public static int GetNextSessionNumber(string directory);

    /// Resolves the playtest output directory via 3-tier search:
    ///   1. PINDER_PLAYTESTS_PATH env var (if set and directory exists)
    ///   2. Walk up from baseDir looking for design/playtests/
    ///   3. Hardcoded fallback path (/root/.openclaw/agents-extra/pinder/design/playtests)
    /// Returns absolute path or null if not found.
    public static string? ResolvePlaytestDirectory(string baseDir);
}
```

### PlayerAgentContext — New Fields (issue #416)

```csharp
public sealed class PlayerAgentContext
{
    // ... existing properties ...

    /// Stat used on the previous turn. Null on first turn.
    public StatType? LastStatUsed { get; }

    /// Stat used two turns ago. Null on first or second turn.
    public StatType? SecondLastStatUsed { get; }

    /// Whether Honesty was available as an option last turn. False on first turn or unknown.
    public bool HonestyAvailableLastTurn { get; }

    // Constructor — new optional parameters appended (backward-compatible):
    public PlayerAgentContext(
        ...,
        int turnNumber,
        StatType? lastStatUsed = null,
        StatType? secondLastStatUsed = null,
        bool honestyAvailableLastTurn = false);
}
```

When all new fields are at defaults (`null`, `null`, `false`), scoring behavior is identical to pre-#416.

### Key Consumed Types
| Type | Namespace | Role |
|------|-----------|------|
| `SessionShadowTracker` | `Pinder.Core.Stats` | Wraps `StatBlock`, tracks per-session shadow deltas |
| `GameSessionConfig` | `Pinder.Core.Conversation` | Carries optional `PlayerShadows`, `OpponentShadows`, clock, etc. |
| `GameSession` | `Pinder.Core.Conversation` | Core turn-based session engine |
| `TurnResult.ShadowGrowthEvents` | `Pinder.Core.Conversation` | `IReadOnlyList<string>` populated when `PlayerShadows` is non-null |
| `ShadowStatType` | `Pinder.Core.Stats` | Enum: Madness, Horniness, Denial, Fixation, Dread, Overthinking |
| `InterestState` | `Pinder.Core.Conversation` | Enum: Unmatched, Bored, Lukewarm, Interested, VeryIntoIt, AlmostThere, DateSecured — used by `OutcomeProjector` |

## Architecture Notes

- **Shadow tracking is opt-in**: passing `GameSessionConfig` with a `SessionShadowTracker` enables shadow growth events. Without config (or with `playerShadows: null`), `TurnResult.ShadowGrowthEvents` is empty and no shadow output is produced.
- **Retained reference pattern**: the session runner keeps a reference to the `SessionShadowTracker` it passes into `GameSessionConfig`. After the game loop exits (normally or via `GameEndedException`), it reads delta/effective values from that same reference for the summary table.
- **`OpponentShadows` is optional**: the config only wires `playerShadows`; opponent shadow tracking is not used by the session runner currently.
- **Shadow growth triggers** (e.g., Nat 1 → Madness, 3 consecutive same-stat picks → Fixation) are handled inside `GameSession`; the session runner only reads and displays results.
- **Playtest directory resolution** uses a 3-tier strategy: (1) `PINDER_PLAYTESTS_PATH` env var override, (2) walk up from `AppContext.BaseDirectory` looking for `design/playtests/`, (3) hardcoded fallback. This replaced a previously hardcoded absolute path in `Program.cs` that caused `GetNextSessionNumber` to scan the wrong directory when the working directory didn't match expectations.

## Player Agents

The session runner supports pluggable player agent strategies via the `IPlayerAgent` interface.

### IPlayerAgent (interface)
```csharp
Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
```
Takes a `TurnStart` (options + game state) and `PlayerAgentContext` (stats, interest, momentum, shadows), returns a `PlayerDecision` with chosen option index, reasoning text, and per-option score breakdowns.

### ScoringPlayerAgent
Deterministic expected-value scoring agent. Pure math, no LLM. Scores all options using success probability × expected gain − failure cost. Applies strategic adjustments for momentum, interest state, trap exposure, and shadow growth risk. Used as the fallback for `LlmPlayerAgent` and for regression testing.

**Shadow growth risk scoring constants** (§7):
| Constant | Value | Effect |
|---|---|---|
| `FixationGrowthPenalty` | 0.5 | Subtracted from score when option's stat matches both `LastStatUsed` and `SecondLastStatUsed` (would trigger 3-in-a-row Fixation growth) |
| `DenialGrowthPenalty` | 0.3 | Subtracted from non-Honesty options when at least one Honesty option is available in the current turn |
| `FixationT1EvMultiplier` | 0.8 | Multiplies `expectedGainOnSuccess` for Chaos options when Fixation is ≥ 6 and < 12 (Tier 1) |
| `StatVarietyBonus` | 0.1 | Added to score for options whose stat was not used in the last two turns |

**Fixation threshold tiers for Chaos options:**
- **T0 (Fixation < 6):** No adjustment.
- **T1 (Fixation ≥ 6, < 12):** `expectedGainOnSuccess *= 0.8`. Success chance unchanged.
- **T2+ (Fixation ≥ 12):** Disadvantage applied to success chance: `adjustedSuccessChance = successChance²`. T3 (≥ 18) uses same T2 treatment.

**Application order:** Fixation threshold modifies intermediate EV calc inputs (success chance or expected gain) *before* EV computation. Fixation growth penalty, denial penalty, and variety bonus adjust the final score *after* EV computation. All four adjustments are independent and stack additively on the final score.

### LlmPlayerAgent
LLM-backed agent that sends full game state and rules context to Anthropic Claude, parses a `PICK: [A/B/C/D]` response. Falls back to `ScoringPlayerAgent` on any failure (API error, parse error, timeout). Implements `IDisposable` to clean up its internal `AnthropicClient`.

**Constructor:** `LlmPlayerAgent(AnthropicOptions, ScoringPlayerAgent, playerName?, opponentName?)`

**Prompt includes:** game state (interest, momentum, traps, shadows, turn), all options with stat/DC/need/%/risk tier/bonus icons, adjusted probabilities including hidden bonuses (momentum, tell, callback), and a rules reminder.

**Agent selection:** controlled via `PLAYER_AGENT` env var (`scoring` or `llm`, default `scoring`). LLM model configurable via `PLAYER_AGENT_MODEL` env var.

### Supporting Types
- **`PlayerDecision`** — result type: `OptionIndex`, `Reasoning` (string), `Scores` (OptionScore[])
- **`OptionScore`** — per-option breakdown: `Score`, `SuccessChance`, `ExpectedInterestGain`, `BonusesApplied`
- **`PlayerAgentContext`** — input context: player/opponent stats, interest, momentum, traps, shadows, turn number, stat history (`LastStatUsed`, `SecondLastStatUsed`), `HonestyAvailableLastTurn`

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #348 | Added `LlmPlayerAgent` with Anthropic integration, adjusted probability display in prompt (base vs adjusted % when hidden bonuses apply), `IDisposable` disposal in Program.cs, player agent docs. Added tests for adjusted probability display, callback bonus display, and dispose idempotency. |
| 2026-04-04 | #350 | Initial creation — wired `SessionShadowTracker` into `GameSession` via `GameSessionConfig`, added per-turn shadow growth event output and session-end shadow delta summary table. Added spec tests in `Issue350_ShadowTrackingSpecTests.cs`. |
| 2026-04-04 | #351 | Added `PlaytestFormatter` static class with `FormatReasoningBlock` and `FormatScoreTable` methods. After each pick, playtest output now shows the agent's reasoning as a blockquote and a score table with all options' metrics. Defensive handling for null decisions, NaN values, missing scores. Tests in `Issue351_PickReasoningTests.cs`. |
| 2026-04-04 | #386 | Added engine-constant sync tests to `ScoringPlayerAgentTests.cs`: callback bonus (opener +3, mid-distance +1) via `CallbackBonus.Compute()`, momentum threshold verification at all streak values (§15), and tell bonus exactness (+2 → 0.1 success chance delta). Guards against silent drift between agent scoring and engine rules. |
| 2026-04-04 | #418 | Fixed `SessionFileCounter` to correctly find existing session files. Added `ResolvePlaytestDirectory(string baseDir)` with 3-tier resolution (env var → walk-up → fallback). `Program.WritePlaytestLog` now uses `ResolvePlaytestDirectory(AppContext.BaseDirectory)` instead of a hardcoded path. Added `SessionFileCounterSpecTests.cs` (25 tests) and extended `SessionFileCounterTests.cs` with AC coverage, edge cases, and resolution tests. |
| 2026-04-04 | #417 | Added `OutcomeProjector` static class for projecting game outcome on turn-cap cutoff. Uses `InterestState`-based dispatch (diverges from spec's pure numeric thresholds but covers same ranges). Session summary shows ⏸️ Incomplete header with projected outcome when no natural game-over reached. Rewrote `OutcomeProjectorTests.cs` with comprehensive coverage: all five decision tiers, boundary values, momentum display, degenerate cases, pure function guarantees. |
| 2026-04-04 | #416 | Added shadow growth risk scoring to `ScoringPlayerAgent`: fixation growth penalty (−0.5 for 3x same stat), denial growth penalty (−0.3 for skipping Honesty), fixation threshold EV reduction (T1: 0.8× gain, T2+: success chance squared for Chaos), stat variety bonus (+0.1 for unused stats). Added `LastStatUsed`, `SecondLastStatUsed`, `HonestyAvailableLastTurn` optional fields to `PlayerAgentContext` (backward-compatible). Note: spec's `HonestyAvailableLastTurn` field exists but denial penalty is evaluated on current-turn options, not previous turn. 922-line test file `ScoringPlayerAgentShadowRiskTests.cs` covers all ACs and edge cases. |
