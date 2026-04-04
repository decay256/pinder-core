# Spec: Issue #446 — Hybrid Rule Engine: RuleBook + RuleEngine

**Module**: docs/modules/rule-engine.md (create new)

---

## Overview

This feature introduces a new `Pinder.Rules` project containing a generic, data-driven rule engine that loads enriched YAML rule definitions and evaluates their conditions against a game state snapshot. The engine replaces hardcoded numeric constants in `FailureScale` and `InterestMeter` with YAML-sourced values, enabling game-balance iteration without recompilation. At prototype maturity, the engine proves equivalence with existing hardcoded logic via tests; direct `GameSession` wiring is deferred to preserve `Pinder.Core`'s zero-dependency invariant.

---

## Function Signatures

All types live in namespace `Pinder.Rules`. The project targets `netstandard2.0` with `LangVersion 8.0` and depends on `Pinder.Core` + `YamlDotNet 16.3.0`.

### RuleEntry

```csharp
namespace Pinder.Rules
{
    /// <summary>
    /// POCO representing a single rule entry deserialized from enriched YAML.
    /// </summary>
    public sealed class RuleEntry
    {
        public string Id { get; set; }           // e.g. "§5.fumble"
        public string Section { get; set; }       // e.g. "§5"
        public string Title { get; set; }         // e.g. "Fumble"
        public string Type { get; set; }          // e.g. "interest_change"
        public string Description { get; set; }   // Original prose from rules doc
        public Dictionary<string, object>? Condition { get; set; }  // Machine-readable trigger
        public Dictionary<string, object>? Outcome { get; set; }    // Machine-readable effect
    }
}
```

All `string` properties default to `""`. `Condition` and `Outcome` are nullable — rules that are purely descriptive (no numeric trigger) may have null for either.

### RuleBook

```csharp
namespace Pinder.Rules
{
    public sealed class RuleBook
    {
        /// <summary>
        /// Parse enriched YAML content and build an indexed rule book.
        /// The YAML must be a sequence of mappings (list of rule entries).
        /// Throws FormatException if the YAML is malformed or not a sequence.
        /// </summary>
        /// <param name="yamlContent">Raw YAML string (file content, not a path).</param>
        /// <returns>An immutable RuleBook instance.</returns>
        public static RuleBook LoadFrom(string yamlContent);

        /// <summary>Look up a rule by its unique id. Returns null if not found.</summary>
        public RuleEntry? GetById(string id);

        /// <summary>
        /// Return all rules whose Type field matches the given value (case-sensitive).
        /// Returns an empty sequence if no rules match.
        /// </summary>
        public IEnumerable<RuleEntry> GetRulesByType(string type);

        /// <summary>All loaded rule entries, in YAML document order.</summary>
        public IReadOnlyList<RuleEntry> All { get; }

        /// <summary>Total number of loaded rules.</summary>
        public int Count { get; }
    }
}
```

### GameState

```csharp
namespace Pinder.Rules
{
    /// <summary>
    /// Immutable snapshot of game state used for condition evaluation.
    /// Constructed by the caller before evaluating rules.
    /// All fields have sensible defaults so callers only set what's relevant.
    /// </summary>
    public sealed class GameState
    {
        public int Interest { get; }
        public int MissMargin { get; }
        public int BeatMargin { get; }
        public int NaturalRoll { get; }
        public int NeedToHit { get; }
        public int Level { get; }
        public int Streak { get; }
        public string? Action { get; }
        public bool IsConversationStart { get; }
        public IReadOnlyDictionary<string, int>? ShadowValues { get; }

        public GameState(
            int interest = 0,
            int missMargin = 0,
            int beatMargin = 0,
            int naturalRoll = 0,
            int needToHit = 0,
            int level = 1,
            int streak = 0,
            string? action = null,
            bool isConversationStart = false,
            IReadOnlyDictionary<string, int>? shadowValues = null);
    }
}
```

Note: `ShadowValues` keys are shadow stat names as strings (e.g. `"Madness"`, `"Horniness"`) to avoid Pinder.Rules needing to reference `ShadowStatType` directly in condition evaluation (YAML stores them as strings). The caller maps from `ShadowStatType` to string when constructing the snapshot.

### ConditionEvaluator

```csharp
namespace Pinder.Rules
{
    public static class ConditionEvaluator
    {
        /// <summary>
        /// Evaluate whether ALL conditions in the dictionary match the game state.
        /// Returns false if condition is null or empty.
        /// All keys must match (AND logic).
        /// Unknown keys are ignored (treated as matching).
        /// </summary>
        public static bool Evaluate(Dictionary<string, object>? condition, GameState state);
    }
}
```

