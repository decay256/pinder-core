# Contract: Issue #463 — Wire GameSession to Rule Engine

## Architecture Overview

This sprint continues the existing architecture with one **structural
addition**: a new `IRuleResolver` interface in `Pinder.Core.Interfaces`
that bridges the dependency gap between `Pinder.Core` (zero deps) and
`Pinder.Rules` (YamlDotNet). This is classic **Dependency Inversion** —
Core defines the abstraction, Rules provides the implementation.

**Existing architecture**: Pinder.Core is a zero-dependency .NET
Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation
turns, calling static classes (`FailureScale`, `SuccessScale`,
`RiskTierBonus`, `ShadowThresholdEvaluator`) and instance methods
(`InterestMeter.GetState()`, `GetMomentumBonus()`) for hardcoded game
constants. `Pinder.Rules` (separate project, depends on Core +
YamlDotNet) provides `RuleBook`, `ConditionEvaluator`, and
`OutcomeDispatcher` but is **not wired** to GameSession.

**What is changing**:

1. New `IRuleResolver` interface in `Pinder.Core.Interfaces/`
2. New `RuleBookResolver` class in `Pinder.Rules/` (implements `IRuleResolver`)
3. `GameSessionConfig` gains optional `IRuleResolver? Rules` property
4. `GameSession` uses `IRuleResolver` when available, falls back to
   hardcoded statics when null
5. `RuleBook.LoadFrom` gains multi-file merge support (or the resolver
   loads multiple RuleBooks)

**What is NOT changing**:
- Static classes remain (FailureScale, SuccessScale, etc.) — fallback
- InterestMeter.GetState() remains — used when no resolver
- Pinder.LlmAdapters — untouched
- Existing test behavior — all 2651 tests pass unchanged
- NullLlmAdapter — untouched

---

## Separation of Concerns Map

- IRuleResolver
  - Responsibility:
    - Abstraction for rule-resolved game constants
    - Lives in Pinder.Core.Interfaces (zero deps)
  - Interface:
    - GetFailureInterestDelta(missMargin, naturalRoll)
    - GetSuccessInterestDelta(beatMargin, naturalRoll)
    - GetInterestState(interest)
    - GetShadowThresholdLevel(shadowValue)
    - GetMomentumBonus(streak)
    - GetRiskTierXpMultiplier(riskTier)
  - Must NOT know:
    - YAML parsing
    - RuleBook internals
    - GameSession orchestration

- RuleBookResolver
  - Responsibility:
    - Implements IRuleResolver using RuleBook + ConditionEvaluator
    - Loads YAML once, caches lookup results
  - Interface:
    - Constructor takes RuleBook (or multiple)
    - Implements all IRuleResolver methods
    - Static factory: FromYamlFiles(params string[])
  - Must NOT know:
    - GameSession internals
    - InterestMeter implementation
    - How GameSession uses the resolved values

- GameSessionConfig (extended)
  - Responsibility:
    - Carries optional IRuleResolver
  - Interface:
    - New property: IRuleResolver? Rules
  - Must NOT know:
    - RuleBook, YAML, or Pinder.Rules

- GameSession (extended)
  - Responsibility:
    - Uses IRuleResolver when available
    - Falls back to hardcoded statics when null
  - Interface:
    - No new public API — behavior change only
  - Must NOT know:
    - RuleBook, YAML parsing, Pinder.Rules project

- Static classes (unchanged, fallback)
  - Responsibility:
    - FailureScale, SuccessScale, RiskTierBonus
    - ShadowThresholdEvaluator, InterestMeter
  - Interface:
    - Existing static methods unchanged
  - Must NOT know:
    - IRuleResolver exists (they are the fallback)

---

## Interface Definitions

### IRuleResolver (new — Pinder.Core.Interfaces)

