# Session Runner

## Overview
The session runner orchestrates simulated playtest sessions between two `CharacterProfile` instances via `GameSession`. It handles turn execution, output formatting (interest bars, trap status, shadow events), and session summary generation including outcome and shadow delta tables.

## Key Components

- **`session-runner/Program.cs`** — Entry point. Parses CLI arguments (`--player`, `--opponent`, `--max-turns`, `--agent`), loads character profiles via `CharacterLoader`, configures `GameSession` with `GameSessionConfig`, runs the turn loop, and prints per-turn status and session summary markdown. Calls `PlaytestFormatter` to render pick reasoning and score tables. Uses `SessionFileCounter.ResolvePlaytestDirectory()` to locate the playtest output directory.
- **`session-runner/CharacterLoader.cs`** — Static utility that loads `CharacterProfile` instances from pre-assembled prompt markdown files (`{name}-prompt.md`). Parses display name, level, primary stats (EFFECTIVE STATS section), shadow stats, and assembled system prompt from code-fenced content. Also provides `ListAvailable()` to discover characters in a directory.
- **`session-runner/CharacterDefinitionLoader.cs`** — Static utility that loads character definition JSON files and runs them through the full `CharacterAssembler` + `PromptBuilder` pipeline to produce `CharacterProfile` instances. Exposes `Load(path, itemRepo, anatomyRepo)` for file-based loading and internal `Parse(json, itemRepo, anatomyRepo)` for direct JSON string parsing (used in tests).
- **`session-runner/DataFileLocator.cs`** — Static utility that resolves paths to data files by walking up from a base directory. Checks `PINDER_DATA_PATH` env var first, then walks parent directories. Also provides `FindRepoRoot()` to locate the repo root (directory containing both `data/` and `src/`).
- **`data/characters/*.json`** — Character definition JSON files (gerald, velvet, sable, brick, zyx). Each defines name, gender_identity, bio, level, item IDs, anatomy selections, build_points, and optional shadows.
- **`data/items/starter-items.json`** — Item definitions consumed by `JsonItemRepository`. Contains stat modifiers, prompt fragments, archetype tendencies, and timing modifiers.
- **`data/anatomy/anatomy-parameters.json`** — Anatomy parameter definitions consumed by `JsonAnatomyRepository`. Contains tier IDs, stat modifiers, fragments, and timing modifiers.
- **`tests/Pinder.Core.Tests/CharacterLoaderTests.cs`** — Unit tests for `CharacterLoader.ParseBio`: unquoted bio extraction, quoted bio with quote stripping, missing bio line returns empty, parameterized tests for all starter characters, bio inside code fence extraction.
- **`tests/Pinder.Core.Tests/CharacterLoaderSpecTests.cs`** — Comprehensive tests for `CharacterLoader`: prompt file parsing (stats, shadows, level, display name, system prompt extraction), error cases (missing file, missing stats, missing EFFECTIVE STATS section), edge cases (mixed case input, shadow lines with/without tilde prefix, parenthetical notes, multiple code fences, level from outside code fence), and conditional integration tests against real prompt files.
- **`tests/Pinder.Core.Tests/Issue415_CharacterDefinitionLoaderSpecTests.cs`** — Spec-driven tests for `CharacterDefinitionLoader` and `DataFileLocator`: assembly pipeline (AC2–AC4), all 5 character definitions load successfully (AC5), `DataFileLocator` resolves character files (AC6), data file presence, edge cases (missing shadows, empty items/anatomy, special chars in name, unknown item IDs), error conditions (file not found, malformed JSON, missing required fields, level range, unknown stat/shadow types), shadow parsing, `DataFileLocator` walk-up and repo root discovery, integration tests for full pipeline across characters.
- **`session-runner/PlaytestFormatter.cs`** — Static utility class for formatting player agent reasoning blocks and option score tables as markdown. Contains `FormatReasoningBlock` and `FormatScoreTable`.
- **`session-runner/LlmPlayerAgent.cs`** — LLM-backed player agent with character context. Sends game state, character personality, conversation history, and scoring advisory to Anthropic Claude, parses `PICK:` response, falls back to `ScoringPlayerAgent` on failure. Implements `IDisposable`. Uses character-aware system message when `playerSystemPrompt` or `playerTextingStyle` is provided; falls back to generic strategic prompt otherwise.
- **`tests/Pinder.Core.Tests/LlmPlayerAgentTests.cs`** — Tests for `LlmPlayerAgent`: adjusted probability display (tell/momentum/callback bonuses), no-bonus raw percentage, dispose idempotency.
- **`tests/Pinder.Core.Tests/LlmPlayerAgentSpecTests.cs`** — Spec tests for issue #492: character-aware system message (player name, opponent name, system prompt, texting style, generic fallback), conversation history in prompt (inclusion, sender names, null/empty omission), scoring advisory section (header, scorer pick marker, omission when null), task instruction (player name, narrative mention, PICK format), fallback behavior (valid index, scores array, reasoning marker), game state in prompt.
- **`tests/Pinder.Core.Tests/Issue350_ShadowTrackingSpecTests.cs`** — Spec tests verifying shadow tracking wiring: `SessionShadowTracker` wraps `StatBlock`, `GameSessionConfig` passes player shadows into `GameSession`, delta accumulation, edge cases (negative deltas, multiple events per turn, game-end readability).
- **`tests/Pinder.Core.Tests/Issue351_PickReasoningTests.cs`** — Tests for `PlaytestFormatter`: reasoning block formatting, score table columns/checkmarks/bold/bonuses, edge cases (null decision, NaN values, empty reasoning, score mismatch, fewer options).
- **`session-runner/SessionFileCounter.cs`** — Static utility class that scans a directory for `session-*.md` files and returns the next available session number (`max + 1`). Also provides `ResolvePlaytestDirectory()` with 3-tier directory resolution: env var override → walk-up search for `design/playtests/` → hardcoded fallback path.
- **`tests/Pinder.Core.Tests/SessionFileCounterTests.cs`** — Tests for `SessionFileCounter`: number extraction, gaps, character names with digits, production write-read flow, large numbers, non-numeric parts, `ResolvePlaytestDirectory` env var/walk-up/null-fallback behavior.
- **`tests/Pinder.Core.Tests/SessionFileCounterSpecTests.cs`** — Spec-driven tests for issue #418: AC1–AC4 coverage, path resolution with trailing slashes and `..` segments, integration test combining `ResolvePlaytestDirectory` + `GetNextSessionNumber`.
- **`session-runner/OutcomeProjector.cs`** — Pure static class that projects likely game outcome when a session hits the turn cap without a natural ending. Uses `InterestState`-based heuristic with momentum and interest level to produce a human-readable projection string.
- **`tests/Pinder.Core.Tests/OutcomeProjectorTests.cs`** — Tests for `OutcomeProjector.Project`: decision table coverage for all five tiers (AlmostThere/VeryIntoIt/Interested/Lukewarm/Bored/Unmatched), boundary values, momentum display, degenerate cases (maxTurns=0/1), out-of-range interest, pure function guarantees (non-null, deterministic).
- **`tests/Pinder.Core.Tests/ScoringPlayerAgentTests.cs`** (engine-sync tests) — Verifies `ScoringPlayerAgent` uses engine constants correctly: `CallbackBonus.Compute()` for opener (+3) and mid-distance (+1) bonuses, momentum thresholds matching GameSession rules §15 (0–2→+0, 3–4→+2, 5+→+3), and tell bonus (+2) producing exactly 0.1 success chance delta.
- **`tests/Pinder.Core.Tests/DcTableHeaderTests.cs`** — Tests that DC Reference table header and column header use dynamic character names via string interpolation, not hardcoded values. Includes source file assertion that `Program.cs` contains no hardcoded "Sable attacking, Brick defending" strings.
- **`tests/Pinder.Core.Tests/ScoringPlayerAgentShadowRiskTests.cs`** — Tests for shadow growth risk scoring adjustments (issue #416): fixation growth penalty (AC1), denial growth penalty (AC2), fixation threshold EV reduction at T0/T1/T2/T3 (AC3), stat variety bonus (AC4), backward compatibility, and edge cases (null history, missing fixation key, combined adjustments).

## API / Public Interface

The session runner is a console application (`Program.cs`), not a library.

### CLI Interface

```
Usage: dotnet run --project session-runner -- --player <name> --opponent <name> [--max-turns <n>] [--agent <scoring|llm>]
       dotnet run --project session-runner -- --player-def <path> --opponent-def <path> [--max-turns <n>] [--agent <scoring|llm>]

  --player <name>        Player character name (tries data/characters/{name}.json first, falls back to prompt file)
  --opponent <name>      Opponent character name (same resolution as --player)
  --player-def <path>    Player character definition JSON file (uses CharacterDefinitionLoader)
  --opponent-def <path>  Opponent character definition JSON file (uses CharacterDefinitionLoader)
  --max-turns <n>        Maximum turns (default: 20)
  --agent <type>         Player agent: scoring or llm (default: scoring)
```

`--player`/`--opponent` and `--player-def`/`--opponent-def` can be mixed freely. The `--player <name>` shorthand first attempts to resolve `data/characters/{name}.json` via `DataFileLocator`; if found, it delegates to `CharacterDefinitionLoader.Load()`. If not found, it falls back to `CharacterLoader.Load()` (prompt file parsing). Running with no args or invalid args prints usage and exits with code 1.

The `--agent` argument replaces the previous `PLAYER_AGENT` environment variable for agent selection.

### CharacterLoader (static class)

Loads and parses pre-assembled prompt markdown files into `CharacterProfile` instances.

```csharp
public static class CharacterLoader
{
    /// Load a CharacterProfile by name from a prompt directory.
    /// Resolves to {promptDirectory}/{name.ToLowerInvariant()}-prompt.md.
    /// Throws FileNotFoundException (with path + available characters) if file missing.
    /// Throws FormatException if file lacks EFFECTIVE STATS or required stats.
    public static CharacterProfile Load(string name, string promptDirectory);

    /// Parse prompt file content into a CharacterProfile.
    /// fallbackName used as display name if no name= or role line found.
    public static CharacterProfile Parse(string content, string fallbackName);

    /// List available character names from *-prompt.md files in directory.
    /// Returns sorted comma-separated names, or "(none)" if empty.
    /// Does not throw on nonexistent directory.
    public static string ListAvailable(string promptDirectory);

    /// Parse the bio value from a "- Bio:" line in the content.
    /// Returns text after "- Bio:" prefix, trimmed.
    /// Strips surrounding double quotes if present. Returns "" if no bio line found.
    internal static string ParseBio(string content);
}
```

**Parsing logic:**
- **DisplayName**: extracted from `name=<X>` in Inputs line, or `You are playing the role of <X>` in code fence, or capitalized fallback name.
- **Level**: from `- Level: N` inside code fence first; falls back to `**Level N —` pattern outside.
- **Bio**: from `- Bio:` line. Extracts text after the prefix, trims whitespace, and strips optional surrounding double quotes. Does not require quotes (fixes prior bug where unquoted bios returned empty).
- **Stats**: 6 required stats from `EFFECTIVE STATS` section inside code fence (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `Self-Awareness`). Missing stats throw `FormatException`.
- **Shadows**: from `Shadow state` section outside code fence. `~` prefix stripped. Missing section defaults all to 0. Parenthetical notes after values are ignored.
- **SystemPrompt**: full text between first and last code fences.
- **Timing**: default `TimingProfile(0, 1.0f, 0.0f, "neutral")` for all prompt-loaded characters.

### CharacterDefinitionLoader (static class)

Loads character definition JSON files through the full `CharacterAssembler` + `PromptBuilder` pipeline.

```csharp
public static class CharacterDefinitionLoader
{
    /// Load a character definition from a JSON file and assemble into a CharacterProfile.
    /// Throws FileNotFoundException if file missing.
    /// Throws FormatException on malformed JSON, missing required fields,
    /// level out of range (1–11), or unknown stat/shadow keys.
    public static CharacterProfile Load(
        string jsonPath, IItemRepository itemRepo, IAnatomyRepository anatomyRepo);

    /// Parse a JSON string directly into a CharacterProfile (internal, for testing).
    internal static CharacterProfile Parse(
        string json, IItemRepository itemRepo, IAnatomyRepository anatomyRepo);
}
```

**Assembly pipeline:** Read JSON → validate required fields (`name`, `gender_identity`, `bio`, `level`, `items`, `anatomy`, `build_points`) → parse stat keys to `StatType`/`ShadowStatType` enums → `CharacterAssembler.Assemble(items, anatomy, buildPoints, shadows)` → `PromptBuilder.BuildSystemPrompt(name, genderIdentity, bio, fragments, new TrapState())` → `new CharacterProfile(fragments.Stats, systemPrompt, name, fragments.Timing, level)`.

**Stat key mapping:** `charm`→Charm, `rizz`→Rizz, `honesty`→Honesty, `chaos`→Chaos, `wit`→Wit, `self_awareness`→SelfAwareness. Shadow keys: `madness`→Madness, `horniness`→Horniness, `denial`→Denial, `fixation`→Fixation, `dread`→Dread, `overthinking`→Overthinking.

**Optional field:** `shadows` — defaults all shadow stats to 0 if omitted.

### DataFileLocator (static class)

Resolves paths to data files by walking up from a base directory.

```csharp
public static class DataFileLocator
{
    internal const string EnvVarName = "PINDER_DATA_PATH";

    /// Find a data file by walking up from baseDir.
    /// Checks PINDER_DATA_PATH env var first, then walks parent directories.
    /// Returns absolute path or null if not found.
    public static string? FindDataFile(string baseDir, string relativePath);

    /// Find the repo root (directory with both "data" and "src" subdirectories).
    /// Returns absolute path or null if not found.
    public static string? FindRepoRoot(string baseDir);
}
```

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

### PlayerAgentContext — Optional Fields

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

    /// Conversation history for LLM context. Each tuple is (SenderName, MessageText).
    /// Null if conversation history is not available (e.g., first turn or scoring-only agent).
    public IReadOnlyList<(string Sender, string Text)>? ConversationHistory { get; }

    // Constructor — optional parameters appended (backward-compatible):
    public PlayerAgentContext(
        ...,
        int turnNumber,
        StatType? lastStatUsed = null,
        StatType? secondLastStatUsed = null,
        bool honestyAvailableLastTurn = false,
        IReadOnlyList<(string Sender, string Text)>? conversationHistory = null);
}
```

When all optional fields are at defaults, scoring behavior is identical to pre-#416. `ConversationHistory` is used only by `LlmPlayerAgent` for prompt context.

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
- **Character loading (dual path)**: Characters can be loaded two ways: (1) **CharacterDefinitionLoader** reads JSON definition files and runs the full `CharacterAssembler` + `PromptBuilder` pipeline — this is the preferred path that exercises real item/anatomy data. (2) **CharacterLoader** reads pre-assembled prompt markdown files — retained as a fallback. CLI args `--player-def`/`--opponent-def` use the definition loader directly; `--player`/`--opponent` shorthand tries `data/characters/{name}.json` via `DataFileLocator` first, falling back to `CharacterLoader` if not found.
- **Data file resolution**: `DataFileLocator` walks up from the working directory to find `data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json`. Also checks the `PINDER_DATA_PATH` env var. If data files cannot be resolved, the runner warns and falls back to prompt file loading.
- **CLI-driven configuration**: The session runner uses positional CLI arguments instead of environment variables for character selection and agent type. The `--max-turns` default is 20 (previously hardcoded as 15).
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
LLM-backed agent that sends character context, conversation history, game state, and scoring advisory to Anthropic Claude, parses a `PICK: [A/B/C/D]` response. Falls back to `ScoringPlayerAgent` on any failure (API error, parse error, timeout). Implements `IDisposable` to clean up its internal `AnthropicClient`.

**Constructor:**
```csharp
LlmPlayerAgent(
    AnthropicOptions options,
    ScoringPlayerAgent fallback,
    string playerName = "the player",
    string opponentName = "the opponent",
    string playerSystemPrompt = "",
    string playerTextingStyle = "")
