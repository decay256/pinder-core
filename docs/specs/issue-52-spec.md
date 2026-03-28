# Issue #52 — Trap Taint Injection: Plumb Active Trap LLM Instructions Through ILlmAdapter

## Overview

When a trap is active (per Rules v3.4 §14/§3.7), its `llm_instruction` text must be injected into **every** LLM-generated message — not just rolls involving the trapped stat. Currently, the LLM context types (`DialogueContext`, `DeliveryContext`, `OpponentContext`) carry only trap _names_ (string IDs) in some cases, and trap _instruction text_ inconsistently. This feature adds a `JsonTrapRepository` to load trap definitions from JSON, updates `ITrapRegistry` to expose LLM instruction lookup, ensures all three LLM context types carry full `ActiveTrapInstructions` (the actual prompt taint text), and wires `GameSession` to populate them every turn.

## Dependencies

- **#27 (GameSession)** — merged
- **#26 (ILlmAdapter + context types)** — merged
- **Pinder.Core.Data.JsonParser** — existing hand-rolled JSON parser (no NuGet)
- **Pinder.Core.Traps.TrapDefinition** — existing class with `LlmInstruction` property
- **Pinder.Core.Traps.TrapState** — existing class with `AllActive` enumeration

---

## 1. JsonTrapRepository

### Purpose

Load `TrapDefinition` objects from JSON strings (same pattern as `JsonItemRepository` — caller passes the JSON string, no file I/O in the repository). Implements `ITrapRegistry`.

### Function Signatures

```csharp
namespace Pinder.Core.Data
{
    public sealed class JsonTrapRepository : ITrapRegistry
    {
        /// <summary>
        /// Construct from a single JSON string (contents of traps.json).
        /// The JSON is a top-level array of trap objects.
        /// </summary>
        /// <param name="json">Full JSON string of the trap definitions file.</param>
        /// <exception cref="FormatException">If the JSON is not a top-level array.</exception>
        public JsonTrapRepository(string json);

        /// <summary>
        /// Add traps from an additional JSON string (e.g. a custom trap file).
        /// Traps with duplicate stat keys overwrite earlier entries.
        /// </summary>
        /// <param name="json">Full JSON string of additional trap definitions.</param>
        /// <exception cref="FormatException">If the JSON is not a top-level array.</exception>
        public void LoadAdditional(string json);

        /// <summary>
        /// Returns the trap definition for the given stat, or null if none defined.
        /// Implements ITrapRegistry.GetTrap.
        /// </summary>
        public TrapDefinition? GetTrap(StatType stat);

        /// <summary>
        /// Returns all loaded trap definitions.
        /// </summary>
        public IEnumerable<TrapDefinition> GetAll();
    }
}
```

### JSON Schema (expected input)

Each trap object in the array must have these fields:

| Field | JSON type | Maps to | Required |
|-------|-----------|---------|----------|
| `id` | string | `TrapDefinition.Id` | yes |
| `stat` | string | `TrapDefinition.Stat` (parsed as `StatType` enum, case-insensitive) | yes |
| `effect` | string | `TrapDefinition.Effect` (parsed as `TrapEffect` enum, case-insensitive) | yes |
| `effect_value` | number (int) | `TrapDefinition.EffectValue` | yes |
| `duration_turns` | number (int) | `TrapDefinition.DurationTurns` | yes |
| `llm_instruction` | string | `TrapDefinition.LlmInstruction` | yes |
| `clear_method` | string | `TrapDefinition.ClearMethod` | yes |
| `nat1_bonus` | string | `TrapDefinition.Nat1Bonus` | yes |

