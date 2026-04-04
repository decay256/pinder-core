# Spec: Session Runner — Load Characters from Command-Line Args

**Issue:** #414  
**Module:** `docs/modules/session-runner.md`

---

## Overview

Replace the hardcoded character stat blocks and prompt-file paths in `session-runner/Program.cs` with command-line argument parsing and a new `CharacterLoader` utility class. The runner should accept `--player <name>` and `--opponent <name>` arguments, resolve the corresponding prompt files from `design/examples/`, parse them into `CharacterProfile` instances, and wire them into `GameSession`. This also adds `--max-turns` (default 20) and `--agent` arguments to the CLI. Running with no args or invalid args prints usage and lists available characters.

---

## Function Signatures

### `CharacterLoader` (new file: `session-runner/CharacterLoader.cs`)

```csharp
namespace Pinder.SessionRunner
{
    /// <summary>
    /// Loads a CharacterProfile from a pre-assembled prompt markdown file
    /// (e.g. design/examples/gerald-prompt.md).
    /// </summary>
    public static class CharacterLoader
    {
        /// <summary>
        /// Load a CharacterProfile by parsing a pre-assembled prompt file.
        /// </summary>
        /// <param name="name">
        ///   Character name (case-insensitive, e.g. "gerald", "Velvet").
        ///   Resolved to file "{basePath}/{name.ToLower()}-prompt.md".
        /// </param>
        /// <param name="basePath">
        ///   Directory containing the prompt markdown files
        ///   (e.g. "/root/.openclaw/agents-extra/pinder/design/examples").
        /// </param>
        /// <returns>A fully constructed CharacterProfile ready for GameSession.</returns>
        /// <exception cref="System.IO.FileNotFoundException">
        ///   Thrown when no file exists at {basePath}/{name}-prompt.md.
        ///   Message MUST include the missing path AND list available character
        ///   names (derived from *-prompt.md files in basePath).
        /// </exception>
        /// <exception cref="System.FormatException">
        ///   Thrown when the file does not contain the expected sections
        ///   (missing stats, missing code fence, unparseable level).
        /// </exception>
        public static CharacterProfile Load(string name, string basePath);
    }
}
```

### `Program.Main` changes (existing file: `session-runner/Program.cs`)

The `Main(string[] args)` method is modified to:

1. Parse CLI arguments before any other logic.
2. Replace all hardcoded stat blocks and prompt-file reads with `CharacterLoader.Load()` calls.
3. Accept `--agent scoring|llm` (default: `scoring`) — replaces the `PLAYER_AGENT` env var lookup.
4. Accept `--max-turns <n>` (default: 20) — replaces the hardcoded `while (turn < 15)`.

```
CLI usage:
  dotnet run --project session-runner -- \
    --player <name> --opponent <name> \
    [--max-turns <n>] [--agent scoring|llm]

Examples:
  dotnet run --project session-runner -- --player gerald --opponent velvet
  dotnet run --project session-runner -- --player sable --opponent brick --max-turns 30
  dotnet run --project session-runner -- --player gerald --opponent zyx --agent llm
```

No new public C# method signatures are added to `Program`; the CLI contract is the public interface.

---

## Input/Output Examples

### Example 1: Successful load

**Input (CLI):**
```
dotnet run --project session-runner -- --player gerald --opponent velvet
```

**Behavior:**
- Resolves `gerald-prompt.md` and `velvet-prompt.md` from the examples directory.
- Parses each file into a `CharacterProfile`.
- Gerald's profile has: `DisplayName = "Gerald_42"`, `Level = 5`, stats `Charm=+13, Rizz=+11, Honesty=+5, Chaos=+9, Wit=+5, SA=+4`, shadows `Madness=5, Horniness=0, Denial=2, Fixation=4, Dread=3, Overthinking=2`.
- Session runs for up to 20 turns (default `--max-turns`).
- `--agent` defaults to `scoring` (ScoringPlayerAgent).

