# Issue #463 — Wire GameSession to RuleEngine for §5/§6/§7/§15 Rules

**Module**: docs/modules/rule-engine.md

---

## Overview

GameSession currently resolves game-balance constants (failure/success interest deltas, interest-state ranges, shadow thresholds, momentum bonuses, risk-tier XP multipliers) by calling hardcoded static classes (`FailureScale`, `SuccessScale`, `RiskTierBonus`, `ShadowThresholdEvaluator`) and private methods (`GetMomentumBonus`, `ApplyRiskTierMultiplier`). This issue introduces an `IRuleResolver` interface in `Pinder.Core.Interfaces` using Dependency Inversion so that GameSession can optionally resolve these values from YAML-loaded rules via `Pinder.Rules`, while falling back to hardcoded constants when no resolver is provided or when a rule lookup returns null.

This enables game designers to tune balance by editing YAML without recompilation. The zero-dependency constraint on `Pinder.Core` is preserved — `IRuleResolver` is an interface in Core; `RuleBookResolver` is the concrete implementation in `Pinder.Rules`.

---

## Function Signatures

### New Interface: `IRuleResolver` (Pinder.Core.Interfaces)

```csharp
namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Abstraction for data-driven rule resolution. All methods return nullable —
    /// null means "no matching rule found, caller should use hardcoded fallback."
    /// </summary>
    public interface IRuleResolver
    {
        /// <summary>
        /// §5: Given a failure tier and miss margin, return the interest delta.
        /// Returns null if no matching rule. Hardcoded fallback: FailureScale.GetInterestDelta().
        /// </summary>
        int? GetFailureInterestDelta(FailureTier tier, int missMargin);

        /// <summary>
        /// §5: Given a success margin and natural roll, return the interest delta.
        /// Returns null if no matching rule. Hardcoded fallback: SuccessScale.GetInterestDelta().
        /// </summary>
        int? GetSuccessInterestDelta(int beatMargin, int naturalRoll);

        /// <summary>
        /// §6: Given the current interest value, return the InterestState.
        /// Returns null if no matching rule. Hardcoded fallback: InterestMeter.GetState().
        /// </summary>
        InterestState? GetInterestState(int interest);

        /// <summary>
        /// §7: Given a shadow value, return the threshold level (0/1/2/3).
        /// Returns null if no matching rule. Hardcoded fallback: ShadowThresholdEvaluator.GetThresholdLevel().
        /// </summary>
        int? GetShadowThresholdLevel(int shadowValue);

        /// <summary>
        /// §15: Given a momentum streak length, return the roll bonus.
        /// Returns null if no matching rule. Hardcoded fallback: GameSession.GetMomentumBonus().
        /// </summary>
        int? GetMomentumBonus(int streak);

        /// <summary>
        /// §15: Given a risk tier, return the XP multiplier (e.g. 1.0, 1.5, 2.0, 3.0).
        /// Returns null if no matching rule. Hardcoded fallback: GameSession.ApplyRiskTierMultiplier().
        /// </summary>
        double? GetRiskTierXpMultiplier(RiskTier riskTier);
    }
}
```

Required using statements: `Pinder.Core.Rolls` (for `FailureTier`, `RiskTier`), `Pinder.Core.Conversation` (for `InterestState`).

### New Class: `RuleBookResolver` (Pinder.Rules)

```csharp
namespace Pinder.Rules
{
    public sealed class RuleBookResolver : IRuleResolver
    {
        /// <summary>
        /// Construct from one or more RuleBooks. Entries are merged additively.
        /// Thread-safe after construction (all lookups are read-only).
        /// </summary>
        public RuleBookResolver(params RuleBook[] books);

        /// <summary>
        /// Convenience factory: load YAML strings and build a resolver.
        /// Throws FormatException if any YAML is invalid.
        /// </summary>
        public static RuleBookResolver FromYaml(params string[] yamlContents);

        // IRuleResolver implementation (see interface above)
        public int? GetFailureInterestDelta(FailureTier tier, int missMargin);
        public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll);
        public InterestState? GetInterestState(int interest);
        public int? GetShadowThresholdLevel(int shadowValue);
        public int? GetMomentumBonus(int streak);
        public double? GetRiskTierXpMultiplier(RiskTier riskTier);
    }
}
```

