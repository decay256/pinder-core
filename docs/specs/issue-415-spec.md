# Spec: Session Runner — CharacterAssembler + JSON Data Files

**Issue:** #415
**Module:** `docs/modules/session-runner.md` (create new)
**Depends on:** #414 (CLI arg parsing + CharacterLoader)

---

## Overview

The session runner currently loads pre-assembled prompt files from `design/examples/` with hardcoded stat blocks, bypassing the entire `CharacterAssembler` pipeline. This issue adds a `CharacterDefinitionLoader` that reads character definition JSON files and runs them through the real `CharacterAssembler` + `PromptBuilder` pipeline, producing `CharacterProfile` objects for `GameSession`. It also copies the required item and anatomy data files into the pinder-core repo and creates starter character definition files.

This enables end-to-end testing of the character assembly pipeline in simulation and eliminates prompt file drift.

---

## Function Signatures

### CharacterDefinitionLoader

```csharp
namespace Pinder.SessionRunner
{
    /// <summary>
    /// Loads a character definition JSON file and runs the full
    /// CharacterAssembler + PromptBuilder pipeline to produce a CharacterProfile.
    /// </summary>
    public static class CharacterDefinitionLoader
    {
        /// <summary>
        /// Load a character definition from a JSON file and assemble it into
        /// a CharacterProfile ready for GameSession.
        /// </summary>
        /// <param name="jsonPath">Absolute or relative path to the character definition JSON file.</param>
        /// <param name="itemRepo">An IItemRepository loaded from starter-items.json.</param>
        /// <param name="anatomyRepo">An IAnatomyRepository loaded from anatomy-parameters.json.</param>
        /// <returns>A fully assembled CharacterProfile.</returns>
        /// <exception cref="System.IO.FileNotFoundException">
        ///   The file at <paramref name="jsonPath"/> does not exist.
        /// </exception>
        /// <exception cref="System.FormatException">
        ///   The JSON is malformed or missing required fields (name, gender_identity, bio, level,
        ///   items, anatomy, build_points).
        /// </exception>
        public static CharacterProfile Load(
            string jsonPath,
            IItemRepository itemRepo,
            IAnatomyRepository anatomyRepo);
    }
}
```

### DataFileLocator

```csharp
namespace Pinder.SessionRunner
{
    /// <summary>
    /// Resolves paths to data files by walking up from a base directory.
    /// Follows the same pattern as TrapRegistryLoader.
    /// </summary>
    public static class DataFileLocator
    {
        /// <summary>
        /// Find a data file by walking up from baseDir.
        /// Checks: {baseDir}/{relativePath}, then parent directories.
        /// Also checks the PINDER_DATA_PATH environment variable.
        /// </summary>
        /// <param name="baseDir">Starting directory for the search.</param>
        /// <param name="relativePath">Relative path to the data file (e.g. "data/items/starter-items.json").</param>
        /// <returns>Absolute path to the file, or null if not found.</returns>
        public static string? FindDataFile(string baseDir, string relativePath);

        /// <summary>
        /// Find the repo root by walking up from baseDir, looking for a directory
        /// that contains both "data" and "src" subdirectories.
        /// </summary>
        /// <param name="baseDir">Starting directory for the search.</param>
        /// <returns>Absolute path to the repo root, or null if not found.</returns>
        public static string? FindRepoRoot(string baseDir);
    }
}
```

### Program.cs Extensions (wiring)

No new public methods. `Program.Main` gains support for:

- `--player-def <path>` — load player character from a definition JSON file via `CharacterDefinitionLoader.Load()`
- `--opponent-def <path>` — load opponent character from a definition JSON file via `CharacterDefinitionLoader.Load()`
- `--player <name>` shorthand resolves to `data/characters/{name}.json` via `DataFileLocator`, then delegates to `CharacterDefinitionLoader.Load()`

---

## Character Definition JSON Schema

Each character definition file lives at `data/characters/{name}.json` and has this structure:

```json
{
  "name": "Gerald_42",
  "gender_identity": "he/him",
  "bio": "Just a normal guy who loves the gym, good food, and real connections.",
  "level": 5,
  "items": [
    "backwards-snapback",
    "salmon-polo-collar-popped",
    "chinos-too-tight",
    "boat-shoes-no-socks",
    "apple-watch",
    "gym-membership-keychain"
  ],
  "anatomy": {
    "length": "long",
    "girth": "average",
    "circumcision": "circumcised",
    "veins": "prominent",
    "texture": "natural",
    "balls": "substantial",
    "tattoos": "none",
    "eyes": "wide"
  },
  "build_points": {
    "charm": 6,
    "rizz": 5,
    "honesty": 2,
    "chaos": 4,
    "wit": 2,
    "self_awareness": 2
  },
  "shadows": {
    "madness": 5,
    "horniness": 0,
    "denial": 2,
    "fixation": 4,
    "dread": 3,
    "overthinking": 2
  }
}
```

