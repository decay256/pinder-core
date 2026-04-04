# Contract: Sim Runner + Scorer Improvements

## Architecture Overview

This sprint continues the existing architecture with no structural
changes to Pinder.Core or Pinder.LlmAdapters. All changes are
confined to `session-runner/` (the .NET 8 console app) with the
exception of copying data files from the external `pinder` repo
into `pinder-core` so the assembly pipeline can run standalone.

**Existing architecture summary**: Pinder.Core is a zero-dependency
.NET Standard 2.0 RPG engine. `GameSession` orchestrates
single-conversation turns, delegating to `RollEngine` (stateless),
`InterestMeter`, `TrapState`, `SessionShadowTracker`, `ComboTracker`,
and `XpLedger`. `Pinder.LlmAdapters` implements `ILlmAdapter` via
`AnthropicLlmAdapter`. The session runner (`session-runner/`) is a
.NET 8 console app that drives `GameSession` + `AnthropicLlmAdapter`
for automated playtesting. Player agent types (`IPlayerAgent`,
`ScoringPlayerAgent`, `LlmPlayerAgent`) live in `session-runner/`.

**No Pinder.Core public API changes this sprint.** All five issues
touch only `session-runner/` code and data files.

### Components being extended

- `SessionFileCounter` — bug fix (#418)
- `Program.cs` — CLI arg parsing, max-turns, projection (#414, #417)
- `ScoringPlayerAgent` — shadow growth risk scoring (#416)
- `PlayerAgentContext` — gains stat history + shadow levels (#416)
- New: `CharacterLoader` — parse prompt files (#414)
- New: `CharacterDefinition` — JSON character def model (#415)
- New: `CharacterDefinitionLoader` — assemble via pipeline (#415)
- New: `DataFileLocator` — resolve data file paths (#415)

### Vision concerns acknowledged

- **#419**: `CharacterAssembler.Assemble()` returns
  `FragmentCollection`, not `CharacterProfile`. The #415
  implementer must bridge: call `PromptBuilder.BuildSystemPrompt()`
  on the fragments, then construct `CharacterProfile` manually.
- **#421**: Item/anatomy JSON data files don't exist in pinder-core.
  Sprint must copy them in from external repo OR use path resolution
  (decision: copy them in — see ADR below).
- **#422**: `--max-turns` arg conflict between #414 and #417.
  Decision: #414 adds the arg with default 20 (adopting #417's
  value). #417 only adds the projection logic.

---

## Separation of Concerns Map

- SessionFileCounter (session-runner)
  - Responsibility:
    - Scan playtest directory for highest session number
    - Return next available number
  - Interface:
    - GetNextSessionNumber(string directory) → int
  - Must NOT know:
    - File content or markdown format
    - GameSession internals
    - How the path is constructed

- CharacterLoader (session-runner) — NEW
  - Responsibility:
    - Parse `design/examples/{name}-prompt.md` files
    - Extract stat block, shadow values, level, system prompt
    - Return CharacterProfile ready for GameSession
  - Interface:
    - Load(string name, string basePath) → CharacterProfile
    - Throws: FileNotFoundException if prompt file missing
    - Throws: FormatException if parsing fails
  - Must NOT know:
    - GameSession internals
    - LLM transport
    - CharacterAssembler pipeline (that's CharacterDefinitionLoader)

- CharacterDefinitionLoader (session-runner) — NEW
  - Responsibility:
    - Load character definition JSON files
    - Run CharacterAssembler + PromptBuilder pipeline
    - Return CharacterProfile
  - Interface:
    - Load(string path, IItemRepository, IAnatomyRepository)
      → CharacterProfile
    - Throws: FileNotFoundException, FormatException
  - Must NOT know:
    - GameSession internals
    - LLM transport
    - Prompt file parsing (that's CharacterLoader)

- DataFileLocator (session-runner) — NEW
  - Responsibility:
    - Resolve paths to data files (items, anatomy, traps, chars)
    - Walk up from base directory like TrapRegistryLoader
  - Interface:
    - FindDataFile(string baseDir, string relativePath) → string?
    - FindRepoRoot(string baseDir) → string?
  - Must NOT know:
    - File content or parsing
    - GameSession internals

- ScoringPlayerAgent (session-runner) — EXTENDED
  - Responsibility:
    - Score options using expected-value formula
    - Apply shadow growth penalty for stat repetition
    - Apply Denial penalty for skipping Honesty
    - Reduce Chaos EV at Fixation thresholds
  - Interface:
    - Implements IPlayerAgent.DecideAsync (unchanged)
    - Pure function: same input → same output
  - Must NOT know:
    - LLM APIs
    - Session file output format
    - GameSession internal state

- PlayerAgentContext (session-runner) — EXTENDED
  - Responsibility:
    - Carry game state for agent decision-making
  - Interface:
    - Existing fields (unchanged)
    - New: LastStatUsed (StatType?) — stat used on previous turn
    - New: SecondLastStatUsed (StatType?) — stat two turns ago
    - New: HonestyAvailableLastTurn (bool)
  - Must NOT know:
    - How the agent uses these fields
    - GameSession internals

- Program.cs (session-runner) — EXTENDED
  - Responsibility:
    - CLI arg parsing
    - Wire up GameSession + IPlayerAgent + output
    - Projected outcome on max-turns cutoff
  - Interface:
    - `--player <name>` (required)
    - `--opponent <name>` (required)
    - `--player-def <path>` (alternative to --player)
    - `--opponent-def <path>` (alternative to --opponent)
    - `--max-turns <n>` (default: 20)
    - `--agent <scoring|llm>` (default: scoring)
    - Exit 0: success, Exit 1: error
  - Must NOT know:
    - Roll resolution internals
    - LLM prompt assembly details

---

## ADR: Copy data files into pinder-core repo

**Context:** #415 requires `starter-items.json` and
`anatomy-parameters.json` for the CharacterAssembler pipeline.
These files exist in the external `pinder` repo at
`/root/.openclaw/agents-extra/pinder/data/`. Vision concern #421
flagged this gap.

**Decision:** Copy data files into `pinder-core` at:
- `data/items/starter-items.json`
- `data/anatomy/anatomy-parameters.json`

This matches the existing pattern: `data/traps/traps.json` already
lives in pinder-core.

**Consequences:** Data files are duplicated across repos. Acceptable
at prototype maturity. A sync mechanism or shared submodule can be
added at MVP.

## ADR: --max-turns owned by #414, projection by #417

**Context:** Vision concern #422 flagged that both #414 and #417
add `--max-turns`. They have conflicting defaults (15 vs 20).

**Decision:** #414 adds all CLI arg parsing including `--max-turns`
with default 20 (adopting #417's recommended value). #417 focuses
only on the projection/reporting logic when max turns is hit. This
eliminates the merge conflict.

**Consequences:** #414 must use default 20 (not 15 as originally
specified). #417 can assume `--max-turns` already exists.

## ADR: CharacterAssembler → CharacterProfile bridging

**Context:** Vision concern #419 flagged that `CharacterAssembler
.Assemble()` returns `FragmentCollection`, not `CharacterProfile`.
`GameSession` needs `CharacterProfile`.

**Decision:** `CharacterDefinitionLoader` handles the full bridge:
1. Parse JSON definition file
2. Call `CharacterAssembler.Assemble(itemIds, anatomy, baseStats, shadows)`
   → `FragmentCollection`
3. Call `PromptBuilder.BuildSystemPrompt(name, gender, bio, fragments, trapState)`
   → `string`
4. Construct `CharacterProfile(fragments.Stats, prompt, name, fragments.Timing, level)`

**Consequences:** The loader needs a `TrapState` for prompt building.
Use `new TrapState()` (empty — no active traps at session start).

---

## Per-Issue Interface Definitions

### #418 — Fix file counter

**Files changed:** `session-runner/SessionFileCounter.cs`

**Problem:** Counter returns 1 even when sessions exist. Likely a
path resolution issue — `WritePlaytestLog` passes a hardcoded
absolute path, but the counter scans correctly.

**Contract:** `SessionFileCounter.GetNextSessionNumber(string dir)`
— no signature change. Fix the bug (likely path mismatch between
where files are written and where they're scanned). Add a test
that creates temp files matching the naming convention and asserts
correct numbering.

**Behavioral contract:**
- Pre: `directory` exists and is readable
- Post: returns max(existing session numbers) + 1, or 1 if none
- File naming: `session-NNN-*.md` where NNN is zero-padded
- Must handle: `session-008-gerald42-vs-zyx.md` (name with digits)

**NFR (prototype):** No latency target — runs once per session.

---

### #414 — CLI arg parsing + CharacterLoader

**Files changed:**
- `session-runner/Program.cs` — replace hardcoded characters with
  arg parsing and CharacterLoader calls
- `session-runner/CharacterLoader.cs` — NEW

**CLI contract:**
```
dotnet run --project session-runner -- \
  --player <name> --opponent <name> \
  [--max-turns <n>] [--agent scoring|llm]

dotnet run --project session-runner -- \
  --player-def <path> --opponent-def <path> \
  [--max-turns <n>] [--agent scoring|llm]

# No args or invalid args → print usage + available characters
# Exit 1 on invalid args
```

**`--max-turns` default: 20** (per #422 ADR — adopts #417 value).

**CharacterLoader contract:**
```csharp
namespace Pinder.SessionRunner
{
    public static class CharacterLoader
    {
        /// <summary>
        /// Load a CharacterProfile from a pre-assembled prompt file.
        /// </summary>
        /// <param name="name">Character name (e.g. "gerald")</param>
        /// <param name="basePath">Directory containing {name}-prompt.md</param>
        /// <returns>CharacterProfile ready for GameSession</returns>
        /// <exception cref="FileNotFoundException">
        ///   No file at {basePath}/{name}-prompt.md
        /// </exception>
        /// <exception cref="FormatException">
        ///   File does not contain expected sections
        /// </exception>
        public static CharacterProfile Load(
            string name, string basePath);
    }
}
```

**Parsing rules for prompt files:**
- System prompt: text between first ```` ``` ```` and last ```` ``` ````
- Level: line matching `- Level: N` → extract N
- Level bonus: line matching `Level bonus: +N` → extract N
  (or compute from LevelTable if available)
- Stats: lines under `EFFECTIVE STATS` matching `- Stat: +N`
- Shadows: lines under shadow section matching `ShadowType: N`
  (default 0 for missing shadows)

**Validation:**
- File must exist → FileNotFoundException with helpful message
  listing available characters
- Stats section must contain all 6 stats → FormatException
- Level must be parseable → FormatException

**NFR (prototype):** No latency target.

---

### #417 — Max turns increase + projected outcome

**Files changed:** `session-runner/Program.cs`

**Depends on:** #414 (which adds `--max-turns` arg parsing)

**Contract:** When the game loop exits due to max-turns cutoff
(not DateSecured/Unmatched/Ghost), output a projection block:

```markdown
## Session Summary
**⏸️ Incomplete ({turns}/{maxTurns} turns) | Interest: {n}/25 | Total XP: {xp}**

Projected: {projectionText}
```

**Projection logic (pure function):**
```csharp
namespace Pinder.SessionRunner
{
    public static class OutcomeProjector
    {
        /// <summary>
        /// Given final game state at cutoff, produce a human-readable
        /// projected outcome string.
        /// </summary>
        /// <param name="interest">Current interest value (0-25)</param>
        /// <param name="momentum">Current momentum streak</param>
        /// <param name="turnsPlayed">Turns completed</param>
        /// <param name="maxTurns">Turn cap</param>
        /// <returns>Projection text string</returns>
        public static string Project(
            int interest,
            int momentum,
            int turnsPlayed,
            int maxTurns);
    }
}
```

**Projection heuristics:**
- Interest ≥ 20 + momentum ≥ 3 → "Likely DateSecured"
- Interest ≥ 16 → "Probable DateSecured with continued play"
- Interest 10-15 → "Uncertain — could go either way"
- Interest 5-9 → "Trending toward Unmatched"
- Interest < 5 → "Likely Unmatched or Ghost"

**NFR (prototype):** No latency target.

---

### #416 — ScoringPlayerAgent shadow growth risk

**Files changed:**
- `session-runner/ScoringPlayerAgent.cs`
- `session-runner/PlayerAgentContext.cs`

**Depends on:** #346 (IPlayerAgent — already merged)

**PlayerAgentContext additions:**
```csharp
// New optional constructor params (defaults for backward compat):
public StatType? LastStatUsed { get; }        // null = first turn
public StatType? SecondLastStatUsed { get; }  // null = first/second turn
public bool HonestyAvailableLastTurn { get; } // false = unknown/first turn
```

All new params have defaults (null/false) so existing constructor
calls in tests and Program.cs compile unchanged.

**ScoringPlayerAgent scoring additions:**

1. **Fixation growth penalty** (§7: same stat 3x → +1 Fixation):
   ```
   if option.Stat == lastStatUsed
      && lastStatUsed == secondLastStatUsed:
     score -= 0.5
   ```

2. **Denial growth penalty** (§7: skip Honesty → +1 Denial):
   ```
   honestyInOptions = any option has Stat == Honesty
   if option.Stat != Honesty && honestyInOptions:
     score -= 0.3
   ```

3. **Fixation threshold EV reduction** (§7):
   ```
   fixation = shadowValues[ShadowStatType.Fixation]
   if option.Stat == Chaos:
     if fixation >= 12: apply disadvantage to successChance calc
     elif fixation >= 6: multiply expectedGainOnSuccess by 0.8
   ```

4. **Stat variety bonus:**
   ```
   if option.Stat not in {lastStatUsed, secondLastStatUsed}:
     score += 0.1
   ```

**Behavioral invariants:**
- All penalties/bonuses are additive to `score`
- When `ShadowValues` is null, skip shadow threshold checks
- When `LastStatUsed` is null, skip Fixation growth penalty
- Existing scoring logic unchanged — new terms are additive
- Deterministic: same inputs → same output

**NFR (prototype):** No latency target.

---

### #415 — CharacterAssembler integration

**Files changed:**
- `session-runner/CharacterDefinitionLoader.cs` — NEW
- `session-runner/DataFileLocator.cs` — NEW
- `session-runner/Program.cs` — wire up `--player-def` /
  `--opponent-def` args
- `data/items/starter-items.json` — NEW (copy from external repo)
- `data/anatomy/anatomy-parameters.json` — NEW (copy from external)
- `data/characters/gerald.json` — NEW
- `data/characters/velvet.json` — NEW
- `data/characters/sable.json` — NEW
- `data/characters/brick.json` — NEW
- `data/characters/zyx.json` — NEW

**Depends on:** #414 (CLI arg parsing)

**CharacterDefinition JSON schema:**
```json
{
  "name": "string (display name)",
  "gender_identity": "string (e.g. he/him)",
  "bio": "string (player-written bio line)",
  "level": "int (1-11)",
  "items": ["string (item IDs from starter-items.json)"],
  "anatomy": {
    "parameterId": "tierId"
  },
  "build_points": {
    "charm": "int", "rizz": "int", "honesty": "int",
    "chaos": "int", "wit": "int", "self_awareness": "int"
  },
  "shadows": {
    "madness": "int", "horniness": "int", "denial": "int",
    "fixation": "int", "dread": "int", "overthinking": "int"
  }
}
```

**CharacterDefinitionLoader contract:**
```csharp
namespace Pinder.SessionRunner
{
    public static class CharacterDefinitionLoader
    {
        /// <summary>
        /// Load a character definition JSON, run it through
        /// CharacterAssembler + PromptBuilder, return a
        /// CharacterProfile ready for GameSession.
        /// </summary>
        /// <param name="jsonPath">Path to character def JSON</param>
        /// <param name="itemRepo">Loaded item repository</param>
        /// <param name="anatomyRepo">Loaded anatomy repository</param>
        /// <returns>CharacterProfile</returns>
        /// <exception cref="FileNotFoundException">
        ///   JSON file not found
        /// </exception>
        /// <exception cref="FormatException">
        ///   JSON malformed or missing required fields
        /// </exception>
        public static CharacterProfile Load(
            string jsonPath,
            IItemRepository itemRepo,
            IAnatomyRepository anatomyRepo);
    }
}
```

**Assembly pipeline (in CharacterDefinitionLoader.Load):**
1. Read JSON file → parse with Newtonsoft.Json (session-runner
   is net8.0, can use System.Text.Json or Newtonsoft)
2. Extract fields: name, gender, bio, level, items[], anatomy{},
   build_points{}, shadows{}
3. Call `assembler.Assemble(items, anatomy, buildPoints, shadows)`
   → `FragmentCollection`
4. Call `PromptBuilder.BuildSystemPrompt(name, gender, bio,
   fragments, new TrapState())` → `string systemPrompt`
5. Compute level from `LevelTable` or use def.level directly
6. Return `new CharacterProfile(fragments.Stats, systemPrompt,
   name, fragments.Timing, level)`

**DataFileLocator contract:**
```csharp
namespace Pinder.SessionRunner
{
    public static class DataFileLocator
    {
        /// <summary>
        /// Find a data file by walking up from baseDir.
        /// Checks: {baseDir}/{relativePath}, then walks up
        /// parent directories looking for the file.
        /// Also checks PINDER_DATA_PATH env var.
        /// </summary>
        public static string? FindDataFile(
            string baseDir, string relativePath);

        /// <summary>
        /// Find the repo root by looking for a directory
        /// containing data/ and src/ subdirectories.
        /// </summary>
        public static string? FindRepoRoot(string baseDir);
    }
}
```

**--player shorthand mapping:**
`--player gerald` → `data/characters/gerald.json`
(resolved via DataFileLocator)

**NFR (prototype):** No latency target.

---

## Implementation Order

```
Wave 0 (no dependencies):
  #418 — file counter fix (isolated, 1 file)

Wave 1 (no inter-issue dependencies):
  #416 — scorer shadow growth (isolated to ScoringPlayerAgent
         + PlayerAgentContext)

Wave 2:
  #414 — CLI arg parsing + CharacterLoader
         (prerequisite for #415 and #417)

Wave 3 (depends on #414):
  #417 — max turns + projection (needs --max-turns from #414)
  #415 — CharacterAssembler integration (needs CLI from #414)
```

**Parallel opportunities:**
- #418 and #416 can run in parallel with each other and with
  everything else
- #417 and #415 can run in parallel after #414 merges

## Tradeoffs

**Shortcut: copying data files** — duplicating JSON data between
repos is technical debt. Acceptable at prototype: the files are
small (<50KB each) and change rarely. At MVP, use a git submodule
or shared NuGet package.

**Shortcut: projection heuristics** — the projected outcome in
#417 uses simple interest thresholds, not Monte Carlo simulation.
Good enough for playtest reporting. Can be made more sophisticated
later.

**Foundation: CharacterDefinitionLoader** — investing in the full
CharacterAssembler pipeline now (#415) pays off immediately: any
new character is a JSON file, not C# code changes. This is the
right call.

**Foundation: shadow-aware scorer** — #416 makes the scorer more
realistic, which improves playtest quality. The penalty constants
(0.5, 0.3, 0.1) are tuning knobs that can be adjusted based on
observed sessions.

## Risk Mitigation

- **#415 data file risk**: If `starter-items.json` or
  `anatomy-parameters.json` don't parse correctly with
  Pinder.Core's `JsonItemRepository`/`JsonAnatomyRepository`,
  fall back to CharacterLoader (prompt file parsing) as the
  default path. The assembler path is additive, not a replacement.
- **#418 path resolution risk**: If the bug is in path resolution
  between build output and source dir, the fix may need
  `AppContext.BaseDirectory` adjustment. Test with actual directory.
- **#416 constant tuning**: Shadow penalty constants are educated
  guesses. Add them as named constants (not magic numbers) so they
  can be tuned based on session data.

---

## Implicit Assumptions for Implementers

1. **netstandard2.0 + LangVersion 8.0** in Pinder.Core.
   Session runner is net8.0 but uses LangVersion 8.0 per csproj.
2. **Zero NuGet dependencies in Pinder.Core** — do not add any.
3. **Session runner CAN use System.Text.Json** (net8.0 built-in)
   for parsing character definition JSON. No need for Newtonsoft
   in session-runner for this.
4. **`CharacterAssembler.Assemble()` returns `FragmentCollection`**
   — NOT `CharacterProfile`. Caller must bridge (see #419).
5. **`PromptBuilder.BuildSystemPrompt()` requires `TrapState`**
   — use `new TrapState()` for initial prompt generation.
6. **`PlayerAgentContext` constructor changes must be backward-
   compatible** — all new params must have defaults.
7. **All existing tests must pass** — no breaking changes.
8. **External data files** live at
   `/root/.openclaw/agents-extra/pinder/data/` — copy into
   pinder-core `data/` directory.
9. **Playtest output directory** is an external absolute path:
   `/root/.openclaw/agents-extra/pinder/design/playtests/`