Example JSON:
```json
[
  {
    "id": "charm_trap_cringe",
    "stat": "Charm",
    "effect": "Disadvantage",
    "effect_value": 0,
    "duration_turns": 3,
    "llm_instruction": "The character becomes painfully over-eager and cringy. Every message should ooze desperation. Use too many exclamation marks and compliments.",
    "clear_method": "Succeed on a Charm roll",
    "nat1_bonus": "Character sends an unsolicited selfie. Lose 2 additional interest."
  },
  {
    "id": "wit_trap_ramble",
    "stat": "Wit",
    "effect": "StatPenalty",
    "effect_value": 2,
    "duration_turns": 2,
    "llm_instruction": "The character can't stop rambling. Every response is three times longer than it needs to be, full of tangents and unnecessary details.",
    "clear_method": "Succeed on a Wit roll",
    "nat1_bonus": "Character sends a 500-word essay about their cat. Opponent stops reading."
  }
]
```

### Parsing Rules

- Uses `Pinder.Core.Data.JsonParser.Parse(json)` — the existing hand-rolled parser.
- Top-level value must be a `JsonArray`. If not, throw `FormatException("Expected top-level JSON array for traps.")`.
- Each element must be a `JsonObject`. Non-object elements are silently skipped.
- `stat` is parsed via case-insensitive match to `StatType` enum values (`Charm`, `Rizz`, `Honesty`, `Chaos`, `Wit`, `SA`). If unrecognized, throw `FormatException($"Unknown stat type: {value}")`.
- `effect` is parsed via case-insensitive match to `TrapEffect` enum values. If unrecognized, throw `FormatException($"Unknown trap effect: {value}")`.
- Traps are keyed by `StatType`. If two traps target the same stat, the later one wins (last-write-wins).
- `LoadAdditional` follows the same parse logic and merges into the existing dictionary with last-write-wins semantics.

---

## 2. ITrapRegistry Update

### Current Signature

```csharp
public interface ITrapRegistry
{
    TrapDefinition? GetTrap(StatType stat);
}
```

### Required Addition

Add a method to retrieve the LLM instruction text for a given stat's active trap:

```csharp
public interface ITrapRegistry
{
    TrapDefinition? GetTrap(StatType stat);

    /// <summary>
    /// Returns the LLM instruction text for the trap on the given stat,
    /// or null if no trap is defined for that stat.
    /// Convenience method equivalent to GetTrap(stat)?.LlmInstruction.
    /// </summary>
    string? GetLlmInstruction(StatType stat);
}
```

**Note:** Both `JsonTrapRepository` and any existing test implementations of `ITrapRegistry` must implement `GetLlmInstruction`. The implementation is trivially: `return GetTrap(stat)?.LlmInstruction;`

---

## 3. Context Type Changes

All three LLM context types must carry **actual LLM instruction text** (not just trap names/IDs).

### 3a. DialogueContext

**Current state:** Has `ActiveTraps: IReadOnlyList<string>` carrying trap **IDs** (names).

**Required change:** Add a new property `ActiveTrapInstructions`:

```csharp
/// <summary>
/// LLM instruction text for each currently active trap.
/// These are the full prompt-taint strings from TrapDefinition.LlmInstruction.
/// One entry per active trap. Empty list if no traps active.
/// </summary>
public IReadOnlyList<string> ActiveTrapInstructions { get; }
```

The constructor gains a new parameter:
```csharp
public DialogueContext(
    string playerPrompt,
    string opponentPrompt,
    IReadOnlyList<(string Sender, string Text)> conversationHistory,
    string opponentLastMessage,
    IReadOnlyList<string> activeTraps,           // existing — trap IDs (kept for backward compat)
    int currentInterest,
    IReadOnlyList<string> activeTrapInstructions  // NEW — actual LLM instruction text
)
```

The new parameter must be non-null (throw `ArgumentNullException` if null).

### 3b. DeliveryContext

**Current state:** Has `ActiveTraps: IReadOnlyList<string>` — the code in `GameSession.ResolveTurnAsync` already populates this with `LlmInstruction` text (not IDs). This is an inconsistency with `DialogueContext` and `OpponentContext` which pass IDs.

**Required change:** Rename or clarify semantics. Add `ActiveTrapInstructions` as a separate property (same as DialogueContext), and keep `ActiveTraps` as trap IDs for consistency:

```csharp
/// <summary>Active trap IDs (names).</summary>
public IReadOnlyList<string> ActiveTraps { get; }

/// <summary>LLM instruction text for each active trap.</summary>
public IReadOnlyList<string> ActiveTrapInstructions { get; }
```

Constructor gains the new `activeTrapInstructions` parameter. The existing `activeTraps` parameter should carry trap **IDs** (fixing the current inconsistency where it carries instruction text).

### 3c. OpponentContext

**Current state:** Has `ActiveTraps: IReadOnlyList<string>` carrying trap **IDs**.

**Required change:** Add `ActiveTrapInstructions` property (same pattern):

```csharp
/// <summary>LLM instruction text for each active trap.</summary>
public IReadOnlyList<string> ActiveTrapInstructions { get; }
```

Constructor gains a new `activeTrapInstructions` parameter (non-null, `ArgumentNullException` on null).

---

## 4. GameSession Wiring

### What Changes

`GameSession` must populate `ActiveTrapInstructions` on all three context types every turn.

#### Helper Method

Add or update a private helper:

```csharp
private List<string> GetActiveTrapInstructions()
{
    return _traps.AllActive
        .Select(t => t.Definition.LlmInstruction)
        .Where(instruction => !string.IsNullOrEmpty(instruction))
        .ToList();
}
```

#### StartTurnAsync

When building `DialogueContext`, pass both trap IDs and trap instructions:

```csharp
var context = new DialogueContext(
    playerPrompt: _player.AssembledSystemPrompt,
    opponentPrompt: _opponent.AssembledSystemPrompt,
    conversationHistory: _history.AsReadOnly(),
    opponentLastMessage: GetLastOpponentMessage(),
    activeTraps: GetActiveTrapNames(),           // IDs
    currentInterest: _interest.Current,
    activeTrapInstructions: GetActiveTrapInstructions()  // NEW
);
```

#### ResolveTurnAsync — DeliveryContext

Fix the current inconsistency. Pass IDs to `activeTraps` and instruction text to `activeTrapInstructions`:

```csharp
var deliveryContext = new DeliveryContext(
    ...,
    activeTraps: GetActiveTrapNames(),                    // IDs (was incorrectly instructions)
    activeTrapInstructions: GetActiveTrapInstructions()   // NEW
);
```

#### ResolveTurnAsync — OpponentContext

```csharp
var opponentContext = new OpponentContext(
    ...,
    activeTraps: GetActiveTrapNames(),                    // IDs (unchanged)
    ...,
    activeTrapInstructions: GetActiveTrapInstructions()   // NEW
);
```

---

## Input/Output Examples

### Example: JsonTrapRepository Construction

**Input JSON:**
```json
[
  {
    "id": "charm_trap_cringe",
    "stat": "Charm",
    "effect": "Disadvantage",
    "effect_value": 0,
    "duration_turns": 3,
    "llm_instruction": "Be painfully over-eager and cringy.",
    "clear_method": "Succeed on a Charm roll",
    "nat1_bonus": "Send unsolicited selfie."
  }
]
```

**After construction:**
- `repo.GetTrap(StatType.Charm)` → returns `TrapDefinition` with `Id="charm_trap_cringe"`, `LlmInstruction="Be painfully over-eager and cringy."`, etc.
- `repo.GetTrap(StatType.Wit)` → returns `null`
- `repo.GetLlmInstruction(StatType.Charm)` → `"Be painfully over-eager and cringy."`
- `repo.GetLlmInstruction(StatType.Wit)` → `null`
- `repo.GetAll()` → single-element collection

### Example: Custom Trap Loading

```csharp
var repo = new JsonTrapRepository(baseJson);
repo.LoadAdditional(customJson);
// If customJson redefines Charm trap, the custom version replaces the base.
```

### Example: GameSession Turn with Active Trap

1. Player has an active Charm trap with `LlmInstruction = "Be painfully cringy."`
2. `StartTurnAsync()` builds `DialogueContext` with:
   - `ActiveTraps = ["charm_trap_cringe"]`
   - `ActiveTrapInstructions = ["Be painfully cringy."]`
