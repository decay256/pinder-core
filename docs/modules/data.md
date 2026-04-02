# Data

## Overview
The `data/` directory contains static JSON data files that drive game mechanics in Pinder Core. These files are loaded at runtime by repository classes (e.g., `JsonTrapRepository`) that parse the JSON and expose typed domain objects. The data layer has zero external dependencies — parsing is handled by the hand-rolled `JsonParser`.

## Key Components

| File / Class | Description |
|---|---|
| `data/traps/traps.json` | Canonical trap definitions — 6 entries, one per `StatType`. Flat JSON array consumed by `JsonTrapRepository`. |
| `data/traps/trap-schema.json` | JSON Schema documenting the expected structure of trap data files. |
| `src/Pinder.Core/Data/JsonTrapRepository.cs` | Parses `traps.json` into `TrapDefinition` objects. Implements `ITrapRegistry`. |
| `src/Pinder.Core/Data/JsonParser.cs` | Hand-rolled JSON parser (no NuGet dependencies). |
| `tests/Pinder.Core.Tests/TrapDataFileValidationTests.cs` | 45 tests validating `traps.json` contents against the spec (IDs, effects, durations, LLM instructions, uniqueness, error conditions). |

## API / Public Interface

### JsonTrapRepository

```csharp
// Constructor — pass the raw JSON string contents of traps.json
public JsonTrapRepository(string json)

// Retrieve trap definition by stat type; returns null if not found
public TrapDefinition? GetTrap(StatType stat)

// Retrieve LLM instruction text by stat type; returns null if not found
public string? GetLlmInstruction(StatType stat)

// Retrieve all loaded trap definitions
public IEnumerable<TrapDefinition> GetAll()
```

### TrapDefinition (value object)

```csharp
public sealed class TrapDefinition
{
    public string Id          { get; }  // e.g., "cringe"
    public StatType Stat      { get; }  // e.g., StatType.Charm
    public TrapEffect Effect  { get; }  // e.g., TrapEffect.Disadvantage
    public int EffectValue    { get; }  // 0 for Disadvantage, positive for penalties
    public int DurationTurns  { get; }  // number of turns the trap lasts
    public string LlmInstruction { get; } // prompt taint text for LLM layer
    public string ClearMethod { get; }  // e.g., "SA vs DC 12"
    public string Nat1Bonus   { get; }  // extra punishment on Nat 1 (currently "" for all)
}
```

### Trap Data Schema (flat JSON per entry)

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `id` | string | Yes | — | Unique trap identifier |
| `stat` | string | Yes | — | Lowercase: `charm`, `rizz`, `honesty`, `chaos`, `wit`, `self_awareness` |
| `effect` | string | Yes | — | `disadvantage`, `stat_penalty`, `opponent_dc_increase` |
| `effect_value` | int | Yes | — | 0 for Disadvantage effects |
| `duration_turns` | int | No | 3 | Explicit in all 6 canonical traps |
| `llm_instruction` | string | Yes | — | Prompt taint text |
| `clear_method` | string | No | `""` | How to clear the trap early |
| `nat1_bonus` | string | No | `""` | Extra Nat 1 punishment |

### Canonical Traps

| id | stat | effect | effect_value | duration_turns |
|----|------|--------|-------------|----------------|
| `cringe` | `charm` | `disadvantage` | 0 | 1 |
| `creep` | `rizz` | `stat_penalty` | 2 | 2 |
| `overshare` | `honesty` | `opponent_dc_increase` | 2 | 1 |
| `unhinged` | `chaos` | `disadvantage` | 0 | 1 |
| `pretentious` | `wit` | `opponent_dc_increase` | 3 | 1 |
| `spiral` | `self_awareness` | `disadvantage` | 0 | 2 |

## Architecture Notes

- **No code changes**: The `JsonTrapRepository` parser existed before this data file. This issue created the data files only.
- **Case-sensitive parsing**: Stat keys must be lowercase snake_case (`self_awareness`, not `SelfAwareness`). Effect keys must also be lowercase (`disadvantage`, not `Disadvantage`). Mismatches throw `FormatException`.
- **Duplicate stat handling**: If two traps share the same `stat`, the parser silently overwrites (last wins). The canonical file has exactly one trap per stat.
- **Extra JSON fields are ignored**: The parser only reads the documented fields; unknown keys are silently skipped.
- **Error conditions**: `null` input → `ArgumentNullException`; invalid JSON → `FormatException`; non-array top-level → `FormatException`; missing required fields → `FormatException` with trap ID context.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-02 | #265 | Initial creation — added `data/traps/traps.json` (6 canonical trap definitions), `data/traps/trap-schema.json`, and `TrapDataFileValidationTests.cs` (45 tests). |