**Supported condition keys and their evaluation logic:**

| Key | Value Type | GameState Field | Match Logic |
|-----|-----------|----------------|-------------|
| `miss_range` | `[int lo, int hi]` | `MissMargin` | `lo <= MissMargin <= hi` |
| `beat_range` | `[int lo, int hi]` | `BeatMargin` | `lo <= BeatMargin <= hi` |
| `interest_range` | `[int lo, int hi]` | `Interest` | `lo <= Interest <= hi` |
| `need_range` | `[int lo, int hi]` | `NeedToHit` | `lo <= NeedToHit <= hi` |
| `level_range` | `[int lo, int hi]` | `Level` | `lo <= Level <= hi` |
| `natural_roll` | `int` | `NaturalRoll` | `NaturalRoll == value` |
| `streak` | `int` | `Streak` | `Streak == value` |
| `streak_minimum` | `int` | `Streak` | `Streak >= value` |
| `action` | `string` | `Action` | case-insensitive string equality |
| `conversation_start` | `bool` | `IsConversationStart` | `IsConversationStart == value` |
| `shadow_threshold` | `{shadow: string, value: int}` | `ShadowValues[shadow]` | `ShadowValues[shadow] >= value` |
| `miss_minimum` | `int` | `MissMargin` | `MissMargin >= value` |

Range values arrive from YAML deserialization as `List<object>` containing boxed integers (or longs). The evaluator must handle both `int` and `long` boxing from YamlDotNet.

### OutcomeDispatcher

```csharp
namespace Pinder.Rules
{
    public static class OutcomeDispatcher
    {
        /// <summary>
        /// Read an outcome dictionary and dispatch effects to the handler.
        /// Does nothing if outcome is null or empty.
        /// Unknown keys are silently ignored.
        /// </summary>
        public static void Dispatch(
            Dictionary<string, object>? outcome,
            GameState state,
            IEffectHandler handler);
    }
}
```

**Recognized outcome keys and their dispatch:**

| Key | Value Type | Handler Call |
|-----|-----------|-------------|
| `interest_delta` | `int` | `handler.ApplyInterestDelta(value)` |
| `trap` | `bool` | `handler.ActivateTrap("")` if true; no-op if false |
| `trap_name` | `string` | `handler.ActivateTrap(value)` |
| `roll_bonus` | `int` | `handler.SetRollModifier("+{value}")` or `"{value}"` |
| `effect` | `string` | `handler.SetRollModifier(value)` — e.g. `"advantage"`, `"disadvantage"` |
| `risk_tier` | `string` | `handler.SetRiskTier(value)` |
| `xp_multiplier` | `double` | `handler.SetXpMultiplier(value)` |
| `shadow_effect` | `{shadow: string, delta: int}` | `handler.ApplyShadowGrowth(shadow, delta, ruleEntry.Title)` |

### IEffectHandler

```csharp
namespace Pinder.Rules
{
    /// <summary>
    /// Callback interface for applying rule outcome effects.
    /// Implemented by GameSession (or test doubles).
    /// </summary>
    public interface IEffectHandler
    {
        void ApplyInterestDelta(int delta);
        void ActivateTrap(string trapId);
        void ApplyShadowGrowth(string shadowName, int delta, string reason);
        void SetRollModifier(string modifier);
        void SetRiskTier(string tier);
        void SetXpMultiplier(double multiplier);
    }
}
```

---

## Input/Output Examples

### Example 1: Loading and querying rules

**Input YAML** (content of an enriched YAML file):
```yaml
- id: "§5.fumble"
  section: "§5"
  title: "Fumble"
  type: "interest_change"
  description: "Miss DC by 1-2. -1 interest."
  condition:
    miss_range: [1, 2]
  outcome:
    interest_delta: -1

- id: "§5.misfire"
  section: "§5"
  title: "Misfire"
  type: "interest_change"
  description: "Miss DC by 3-5. -1 interest."
  condition:
    miss_range: [3, 5]
  outcome:
    interest_delta: -1

- id: "§5.trope-trap"
  section: "§5"
  title: "Trope Trap"
  type: "interest_change"
  description: "Miss DC by 6-9. -2 interest, activates a trap."
  condition:
    miss_range: [6, 9]
  outcome:
    interest_delta: -2
    trap: true
```