### Extended Class: `GameSessionConfig` (Pinder.Core.Conversation)

Add one new optional parameter and property:

```csharp
public sealed class GameSessionConfig
{
    // Existing properties (unchanged)...
    public IGameClock? Clock { get; }
    public SessionShadowTracker? PlayerShadows { get; }
    public SessionShadowTracker? OpponentShadows { get; }
    public int? StartingInterest { get; }
    public string? PreviousOpener { get; }

    // New property
    /// <summary>Optional data-driven rule resolver. Null = use hardcoded statics.</summary>
    public IRuleResolver? Rules { get; }

    public GameSessionConfig(
        IGameClock? clock = null,
        SessionShadowTracker? playerShadows = null,
        SessionShadowTracker? opponentShadows = null,
        int? startingInterest = null,
        string? previousOpener = null,
        IRuleResolver? rules = null);  // NEW — appended, default null
}
```

### Extended Class: `GameSession` (Pinder.Core.Conversation)

No new public API. Internal changes only — the `_rules` field (type `IRuleResolver?`) is read from `GameSessionConfig.Rules`. Six call sites gain the fallback pattern.

---

## Input/Output Examples

### Example 1: Failure interest delta via YAML

**Input**: RuleBookResolver loaded from `rules-v3-enriched.yaml`. GameSession encounters a Misfire (miss by 4).

**YAML rule matched**: `§7.fail-tier.misfire` — condition `miss_range: [3, 5]`, outcome `interest_delta: -1`.

**Resolver call**: `GetFailureInterestDelta(FailureTier.Misfire, 4)` → `-1`.

**GameSession behavior**: Uses `-1` instead of calling `FailureScale.GetInterestDelta()`.

### Example 2: Success interest delta via YAML

**Input**: RuleBookResolver loaded. Player beats DC by 7.

**YAML rule matched**: `§7.success-scale.5-9` — condition `beat_range: [5, 9]`, outcome `interest_delta: 2`.

**Resolver call**: `GetSuccessInterestDelta(7, 14)` → `2`.

**GameSession behavior**: Uses `2` instead of calling `SuccessScale.GetInterestDelta()`.

### Example 3: Nat 20 success

**Input**: Natural roll is 20.

**YAML rule matched**: `§7.success-scale.nat-20` — condition `natural_roll: 20`, outcome `interest_delta: 4`.

**Resolver call**: `GetSuccessInterestDelta(beatMargin, 20)` → `4`. The Nat 20 rule matches on `natural_roll` before any `beat_range` rules.

### Example 4: Interest state from YAML

**Input**: Current interest = 12.

**YAML rule matched**: `§6.interest-state.😊-interested` — condition `interest_range: [10, 15]`, outcome `state: "😊 Interested"`.

**Resolver call**: `GetInterestState(12)` → `InterestState.Interested`.

### Example 5: Shadow threshold from YAML

**Input**: Shadow value = 14.

**YAML rule matched**: `§9.shadow-threshold.*.t2` — condition `threshold: 12` (closest that is ≤ 14).

**Resolver call**: `GetShadowThresholdLevel(14)` → `2`.

### Example 6: Momentum bonus from YAML

**Input**: Momentum streak = 4.

**YAML rule matched**: `§6.momentum.4-wins` — condition `streak: 4`, outcome `roll_bonus: 2`.

**Resolver call**: `GetMomentumBonus(4)` → `2`.

### Example 7: Risk-tier XP multiplier from YAML

**Input**: Risk tier = Hard.

**YAML rule matched**: `§2.risk-tier.hard` — condition `need_range: [11, 15]`, outcome `xp_multiplier: 2.0`.

**Resolver call**: `GetRiskTierXpMultiplier(RiskTier.Hard)` → `2.0`.

### Example 8: Fallback when no resolver