**Required fields:** `name` (string), `gender_identity` (string), `bio` (string), `level` (int, 1–11), `items` (string[]), `anatomy` (object), `build_points` (object with all 6 stat keys).

**Optional field:** `shadows` (object) — defaults to all zeros if omitted.

**Stat key mapping:**
- `build_points` keys: `charm`, `rizz`, `honesty`, `chaos`, `wit`, `self_awareness` → `StatType.Charm`, `StatType.Rizz`, `StatType.Honesty`, `StatType.Chaos`, `StatType.Wit`, `StatType.SelfAwareness`
- `shadows` keys: `madness`, `horniness`, `denial`, `fixation`, `dread`, `overthinking` → `ShadowStatType.Madness`, `ShadowStatType.Horniness`, `ShadowStatType.Denial`, `ShadowStatType.Fixation`, `ShadowStatType.Dread`, `ShadowStatType.Overthinking`

---

## Assembly Pipeline (within `CharacterDefinitionLoader.Load`)

1. **Read file** — `File.ReadAllText(jsonPath)`. Throw `FileNotFoundException` if missing.
2. **Parse JSON** — Use `System.Text.Json` (available in session-runner's net8.0 target). Throw `FormatException` on malformed JSON or missing required fields.
3. **Load repositories** — The caller passes pre-loaded `IItemRepository` and `IAnatomyRepository` instances (constructed from `data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json` respectively via `new JsonItemRepository(json)` and `new JsonAnatomyRepository(json)`).
4. **Call CharacterAssembler.Assemble()** — Pass extracted `items` (as `IEnumerable<string>`), `anatomy` (as `IReadOnlyDictionary<string, string>`), `build_points` (as `IReadOnlyDictionary<StatType, int>`), `shadows` (as `IReadOnlyDictionary<ShadowStatType, int>`). Returns `FragmentCollection`.
5. **Build system prompt** — Call `PromptBuilder.BuildSystemPrompt(name, gender_identity, bio, fragments, new TrapState())`. The `TrapState` is empty because no traps are active at session start.
6. **Construct CharacterProfile** — `new CharacterProfile(fragments.Stats, systemPrompt, name, fragments.Timing, level)`.
7. **Return** the `CharacterProfile`.

---

## Input/Output Examples

### Example 1: Load via `--player-def`

**Input (CLI):**
```bash
dotnet run --project session-runner -- --player-def data/characters/gerald.json --opponent-def data/characters/velvet.json
```

**Behavior:**
1. `CharacterDefinitionLoader.Load("data/characters/gerald.json", itemRepo, anatomyRepo)` → `CharacterProfile` with:
   - `DisplayName` = `"Gerald_42"`
   - `Stats` = `StatBlock` computed from build_points + item modifiers + anatomy modifiers
   - `AssembledSystemPrompt` = full prompt built from assembled fragments
   - `Level` = `5`
   - `Timing` = `TimingProfile` computed from item + anatomy timing modifiers
2. Same for velvet.json → opponent `CharacterProfile`
3. Both profiles passed to `GameSession` constructor

### Example 2: Load via `--player` shorthand

**Input (CLI):**
```bash
dotnet run --project session-runner -- --player gerald --opponent velvet
```

**Behavior:**
1. `--player gerald` → resolve `data/characters/gerald.json` via `DataFileLocator.FindDataFile(baseDir, "data/characters/gerald.json")`
2. If found → delegate to `CharacterDefinitionLoader.Load(resolvedPath, itemRepo, anatomyRepo)`
3. If NOT found → fall back to `CharacterLoader.Load("gerald", promptBasePath)` (the pre-assembled prompt file loader from #414)

### Example 3: DataFileLocator resolution

**Input:** `FindDataFile("/root/.openclaw/workspace/pinder-core/session-runner/bin/Debug/net8.0", "data/items/starter-items.json")`

**Search order:**
1. `/root/.openclaw/workspace/pinder-core/session-runner/bin/Debug/net8.0/data/items/starter-items.json` — not found
2. `/root/.openclaw/workspace/pinder-core/session-runner/bin/Debug/data/items/starter-items.json` — not found
3. `/root/.openclaw/workspace/pinder-core/session-runner/bin/data/items/starter-items.json` — not found
4. `/root/.openclaw/workspace/pinder-core/session-runner/data/items/starter-items.json` — not found
5. `/root/.openclaw/workspace/pinder-core/data/items/starter-items.json` — **found**, returns this path

Also checks `PINDER_DATA_PATH` env var first if set.

---

## Acceptance Criteria

### AC1: Runner accepts `--player-def <path>` and `--opponent-def <path>` args

`Program.Main` must parse `--player-def` and `--opponent-def` command-line arguments. Each takes a file path (absolute or relative) pointing to a character definition JSON file. When both are provided, the runner loads characters via `CharacterDefinitionLoader` instead of `CharacterLoader`. If only one is `--player-def` and the other is `--player`, that is valid — they can be mixed.

### AC2: CharacterAssembler is called with real item/anatomy data

`CharacterDefinitionLoader.Load()` must instantiate `CharacterAssembler` with `IItemRepository` and `IAnatomyRepository` loaded from actual JSON data files (`data/items/starter-items.json` and `data/anatomy/anatomy-parameters.json`). It must call `assembler.Assemble()` — not bypass or mock the pipeline.

### AC3: Stat block computed from items + anatomy + build points (not hardcoded)

The returned `CharacterProfile.Stats` must be the `StatBlock` produced by `FragmentCollection.Stats` (i.e., `CharacterAssembler.Assemble()` output). The stats reflect the sum of `build_points` + item stat modifiers + anatomy tier stat modifiers. They must NOT be hardcoded `StatBlock` dictionaries as in the current `Program.cs`.

### AC4: System prompt assembled from fragments (not loaded from `design/examples/`)

The returned `CharacterProfile.AssembledSystemPrompt` must be produced by `PromptBuilder.BuildSystemPrompt()` using the `FragmentCollection` from `CharacterAssembler.Assemble()`. It must NOT be loaded from `design/examples/{name}-prompt.md`.

### AC5: Starter character definition files created for all 5 characters

The following files must exist in the repo:
- `data/characters/gerald.json`
- `data/characters/velvet.json`
- `data/characters/sable.json`
- `data/characters/brick.json`
- `data/characters/zyx.json`

Each must conform to the JSON schema defined above with valid item IDs (matching `starter-items.json`) and anatomy parameter/tier IDs (matching `anatomy-parameters.json`).

### AC6: Shorthand `--player gerald` maps to `data/characters/gerald.json`

When `--player <name>` is used (without `--player-def`), the runner must:
1. Attempt to resolve `data/characters/{name}.json` via `DataFileLocator`
2. If found, load via `CharacterDefinitionLoader.Load()`
3. If NOT found, fall back to `CharacterLoader.Load()` (prompt file parsing from #414)

Same behavior for `--opponent <name>`.

### AC7: Build clean, all tests pass

`dotnet build` must succeed with zero errors. All existing tests (`dotnet test`) must pass. No breaking changes to Pinder.Core or Pinder.LlmAdapters.

---

## Data Files to Add

### `data/items/starter-items.json`

Copy from `/root/.openclaw/agents-extra/pinder/data/items/starter-items.json`. This file contains item definitions with IDs, stat modifiers, personality/backstory/texting fragments, archetype tendencies, and timing modifiers. It is consumed by `new JsonItemRepository(json)`.

### `data/anatomy/anatomy-parameters.json`

Copy from `/root/.openclaw/agents-extra/pinder/data/anatomy/anatomy-parameters.json`. This file contains anatomy parameter definitions with tier IDs, stat modifiers, fragments, and timing modifiers. It is consumed by `new JsonAnatomyRepository(json)`.

### `data/characters/*.json`

Five new files (one per starter character), conforming to the schema above. Values must match the character designs from the external `pinder` repo. Item IDs must exist in `starter-items.json`; anatomy tier IDs must exist in `anatomy-parameters.json`.

---

## Edge Cases

1. **Missing item IDs** — `CharacterAssembler.Assemble()` silently skips item IDs not found in the repository. The loader should NOT throw on missing items; the assembler handles this gracefully.

2. **Missing anatomy parameters/tiers** — Similarly silently skipped by `CharacterAssembler.Assemble()`. The loader should NOT throw.

3. **Missing `shadows` field in JSON** — Default all shadow stats to 0. This is a valid character with no shadow growth.

4. **Empty `items` array** — Valid. Character has no equipped items. Stats come from `build_points` only.

5. **Empty `anatomy` object** — Valid. Character has no anatomy selections. No anatomy modifiers applied.

6. **Character name with special characters** — The `name` field is passed directly to `CharacterProfile.DisplayName`. No sanitization needed.

7. **`--player-def` path does not exist** — `CharacterDefinitionLoader.Load()` throws `FileNotFoundException`. `Program.Main` should catch this and print a helpful error message, then exit with code 1.

8. **`--player gerald` but `data/characters/gerald.json` not found** — Fall back to `CharacterLoader.Load("gerald", promptBasePath)`. If that also fails, print error and exit 1.

9. **Item/anatomy data files not found** — If `DataFileLocator` cannot resolve `starter-items.json` or `anatomy-parameters.json`, the runner should print a warning and fall back to `CharacterLoader` (prompt file path). Do NOT crash silently.

10. **`build_points` missing a stat key** — `CharacterAssembler.Assemble()` calls `TryGetValue` on the dictionary, so missing keys default to 0. The loader may choose to throw `FormatException` for missing keys or allow defaults — either is acceptable, but at minimum `charm`, `rizz`, `honesty`, `chaos`, `wit`, `self_awareness` should be present.

11. **Concurrent loading** — Not applicable. Session runner is single-threaded at startup.

---

## Error Conditions

| Condition | Exception Type | Message Pattern |
|-----------|---------------|-----------------|
| JSON file not found | `FileNotFoundException` | `"Character definition file not found: {path}"` |
| JSON parse failure | `FormatException` | `"Failed to parse character definition: {detail}"` |
| Missing required field (`name`, `gender_identity`, `bio`, `level`, `items`, `anatomy`, `build_points`) | `FormatException` | `"Character definition missing required field: {fieldName}"` |
| `level` out of range (< 1 or > 11) | `FormatException` | `"Character level must be between 1 and 11, got: {value}"` |
| Unrecognized stat key in `build_points` | `FormatException` | `"Unknown stat type: {key}"` |
| Unrecognized shadow key in `shadows` | `FormatException` | `"Unknown shadow stat type: {key}"` |
| Item/anatomy data files not resolvable | No exception — graceful fallback | Warning to stderr: `"[WARN] Could not find {file} — falling back to prompt file loading"` |

---

## Dependencies

- **Pinder.Core.Characters.CharacterAssembler** — the assembly pipeline. Takes `IItemRepository`, `IAnatomyRepository`, calls `Assemble()` returning `FragmentCollection`.
- **Pinder.Core.Characters.FragmentCollection** — output of `Assemble()`. Contains `Stats` (`StatBlock`), `Timing` (`TimingProfile`), and prompt fragments.
- **Pinder.Core.Characters.CharacterProfile** — the final product. Constructor: `(StatBlock, string, string, TimingProfile, int)`.
- **Pinder.Core.Prompts.PromptBuilder** — `BuildSystemPrompt(displayName, genderIdentity, bioOneLiner, fragments, activeTraps)` returns `string`.
- **Pinder.Core.Traps.TrapState** — `new TrapState()` for an empty trap state during prompt building.
- **Pinder.Core.Data.JsonItemRepository** — `new JsonItemRepository(string json)` parses item JSON.
- **Pinder.Core.Data.JsonAnatomyRepository** — `new JsonAnatomyRepository(string json)` parses anatomy JSON.
- **Pinder.Core.Interfaces.IItemRepository** — interface for item lookup.
- **Pinder.Core.Interfaces.IAnatomyRepository** — interface for anatomy parameter lookup.
- **Pinder.Core.Stats.StatType** — enum: `Charm, Rizz, Honesty, Chaos, Wit, SelfAwareness`.
- **Pinder.Core.Stats.ShadowStatType** — enum: `Madness, Horniness, Denial, Fixation, Dread, Overthinking`.
- **System.Text.Json** — available in session-runner (net8.0). Used for parsing character definition JSON. Do NOT add to Pinder.Core.
- **#414 (CLI arg parsing + CharacterLoader)** — must be merged first. Provides the `--player`/`--opponent` arg parsing infrastructure and `CharacterLoader` as fallback.
- **External data files** — `starter-items.json` and `anatomy-parameters.json` from `/root/.openclaw/agents-extra/pinder/data/`.