```csharp
// src/Pinder.Core/Interfaces/IRuleResolver.cs
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Abstraction for data-driven game rule resolution.
    /// When injected into GameSession via GameSessionConfig, replaces
    /// hardcoded static class lookups for §5/§6/§7/§15 rules.
    /// Returns null when no matching rule is found (caller uses
    /// hardcoded fallback).
    /// </summary>
    public interface IRuleResolver
    {
        /// <summary>
        /// §5 failure tier → interest delta.
        /// Returns null if no matching rule found.
        /// </summary>
        /// <param name="missMargin">
        ///   How much the roll missed by (positive int).
        /// </param>
        /// <param name="naturalRoll">
        ///   The natural d20 value (1-20). 1 = Legendary fail.
        /// </param>
        int? GetFailureInterestDelta(int missMargin, int naturalRoll);

        /// <summary>
        /// §5 success scale → interest delta.
        /// Returns null if no matching rule found.
        /// </summary>
        /// <param name="beatMargin">
        ///   How much the roll beat DC by (positive int).
        /// </param>
        /// <param name="naturalRoll">
        ///   The natural d20 value (1-20). 20 = crit.
        /// </param>
        int? GetSuccessInterestDelta(int beatMargin, int naturalRoll);

        /// <summary>
        /// §6 interest value → InterestState mapping.
        /// Returns null if no matching rule found.
        /// </summary>
        InterestState? GetInterestState(int interest);

        /// <summary>
        /// §7 shadow value → threshold level (0/1/2/3).
        /// Returns null if no matching rule found.
        /// </summary>
        int? GetShadowThresholdLevel(int shadowValue);

        /// <summary>
        /// §15 momentum streak → roll bonus.
        /// Returns null if no matching rule found.
        /// </summary>
        int? GetMomentumBonus(int streak);

        /// <summary>
        /// §15 risk tier → XP multiplier.
        /// Returns null if no matching rule found.
        /// </summary>
        double? GetRiskTierXpMultiplier(RiskTier riskTier);
    }
}
```

**Design notes:**
- All methods return nullable — null means "no rule matched, use
  hardcoded fallback". This is the safety net per AC.
- Parameters are primitives, not RollResult — keeps the interface
  decoupled from internal types where possible.