**Input**: `GameSessionConfig` constructed without `rules` parameter (null).

**Behavior**: GameSession calls `FailureScale.GetInterestDelta()`, `SuccessScale.GetInterestDelta()`, etc. directly — identical to current behavior. Zero regression.

### Example 9: Fallback when resolver returns null

**Input**: Resolver loaded from incomplete YAML missing §5 rules.

**Resolver call**: `GetFailureInterestDelta(FailureTier.Fumble, 2)` → `null`.

**GameSession behavior**: Falls back to `FailureScale.GetInterestDelta(rollResult)` → `-1`.

---

## Acceptance Criteria

### AC-1: RuleBook loaded from `rules/extracted/rules-v3-enriched.yaml` at session start

The host (e.g., session-runner) loads the YAML file content and creates a `RuleBookResolver` instance. This resolver is passed into `GameSessionConfig.Rules`. The `RuleBook.LoadFrom(string)` call happens **outside** GameSession — GameSession receives an `IRuleResolver` and is unaware of YAML.

For multi-file scenarios (§5/§6/§7 from `rules-v3-enriched.yaml`, §15 from `risk-reward-and-hidden-depth-enriched.yaml`), the host loads both and passes both to `RuleBookResolver(book1, book2)`.

### AC-2: §5 failure tier → interest delta flows through the engine

**Current code** (GameSession.cs ~line 488):
```csharp
interestDelta = FailureScale.GetInterestDelta(rollResult);
```

**New code pattern**:
```csharp
interestDelta = _rules?.GetFailureInterestDelta(rollResult.Tier, rollResult.MissMargin)
                ?? FailureScale.GetInterestDelta(rollResult);
```

The `RuleBookResolver.GetFailureInterestDelta(tier, missMargin)` method constructs a `GameState(missMargin: missMargin)`, iterates over rules of type `"interest_change"` with `miss_range` conditions, runs `ConditionEvaluator.Evaluate()`, and returns the `interest_delta` from the first matching rule's outcome. For `FailureTier.Legendary` (Nat 1), the method should check `natural_roll: 1` condition as well.

**YAML rules consumed**:
| Rule ID | Condition | Outcome |
|---------|-----------|---------|
| `§7.fail-tier.fumble` | `miss_range: [1, 2]` | `interest_delta: -1` |
| `§7.fail-tier.misfire` | `miss_range: [3, 5]` | `interest_delta: -1` |
| `§7.fail-tier.trope-trap` | `miss_range: [6, 9]` | `interest_delta: -2` |
| `§7.fail-tier.catastrophe` | `miss_range: [10, 99]` | `interest_delta: -3` |
| `§7.fail-tier.legendary` | `natural_roll: 1` | `interest_delta: -4` |

### AC-3: §6 interest ranges → InterestState flows through the engine

**Current code** (GameSession uses `_interest.GetState()` in multiple places):
The `InterestMeter.GetState()` method is called directly.

**New behavior**: GameSession wraps interest-state resolution:
```csharp
InterestState state = _rules?.GetInterestState(_interest.Current) ?? _interest.GetState();
```

Note: `InterestMeter` class itself is NOT modified. GameSession overrides the state lookup at call sites where the state drives game logic (advantage/disadvantage, ghost trigger, end conditions). The `InterestMeter.GrantsAdvantage` and `GrantsDisadvantage` properties still use the internal `GetState()` — if the resolver changes state boundaries, GameSession must also override those checks.

**YAML rules consumed**:
| Rule ID | Condition | Outcome State |
|---------|-----------|---------------|
| `§6.interest-state.💀-unmatched` | `interest_range: [0, 0]` | Unmatched |
| `§6.interest-state.😐-bored` | `interest_range: [1, 4]` | Bored |
| `§6.interest-state.🤔-lukewarm` | `interest_range: [5, 9]` | Lukewarm |
| `§6.interest-state.😊-interested` | `interest_range: [10, 15]` | Interested |
| `§6.interest-state.😍-very-into-it` | `interest_range: [16, 20]` | VeryIntoIt |
| `§6.interest-state.🔥-almost-there` | `interest_range: [21, 24]` | AlmostThere |
| `§6.interest-state.🎉-date-secured` | `interest_range: [25, 25]` | DateSecured |