3. Player selects option, `ResolveTurnAsync(0)` builds:
   - `DeliveryContext.ActiveTraps = ["charm_trap_cringe"]`
   - `DeliveryContext.ActiveTrapInstructions = ["Be painfully cringy."]`
   - `OpponentContext.ActiveTraps = ["charm_trap_cringe"]`
   - `OpponentContext.ActiveTrapInstructions = ["Be painfully cringy."]`

### Example: No Active Traps

All `ActiveTrapInstructions` lists are empty (`[]`). All `ActiveTraps` lists are empty (`[]`).

### Example: Multiple Active Traps

Player has Charm trap and Wit trap active simultaneously:
- `ActiveTrapInstructions = ["Be painfully cringy.", "Ramble endlessly."]`
- `ActiveTraps = ["charm_trap_cringe", "wit_trap_ramble"]`
- Order matches `TrapState.AllActive` enumeration order (dictionary iteration order — not guaranteed stable, but consistent within a turn).

---

## Acceptance Criteria

### AC1: JsonTrapRepository loads `data/traps/traps.json` and custom directory

- `JsonTrapRepository` parses a JSON array of trap objects into `TrapDefinition` instances.
- Constructor takes a `string json` parameter (the file contents — no file I/O inside the class).
- `LoadAdditional(string json)` allows loading extra trap files (custom traps).
- All `TrapDefinition` fields are populated: `Id`, `Stat`, `Effect`, `EffectValue`, `DurationTurns`, `LlmInstruction`, `ClearMethod`, `Nat1Bonus`.
- Follows the same pattern as `JsonItemRepository`.

### AC2: ITrapRegistry updated to expose `GetLlmInstruction(StatType)`

- New method `string? GetLlmInstruction(StatType stat)` added to `ITrapRegistry` interface.
- Returns the `LlmInstruction` string for the trap targeting that stat, or `null` if no trap defined.
- `JsonTrapRepository` implements this method.
- Any test doubles/mocks of `ITrapRegistry` must also implement it.

### AC3: All three context types carry `ActiveTrapInstructions` string array

- `DialogueContext` gains `IReadOnlyList<string> ActiveTrapInstructions` property and constructor parameter.
- `DeliveryContext` gains `IReadOnlyList<string> ActiveTrapInstructions` property and constructor parameter. The existing `ActiveTraps` property is fixed to carry trap IDs (not instruction text).
- `OpponentContext` gains `IReadOnlyList<string> ActiveTrapInstructions` property and constructor parameter.
- All three throw `ArgumentNullException` if `activeTrapInstructions` is null.

### AC4: GameSession populates instructions from active traps each turn

- `GameSession.StartTurnAsync()` populates `DialogueContext.ActiveTrapInstructions` with LLM instruction text from all active traps via `TrapState.AllActive`.
- `GameSession.ResolveTurnAsync()` populates `DeliveryContext.ActiveTrapInstructions` and `OpponentContext.ActiveTrapInstructions` similarly.
- Empty list when no traps are active.
- Instructions with null/empty `LlmInstruction` are filtered out.

### AC5: Tests

- **JsonTrapRepository** loads correct trap data from valid JSON.
- **JsonTrapRepository** `LoadAdditional` merges/overwrites correctly.
- **JsonTrapRepository** throws `FormatException` on non-array JSON.
- **JsonTrapRepository** throws `FormatException` on unknown stat type.
- **GameSession** passes `ActiveTrapInstructions` when a trap is active.
- **GameSession** passes empty `ActiveTrapInstructions` when no traps active.

### AC6: Build clean

- `dotnet build` produces zero errors and zero warnings on the solution.
- All existing tests continue to pass.

