# Vision Review — Sprint: Wire GameSession to Rule Engine

## Issue: #463

## Alignment: ✅

This sprint is a natural and well-timed progression. The rule engine was built in the previous sprint but left disconnected — wiring it to GameSession is the obvious next step that unlocks the product's key value prop: **balance tuning via YAML without recompilation**. The Dependency Inversion approach (IRuleResolver in Core, RuleBookResolver in Rules) correctly preserves the zero-dependency invariant that makes Unity integration possible. This is the highest-leverage work right now — without this wiring, the entire Rules DSL pipeline is a dead-end artifact.

## Data Flow Traces

### §5 Failure/Success Interest Delta
- Roll resolves → `RollResult` has `MissMargin`/`BeatMargin` + `NaturalRoll` → GameSession calls `_rules?.GetFailureInterestDelta(missMargin, naturalRoll)` → `RuleBookResolver` creates `GameState(missMargin, naturalRoll)` → `ConditionEvaluator.Evaluate()` against `miss_range`/`natural_roll` conditions → extract `interest_delta` from outcome → return to GameSession
- Required fields: `missMargin`, `beatMargin`, `naturalRoll`
- ✅ All fields available in `GameState` and supported by `ConditionEvaluator`

### §6 Interest State Resolution
- `InterestMeter.Current` → `_rules?.GetInterestState(interest)` → `RuleBookResolver` creates `GameState(interest)` → evaluate `interest_range` conditions → extract `state` string → parse to `InterestState` enum → return
- Required fields: `interest` (int)
- ✅ `interest_range` is supported by `ConditionEvaluator`

### §7 Shadow Threshold Level
- Shadow value → `_rules?.GetShadowThresholdLevel(shadowValue)` → `RuleBookResolver` creates `GameState(???)` → evaluate against shadow threshold rules
- Required fields: shadow value, shadow name
- ⚠️ **Gap**: YAML shadow threshold rules use `condition: { shadow: "Dread", threshold: 6 }`. Neither `shadow` nor `threshold` are recognized keys in `ConditionEvaluator` — they fall through to `default: break` (unknown keys ignored = treated as matching). This means ALL shadow threshold rules would match any `GameState`, producing incorrect results. The implementer must extend `ConditionEvaluator` with `threshold` support, or `RuleBookResolver` must handle shadow thresholds via custom logic outside the evaluator.

### §15 Momentum Bonus
- Streak count → `_rules?.GetMomentumBonus(streak)` → `RuleBookResolver` creates `GameState(streak)` → evaluate `streak`/`streak_minimum` conditions → extract `roll_bonus` from outcome
- Required fields: `streak` (int)
- ✅ Both `streak` and `streak_minimum` are supported by `ConditionEvaluator`

### §15 Risk-Tier XP Multiplier
- `RiskTier` → `_rules?.GetRiskTierXpMultiplier(riskTier)` → `RuleBookResolver` creates `GameState(needToHit)` → evaluate `need_range` conditions → extract `xp_multiplier` from outcome
- Required fields: `needToHit` (int) — note: risk tier is derived from need-to-hit
- ✅ `need_range` is supported by `ConditionEvaluator`

## Unstated Requirements

- **Rule evaluation order matters for interest state**: Multiple `interest_range` rules may overlap at boundary values. The implementer likely expects first-match semantics. The current `RuleBook.GetRulesByType()` returns a list — iteration order must match YAML definition order.
- **Error transparency**: When YAML is corrupt or a rule doesn't match, the fallback to hardcoded is silent. The game designer probably expects some logging/diagnostics to know whether YAML rules are actually being used vs. falling back everywhere.

## Domain Invariants

- **Zero-dependency invariant**: `Pinder.Core` must NEVER reference `Pinder.Rules` or `YamlDotNet`. The interface-in-Core / implementation-in-Rules pattern must hold.
- **Behavioral equivalence**: When `IRuleResolver` is null (no YAML), GameSession must behave identically to pre-sprint behavior. The 2651 existing tests validate this.
- **Fallback safety**: Any `IRuleResolver` method returning null must trigger the exact same hardcoded path that exists today. No behavioral gap between "resolver missing" and "resolver returns null for this query."

## Gaps

- **Gap (implementation detail)**: `ConditionEvaluator` doesn't support `threshold` key used by §7/§9 shadow threshold YAML rules. The implementer will need to extend `ConditionEvaluator` or use alternative logic in `RuleBookResolver` for shadow thresholds. Not blocking — the architecture handles this gracefully (nullable fallback), but §7 AC won't pass without this extension.
- **Missing (minor)**: No diagnostic/logging mechanism to tell the game designer which rules are being resolved from YAML vs. falling back to hardcoded. Acceptable at prototype maturity.
- **Assumption**: The YAML files are well-formed and condition/outcome vocabulary is consistent across all rules. The 45 `RulesSpecTests` validate this, but there's no runtime schema validation.

## Requirements Compliance Check

- **NFR (Performance)**: ✅ Architecture loads RuleBook once at construction. O(n) scan per rule type per call is acceptable for <100 rules.
- **NFR (Dependency)**: ✅ IRuleResolver in Core, implementation in Rules. Clean Dependency Inversion.
- **NFR (Backward compatibility)**: ✅ Nullable returns + fallback ensures all existing tests pass unchanged.
- **Out of scope boundaries**: ✅ No LLM/qualitative rules, no removal of hardcoded constants, no ConversationRegistry changes, no LlmAdapters changes.

## Recommendations

1. **The `ConditionEvaluator` threshold gap is a known implementation detail, not an architecture concern.** The backend engineer should extend `ConditionEvaluator` with `threshold` key support (comparing against a shadow value passed via `GameState`), or implement shadow threshold resolution as custom logic in `RuleBookResolver` that filters rules by `shadow` name and compares `threshold` values directly. Either approach works within the existing architecture.
2. Proceed with implementation as designed. The Dependency Inversion pattern is clean, appropriately scoped for prototype maturity, and easy to evolve at MVP (e.g., replacing nullable fallback with mandatory resolver when YAML becomes the sole source of truth).

**VERDICT: CLEAN**