The resolver must parse the emoji-prefixed state string to the `InterestState` enum. Mapping: strip emoji prefix, normalize to enum name (e.g., `"😊 Interested"` → `InterestState.Interested`, `"😍 Very Into It"` → `InterestState.VeryIntoIt`).

### AC-4: §7 shadow thresholds flow through the engine

**Current code** (GameSession.cs ~lines 118, 263, 1049, 1164):
```csharp
int tier = ShadowThresholdEvaluator.GetThresholdLevel(effectiveVal);
```

**New code pattern**:
```csharp
int tier = _rules?.GetShadowThresholdLevel(effectiveVal)
           ?? ShadowThresholdEvaluator.GetThresholdLevel(effectiveVal);
```

The `RuleBookResolver.GetShadowThresholdLevel(shadowValue)` method determines the highest matching threshold. YAML rules use `threshold: 6`, `threshold: 12`, `threshold: 18` as conditions. The resolver returns:
- `0` if shadowValue < 6
- `1` if 6 ≤ shadowValue < 12
- `2` if 12 ≤ shadowValue < 18
- `3` if shadowValue ≥ 18

This is generic (not per-shadow-type). The per-shadow-type effects (which stat gets disadvantage, etc.) are already handled by GameSession's existing logic — only the tier number comes from the resolver.

### AC-5: §15 momentum bonuses flow through the engine

**Current code** (GameSession.cs ~line 382 and method at ~line 968):
```csharp
_pendingMomentumBonus = GetMomentumBonus(_momentumStreak);
// ...
private static int GetMomentumBonus(int streak)
{
    if (streak >= 5) return 3;
    if (streak >= 3) return 2;
    return 0;
}
```

**New code pattern**:
```csharp
_pendingMomentumBonus = _rules?.GetMomentumBonus(_momentumStreak)
                        ?? GetMomentumBonus(_momentumStreak);
```

**YAML rules consumed** (from `risk-reward-and-hidden-depth-enriched.yaml`):
| Rule ID | Condition | Outcome |
|---------|-----------|---------|
| `§6.momentum.2-wins` | `streak: 2` | `roll_bonus: 0` (effect: none) |
| `§6.momentum.3-wins` | `streak: 3` | `roll_bonus: 2` |
| `§6.momentum.4-wins` | `streak: 4` | `roll_bonus: 2` |
| `§6.momentum.5plus-wins` | `streak_minimum: 5` | `roll_bonus: 3` |

The resolver iterates momentum rules in order. For `streak: 4`, it matches `§6.momentum.4-wins` (exact match). For `streak: 7`, it matches `§6.momentum.5plus-wins` (via `streak_minimum: 5`).

### AC-6: §15 risk tier XP multipliers flow through the engine

**Current code** (GameSession.cs ~line 728 and method at ~line 745):
```csharp
int xp = ApplyRiskTierMultiplier(baseXp, rollResult.RiskTier);
// ...
private static int ApplyRiskTierMultiplier(int baseXp, RiskTier riskTier)
```

**New code pattern**:
```csharp
double? yamlMultiplier = _rules?.GetRiskTierXpMultiplier(rollResult.RiskTier);
int xp = yamlMultiplier.HasValue
    ? (int)Math.Round(baseXp * yamlMultiplier.Value)
    : ApplyRiskTierMultiplier(baseXp, rollResult.RiskTier);
```

**YAML rules consumed** (from `risk-reward-and-hidden-depth-enriched.yaml`):
| Rule ID | Condition | Outcome |
|---------|-----------|---------|
| `§2.risk-tier.safe` | `need_range: [1, 5]` | `xp_multiplier: 1.0` |
| `§2.risk-tier.medium` | `need_range: [6, 10]` | `xp_multiplier: 1.5` |
| `§2.risk-tier.hard` | `need_range: [11, 15]` | `xp_multiplier: 2.0` |
| `§2.risk-tier.bold` | `need_range: [16, 99]` | `xp_multiplier: 3.0` |

