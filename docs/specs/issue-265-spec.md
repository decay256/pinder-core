# Specification: Issue #265 — Create data/traps/traps.json with all 6 canonical trap definitions

**Module**: docs/modules/data.md (create new)

---

## Overview

The `JsonTrapRepository` class is ready to load trap data from a JSON file, but the actual `data/traps/traps.json` file does not exist. This issue creates the canonical JSON data file containing all 6 trap definitions (one per `StatType`) and an accompanying JSON schema file. The 6 traps encode the game's "trope trap" mechanic — negative status effects triggered when a player's roll misses by 6–9 on a given stat.

---

## Critical: JSON Schema Must Match Parser

**The issue body contains a nested JSON schema** (e.g., `mechanical_effect.type`, `prompt_taint.llm_instruction`, PascalCase stat names like `"Charm"`). **This schema is incompatible with `JsonTrapRepository.ParseTrap()`.**

The parser (`src/Pinder.Core/Data/JsonTrapRepository.cs`) reads **flat fields** with **lowercase/snake_case keys**. The implementation MUST use the flat schema documented below.

### Parser-Expected Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | `string` | Yes | — | Unique trap identifier (e.g., `"cringe"`) |
| `stat` | `string` | Yes | — | Lowercase stat key: `"charm"`, `"rizz"`, `"honesty"`, `"chaos"`, `"wit"`, `"self_awareness"` |
| `effect` | `string` | Yes | — | Effect type: `"disadvantage"`, `"stat_penalty"`, `"opponent_dc_increase"` |
| `effect_value` | `int` | Yes | — | Magnitude of the effect (0 for Disadvantage, positive int for penalties/DC increases) |
| `duration_turns` | `int` | No | `3` | Number of turns the trap lasts |
| `llm_instruction` | `string` | Yes | — | Prompt taint text passed to the LLM layer |
| `clear_method` | `string` | No | `""` | How to clear the trap early |
| `nat1_bonus` | `string` | No | `""` | Extra punishment on Nat 1 while trap is active |

### Parser-Accepted Stat Keys

These are the exact string values accepted by `JsonTrapRepository.TryParseStatType()`:

- `"charm"` → `StatType.Charm`
- `"rizz"` → `StatType.Rizz`
- `"honesty"` → `StatType.Honesty`
- `"chaos"` → `StatType.Chaos`
- `"wit"` → `StatType.Wit`
- `"self_awareness"` → `StatType.SelfAwareness`

### Parser-Accepted Effect Keys

These are the exact string values accepted by `JsonTrapRepository.TryParseTrapEffect()`:

- `"disadvantage"` → `TrapEffect.Disadvantage`
- `"stat_penalty"` → `TrapEffect.StatPenalty`
- `"opponent_dc_increase"` → `TrapEffect.OpponentDCIncrease`

---

## Function Signatures

No new code functions are introduced. The existing `JsonTrapRepository` API is used:

```csharp
// Constructor — caller passes JSON string contents of traps.json
public JsonTrapRepository(string json)

// Retrieve trap by stat
public TrapDefinition? GetTrap(StatType stat)

// Retrieve LLM instruction text by stat
public string? GetLlmInstruction(StatType stat)

// Retrieve all loaded traps
public IEnumerable<TrapDefinition> GetAll()
```

The `TrapDefinition` value object returned has these properties:

```csharp
public sealed class TrapDefinition
{
    public string Id          { get; }  // e.g., "cringe"
    public StatType Stat      { get; }  // e.g., StatType.Charm
    public TrapEffect Effect  { get; }  // e.g., TrapEffect.Disadvantage
    public int EffectValue    { get; }  // e.g., 0
    public int DurationTurns  { get; }  // e.g., 1
    public string LlmInstruction { get; } // prompt taint text
    public string ClearMethod { get; }  // e.g., "SA vs DC 12"
    public string Nat1Bonus   { get; }  // e.g., ""
}
```

---

## Input/Output Examples

### Example: Loading traps.json

**Input** (partial — one trap):
```json
[
  {
    "id": "cringe",
    "stat": "charm",
    "effect": "disadvantage",
    "effect_value": 0,
    "duration_turns": 1,
    "llm_instruction": "You are aware of how you're coming across, which is making it worse. Every message is slightly over-explained or self-undermined. The more you try to be charming, the harder you try.",
    "clear_method": "SA vs DC 12",
    "nat1_bonus": ""
  }
]
```