- `GetInterestState` returns `InterestState?` (Core's own enum).
- `GetShadowThresholdLevel` returns `int?` (0/1/2/3 tier, same
  semantics as `ShadowThresholdEvaluator`).

---

### GameSessionConfig (extended — Pinder.Core.Conversation)

```csharp
// Add to existing GameSessionConfig.cs
using Pinder.Core.Interfaces;

// New property:
/// <summary>
/// Optional rule resolver for data-driven game constants.
/// When non-null, GameSession uses this for §5/§6/§7/§15 lookups.
/// When null or when a lookup returns null, hardcoded fallback is used.
/// </summary>
public IRuleResolver? Rules { get; }

// Constructor gains new optional param:
public GameSessionConfig(
    IGameClock? clock = null,
    SessionShadowTracker? playerShadows = null,
    SessionShadowTracker? opponentShadows = null,
    int? startingInterest = null,
    string? previousOpener = null,
    IRuleResolver? rules = null)  // NEW
```

**Backward compatibility**: The new param has a default of null.
All existing callers compile unchanged. When null, GameSession
uses hardcoded statics (identical to current behavior).

---

### RuleBookResolver (new — Pinder.Rules)

```csharp
// src/Pinder.Rules/RuleBookResolver.cs
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;

namespace Pinder.Rules
{
    /// <summary>
    /// Implements IRuleResolver by evaluating conditions against
    /// loaded RuleBook entries. Loads YAML once at construction.
    ///
    /// Lookup strategy:
    /// - Iterates rules of matching type
    /// - Evaluates each rule's condition against a GameState snapshot
    /// - Returns the first matching rule's outcome value
    /// - Returns null if no rule matches
    ///
    /// Thread-safe after construction (RuleBook is immutable).
    /// </summary>
    public sealed class RuleBookResolver : IRuleResolver
    {
        private readonly RuleBook _rules;

        /// <summary>
        /// Create resolver from a pre-loaded RuleBook.
        /// </summary>
        public RuleBookResolver(RuleBook rules);

        /// <summary>
        /// Create resolver from one or more RuleBook instances.
        /// Rules are merged — later books override earlier on id
        /// collision.
        /// </summary>
        public RuleBookResolver(params RuleBook[] books);

        /// <summary>
        /// Convenience factory: load YAML content strings and
        /// create a resolver. Throws FormatException on bad YAML.
        /// </summary>
        public static RuleBookResolver FromYaml(
            params string[] yamlContents);

        // --- IRuleResolver implementation ---

        /// <summary>
        /// §5: Matches fail-tier rules by miss_range/natural_roll.
        /// Scans rules with type "interest_change" in §7 section.
        /// Returns outcome.interest_delta or null.
        /// </summary>
        public int? GetFailureInterestDelta(
            int missMargin, int naturalRoll);

        /// <summary>
        /// §5: Matches success-scale rules by beat_range/natural_roll.
        /// Scans rules with type "interest_change" in §7 section.
        /// Returns outcome.interest_delta or null.
        /// </summary>
        public int? GetSuccessInterestDelta(
            int beatMargin, int naturalRoll);

        /// <summary>
        /// §6: Matches interest-state rules by interest_range.
        /// Scans rules with id prefix "§6.interest-state.".
        /// Parses outcome.state → InterestState enum.
        /// Returns null if no match or unparseable state.
        /// </summary>
        public InterestState? GetInterestState(int interest);

        /// <summary>
        /// §7: Shadow thresholds are at fixed values (6/12/18).
        /// Scans rules with id prefix "§9.shadow-threshold.".
        /// Returns highest matching tier for the value.
        /// Returns null if no shadow threshold rules found.
        /// NOTE: Shadow thresholds are generic (not per-shadow-type)
        /// for the tier calculation. Per-shadow effects are separate.
        /// </summary>
        public int? GetShadowThresholdLevel(int shadowValue);

        /// <summary>
        /// §15: Momentum bonus from streak.
        /// Scans rules with id prefix "§6.momentum.".
        /// Located in risk-reward-and-hidden-depth-enriched.yaml.
        /// Returns outcome.roll_bonus or null.
        /// </summary>
        public int? GetMomentumBonus(int streak);

        /// <summary>
        /// §15: Risk tier XP multiplier.
        /// Scans rules with id prefix "§2.risk-tier.".
        /// Located in risk-reward-and-hidden-depth-enriched.yaml.
        /// Returns outcome.xp_multiplier or null.
        /// </summary>
        public double? GetRiskTierXpMultiplier(RiskTier riskTier);
    }
}
```

---

### GameSession integration pattern (behavioral contract)

```
WHEN GameSession needs a failure interest delta:
  IF _rules != null:
    resolved = _rules.GetFailureInterestDelta(missMargin, naturalRoll)
    IF resolved != null: use resolved
    ELSE: fallback to FailureScale.GetInterestDelta(rollResult)
  ELSE:
    use FailureScale.GetInterestDelta(rollResult)

WHEN GameSession needs a success interest delta:
  IF _rules != null:
    resolved = _rules.GetSuccessInterestDelta(beatMargin, naturalRoll)
    IF resolved != null: use resolved
    ELSE: fallback to SuccessScale.GetInterestDelta(rollResult)
  ELSE:
    use SuccessScale.GetInterestDelta(rollResult)

WHEN InterestMeter.GetState() is called:
  InterestMeter gains an optional IRuleResolver in constructor.
  IF resolver != null:
    resolved = resolver.GetInterestState(Current)
    IF resolved != null: return resolved.Value
  Fallback to hardcoded switch.

WHEN ShadowThresholdEvaluator.GetThresholdLevel() is needed:
  GameSession calls _rules.GetShadowThresholdLevel(shadowValue) ?? 
    ShadowThresholdEvaluator.GetThresholdLevel(shadowValue)

WHEN GetMomentumBonus() is needed:
  GameSession calls _rules.GetMomentumBonus(streak) ??
    GetMomentumBonus(streak)  // existing private static

WHEN ApplyRiskTierMultiplier() is needed:
  GameSession calls _rules.GetRiskTierXpMultiplier(riskTier) ??
    hardcoded switch
```

---

### YAML loading contract

**Which files to load:**
- `rules/extracted/rules-v3-enriched.yaml` — §5, §6, §7 rules
- `rules/extracted/risk-reward-and-hidden-depth-enriched.yaml` — §15 rules

**Loading responsibility**: The **host** (session-runner or test setup)
loads YAML files, creates `RuleBookResolver`, and passes it via
`GameSessionConfig.Rules`. GameSession does NOT load files itself.

**RuleBook merge**: `RuleBookResolver(params RuleBook[])` merges
entries from multiple RuleBooks. This is needed because §15 rules
live in a different YAML file than §5/§6/§7.

---

### Test contract

```
tests/Pinder.Rules.Tests/RuleBookResolverTests.cs
- Test each IRuleResolver method against known YAML entries
- Verify fallback (null return) when rules are empty
- Verify equivalence with hardcoded C# for all §5/§6/§7/§15 values

tests/Pinder.Core.Tests/GameSession/RuleResolverIntegrationTests.cs
- GameSession with IRuleResolver wired via GameSessionConfig
- Verify failure deltas match rule-resolved values
- Verify interest states match rule-resolved values
- Verify shadow thresholds match rule-resolved values
- Verify momentum bonuses match rule-resolved values
- Verify XP multipliers match rule-resolved values
- Verify fallback when IRuleResolver returns null
- Verify fallback when IRuleResolver is not provided (config.Rules = null)
```

---

## Dependencies

| Component | Depends on |
|-----------|-----------|
| IRuleResolver | Pinder.Core.Conversation.InterestState, Pinder.Core.Rolls.RiskTier |
| RuleBookResolver | Pinder.Rules.RuleBook, Pinder.Rules.ConditionEvaluator, Pinder.Core.Interfaces.IRuleResolver |
| GameSessionConfig | Pinder.Core.Interfaces.IRuleResolver |
| GameSession | IRuleResolver (optional), existing static classes (fallback) |

---

## NFR (prototype)

- **Latency**: IRuleResolver lookups < 1ms per call. RuleBook loaded
  once at construction, not per-turn.
- **Reliability**: Null-return fallback guarantees zero regression.
  If YAML is missing/corrupt, RuleBookResolver.FromYaml throws at
  construction time — host catches and passes null.
- **Backward compat**: All 2651 existing tests pass unchanged.
  IRuleResolver is optional with null default.

---

## Implementation Strategy

### Single-issue sprint — one implementer

This is a single issue (#463). One backend-engineer implements all
changes in order:

1. **IRuleResolver interface** (Pinder.Core.Interfaces/) — 1 file
2. **GameSessionConfig extension** (add Rules property) — 1 file edit
3. **RuleBookResolver** (Pinder.Rules/) — 1 new file
4. **GameSession wiring** — edit ~15 lines across 5 call sites
5. **InterestMeter.GetState() wiring** (optional — see note below)
6. **Tests** — RuleBookResolverTests + GameSession integration tests
7. **Verify all 2651 existing tests + 45 RulesSpec tests pass**

### InterestMeter.GetState() decision

Two options for wiring `GetState()`:
- **Option A**: GameSession calls `_rules.GetInterestState()` and
  never touches InterestMeter.GetState() — simpler, less invasive
- **Option B**: InterestMeter gains an IRuleResolver — more elegant
  but invasive to a widely-used class

**Recommended: Option A**. GameSession already reads `_interest.Current`
and calls `_interest.GetState()`. Replace the `GetState()` calls in
GameSession with `_rules?.GetInterestState(_interest.Current) ?? _interest.GetState()`.
This avoids modifying InterestMeter (used in 50+ test files).

### Shadow threshold wiring note

`ShadowThresholdEvaluator.GetThresholdLevel()` is called in 4 places
in GameSession. The implementer should create a private helper:

```csharp
private int GetShadowThreshold(int shadowValue)
{
    return _rules?.GetShadowThresholdLevel(shadowValue)
        ?? ShadowThresholdEvaluator.GetThresholdLevel(shadowValue);
}
```

### Risk: Momentum rules in separate YAML

The momentum rules (§15) live in `risk-reward-and-hidden-depth-enriched.yaml`,
not `rules-v3-enriched.yaml`. The AC says "loaded from
`rules/extracted/rules-v3-enriched.yaml`" but also requires §15 wiring.

**Resolution**: Load both files. `RuleBookResolver.FromYaml()` accepts
multiple YAML strings. The host loads both files and passes them in.
This satisfies both the "load from YAML" AC and the §15 wiring AC.

### Fallback safety

If YAML loading fails (missing file, corrupt YAML):
- `RuleBookResolver.FromYaml()` throws `FormatException`
- Host catches the exception and passes `rules: null` to GameSessionConfig
- GameSession uses hardcoded fallback (identical to current behavior)
- Zero regression risk

---

## SPRINT PLAN CHANGES

None. The issue is well-defined and implementable by a single agent
in one session.
