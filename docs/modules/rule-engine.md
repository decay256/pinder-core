# Rule Engine

## Overview
`Pinder.Rules` is a data-driven rule engine that loads enriched YAML rule definitions and evaluates their conditions against an immutable game-state snapshot. It provides a parallel, YAML-sourced path for game-balance values (failure tiers, interest states) currently hardcoded in `FailureScale` and `InterestMeter`, enabling iteration without recompilation. Direct `GameSession` wiring is deferred; equivalence is proven via tests.

## Key Components

### Source (`src/Pinder.Rules/`)

| File | Description |
|------|-------------|
| `Pinder.Rules.csproj` | Project file — targets `netstandard2.0`, `LangVersion 8.0`, depends on `Pinder.Core` + `YamlDotNet 16.3.0` |
| `RuleEntry.cs` | POCO for a single YAML rule entry (Id, Section, Title, Type, Description, Condition, Outcome) |
| `RuleBook.cs` | Loads enriched YAML into an indexed, immutable collection; provides `GetById`, `GetRulesByType`, `All`, `Count` |
| `GameState.cs` | Immutable snapshot of game state (interest, margins, roll, level, streak, action, shadow values, etc.) |
| `ConditionEvaluator.cs` | Static evaluator — checks all keys in a condition dict against `GameState` using AND logic |
| `OutcomeDispatcher.cs` | Static dispatcher — reads outcome dict keys and calls corresponding `IEffectHandler` methods |
| `IEffectHandler.cs` | Callback interface for applying rule outcome effects (6 methods) |

### Tests (`tests/Pinder.Rules.Tests/`)

| File | Description |
|------|-------------|
| `RuleBookTests.cs` | YAML loading, querying by id/type, empty/malformed YAML handling |
| `ConditionEvaluatorTests.cs` | Per-key condition tests, null/empty conditions, AND logic, unknown keys |
| `OutcomeDispatcherTests.cs` | Per-key outcome dispatch tests, null/empty outcomes, unknown keys |
| `EquivalenceTests.cs` | Proves YAML-driven engine matches hardcoded `FailureScale` and `InterestMeter` outputs |
| `SpecComplianceTests.cs` | Additional edge-case and mutation-target coverage from spec |
| `TestEffectHandler.cs` | Test double implementing `IEffectHandler` that records all calls |

## API / Public Interface

### RuleEntry
```csharp
public sealed class RuleEntry
{
    public string Id { get; set; }            // e.g. "§5.fumble"
    public string Section { get; set; }       // e.g. "§5"
    public string Title { get; set; }         // e.g. "Fumble"
    public string Type { get; set; }          // e.g. "interest_change"
    public string Description { get; set; }   // Original prose
    public Dictionary<string, object>? Condition { get; set; }
    public Dictionary<string, object>? Outcome { get; set; }
}
```
All string properties default to `""`. Condition/Outcome are nullable (descriptive rules may omit them).

### RuleBook
```csharp
public sealed class RuleBook
{
    public static RuleBook LoadFrom(string yamlContent);  // throws FormatException on bad YAML
    public RuleEntry? GetById(string id);                  // null if not found
    public IEnumerable<RuleEntry> GetRulesByType(string type); // empty if no match
    public IReadOnlyList<RuleEntry> All { get; }
    public int Count { get; }
}
```
- `LoadFrom(null)` or empty string → `FormatException`.
- YAML root must be a sequence; mapping root → `FormatException`.
- Duplicate ids: last entry wins in `GetById`; both appear in `All`.
- Id/Type lookups use case-insensitive comparison.

### GameState
```csharp
public sealed class GameState
{
    public GameState(
        int interest = 0, int missMargin = 0, int beatMargin = 0,
        int naturalRoll = 0, int needToHit = 0, int level = 1,
        int streak = 0, string? action = null,
        bool isConversationStart = false,
        IReadOnlyDictionary<string, int>? shadowValues = null);
    // All parameters exposed as read-only properties.
}
```

### ConditionEvaluator
```csharp
public static class ConditionEvaluator
{
    public static bool Evaluate(Dictionary<string, object>? condition, GameState state);
}
```

**Supported condition keys:**