---

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| Empty JSON array `[]` | `JsonTrapRepository` constructs successfully with zero traps. `GetTrap` returns null for all stats. `GetAll()` is empty. |
| JSON with unknown stat string (e.g. `"stat": "Strength"`) | Throws `FormatException("Unknown stat type: Strength")` during construction. |
| JSON with unknown effect string | Throws `FormatException("Unknown trap effect: ...")` during construction. |
| JSON is not an array (e.g. `{}`) | Throws `FormatException("Expected top-level JSON array for traps.")` |
| JSON has null/missing `llm_instruction` field | Throw `FormatException` or default to `""`. Recommend: throw `FormatException("Missing required field: llm_instruction")`. |
| Two traps target same stat in base JSON | Last one wins (dictionary overwrite). |
| Custom trap overwrites base trap for same stat | `LoadAdditional` overwrites — last-write-wins. |
| `LoadAdditional` called with empty array | No-op. Existing traps unchanged. |
| `LlmInstruction` is empty string `""` | Stored as-is. `GetActiveTrapInstructions()` in GameSession filters it out (via `!string.IsNullOrEmpty`). |
| Trap expires mid-turn (after `AdvanceTurn()`) | In `ResolveTurnAsync`, trap timers advance at step 6. `DeliveryContext` is built at step 7 (after advance), so a trap that just expired will NOT appear in delivery/opponent contexts. `DialogueContext` (built in `StartTurnAsync`) reflects pre-advance state. |
| No traps active | All instruction lists are empty `[]`. |
| Multiple traps active simultaneously | All instructions included. Order follows `TrapState.AllActive` enumeration. |
| `NullLlmAdapter` receives contexts with instructions | `NullLlmAdapter` ignores the instructions (it already ignores trap data). No change needed to `NullLlmAdapter`. |

---

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `JsonTrapRepository(null)` | `ArgumentNullException` | `json` |
| `JsonTrapRepository("")` — empty but valid to parse? | Depends on `JsonParser` behavior. If it returns null/non-array, throw `FormatException("Expected top-level JSON array for traps.")` |
| Non-array top-level JSON | `FormatException` | `"Expected top-level JSON array for traps."` |
| Unknown `stat` value in JSON | `FormatException` | `"Unknown stat type: {value}"` |
| Unknown `effect` value in JSON | `FormatException` | `"Unknown trap effect: {value}"` |
| Missing required field in trap object | `FormatException` | `"Missing required field: {fieldName}"` (recommend checking all 8 fields) |
| `activeTrapInstructions` constructor param is null | `ArgumentNullException` | `nameof(activeTrapInstructions)` (on DialogueContext, DeliveryContext, OpponentContext) |
| `LoadAdditional(null)` | `ArgumentNullException` | `json` |

---

## Implementation Notes

### File Locations

| Component | File |
|-----------|------|
| `JsonTrapRepository` | `src/Pinder.Core/Data/JsonTrapRepository.cs` (new) |
| `ITrapRegistry` | `src/Pinder.Core/Interfaces/IRollDataProvider.cs` (modify) |
| `DialogueContext` | `src/Pinder.Core/Conversation/DialogueContext.cs` (modify) |
| `DeliveryContext` | `src/Pinder.Core/Conversation/DeliveryContext.cs` (modify) |
| `OpponentContext` | `src/Pinder.Core/Conversation/OpponentContext.cs` (modify) |
| `GameSession` | `src/Pinder.Core/Conversation/GameSession.cs` (modify) |

### Constraints

- **netstandard2.0 + LangVersion 8.0**: No `record` types. Use `sealed class`.
- **Zero NuGet dependencies**: Use existing `JsonParser`.
- **Nullable reference types enabled**: Use `?` annotations.
- **No file I/O in repository**: Caller passes JSON strings. The `data/traps/` directory path is the host's concern.

### Breaking Changes

- Adding `GetLlmInstruction` to `ITrapRegistry` is a **breaking change** for all existing implementors of the interface. All test doubles/mocks must be updated.
- Adding a constructor parameter to `DialogueContext`, `DeliveryContext`, and `OpponentContext` is a **breaking change** for all callers. `GameSession`, `NullLlmAdapter`, and all tests that construct these types must be updated.
