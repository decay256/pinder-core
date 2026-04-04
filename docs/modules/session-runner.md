# Session Runner

## Overview
The session runner orchestrates simulated playtest sessions between two `CharacterProfile` instances via `GameSession`. It handles turn execution, output formatting (interest bars, trap status, shadow events), and session summary generation including outcome and shadow delta tables.

## Key Components

- **`session-runner/Program.cs`** — Entry point. Builds character profiles, configures `GameSession` with `GameSessionConfig`, runs the turn loop, and prints per-turn status and session summary markdown.
- **`tests/Pinder.Core.Tests/Issue350_ShadowTrackingSpecTests.cs`** — Spec tests verifying shadow tracking wiring: `SessionShadowTracker` wraps `StatBlock`, `GameSessionConfig` passes player shadows into `GameSession`, delta accumulation, edge cases (negative deltas, multiple events per turn, game-end readability).

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

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #350 | Initial creation — wired `SessionShadowTracker` into `GameSession` via `GameSessionConfig`, added per-turn shadow growth event output and session-end shadow delta summary table. Added spec tests in `Issue350_ShadowTrackingSpecTests.cs`. |