Note: The resolver maps `RiskTier` enum to `need_range` internally. The `RiskTier` values correspond to: Safe → need ≤ 5, Medium → need 6–10, Hard → need 11–15, Bold → need 16+. The resolver can use a representative `need` value from each tier's range to match against the YAML condition.

### AC-7: All 45 `RulesSpecTests` assertions pass against the wired implementation

The 45 non-skipped tests in `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs` currently test the hardcoded static classes directly. They must continue to pass unchanged. Additionally, new tests should be added (in `Pinder.Rules.Tests`) that verify `RuleBookResolver` returns identical values to the hardcoded statics for all rule IDs listed in AC-2 through AC-6.

### AC-8: All 17 `NotImplementedException` stubs remain as stubs

The 17 `[Fact(Skip = "...")]` tests in `RulesSpecTests.cs` are for LLM/qualitative effects that cannot be mechanically tested. They must not be un-skipped or modified.

### AC-9: All 2507 existing tests still pass

All changes must be backward-compatible. When `GameSessionConfig.Rules` is null (the default), behavior is identical to the pre-change code. The new `rules` parameter on `GameSessionConfig` has a default of `null`, so existing callers compile without modification.

### AC-10: Fallback to hardcoded constants if YAML missing/corrupt

If `RuleBook.LoadFrom()` throws `FormatException` due to corrupt YAML, the host should catch the exception and construct `GameSessionConfig` without a resolver (i.e., `rules: null`). GameSession itself never loads YAML — it only receives `IRuleResolver?`.

If the resolver is provided but returns `null` for a specific lookup (e.g., no matching §5 rule), GameSession falls back to the corresponding hardcoded static method. This is the `??` fallback pattern described in each AC above.

### AC-11: Build clean

The solution must compile with zero warnings and zero errors. `Pinder.Core` must not reference `Pinder.Rules` or `YamlDotNet`.

---

## Edge Cases

### Empty or null YAML content
`RuleBook.LoadFrom("")` and `RuleBook.LoadFrom(null)` throw `FormatException`. The host catches this and passes `rules: null` to `GameSessionConfig`. GameSession operates with hardcoded fallback only.

### YAML with missing rule types
If the loaded YAML has §5 rules but no §6 rules, `GetInterestState()` returns null for all calls. GameSession falls back to `InterestMeter.GetState()` for interest-state resolution while still using YAML for failure deltas.

### Overlapping conditions in YAML
Multiple rules may match the same game state (e.g., `§7.success-scale.nat-20` with `natural_roll: 20` AND a `beat_range` rule). The resolver must prioritize specific conditions over general ones. For Nat 20, `natural_roll: 20` takes precedence. Implementation detail: check Nat 20 condition first, then fall through to `beat_range`.

### Streak value not covered by any rule
For `streak: 1` or `streak: 0`, no momentum rule matches (`§6.momentum.2-wins` requires exactly 2). Resolver returns `null`. GameSession falls back to `GetMomentumBonus(1)` → `0`.

### Shadow value of 0
`GetShadowThresholdLevel(0)` should return `0` (no threshold reached). No YAML rule has `threshold: 0`, so the resolver returns `0` (or `null`, triggering fallback to `ShadowThresholdEvaluator.GetThresholdLevel(0)` → `0`).

### Multiple RuleBooks with no ID collisions
`RuleBookResolver(book1, book2)` merges entries additively. All lookups search across all books. Since `rules-v3-enriched.yaml` uses `§N.` prefixes and `risk-reward-and-hidden-depth-enriched.yaml` uses `§N.` with different section numbers, no ID collisions are expected.

### Resolver provided but all methods return null
Degenerate case: resolver is non-null but loaded from an empty or irrelevant YAML. Every call returns null. GameSession falls back to hardcoded statics for every operation. Functionally identical to `rules: null`.