**Usage**:
```
var book = RuleBook.LoadFrom(yamlContent);
book.Count → 3
book.GetById("§5.fumble") → RuleEntry { Id="§5.fumble", Type="interest_change", ... }
book.GetById("nonexistent") → null
book.GetRulesByType("interest_change") → [fumble, misfire, trope-trap]
book.GetRulesByType("shadow_growth") → empty sequence
```

### Example 2: Evaluating a failure tier condition

**Scenario**: Player missed DC by 4 (Misfire range).

```
var state = new GameState(missMargin: 4);
var fumbleRule = book.GetById("§5.fumble");   // condition: miss_range [1, 2]
var misfireRule = book.GetById("§5.misfire"); // condition: miss_range [3, 5]

ConditionEvaluator.Evaluate(fumbleRule.Condition, state) → false  (4 not in [1,2])
ConditionEvaluator.Evaluate(misfireRule.Condition, state) → true  (4 in [3,5])
```

### Example 3: Dispatching an outcome

**Scenario**: Misfire rule matched, dispatch outcome.

```
var handler = new TestEffectHandler(); // implements IEffectHandler
OutcomeDispatcher.Dispatch(misfireRule.Outcome, state, handler);
// handler.ApplyInterestDelta(-1) was called
// No other handler methods were called
```

### Example 4: Interest state range evaluation

**Input YAML**:
```yaml
- id: "§6.bored"
  section: "§6"
  title: "Bored"
  type: "interest_state"
  description: "Interest 1-4. Grants disadvantage."
  condition:
    interest_range: [1, 4]
  outcome:
    effect: "disadvantage"
```

**Usage**:
```
var state = new GameState(interest: 3);
ConditionEvaluator.Evaluate(boredRule.Condition, state) → true

var handler = new TestEffectHandler();
OutcomeDispatcher.Dispatch(boredRule.Outcome, state, handler);
// handler.SetRollModifier("disadvantage") was called
```

### Example 5: Multi-condition evaluation (AND logic)

**Input YAML**:
```yaml
- id: "§7.shadow-threshold-example"
  section: "§7"
  title: "High Dread"
  type: "shadow_effect"
  description: "When Dread ≥ 18 and conversation starts, interest starts at 5."
  condition:
    shadow_threshold:
      shadow: "Dread"
      value: 18
    conversation_start: true
  outcome:
    starting_interest: 5
```

**Usage**:
```
var shadows = new Dictionary<string, int> { { "Dread", 20 } };
var state1 = new GameState(isConversationStart: true, shadowValues: shadows);
ConditionEvaluator.Evaluate(rule.Condition, state1) → true

var state2 = new GameState(isConversationStart: false, shadowValues: shadows);
ConditionEvaluator.Evaluate(rule.Condition, state2) → false  (conversation_start mismatch)
```

---

## Acceptance Criteria

### AC1: Core types implemented — RuleBook, ConditionEvaluator, OutcomeDispatcher, IEffectHandler

A new project `src/Pinder.Rules/Pinder.Rules.csproj` exists targeting `netstandard2.0` with `LangVersion 8.0`, referencing `Pinder.Core` and `YamlDotNet 16.3.0`. The following public types are implemented:

- `RuleEntry` — POCO with Id, Section, Title, Type, Description, Condition, Outcome properties
- `RuleBook` — static `LoadFrom(string yamlContent)` factory, `GetById(string)`, `GetRulesByType(string)`, `All`, `Count`
- `GameState` — immutable snapshot with all fields listed in Function Signatures
- `ConditionEvaluator` — static `Evaluate(Dictionary<string, object>?, GameState)` supporting all 12 condition keys
- `OutcomeDispatcher` — static `Dispatch(Dictionary<string, object>?, GameState, IEffectHandler)` supporting all 8 outcome keys
- `IEffectHandler` — interface with 6 methods as specified

`Pinder.Core` MUST NOT reference `Pinder.Rules`. Dependency is strictly one-way: `Pinder.Rules → Pinder.Core`.

### AC2: §5 failure tiers run through the engine

The rule engine can load YAML entries representing the §5 failure tier → interest delta mappings and produce identical results to the hardcoded `FailureScale.GetInterestDelta()`:

| Failure Tier | Miss Range | FailureScale Output | Rule Engine Output |
|---|---|---|---|
| Fumble | 1–2 | -1 | -1 |
| Misfire | 3–5 | -1 | -1 |
| TropeTrap | 6–9 | -2 | -2 (+ trap: true) |
| Catastrophe | 10+ | -3 | -3 (+ trap: true) |
| Legendary (Nat 1) | n/a (natural_roll: 1) | -4 | -4 |