**Output** (via `GetTrap(StatType.Charm)`):
```
TrapDefinition {
  Id = "cringe",
  Stat = StatType.Charm,
  Effect = TrapEffect.Disadvantage,
  EffectValue = 0,
  DurationTurns = 1,
  LlmInstruction = "You are aware of how you're coming across...",
  ClearMethod = "SA vs DC 12",
  Nat1Bonus = ""
}
```

### Example: Missing stat query

**Input**: `GetTrap(StatType.Charm)` when `traps.json` has no charm entry.

**Output**: `null`

---

## Complete Trap Data Table

All 6 traps that MUST appear in `data/traps/traps.json`:

| id | stat | effect | effect_value | duration_turns | clear_method | llm_instruction (summary) |
|----|------|--------|-------------|----------------|--------------|---------------------------|
| `cringe` | `charm` | `disadvantage` | `0` | `1` | `SA vs DC 12` | Over-explained, self-undermined messages |
| `creep` | `rizz` | `stat_penalty` | `2` | `2` | `SA vs DC 12` | Subtle "agenda" quality in messages |
| `overshare` | `honesty` | `opponent_dc_increase` | `2` | `1` | `SA vs DC 12` | Accidental personal details intrude |
| `unhinged` | `chaos` | `disadvantage` | `0` | `1` | `SA vs DC 12` | Momentum/acceleration, messages derail |
| `pretentious` | `wit` | `opponent_dc_increase` | `3` | `1` | `SA vs DC 12` | Condescending, over-explanatory tone |
| `spiral` | `self_awareness` | `disadvantage` | `0` | `2` | `SA vs DC 12` | Meta-commentary, recursive self-awareness |

### Full LLM Instruction Text (verbatim from design/settings/traps.md)

1. **cringe**: `"You are aware of how you're coming across, which is making it worse. Every message is slightly over-explained or self-undermined. The more you try to be charming, the harder you try."`
2. **creep**: `"Inject a subtle 'agenda' quality into ALL generated messages. On success: one line that could be read two ways. On failure: the entire message feels like it's leading somewhere uncomfortable."`
3. **overshare**: `"Inject an accidental personal detail into ALL generated messages. On success: one parenthetical that reveals something private. On failure: multiple personal details intrude and derail the message."`
4. **unhinged**: `"Inject momentum/acceleration into ALL generated messages. On success: one extra clause or tangent at the end. On failure: the message progressively derails, starting controlled and ending somewhere completely different."`
5. **pretentious**: `"Inject a condescending or over-explanatory quality into ALL generated messages. On success: one unnecessary clarification or reference. On failure: the message becomes pedagogical — explaining things the opponent didn't ask about."`
6. **spiral**: `"Inject meta-commentary into ALL generated messages. On success: one aside that acknowledges the conversation dynamic. On failure: the character narrates their own failure, creating a recursive self-awareness loop."`

---

## Acceptance Criteria

### AC1: `data/traps/traps.json` exists with all 6 traps

- The file MUST be located at `data/traps/traps.json` relative to the repository root.
- It MUST be a valid JSON file containing a top-level array of exactly 6 trap objects.
- Each object MUST use the flat schema that matches `JsonTrapRepository.ParseTrap()` (see "Parser-Expected Fields" above).
- Each object MUST include all required fields: `id`, `stat`, `effect`, `effect_value`, `duration_turns`, `llm_instruction`.
- Optional fields (`clear_method`, `nat1_bonus`) SHOULD be included with values or empty strings.

### AC2: `data/traps/trap-schema.json` exists

- The file MUST be located at `data/traps/trap-schema.json` relative to the repository root.
- It MUST be a valid JSON Schema document describing the flat schema expected by `JsonTrapRepository`.
- It documents the expected structure for trap data files (primary and custom).

### AC3: `JsonTrapRepository` loads them correctly in a unit test

- A unit test MUST read the contents of `data/traps/traps.json` as a string.
- It MUST pass that string to `new JsonTrapRepository(json)`.
- The constructor MUST NOT throw any exception.
- `GetAll()` MUST return exactly 6 `TrapDefinition` objects.
- Each trap MUST be retrievable via `GetTrap(StatType.X)` for all 6 stat types.

