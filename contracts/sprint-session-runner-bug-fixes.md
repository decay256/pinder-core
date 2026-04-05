# Contract: Sprint — Session Runner Bug Fixes

## Architecture Overview

This sprint continues the existing architecture with no structural changes. All 5 issues are isolated bug fixes within `session-runner/` (the .NET 8 console app that drives automated playtesting). No changes to `Pinder.Core` game logic or `Pinder.LlmAdapters`. No new components, projects, or dependencies.

**Existing architecture summary**: `session-runner/` is a .NET 8 console app that creates `GameSession` + `AnthropicLlmAdapter` and runs automated playtests. Character loading flows through two paths: `CharacterDefinitionLoader` (JSON → `CharacterAssembler` pipeline) and `CharacterLoader` (prompt file parsing). `Program.cs` tries the assembler path first, then falls back to prompt files. `ScoringPlayerAgent` evaluates option expected-value for deterministic pick selection. `SessionFileCounter` resolves output directory and computes next session number.

**Components being extended**:
- `CharacterLoader` — ParseBio fix (#513), ParseLevel fix (#516)
- `Program.cs` — DC table header fix (#514), session number header fix (#515)
- `SessionFileCounter` — repeated write fix (#515)
- `ScoringPlayerAgent` — EV overestimation fix (#517)

**Implicit assumptions for implementers**:
1. **net8.0 + LangVersion 8.0** in session-runner
2. **All existing tests must pass** — changes are backward-compatible
3. **CharacterLoader.Parse* methods are `internal static`** — testable directly
4. **ScoringPlayerAgent is deterministic** — same inputs → same output
5. **Program.cs LoadCharacter tries assembler first** — prompt file is fallback

---

## Issue #513 — ParseBio returns empty (no quotes)

### Root Cause

`CharacterLoader.ParseBio()` searches for `"` delimiters around bio text. Actual prompt files have unquoted bios:
```
- Bio: I will absolutely judge your taste in music.
```
The method finds no `"` → returns `string.Empty`.

### Contract

**Component**: `CharacterLoader.ParseBio` (session-runner/CharacterLoader.cs)

**Current signature**: `private static string ParseBio(string content)`

**Fix**: After finding `- Bio:` line, extract everything after `- Bio: ` (trimmed). If quotes present, strip them. If no quotes, return the raw text after the colon.

**Postconditions**:
- Given `- Bio: text without quotes` → returns `"text without quotes"`
- Given `- Bio: "text with quotes"` → returns `"text with quotes"` (quotes stripped)
- Given `- Bio:` with nothing after → returns `string.Empty`
- Given no `- Bio:` line → returns `string.Empty`

**Test requirements**:
- Unit test for unquoted bio (all 5 characters)
- Unit test for quoted bio (backward compat)
- Unit test for missing bio line

**Files changed**: `session-runner/CharacterLoader.cs`, test file

---

## Issue #514 — DC table header hardcoded

### Root Cause

`Program.cs` line ~288 and ~303:
```csharp
Console.WriteLine("## DC Reference (Sable attacking, Brick defending)");
Console.WriteLine("| Stat | Sable mod | Brick defends | DC | Need | % | Risk |");
```
Character names are hardcoded as "Sable" and "Brick".

### Contract

**Component**: `Program.cs` DC table output section

**Fix**: Replace hardcoded names with `player1` and `player2` variables (already available in scope).

**Postconditions**:
- DC table header reads `## DC Reference ({player1} attacking, {player2} defending)`
- Column header reads `| Stat | {player1} mod | {player2} defends | DC | Need | % | Risk |`

**Test requirements**:
- No unit test needed — string interpolation fix
- Verified via playtest output review

**Files changed**: `session-runner/Program.cs`

---

## Issue #515 — SessionFileCounter writes session-006 repeatedly

### Root Cause (multi-part)

Two bugs combine:

1. **Hardcoded session number in output content**: `Program.cs` line ~288:
   ```csharp
   Console.WriteLine($"# Playtest Session 006 — {player1} × {player2}");
   ```
   The `006` is hardcoded — should use the counter's next number.

2. **Counter/writer path mismatch**: `WritePlaytestLog` calls `ResolvePlaytestDirectory(AppContext.BaseDirectory)` to get the directory, then `GetNextSessionNumber(dir)` to count existing files. If the actual playtest files live in a directory reachable via `PINDER_PLAYTESTS_PATH` but the env var is unset, the fallback `design/playtests/` walk may resolve to a different path (or fail), causing the counter to always see 0 files.

### Contract

**Component**: `SessionFileCounter` + `Program.cs` header

**Fix**:
1. Move session number computation BEFORE the markdown header so it can be interpolated.
2. Pass the session number into the header: `# Playtest Session {nextNum:D3} — ...`
3. Ensure `WritePlaytestLog` receives and uses the same session number (not re-computing it).
4. Verify `ResolvePlaytestDirectory` returns consistent paths.

**Refactoring needed**:
- `WritePlaytestLog` should accept the pre-computed session number as a parameter.
- Alternatively, compute the session number early (before writing output) and thread it through.

**Postconditions**:
- Each run produces a uniquely numbered file
- The content header matches the file number
- `PINDER_PLAYTESTS_PATH` is the canonical path when set

**Test requirements**:
- Unit test: `GetNextSessionNumber` with 0, 1, N files
- Unit test: `GetNextSessionNumber` returns correct number after file is written
- Integration-style: verify header matches filename number

**Files changed**: `session-runner/Program.cs`, `session-runner/SessionFileCounter.cs` (potentially)

---

## Issue #516 — ParseLevel reads wrong level (Velvet: 4 instead of 7)

### Root Cause

`LoadCharacter()` in `Program.cs` tries the `CharacterDefinitionLoader` (JSON) path FIRST before falling back to `CharacterLoader` (prompt file). The JSON definition files in `data/characters/` have stale/incorrect level values:

```json
// data/characters/velvet.json
{ "level": 4, ... }
```

But the prompt file (the source of truth after assembly) says:
```
- Level: 7 (Veteran) | Level bonus: +3
```

**Note**: This is NOT a parsing bug — both parsers work correctly for their respective inputs. The bug is stale data in JSON definition files.

### Contract

**Component**: `data/characters/*.json` (all 5 character definition files)

**Fix**: Update `"level"` field in each JSON file to match the prompt file values:

| Character | JSON current | Prompt file (correct) |
|-----------|-------------|----------------------|
| gerald    | verify      | 5                    |
| sable     | verify      | 3                    |
| velvet    | 4           | 7                    |
| brick     | verify      | 9                    |
| zyx       | verify      | 1                    |

**Postconditions**:
- `LoadCharacter("velvet", ...)` returns level 7 (not 4)
- All 5 characters load their correct level regardless of load path
- Unit test verifying all 5 JSON files have correct levels

**Test requirements**:
- Test that loads each JSON definition and asserts correct level
- Test that loads each prompt file and asserts correct level
- Comparison test: JSON level matches prompt file level for all characters

**Files changed**: `data/characters/*.json`, test file

---

## Issue #517 — ScoringPlayerAgent EV overestimation on low-success options

### Root Cause

In `ScoringPlayerAgent.DecideAsync()`, combo bonus (+1.0) and tell bonus (+2 to roll, increasing successChance) are applied at full value regardless of success probability. For an option with 15% success chance:
- Combo bonus adds +1.0 to `expectedGainOnSuccess`, but since success only happens 15% of the time, the actual EV contribution is only 0.15
- Tell bonus increases the effective modifier, but the EV formula doesn't account for the trap cost in the fail zone (miss by 6-9 = TropeTrap = -3 interest)

The current formula:
```
EV = successChance × (baseGain + riskBonus + comboBonus) − failChance × DefaultFailCost
```

Problems:
1. `comboBonus` contributes the same regardless of `successChance` (it's inside the success multiply, but the strategic scoring doesn't differentiate)
2. `DefaultFailCost = 1.5` is a flat average, ignoring that low-success options cluster failures in worse tiers (TropeTrap, Catastrophe)
3. Tell and callback bonuses increase the modifier, which DOES correctly flow through `need` → `successChance`, but the risk assessment doesn't account for trap exposure in the remaining failure space

### Contract

**Component**: `ScoringPlayerAgent` (session-runner/ScoringPlayerAgent.cs)

**Fix**: Scale conditional bonuses by success probability and apply tier-weighted failure costs.

**Changes**:
1. **Tier-weighted fail cost**: Replace flat `DefaultFailCost = 1.5` with a distribution based on where failures land:
   - If most failures land in miss 1-5 range: cost ≈ 1.5
   - If most failures land in miss 6-9 range (TropeTrap): cost ≈ 3.0
   - If most failures land in miss 10+ (Catastrophe): cost ≈ 4.0
   - Compute weighted average based on the `need` value

2. **No change to combo/tell bonus application**: These already multiply by successChance correctly (they're inside `expectedGainOnSuccess` which gets multiplied by `successChance`). The issue is the flat fail cost not reflecting trap exposure.

**Key formula change**:
```
failCost = WeightedFailCost(need)  // instead of flat 1.5

// Where WeightedFailCost distributes the 20 failure rolls across tiers:
// miss 1-2 → Fumble (-1), miss 3-5 → Misfire (-2),
// miss 6-9 → TropeTrap (-3), miss 10+ → Catastrophe (-4)
```

**Postconditions**:
- Given option with <20% success and TropeTrap-range miss, EV is lower than with flat fail cost
- Given option with >70% success, EV is approximately unchanged (most failures are Fumble/Misfire)
- Determinism preserved: same inputs → same output

**Test requirements**:
- Unit test: 15% success option with TropeTrap-range miss scores lower than same option at 50% success
- Unit test: high-success option (>70%) EV is approximately the same as before
- Unit test: combo bonus doesn't mask trap cost on low-success options
- Regression: existing scoring tests pass

**Files changed**: `session-runner/ScoringPlayerAgent.cs`, test file

---

## Separation of Concerns Map

- CharacterLoader
  - Responsibility:
    - Parse prompt files to CharacterProfile
    - Extract bio, level, stats, shadows, texting style
  - Interface:
    - Load(name, directory) → CharacterProfile
    - Parse(content, fallbackName) → CharacterProfile
    - ParseBio(content) → string
    - ParseLevel(content) → int
  - Must NOT know:
    - JSON character definitions
    - CharacterAssembler pipeline
    - GameSession or LLM details

- SessionFileCounter
  - Responsibility:
    - Count existing session files
    - Resolve playtest output directory
  - Interface:
    - GetNextSessionNumber(directory) → int
    - ResolvePlaytestDirectory(baseDir) → string?
  - Must NOT know:
    - Session content or format
    - Character loading
    - Game mechanics

- ScoringPlayerAgent
  - Responsibility:
    - Deterministic EV-based option scoring
    - Strategic adjustments for game state
  - Interface:
    - DecideAsync(TurnStart, PlayerAgentContext) → PlayerDecision
  - Must NOT know:
    - LLM communication
    - File I/O
    - Session management

- Program (session-runner entry)
  - Responsibility:
    - CLI argument parsing
    - Character loading orchestration
    - Session loop and output formatting
  - Interface:
    - Main(args) → exit code
  - Must NOT know:
    - CharacterLoader internals
    - ScoringPlayerAgent scoring formula
    - Pinder.Core internal state

---

## Implementation Strategy

### Recommended order

1. **#513 (ParseBio)** — Zero dependencies, isolated fix
2. **#516 (ParseLevel / JSON data)** — Zero dependencies, data fix
3. **#514 (DC table header)** — Zero dependencies, string fix
4. **#515 (SessionFileCounter)** — Requires understanding file flow
5. **#517 (ScoringPlayerAgent)** — Most complex, independent

All 5 issues are independent and can be implemented in parallel.

### Tradeoffs

- **#516**: Fixing the JSON data files is the right call. The alternative (preferring prompt file over assembler) would break the intended load priority. The JSON files are the source that should match prompt files.
- **#517**: The tier-weighted fail cost is a meaningful improvement. A simpler fix (just clamping EV at 0 for <20% success) would be faster but less accurate. The weighted approach is correct for prototype.
- **#515**: The hardcoded session number in the header should be dynamic. This requires restructuring `Main()` slightly to compute the session number before output starts, OR using a placeholder and replacing it later.

### Risk mitigation

- All fixes are in `session-runner/` only — zero risk to Pinder.Core's 2295+ tests
- Each fix is independently testable and deployable
- If #517's weighted fail cost is too complex, fall back to the simpler success-probability scaling of bonuses

---

## NFR Notes (Prototype)

- **Latency**: No latency targets — session runner is batch processing
- **Reliability**: ParseBio/ParseLevel must not throw on well-formed input
- **Backward compat**: All existing tests must pass unchanged