Tests must load actual enriched YAML and verify the engine produces the same `interest_delta` as `FailureScale.GetInterestDelta()` for every tier.

### AC3: §6 interest states run through the engine

The rule engine can load YAML entries representing the §6 interest state → modifier mappings and produce identical results to `InterestMeter.GetState()` / `GrantsAdvantage` / `GrantsDisadvantage`:

| Interest Range | State | Effect |
|---|---|---|
| 0 | Unmatched | — |
| 1–4 | Bored | disadvantage |
| 5–9 | Lukewarm | — |
| 10–15 | Interested | — |
| 16–20 | VeryIntoIt | advantage |
| 21–24 | AlmostThere | advantage |
| 25 | DateSecured | — |

Tests must verify that for each interest value 0–25, the matching rule's condition evaluates correctly and the outcome effect matches `InterestMeter`'s behavior.

### AC4: GameSession integration path

Per the architecture decision (ADR in contracts/sprint-rules-dsl-rule-engine.md), direct `GameSession` wiring is deferred to avoid breaking Pinder.Core's zero-dependency invariant. The AC item "GameSession uses the engine for those two sections" is satisfied by:

1. Equivalence tests proving the rule engine produces identical outputs to hardcoded `FailureScale` and `InterestMeter` logic for all input ranges.
2. A documented integration path: the host (e.g. session-runner) can wrap `GameSession` calls with rule-engine lookups, or a future sprint can add delegate-based injection via `GameSessionConfig`.

No changes to `GameSession.cs` or `GameSessionConfig.cs` are made this sprint.

### AC5: All 2238+ existing tests still pass

No existing test behavior changes. The new `Pinder.Rules` project is additive. The solution file gains a new project reference. The test project `Pinder.Rules.Tests` is also additive.

### AC6: New tests — engine evaluates each condition type correctly

A new test project `tests/Pinder.Rules.Tests/` (targeting `net8.0`, referencing `Pinder.Rules` + `xunit`) contains:

- **RuleBookTests** — loading valid YAML, querying by id, querying by type, loading empty YAML, handling malformed YAML
- **ConditionEvaluatorTests** — one or more tests per condition key (miss_range, beat_range, interest_range, need_range, level_range, natural_roll, streak, streak_minimum, action, conversation_start, shadow_threshold, miss_minimum), plus: null condition → false, empty condition → false, unknown keys ignored, multi-condition AND logic
- **OutcomeDispatcherTests** — one or more tests per outcome key, null outcome → no-op, unknown keys → no-op
- **EquivalenceTests** — for each §5 failure tier and each §6 interest state, load real enriched YAML and verify the engine matches the hardcoded C# result

### AC7: No hardcoded numeric constants remain in FailureScale.cs or InterestMeter.cs

This AC from the issue is **deferred**. The architecture decision explicitly preserves existing hardcoded constants for backward compatibility at prototype maturity. The rule engine provides a parallel, data-driven path. Constant removal happens in a follow-up sprint once GameSession integration is wired.

**Rationale**: Removing constants from `FailureScale.cs` and `InterestMeter.cs` would require `Pinder.Core` to depend on `Pinder.Rules` (to load the YAML-based replacements), breaking the zero-dependency invariant. The equivalence tests prove the YAML values match, making future removal safe.

---

## Edge Cases

### YAML Parsing

- **Empty YAML string**: `RuleBook.LoadFrom("")` returns a `RuleBook` with `Count == 0` and `All` as empty list. Does not throw.
- **YAML with no entries (empty sequence `[]`)**: Returns `RuleBook` with `Count == 0`.
- **Entries missing optional fields**: If a YAML entry has no `condition` or `outcome` key, the corresponding property is `null`. The entry is still loaded (descriptive rules exist).
- **Duplicate ids**: Later entry with the same id overwrites earlier one in the id index. `All` contains both. `GetById()` returns the last one.
- **Very large YAML (1000+ entries)**: Must load within 100ms. No architectural limit on entry count.

### Condition Evaluation