### Example 2: No args — usage + character list

**Input (CLI):**
```
dotnet run --project session-runner
```

**Output (stderr, exit code 1):**
```
Usage: dotnet run --project session-runner -- --player <name> --opponent <name> [--max-turns <n>] [--agent scoring|llm]

Available characters: brick, gerald, sable, velvet, zyx
```

The available characters list is generated dynamically by scanning `*-prompt.md` files in the examples directory.

### Example 3: Unknown character

**Input (CLI):**
```
dotnet run --project session-runner -- --player chad --opponent velvet
```

**Output (stderr, exit code 1):**
```
Error: Character 'chad' not found at /root/.openclaw/agents-extra/pinder/design/examples/chad-prompt.md
Available characters: brick, gerald, sable, velvet, zyx
```

### Example 4: Prompt file parsing

**Input file** (`gerald-prompt.md`, abbreviated):
```markdown
# Gerald — Assembled System Prompt

> **Inputs:** name=Gerald_42 · he/him · ...

---

\```
You are playing the role of Gerald_42, a sentient penis...

EFFECTIVE STATS
- Charm: +13
- Rizz: +11
- Honesty: +5
- Chaos: +9
- Wit: +5
- Self-Awareness: +4
\```

---

## Assembly Notes

...

**Shadow state (estimated after 5 levels of play):**
- Madness: ~5
- Fixation: ~4
- Dread: ~3
- Denial: ~2
...

## Level & Progression

**Level 5 — Smooth-ish | +2 level bonus | 21 total build points**
```