```

**System message (`BuildSystemMessage()`):** When `playerSystemPrompt` or `playerTextingStyle` is non-empty, builds a character-aware system message containing the player/opponent names, the character's assembled system prompt, texting style fragment, and an instruction that character fit and narrative moment take priority over pure optimization. When both are empty, falls back to a generic strategic prompt (backward-compatible).

**User prompt (`BuildPrompt()`):** Sections in order:
1. **Conversation So Far** — from `PlayerAgentContext.ConversationHistory` (omitted if null/empty)
2. **Current State** — interest, momentum, traps, shadows, turn number
3. **Your Options** — stat, DC, success %, risk tier, bonus icons, intended text
4. **Scoring Agent Advisory** — `ScoringPlayerAgent`'s EV table with `← scorer pick` marker (omitted if no scoring decision)
5. **Rules Reminder** — success/failure tiers, risk bonus, momentum, bonus icons
6. **Task** — character-fit reasoning instruction with `PICK: [A/B/C/D]` format

**Agent selection:** controlled via `--agent` CLI argument (`scoring` or `llm`, default `scoring`). The `PLAYER_AGENT` env var is no longer used. LLM model configurable via `PLAYER_AGENT_MODEL` env var.

### Supporting Types
- **`PlayerDecision`** — result type: `OptionIndex`, `Reasoning` (string), `Scores` (OptionScore[])
- **`OptionScore`** — per-option breakdown: `Score`, `SuccessChance`, `ExpectedInterestGain`, `BonusesApplied`
- **`PlayerAgentContext`** — input context: player/opponent stats, interest, momentum, traps, shadows, turn number, stat history (`LastStatUsed`, `SecondLastStatUsed`), `HonestyAvailableLastTurn`, `ConversationHistory`

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #348 | Added `LlmPlayerAgent` with Anthropic integration, adjusted probability display in prompt (base vs adjusted % when hidden bonuses apply), `IDisposable` disposal in Program.cs, player agent docs. Added tests for adjusted probability display, callback bonus display, and dispose idempotency. |
| 2026-04-04 | #350 | Initial creation — wired `SessionShadowTracker` into `GameSession` via `GameSessionConfig`, added per-turn shadow growth event output and session-end shadow delta summary table. Added spec tests in `Issue350_ShadowTrackingSpecTests.cs`. |
| 2026-04-04 | #351 | Added `PlaytestFormatter` static class with `FormatReasoningBlock` and `FormatScoreTable` methods. After each pick, playtest output now shows the agent's reasoning as a blockquote and a score table with all options' metrics. Defensive handling for null decisions, NaN values, missing scores. Tests in `Issue351_PickReasoningTests.cs`. |
| 2026-04-04 | #386 | Added engine-constant sync tests to `ScoringPlayerAgentTests.cs`: callback bonus (opener +3, mid-distance +1) via `CallbackBonus.Compute()`, momentum threshold verification at all streak values (§15), and tell bonus exactness (+2 → 0.1 success chance delta). Guards against silent drift between agent scoring and engine rules. |
| 2026-04-04 | #418 | Fixed `SessionFileCounter` to correctly find existing session files. Added `ResolvePlaytestDirectory(string baseDir)` with 3-tier resolution (env var → walk-up → fallback). `Program.WritePlaytestLog` now uses `ResolvePlaytestDirectory(AppContext.BaseDirectory)` instead of a hardcoded path. Added `SessionFileCounterSpecTests.cs` (25 tests) and extended `SessionFileCounterTests.cs` with AC coverage, edge cases, and resolution tests. |
| 2026-04-04 | #417 | Added `OutcomeProjector` static class for projecting game outcome on turn-cap cutoff. Uses `InterestState`-based dispatch (diverges from spec's pure numeric thresholds but covers same ranges). Session summary shows ⏸️ Incomplete header with projected outcome when no natural game-over reached. Rewrote `OutcomeProjectorTests.cs` with comprehensive coverage: all five decision tiers, boundary values, momentum display, degenerate cases, pure function guarantees. |
| 2026-04-04 | #414 | Added `CharacterLoader` to load characters from prompt files. Replaced all hardcoded stat blocks in `Program.cs` with `CharacterLoader.Load()` calls. Added CLI argument parsing (`--player`, `--opponent`, `--max-turns` default 20, `--agent` replacing `PLAYER_AGENT` env var). Usage/error messages printed to stderr with exit code 1. Added `CharacterLoaderSpecTests.cs` with comprehensive parsing, error, and edge-case coverage. |
| 2026-04-04 | #416 | Added shadow growth risk scoring to `ScoringPlayerAgent`: fixation growth penalty (−0.5 for 3x same stat), denial growth penalty (−0.3 for skipping Honesty), fixation threshold EV reduction (T1: 0.8× gain, T2+: success chance squared for Chaos), stat variety bonus (+0.1 for unused stats). Added `LastStatUsed`, `SecondLastStatUsed`, `HonestyAvailableLastTurn` optional fields to `PlayerAgentContext` (backward-compatible). Note: spec's `HonestyAvailableLastTurn` field exists but denial penalty is evaluated on current-turn options, not previous turn. 922-line test file `ScoringPlayerAgentShadowRiskTests.cs` covers all ACs and edge cases. |
| 2026-04-04 | #415 | Added `CharacterDefinitionLoader` and `DataFileLocator` to enable loading characters from JSON definition files through the full `CharacterAssembler` + `PromptBuilder` pipeline. Added `--player-def`/`--opponent-def` CLI args and shorthand resolution (`--player gerald` → `data/characters/gerald.json`). Copied `starter-items.json` and `anatomy-parameters.json` data files into repo. Created 5 starter character definitions (gerald, velvet, sable, brick, zyx). 736-line spec test file covers assembly pipeline, data file presence, edge cases, error conditions, and integration tests. |
| 2026-04-05 | #513 | Fixed `CharacterLoader.ParseBio` bug: bio lines without surrounding quotes previously returned empty string. Changed parsing to extract text after `- Bio:` prefix and optionally strip quotes instead of requiring them. Changed `ParseBio` visibility from `private` to `internal`. Added 5 test methods in `CharacterLoaderTests.cs` covering unquoted, quoted, missing, parameterized starter characters, and code-fence scenarios. |
| 2026-04-05 | #514 | Fixed hardcoded "Sable"/"Brick" names in DC Reference table header and column header in `Program.cs`. Now uses `player1`/`player2` string interpolation for dynamic character names. Added `DcTableHeaderTests.cs` (4 tests) verifying interpolation with custom names, default names, and source file assertion that no hardcoded header remains. |