### Interest at boundary values
- Interest = 0 → Unmatched (both hardcoded and YAML agree: `interest_range: [0, 0]`)
- Interest = 4 → Bored (boundary: `interest_range: [1, 4]`)
- Interest = 5 → Lukewarm (boundary: `interest_range: [5, 9]`)
- Interest = 25 → DateSecured (boundary: `interest_range: [25, 25]`)

### Miss margin = 0 (exact DC)
A miss margin of 0 means the roll exactly equaled DC (which is still a success since `FinalTotal >= DC` ⇒ success). This case should not reach `GetFailureInterestDelta()`. If it does, no `miss_range` rule matches (smallest is `[1, 2]`), so resolver returns null, fallback returns 0.

---

## Error Conditions

| Error | Source | Expected behavior |
|-------|--------|-------------------|
| Corrupt YAML | `RuleBook.LoadFrom()` | Throws `FormatException`. Host catches, creates config without resolver. |
| YAML file not found | Host (file I/O) | Host catches `IOException` / `FileNotFoundException`, creates config without resolver. |
| `IRuleResolver` method throws unexpected exception | `RuleBookResolver` bug | GameSession should NOT catch — let it propagate. This is a programming error, not a data error. |
| `GameSessionConfig.Rules` is null | Normal operation | All six call sites use hardcoded fallback via `??` operator. |
| YAML rule has `outcome` without `interest_delta` key | `RuleBookResolver` | Returns null for that lookup (key not present → no value to extract). Fallback to hardcoded. |
| YAML rule `interest_delta` is non-numeric | `RuleBookResolver` | `ToInt()` helper (already in `ConditionEvaluator`) returns 0. Resolver may return 0 or null depending on implementation — either way, fallback is safe. |
| Enum parsing fails for InterestState | `RuleBookResolver.GetInterestState()` | If the YAML state string cannot be mapped to `InterestState` enum, return null. Fallback to `InterestMeter.GetState()`. |

---

## Dependencies

### Internal (Pinder.Core)

| Component | Usage |
|-----------|-------|
| `Pinder.Core.Interfaces` | `IRuleResolver` interface definition lives here |
| `Pinder.Core.Rolls.FailureTier` | Parameter type for `GetFailureInterestDelta()` |
| `Pinder.Core.Rolls.RiskTier` | Parameter type for `GetRiskTierXpMultiplier()` |
| `Pinder.Core.Conversation.InterestState` | Return type for `GetInterestState()` |
| `Pinder.Core.Conversation.GameSessionConfig` | Extended with `IRuleResolver? Rules` |
| `Pinder.Core.Conversation.GameSession` | Six call sites modified with fallback pattern |
| `Pinder.Core.Rolls.FailureScale` | Hardcoded fallback (unchanged) |
| `Pinder.Core.Rolls.SuccessScale` | Hardcoded fallback (unchanged) |
| `Pinder.Core.Rolls.RiskTierBonus` | Hardcoded fallback (unchanged, used indirectly) |
| `Pinder.Core.Stats.ShadowThresholdEvaluator` | Hardcoded fallback (unchanged) |

### Internal (Pinder.Rules)

| Component | Usage |
|-----------|-------|
| `Pinder.Rules.RuleBook` | Loaded from YAML by host, passed to `RuleBookResolver` |
| `Pinder.Rules.RuleEntry` | Individual rule lookup by ID or type |
| `Pinder.Rules.ConditionEvaluator` | Used by `RuleBookResolver` to match rules against game state |
| `Pinder.Rules.GameState` | Snapshot passed to `ConditionEvaluator.Evaluate()` |

### External

| Dependency | Project | Version |
|------------|---------|---------|
| `YamlDotNet` | `Pinder.Rules` only | 16.3.0 |

### YAML Data Files

| File | Rules Provided |
|------|---------------|
| `rules/extracted/rules-v3-enriched.yaml` | §5 failure tiers, §5 success scale, §6 interest states, §7 shadow thresholds |
| `rules/extracted/risk-reward-and-hidden-depth-enriched.yaml` | §15 momentum bonuses, §15 risk-tier XP multipliers |

### Prerequisite

- Issue #446 (rule engine project) must be merged before implementation.