**Parsed output:**
- `DisplayName`: `"Gerald_42"` — extracted from the line `name=Gerald_42` in the Inputs block, or from the `You are playing the role of Gerald_42` line inside the code fence.
- `Level`: `5` — from the line `- Level: 5` or `**Level 5 —` pattern.
- `Stats`: `StatBlock` with primary stats `{Charm:13, Rizz:11, Honesty:5, Chaos:9, Wit:5, SelfAwareness:4}`.
- `Shadows`: `{Madness:5, Horniness:0, Denial:2, Fixation:4, Dread:3, Overthinking:2}` — from the `Shadow state` section. Values prefixed with `~` are parsed as integers. Missing shadows default to `0`.
- `AssembledSystemPrompt`: The full text between the first `` ``` `` and the last `` ``` `` in the file.
- `Timing`: `new TimingProfile(0, 1.0f, 0.0f, "neutral")` — default timing profile (prompt files don't encode timing parameters in a structured way).

---

## Acceptance Criteria

### AC1: `--player` and `--opponent` CLI arguments

The session runner accepts `--player <name>` and `--opponent <name>` as required command-line arguments. Both are case-insensitive. The `<name>` value maps to a prompt file at `{basePath}/{name.ToLower()}-prompt.md`.

All hardcoded stat blocks (`sableStats`, `brickStats`), hardcoded names (`"Gerald_42"`, `"Velvet"`), hardcoded levels (`p1Level = 5`, `p2Level = 7`), and hardcoded `File.ReadAllText` calls for prompt files are removed from `Program.cs`. Character data comes exclusively from `CharacterLoader.Load()`.

### AC2: `--max-turns` CLI argument

The runner accepts `--max-turns <n>` with a default of `20`. The game loop condition changes from `while (turn < 15)` to `while (turn < maxTurns)`. The value must be a positive integer ≥ 1.

### AC3: `--agent` CLI argument

The runner accepts `--agent scoring|llm` with a default of `scoring`. This replaces the `PLAYER_AGENT` environment variable. `scoring` → `ScoringPlayerAgent`, `llm` → `LlmPlayerAgent`. Invalid values print an error and exit 1.

### AC4: Usage + available characters on bad input

When run with no arguments, missing required arguments (`--player` or `--opponent`), or unrecognized arguments, the runner prints a usage message to stderr listing all available options and dynamically-discovered character names, then exits with code 1.

### AC5: `CharacterLoader.Load` parses prompt files correctly

`CharacterLoader.Load("gerald", basePath)` returns a `CharacterProfile` with:
- `DisplayName` extracted from the prompt file (the name used in `name=<X>` or `You are playing the role of <X>`).
- `Level` extracted from the `- Level: N` line inside the code fence.
- `Stats` (a `StatBlock`) with primary stats from the `EFFECTIVE STATS` section and shadow stats from the `Shadow state` section.
- `AssembledSystemPrompt` containing the full text between the first and last code fences.
- `Timing` set to a default `TimingProfile(0, 1.0f, 0.0f, "neutral")`.

### AC6: Session header uses loaded character data

The playtest output header (`# Playtest Session`, character table, DC reference table) must derive all names, levels, level bonuses, and stat values from the loaded `CharacterProfile` instances — not from separate variables.

### AC7: Shadow tracking uses loaded shadow stats

The `SessionShadowTracker` is constructed from the loaded player character's `StatBlock` (which includes shadow values from the prompt file). The existing shadow tracking and shadow delta table output continue to work.

---

## Edge Cases

### Missing prompt file
If `{basePath}/{name}-prompt.md` does not exist, `CharacterLoader.Load` throws `FileNotFoundException` with a message that includes the attempted path and lists available characters in `basePath`.

### Empty examples directory
If the examples directory contains no `*-prompt.md` files, the available characters list is empty. Usage prints `Available characters: (none)`.

### Prompt file missing EFFECTIVE STATS section
If the code-fenced block does not contain an `EFFECTIVE STATS` header, `CharacterLoader.Load` throws `FormatException` with message: `"File '{path}' does not contain an EFFECTIVE STATS section"`.

### Prompt file missing some stats
If any of the 6 required stats (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `Self-Awareness`) is not listed under `EFFECTIVE STATS`, throw `FormatException` listing the missing stat names.

### Shadow section absent
If the prompt file has no recognizable shadow section (no `Shadow state` or similar heading), all shadow values default to `0`. This is not an error — it produces a valid `StatBlock` with zero shadows.

### Stat line parsing
Stat lines under `EFFECTIVE STATS` follow the format `- StatName: +N` or `- StatName: -N` or `- StatName: N`. The parser must handle:
- `+` prefix (e.g. `+13`)
- `-` prefix (e.g. `-2`)
- No prefix (e.g. `0`)
- `Self-Awareness` maps to `StatType.SelfAwareness` (hyphenated display name)

### Shadow line parsing
Shadow lines follow the format `- ShadowName: ~N` or `- ShadowName: N`. The `~` prefix (meaning "approximately") is stripped before integer parsing.

### Level parsing
The level line may appear as:
- `- Level: 5` (inside code fence) — preferred
- `**Level 5 —` (outside code fence, in the Level & Progression section)
The parser should check inside the code fence first. If not found, scan for the pattern outside.

### `--max-turns 0` or negative
Print error `"--max-turns must be a positive integer"` and exit 1.

### `--max-turns` not a number
Print error `"--max-turns must be a positive integer"` and exit 1.

### Same player and opponent
Allowed. `--player gerald --opponent gerald` is a valid invocation (Gerald talking to himself). No special handling needed.

### Base path resolution
The examples directory path should be resolved similarly to the existing pattern in `Program.cs` (currently hardcoded as `/root/.openclaw/agents-extra/pinder/design/examples`). The path can remain hardcoded for prototype maturity or be resolved via `DataFileLocator` if #415 has merged. If the directory does not exist, print an error with the attempted path and exit 1.

---

## Error Conditions

| Condition | Error Type | Message Pattern |
|---|---|---|
| `ANTHROPIC_API_KEY` not set | stderr + exit 1 | `"ANTHROPIC_API_KEY not set"` (unchanged) |
| Missing `--player` | stderr + exit 1 | `"Missing required argument: --player\n{usage}"` |
| Missing `--opponent` | stderr + exit 1 | `"Missing required argument: --opponent\n{usage}"` |
| Unknown argument | stderr + exit 1 | `"Unknown argument: --foo\n{usage}"` |
| Character not found | `FileNotFoundException` → stderr + exit 1 | `"Character '{name}' not found at {path}\nAvailable characters: {list}"` |
| Prompt file parse failure | `FormatException` → stderr + exit 1 | `"Failed to parse character '{name}': {detail}"` |
| `--max-turns` invalid | stderr + exit 1 | `"--max-turns must be a positive integer"` |
| `--agent` invalid | stderr + exit 1 | `"Invalid agent type '{value}'. Must be 'scoring' or 'llm'"` |
| Examples directory missing | stderr + exit 1 | `"Character directory not found: {path}"` |

All error paths write to `Console.Error` (stderr) and return exit code `1`. Exceptions from `CharacterLoader` are caught in `Main` and converted to user-friendly error messages.

---

## Dependencies

- **Pinder.Core.Characters.CharacterProfile** — the target type constructed by `CharacterLoader`.
- **Pinder.Core.Stats.StatBlock** — constructed from parsed stat/shadow dictionaries.
- **Pinder.Core.Stats.StatType** / **ShadowStatType** — enum values for stat dictionaries.
- **Pinder.Core.Conversation.TimingProfile** — default instance used for all prompt-file-loaded characters.
- **Pinder.Core.Conversation.GameSessionConfig** — for passing `SessionShadowTracker` (unchanged usage).
- **Pinder.Core.Stats.SessionShadowTracker** — constructed from loaded player stats (unchanged usage).
- **Pinder.SessionRunner.ScoringPlayerAgent** / **LlmPlayerAgent** — wired based on `--agent` argument.
- **Pinder.SessionRunner.TrapRegistryLoader** — existing, unchanged.
- **Prompt file directory** — external path `/root/.openclaw/agents-extra/pinder/design/examples/` containing `*-prompt.md` files. Files must follow the format described in Input/Output Examples.
- **No new NuGet dependencies.** `CharacterLoader` uses only `System.IO`, `System.Collections.Generic`, `System.Linq`, and `System` types available in .NET 8.

---

## Notes for Implementer

1. **Remove all hardcoded character data from Program.cs.** The variables `player1`, `player2`, `p1Level`, `p2Level`, `p1LevelBonus`, `p2LevelBonus`, `sableStats`, `brickStats`, and the `ExtractSystemPrompt` helper are all replaced by `CharacterLoader.Load()`.

2. **`ExtractSystemPrompt` logic moves into `CharacterLoader`.** The method already exists in `Program.cs` — extract it as a private helper inside `CharacterLoader` or reuse the same algorithm.

3. **Level bonus computation:** `CharacterProfile` does not expose `LevelBonus` directly. Compute it from the level via `Pinder.Core.Progression.LevelTable.GetLevelBonus(level)` if that method is available, or parse the `Level bonus: +N` line from the prompt file.

4. **The header output section** (character table, DC reference table) currently references `sableStats`/`brickStats` directly. After this change, use `playerProfile.Stats` and `opponentProfile.Stats`. Level and level bonus come from the profile.

5. **`--agent` supersedes `PLAYER_AGENT` env var.** The env var check (`Environment.GetEnvironmentVariable("PLAYER_AGENT")`) is removed. The `--agent` CLI argument is the sole source.

6. **Backward compatibility of session output format.** The markdown output format must remain identical — only the data values change. The playtest file naming (`session-NNN-{p1}-vs-{p2}.md`) uses the loaded `DisplayName` values.

7. **`--max-turns` default is 20** (per ADR #422). This replaces the hardcoded `turn < 15`.
