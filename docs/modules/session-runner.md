# Session Runner

## Overview
The session runner is a standalone C# program (`session-runner/Program.cs`) that orchestrates automated Pinder playtest sessions. It runs games between characters and writes playtest log files to disk with sequentially numbered filenames.

## Key Components

| File / Class | Description |
|---|---|
| `session-runner/Program.cs` | Entry point. Contains `WritePlaytestLog` which writes session markdown files, delegating numbering to `SessionFileCounter`. Delegates trap loading to `TrapRegistryLoader.Load`. Uses `IPlayerAgent.DecideAsync` for turn decisions. |
| `session-runner/IPlayerAgent.cs` | Public interface for pluggable turn-decision agents. Single method: `DecideAsync(TurnStart, PlayerAgentContext) → Task<PlayerDecision>`. |
| `session-runner/PlayerDecision.cs` | Sealed DTO returned by `IPlayerAgent.DecideAsync`. Contains chosen `OptionIndex`, `Reasoning` string, and `OptionScore[]` for all options. Constructor validates non-null and index range. |
| `session-runner/OptionScore.cs` | Sealed DTO for per-option score breakdown: composite `Score`, `SuccessChance` (0.0–1.0, clamped), `ExpectedInterestGain`, and `BonusesApplied` string array. |
| `session-runner/PlayerAgentContext.cs` | Sealed DTO carrying agent context beyond `TurnStart`: player/opponent `StatBlock`, `CurrentInterest`, `InterestState`, `MomentumStreak`, `ActiveTrapNames`, `SessionHorniness`, nullable `ShadowValues`, and `TurnNumber`. |
| `session-runner/HighestModAgent.cs` | Baseline `IPlayerAgent` implementation. Picks the option with the highest effective stat modifier (replicating the former `BestOption` logic). Computes `SuccessChance` via `(21 - (DC - mod)) / 20` clamped to [0,1]. |
| `session-runner/TrapRegistryLoader.cs` | `internal static` class that loads an `ITrapRegistry` from `traps.json`. Searches env var override → relative path → upward directory walk. Falls back to `NullTrapRegistry` on failure. |
| `session-runner/SessionFileCounter.cs` | `internal static` class that scans a directory for `session-*.md` files and returns the next available session number. |
| `tests/Pinder.Core.Tests/PlayerAgentSpecTests.cs` | Spec-driven tests for `PlayerDecision`, `OptionScore`, `PlayerAgentContext`, and `HighestModAgent`. Covers constructor validation, clamping, edge cases (empty options, single option, identical stats, all-Rizz), and DC/success-chance calculations. |
| `tests/Pinder.Core.Tests/SessionFileCounterTests.cs` | Tests for `SessionFileCounter.GetNextSessionNumber` — validates glob pattern matching and number extraction from session filenames. |
| `tests/Pinder.Core.Tests/SessionRunnerTrapLoadingTests.cs` | Tests for `TrapRegistryLoader.Load` — env var override, upward search, missing file fallback, corrupt JSON fallback, trap activation with real registry. |

## API / Public Interface

### IPlayerAgent

```csharp
public interface IPlayerAgent
{
    Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);
}
```

Pluggable decision-making interface for sim agents. `TurnStart` (from `Pinder.Core.Conversation`) provides the dialogue options and game state snapshot. `PlayerAgentContext` provides additional context (stat blocks, interest, momentum, shadows, etc.). Returns a `PlayerDecision` containing the chosen option index, reasoning, and per-option score breakdowns.

**Error conditions:** Implementations must throw `ArgumentNullException` for null `turn` or `context`, and `InvalidOperationException` if `turn.Options` is empty.

### PlayerDecision

```csharp
public sealed class PlayerDecision
{
    public int OptionIndex { get; }
    public string Reasoning { get; }
    public OptionScore[] Scores { get; }
    public PlayerDecision(int optionIndex, string reasoning, OptionScore[] scores);
}
```

**Invariants:** `OptionIndex` in `[0, Scores.Length)`. `Reasoning` and `Scores` are never null. Constructor throws `ArgumentNullException` for null `reasoning`/`scores`, `ArgumentOutOfRangeException` for invalid `optionIndex`.

### OptionScore

```csharp
public sealed class OptionScore
{
    public int OptionIndex { get; }
    public float Score { get; }
    public float SuccessChance { get; }       // clamped to [0.0, 1.0]
    public float ExpectedInterestGain { get; } // can be negative
    public string[] BonusesApplied { get; }
    public OptionScore(int optionIndex, float score, float successChance,
                       float expectedInterestGain, string[] bonusesApplied);
}
```

**Invariants:** `SuccessChance` is clamped to `[0.0, 1.0]` in constructor. `BonusesApplied` is never null. Constructor throws `ArgumentNullException` for null `bonusesApplied`.

### PlayerAgentContext

```csharp
public sealed class PlayerAgentContext
{
    public StatBlock PlayerStats { get; }
    public StatBlock OpponentStats { get; }
    public int CurrentInterest { get; }          // 0–25
    public InterestState InterestState { get; }
    public int MomentumStreak { get; }           // >= 0
    public string[] ActiveTrapNames { get; }
    public int SessionHorniness { get; }
    public Dictionary<ShadowStatType, int>? ShadowValues { get; }
    public int TurnNumber { get; }
    public PlayerAgentContext(StatBlock playerStats, StatBlock opponentStats,
        int currentInterest, InterestState interestState, int momentumStreak,
        string[] activeTrapNames, int sessionHorniness,
        Dictionary<ShadowStatType, int>? shadowValues, int turnNumber);
}
```

