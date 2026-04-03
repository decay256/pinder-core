# Traps

## Overview
The traps module defines trap mechanics for Pinder conversations. Each stat has one associated trap that imposes mechanical penalties (disadvantage, stat penalty, or opponent DC increase) and taints LLM-generated messages with flavored instructions. Traps are loaded from `data/traps/traps.json` using a flat JSON schema.

## Key Components

| File | Description |
|------|-------------|
| `src/Pinder.Core/Traps/TrapDefinition.cs` | `TrapDefinition` data model and `TrapEffect` enum (Disadvantage, StatPenalty, OpponentDCIncrease) |
| `src/Pinder.Core/Traps/TrapState.cs` | `TrapState` — tracks active traps per character; `ActiveTrap` — countdown wrapper |
| `src/Pinder.Core/Data/JsonTrapRepository.cs` | `JsonTrapRepository` — parses flat-schema JSON into `TrapDefinition` objects; implements `ITrapRegistry` |
| `data/traps/traps.json` | Trap data file — flat array of 6 trap objects, one per `StatType` |
| `data/traps/trap-schema.json` | JSON schema for trap data validation |
| `tests/Pinder.Core.Tests/TrapsJsonIssue306Tests.cs` | Regression tests for schema mismatch fix — validates flat schema, field values, error handling |
| `tests/Pinder.Core.Tests/TrapDataFileValidationTests.cs` | Data file validation tests |
| `tests/Pinder.Core.Tests/JsonTrapRepositoryDataFileTests.cs` | Repository integration tests |
| `tests/Pinder.Core.Tests/TrapTaintInjectionTests.cs` | LLM taint injection tests |

## API / Public Interface

### `TrapDefinition` (class)
```csharp
public sealed class TrapDefinition
{
    public string Id { get; }
    public StatType Stat { get; }
    public TrapEffect Effect { get; }
    public int EffectValue { get; }
    public int DurationTurns { get; }
    public string LlmInstruction { get; }
    public string ClearMethod { get; }
    public string Nat1Bonus { get; }
}
```

### `TrapEffect` (enum)
- `Disadvantage` — roll stat at disadvantage
- `StatPenalty` — flat penalty to stat modifier
- `OpponentDCIncrease` — raises opponent's DC

### `TrapState` (class)
```csharp
public sealed class TrapState
{
    public bool HasActive { get; }
    public void Activate(TrapDefinition definition);
    public bool IsActive(StatType stat);
    public ActiveTrap? GetActive(StatType stat);
    public IEnumerable<ActiveTrap> AllActive { get; }
    public void AdvanceTurn();
    public void Clear(StatType stat);
    public void ClearAll();
}
```

### `JsonTrapRepository` (class, implements `ITrapRegistry`)
```csharp
public sealed class JsonTrapRepository : ITrapRegistry
{
    public JsonTrapRepository(string json);
    public JsonTrapRepository(string json, IEnumerable<string> customJsonFiles);
    public TrapDefinition? GetTrap(StatType stat);
    public string? GetLlmInstruction(StatType stat);
    public IEnumerable<TrapDefinition> GetAll();
}
```

### JSON Schema (flat format)
Each trap object in the top-level array:
```json
{
  "id": "cringe",
  "stat": "charm",
  "effect": "disadvantage",
  "effect_value": 0,
  "duration_turns": 1,
  "llm_instruction": "...",
  "clear_method": "SA vs DC 12",
  "nat1_bonus": ""
}
```

**Critical**: Stat keys must be lowercase snake_case (`charm`, `rizz`, `honesty`, `chaos`, `wit`, `self_awareness`). Effect keys must be lowercase snake_case (`disadvantage`, `stat_penalty`, `opponent_dc_increase`). PascalCase is rejected.

## Architecture Notes
- **Flat schema**: `traps.json` uses flat top-level fields (`stat`, `effect`, `effect_value`, etc.). A previous nested schema (with `triggered_by_stat`, `mechanical_effect`, `prompt_taint` sub-objects) caused parser crashes — issue #306 fixed this by migrating to the flat schema the parser expects.
- **One trap per stat**: The 6 traps map 1:1 to `StatType` values. `JsonTrapRepository` stores them in a `Dictionary<StatType, TrapDefinition>`; duplicate stats are last-write-wins.
- **LLM taint**: Each trap carries an `llm_instruction` that gets injected into LLM prompts for ALL messages while the trap is active, not just the trapped stat's messages.
- **Clear method**: All traps currently use "SA vs DC 12" (Self-Awareness check vs DC 12) as the early clear mechanism.
- **Custom traps**: The overloaded constructor accepts additional JSON strings for extensibility.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #306 | Initial creation — documents traps module after fixing traps.json schema mismatch (nested → flat). Added 287-line regression test suite covering schema validation, field correctness, error handling, and case sensitivity. |