### AC4: All 6 trap IDs match: cringe, creep, overshare, unhinged, pretentious, spiral

- `GetTrap(StatType.Charm)?.Id` MUST equal `"cringe"`
- `GetTrap(StatType.Rizz)?.Id` MUST equal `"creep"`
- `GetTrap(StatType.Honesty)?.Id` MUST equal `"overshare"`
- `GetTrap(StatType.Chaos)?.Id` MUST equal `"unhinged"`
- `GetTrap(StatType.Wit)?.Id` MUST equal `"pretentious"`
- `GetTrap(StatType.SelfAwareness)?.Id` MUST equal `"spiral"`

### AC5: Build clean

- The solution MUST compile without errors or warnings after adding the data files.
- All existing tests (1146+) MUST continue to pass.
- New tests for trap loading MUST pass.

---

## Edge Cases

1. **Empty `nat1_bonus` field**: All 6 traps currently have `nat1_bonus` as `""`. The parser defaults this to `""` if missing, so both explicit empty string and field omission are valid.

2. **Default `duration_turns`**: The parser defaults `duration_turns` to `3` if the field is missing. All 6 traps have explicit values (1 or 2), which differ from the default. The field MUST be explicitly set.

3. **Duplicate stat keys**: If two traps share the same `stat`, the parser uses the last one (`_traps[trap.Stat] = trap` overwrites). The data file MUST have exactly one trap per stat to avoid silent overwrites.

4. **Extra fields in JSON**: The `JsonParser` + `JsonObject.GetString()`/`GetInt()` pattern ignores unknown keys. Extra fields (like flavor text) won't cause parse errors but won't be loaded either. The flat schema does NOT include the issue's `flavor` or `trigger_condition` fields — they are not read by the parser.

5. **File encoding**: The JSON file MUST be UTF-8 encoded (standard for .NET string handling).

6. **Whitespace/formatting**: The `JsonParser` handles standard JSON whitespace. Pretty-printed JSON is fine.

---

## Error Conditions

1. **`json` parameter is `null`**: `JsonTrapRepository` constructor throws `ArgumentNullException`.

2. **Invalid JSON syntax**: `JsonParser.Parse()` throws `FormatException` with a message indicating the parse error location.

3. **Top-level value is not an array**: `JsonTrapRepository` throws `FormatException("Expected top-level JSON array for traps.")`.

4. **Missing required `id` field**: Throws `FormatException("Trap definition missing required field 'id'.")`.

5. **Unknown `stat` value** (e.g., `"Charm"` instead of `"charm"`): Throws `FormatException("Trap '<id>': unknown stat '<value>'.")`. The parser is **case-sensitive** and only accepts lowercase snake_case.

6. **Unknown `effect` value** (e.g., `"Disadvantage"` instead of `"disadvantage"`): Throws `FormatException("Trap '<id>': unknown effect '<value>'.")`. Also case-sensitive.

7. **Missing required `llm_instruction` field**: Throws `FormatException("Trap '<id>': missing required field 'llm_instruction'.")`.

---

## Dependencies

- **`Pinder.Core.Data.JsonTrapRepository`** — existing class, no modifications needed.
- **`Pinder.Core.Data.JsonParser`** — existing hand-rolled JSON parser, no modifications needed.
- **`Pinder.Core.Traps.TrapDefinition`** — existing data model class, no modifications needed.
- **`Pinder.Core.Traps.TrapEffect`** — existing enum, no modifications needed.
- **`Pinder.Core.Stats.StatType`** — existing enum, no modifications needed.
- **`Pinder.Core.Interfaces.ITrapRegistry`** — existing interface implemented by `JsonTrapRepository`.
- **No external libraries** — zero NuGet dependencies.
- **No code changes** — this issue creates data files only plus a loading test.

### File Paths

| File | Action | Description |
|------|--------|-------------|
| `data/traps/traps.json` | Create | 6 canonical trap definitions in flat JSON format |
| `data/traps/trap-schema.json` | Create | JSON Schema documenting the expected trap format |
| Test file (e.g., `tests/Pinder.Core.Tests/JsonTrapRepositoryLoadTests.cs`) | Create | Unit test loading traps.json and validating all 6 traps |