| Key | Match Logic |
|-----|-------------|
| `miss_range` | `[lo, hi]` — `lo <= MissMargin <= hi` |
| `beat_range` | `[lo, hi]` — `lo <= BeatMargin <= hi` |
| `interest_range` | `[lo, hi]` — `lo <= Interest <= hi` |
| `need_range` | `[lo, hi]` — `lo <= NeedToHit <= hi` |
| `level_range` | `[lo, hi]` — `lo <= Level <= hi` |
| `natural_roll` | `NaturalRoll == value` |
| `streak` | `Streak == value` |
| `streak_minimum` | `Streak >= value` |
| `action` | Case-insensitive string equality |
| `conversation_start` | `IsConversationStart == value` |
| `miss_minimum` | `MissMargin >= value` |

- Null/empty condition → `false`.
- Unknown keys are ignored (treated as matching).
- All keys must match (AND logic).
- Handles YamlDotNet `long` boxing via `Convert` helpers.
- Malformed range (not 2-element list) → `false` for that key.

> **Note:** `shadow_threshold` is specified in the spec but **not implemented** in the current `ConditionEvaluator`. The switch statement has no `shadow_threshold` case; unknown keys fall through as matching.

### OutcomeDispatcher
```csharp
public static class OutcomeDispatcher
{
    public static void Dispatch(
        Dictionary<string, object>? outcome,
        GameState state,
        IEffectHandler handler);
}
```

**Recognized outcome keys:**

| Key | Handler Call |
|-----|-------------|
| `interest_delta` | `ApplyInterestDelta(value)` |
| `trap` | `ActivateTrap("")` if true |
| `trap_name` | `ActivateTrap(value)` |
| `roll_bonus` | `SetRollModifier("+{value}")` |
| `effect` | `SetRollModifier(value)` |
| `risk_tier` | `SetRiskTier(value)` |
| `xp_multiplier` | `SetXpMultiplier(value)` |
| `shadow_effect` | `ApplyShadowGrowth(shadow, delta, "rule engine")` |
| `starting_interest` | `ApplyInterestDelta(value)` |

- Null outcome or null handler → no-op (no `ArgumentNullException`).
- Unknown keys silently ignored.
- Wrong-type values handled defensively via `ToInt`/`ToDouble` helpers.

### IEffectHandler
```csharp
public interface IEffectHandler
{
    void ApplyInterestDelta(int delta);
    void ActivateTrap(string trapId);
    void ApplyShadowGrowth(string shadowName, int delta, string reason);
    void SetRollModifier(string modifier);
    void SetRiskTier(string tier);
    void SetXpMultiplier(double multiplier);
}
```

## Architecture Notes

- **Dependency direction**: `Pinder.Rules → Pinder.Core` (one-way). `Pinder.Core` has no reference to `Pinder.Rules`.
- **No GameSession changes**: Direct wiring is deferred to preserve `Pinder.Core`'s zero-dependency invariant. The host (e.g. session-runner) can wrap `GameSession` calls with rule-engine lookups in a future sprint.
- **Shadow values as strings**: `GameState.ShadowValues` uses `string` keys (not `ShadowStatType`) to avoid tight coupling. The caller maps enum → string when constructing the snapshot.
- **Evaluation philosophy**: Loading is strict (throws `FormatException` on bad YAML); evaluation is lenient (bad data in a condition/outcome key returns false or is skipped, never crashes).
- **YAML deserialization**: Uses `YamlDotNet` `DeserializerBuilder` with `UnderscoredNamingConvention`. Raw YAML is deserialized as `List<Dictionary<object, object>>` then normalized to `Dictionary<string, object>` recursively.

### Spec divergences in implementation
- `RuleBook.LoadFrom(null)` throws `FormatException` (spec says `ArgumentNullException`).
- `ConditionEvaluator.Evaluate(cond, null)` throws `NullReferenceException` (spec says `ArgumentNullException`).
- `OutcomeDispatcher.Dispatch(outcome, state, null)` silently returns (spec says `ArgumentNullException`).
- `shadow_threshold` condition key is not implemented; falls through as unknown (always matching).
- `starting_interest` outcome key is implemented (dispatches as `ApplyInterestDelta`) but not listed in spec's outcome table.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #446 | Initial creation — hybrid rule engine with RuleBook, ConditionEvaluator, OutcomeDispatcher, GameState, IEffectHandler. Equivalence tests for §5 failure tiers and §6 interest states. SpecComplianceTests added for edge-case coverage. |