**Invariants:** `PlayerStats`, `OpponentStats`, `ActiveTrapNames` are never null (constructor throws `ArgumentNullException`). `ShadowValues` is nullable (null when shadow tracking disabled).

### HighestModAgent

```csharp
public sealed class HighestModAgent : IPlayerAgent
{
    public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context);
}
```

Baseline agent replicating the former `BestOption` logic. Picks the option whose stat has the highest effective modifier on the player's `StatBlock`. Computes `SuccessChance` as `max(0, min(1, (21 - (DC - mod)) / 20))`. Tiebreaks to lowest index. Returns `Task.FromResult(...)` (synchronous).

### WritePlaytestLog (private static)

```csharp
static void WritePlaytestLog(string content, string p1, string p2, GameOutcome? outcome)
```

Writes a playtest log to `/root/.openclaw/agents-extra/pinder/design/playtests/` as a markdown file. The filename follows the pattern `session-{NNN}-{p1}-vs-{p2}.md` where `NNN` is zero-padded to 3 digits and auto-incremented based on existing files.

Delegates session numbering to `SessionFileCounter.GetNextSessionNumber`.

### TrapRegistryLoader.Load

```csharp
internal static class TrapRegistryLoader
{
    internal const string EnvVarName = "PINDER_TRAPS_PATH";
    internal static ITrapRegistry Load(string baseDir, TextWriter warningWriter)
}
```

Loads an `ITrapRegistry` from `traps.json` with graceful fallback. Search order:
1. `PINDER_TRAPS_PATH` environment variable (if set)
2. Relative path: `{baseDir}/data/traps/traps.json`
3. Upward directory walk from `baseDir` looking for `data/traps/traps.json`

Returns `JsonTrapRepository` on success, `NullTrapRegistry` on any failure. Logs `[INFO]` or `[WARN]` messages to `warningWriter`.

### SessionFileCounter.GetNextSessionNumber

```csharp
internal static class SessionFileCounter
{
    public static int GetNextSessionNumber(string directory)
}
```

Scans the given directory for `session-*.md` files. Splits each filename on `-` and parses the second segment as the session number. Returns `max(existing) + 1`, defaulting to `1` if no matching files exist. Exposed to `Pinder.Core.Tests` via `InternalsVisibleTo`.

## Architecture Notes

- Session files are named with character slugs (e.g., `session-005-sable-vs-brick.md`), which means filenames contain variable numbers of hyphens.
- The glob pattern `session-*.md` (not `session-???.md`) ensures filenames with suffixes beyond the 3-digit number are matched correctly.
- Number extraction uses `Split('-')[1]` to isolate the numeric segment, which is robust against hyphenated character names in the suffix.
- The file counter logic is extracted into `SessionFileCounter` (internal static class) for testability. `session-runner.csproj` uses `InternalsVisibleTo` to expose it to `Pinder.Core.Tests`.
- **Trap loading** is delegated to `TrapRegistryLoader` (extracted from `Program.cs` for testability). Uses a three-tier search: `PINDER_TRAPS_PATH` env var → relative path from base dir → upward directory walk. Falls back to `NullTrapRegistry` with a warning on stderr if no valid `traps.json` is found.
- **Player agent abstraction:** Turn decisions are delegated to pluggable `IPlayerAgent` implementations via `DecideAsync`. This replaces the former inline `BestOption` static method. All agent types (`IPlayerAgent`, `PlayerDecision`, `OptionScore`, `PlayerAgentContext`) live in `session-runner/` (namespace `Pinder.SessionRunner`), NOT in `Pinder.Core`, per vision concern #355.
- **HighestModAgent** is the baseline implementation, replicating the original highest-modifier selection logic. It serves as a placeholder until `ScoringPlayerAgent` (#347) and `LlmPlayerAgent` (#348) are implemented.
- **Success probability formula:** `need = DC - modifier`, `successChance = max(0, min(1, (21 - need) / 20))`. Natural 20 always succeeds, natural 1 always fails (captured by the clamp).
- **LangVersion 8.0 constraint:** All new types use `sealed class` with constructor + get-only properties (no C# 9+ records or init-only setters).

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #354 | Initial creation — Fixed file counter bug where `session-???.md` glob missed files with character name suffixes (e.g., `session-005-sable-vs-brick.md`). Changed to `session-*.md` glob with `Split('-')[1]` number extraction. Added `SessionFileCounterTests`. |
| 2026-04-03 | #353 | Replaced hardcoded `NullTrapRegistry` with `JsonTrapRepository` loaded from `data/traps/traps.json`. Added multi-path resolution with graceful fallback. Added `SessionRunnerTrapLoadingTests`. |
| 2026-04-03 | #353 | Extracted inline trap-loading logic from `Program.cs` into `TrapRegistryLoader` (new file). Replaced hardcoded paths with env var override (`PINDER_TRAPS_PATH`) + upward directory walk. Tests rewritten to exercise `TrapRegistryLoader.Load` directly. |
| 2026-04-03 | #354 | Extracted inline file counter logic from `WritePlaytestLog` into `SessionFileCounter.GetNextSessionNumber` (new file `SessionFileCounter.cs`). Tests now call the real class instead of a mirrored helper. Added `InternalsVisibleTo` for test access. |
| 2026-04-04 | #346 | Added `IPlayerAgent` interface, `PlayerDecision`, `OptionScore`, `PlayerAgentContext` DTOs, and `HighestModAgent` baseline implementation. Replaced `BestOption` static method with agent-based turn decisions. Added `PlayerAgentSpecTests.cs` (749 lines). |