- **Null condition**: `Evaluate(null, state)` returns `false`.
- **Empty condition dictionary**: `Evaluate(new Dictionary<string,object>(), state)` returns `false`.
- **Range boundary values**: `miss_range: [1, 2]` with `MissMargin == 1` → true. `MissMargin == 2` → true. `MissMargin == 0` → false. `MissMargin == 3` → false.
- **Range with equal lo and hi**: `miss_range: [5, 5]` matches only `MissMargin == 5`.
- **Open-ended range**: Use a very large hi value (e.g. `miss_range: [10, 999]`). The evaluator does not have special "infinity" handling.
- **Missing shadow key**: If `shadow_threshold` references `"Dread"` but `ShadowValues` is null or doesn't contain `"Dread"`, the condition fails (returns false for that key).
- **YamlDotNet boxing**: Range values may deserialize as `List<object>` containing `long` values (YamlDotNet's default for integers). The evaluator must `Convert.ToInt32()` or handle both `int` and `long`.
- **Unknown condition keys**: Silently treated as matching. A condition `{ "unknown_key": 42, "miss_range": [1, 5] }` matches if `miss_range` matches — the unknown key does not cause failure.

### Outcome Dispatch

- **Null outcome**: `Dispatch(null, state, handler)` does nothing. No handler methods called.
- **Empty outcome dictionary**: Same as null — no-op.
- **Multiple outcome keys**: All recognized keys are dispatched. E.g. `{ interest_delta: -2, trap: true }` calls both `ApplyInterestDelta(-2)` and `ActivateTrap("")`.
- **`trap: false`**: No call to `ActivateTrap`. Only `trap: true` triggers activation.
- **`shadow_effect` nested dict**: Must handle YamlDotNet deserializing as `Dictionary<object, object>`. Convert keys to strings.
- **Unknown outcome keys**: Silently ignored.

---

## Error Conditions

| Scenario | Expected Behavior |
|----------|-------------------|
| `RuleBook.LoadFrom(null)` | Throws `ArgumentNullException` |
| `RuleBook.LoadFrom("not: valid: yaml: [")` | Throws `FormatException` with message indicating YAML parse failure |
| `RuleBook.LoadFrom("key: value")` | Throws `FormatException` — YAML is valid but root is a mapping, not a sequence |
| `ConditionEvaluator.Evaluate(condition, null)` | Throws `ArgumentNullException` |
| `OutcomeDispatcher.Dispatch(outcome, state, null)` | Throws `ArgumentNullException` |
| Range value is not a 2-element list | Condition key evaluates to `false` (does not throw) — defensive parsing |
| Outcome value is wrong type (e.g. `interest_delta: "abc"`) | Key is silently skipped (does not throw) — defensive parsing |
| `shadow_threshold` with missing `shadow` or `value` key | Condition key evaluates to `false` — defensive parsing |

The general error philosophy is: loading is strict (throw on malformed YAML), but evaluation is lenient (bad data in a single condition/outcome key does not crash the engine — it returns false or skips the key).

---

## Dependencies

### External Libraries
- **YamlDotNet 16.3.0** — YAML deserialization. netstandard2.0 compatible, zero transitive dependencies. Used in `Pinder.Rules` only.

### Internal Dependencies
- **Pinder.Core** — `Pinder.Rules` references `Pinder.Core` for type awareness (e.g. `ShadowStatType` if needed by the caller). The rule engine itself uses string-based shadow names to avoid tight coupling.
- **Enriched YAML files** — produced by #443 (round-trip fixes) and #444 (enrichment). At minimum, rules-v3-enriched.yaml must contain §5 and §6 entries with `condition`/`outcome` fields. If #444 is not yet complete, equivalence tests can use hand-crafted YAML matching the expected schema.

### Upstream Issue Dependencies
- **#443** — Round-trip diff fixes (clean YAML extraction)
- **#444** — Enrichment of all 9 YAML files (structured condition/outcome fields)

### What This Does NOT Depend On
- `Pinder.LlmAdapters` — no interaction
- `session-runner/` — no interaction (session-runner may consume Pinder.Rules in a future sprint)
- `GameSession` — no changes to GameSession this sprint

---

## Project Structure

```
src/Pinder.Rules/
├── Pinder.Rules.csproj
├── RuleEntry.cs
├── RuleBook.cs
├── GameState.cs
├── ConditionEvaluator.cs
├── OutcomeDispatcher.cs
└── IEffectHandler.cs

tests/Pinder.Rules.Tests/
├── Pinder.Rules.Tests.csproj    (net8.0, refs Pinder.Rules + xUnit)
├── RuleBookTests.cs
├── ConditionEvaluatorTests.cs
├── OutcomeDispatcherTests.cs
└── EquivalenceTests.cs
```

The solution file (`Pinder.sln` or equivalent) must be updated to include both new projects.

---

## Non-Functional Requirements (Prototype)

- Rule evaluation latency: < 1ms per rule condition+outcome
- YAML loading: < 100ms for all enriched files combined
- Memory: RuleBook holds all entries in memory (acceptable for < 500 rules total)
