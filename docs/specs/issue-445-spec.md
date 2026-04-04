# Spec: Rules DSL — Integrate Generated Test Stubs into Pinder.Core Test Suite

**Issue**: #445
**Module**: docs/modules/rules-dsl-pipeline.md (create new)

---

## Overview

This feature integrates 54 auto-generated xUnit test stubs from the Rules DSL pipeline into the `Pinder.Core.Tests` project so they run in CI. The tests validate that hardcoded C# game constants (failure/success deltas, interest states, shadow thresholds, risk bonuses, XP/level progression) match the authoritative values defined in `rules/extracted/rules-v3-enriched.yaml`. Seventeen of the 54 stubs are for LLM/qualitative effects that cannot be unit-tested mechanically and must remain as skipped stubs.

---

## Function Signatures

This issue does not introduce new production code. It adds a single test class with the following public surface:

### Test Class

```csharp
namespace Pinder.Core.Tests.RulesSpec
{
    public class RulesSpecTests
    {
        // --- Helpers (private) ---
        private static RollResult MakeFailureResult(FailureTier tier, int missMargin);
        private static RollResult MakeSuccessResult(int beatMargin, bool isNat20);

        // --- §5 Failure Scale (5 tests) ---
        [Fact] public void Rule_S5_Fumble_MissBy1To2_NegativeOne();
        [Fact] public void Rule_S5_Misfire_MissBy3To5_NegativeOne();
        [Fact] public void Rule_S5_TropeTrap_MissBy6To9_NegativeTwo();
        [Fact] public void Rule_S5_Catastrophe_MissBy10Plus_NegativeThree();
        [Fact] public void Rule_S5_Legendary_Nat1_NegativeFour();

        // --- §5 Success Scale (4 tests) ---
        [Fact] public void Rule_S5_BeatDCBy1To4_PlusOne();
        [Fact] public void Rule_S5_BeatDCBy5To9_PlusTwo();
        [Fact] public void Rule_S5_BeatDCBy10Plus_PlusThree();
        [Fact] public void Rule_S5_Nat20_PlusFour();

        // --- §5 Risk Tiers (4 tests) ---
        [Fact] public void Rule_S5_RiskTier_Safe_NeedLte5();
        [Fact] public void Rule_S5_RiskTier_Medium_Need6To10();
        [Fact] public void Rule_S5_RiskTier_Hard_Need11To15();
        [Fact] public void Rule_S5_RiskTier_Bold_Need16Plus();

        // --- §5 Risk Bonuses (4 tests) ---
        [Fact] public void Rule_S5_RiskBonus_Safe_Zero();
        [Fact] public void Rule_S5_RiskBonus_Hard_PlusOne();
        [Fact] public void Rule_S5_RiskBonus_Bold_PlusTwo();
        [Fact] public void Rule_S5_RiskBonus_Failure_Zero();

        // --- §6 Interest States (7 tests) ---
        [Fact] public void Rule_S6_Interest0_Unmatched();
        [Fact] public void Rule_S6_Interest1To4_Bored();
        [Fact] public void Rule_S6_Interest5To9_Lukewarm();
        [Fact] public void Rule_S6_Interest10To15_Interested();
        [Fact] public void Rule_S6_Interest16To20_VeryIntoIt();
        [Fact] public void Rule_S6_Interest21To24_AlmostThere();
        [Fact] public void Rule_S6_Interest25_DateSecured();

        // --- §6 Interest Meter Properties (3 tests) ---
        [Fact] public void Rule_S6_StartingInterest_10();
        [Fact] public void Rule_S6_Interest_Max25();
        [Fact] public void Rule_S6_Interest_Min0();

        // --- §7 Shadow Thresholds (4 tests) ---
        [Fact] public void Rule_S7_Shadow_Below6_Tier0();
        [Fact] public void Rule_S7_Shadow_6To11_Tier1();
        [Fact] public void Rule_S7_Shadow_12To17_Tier2();
        [Fact] public void Rule_S7_Shadow_18Plus_Tier3();

        // --- §10 Progression (6 tests) ---
        [Fact] public void Rule_S10_XP0_Level1();
        [Fact] public void Rule_S10_XP50_Level2();
        [Fact] public void Rule_S10_XP150_Level3();
        [Fact] public void Rule_S10_XP3500_Level11();
        [Fact] public void Rule_S10_LevelBonus_L1_Zero();
        [Fact] public void Rule_S10_LevelBonus_L5_Two();

        // --- Skipped: LLM/Qualitative (17 stubs) ---
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Dread_T1();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Dread_T2();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Dread_T3();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Fixation_T1();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Fixation_T2();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Fixation_T3();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Denial_T1();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Denial_T2();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Denial_T3();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Madness_T1();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Madness_T2();
        [Fact(Skip = "...")] public void Rule_S7_ShadowTaint_Madness_T3();
        [Fact(Skip = "...")] public void Rule_S5_Failure_Fumble_Narrative();
        [Fact(Skip = "...")] public void Rule_S5_Failure_Catastrophe_Narrative();
        [Fact(Skip = "...")] public void Rule_S5_Failure_Legendary_Narrative();
        [Fact(Skip = "...")] public void Rule_S6_Ghost_Trigger_Narrative();
        [Fact(Skip = "...")] public void Rule_S11_LLM_Prompt_Taint_Injection();
    }
}
```

### Pinder.Core Public API Used by Tests

The tests call only existing public API — no new production code is needed:

| API | Namespace | Used For |
|-----|-----------|----------|
| `FailureScale.GetInterestDelta(RollResult)` | `Pinder.Core.Rolls` | §5 failure deltas |
| `SuccessScale.GetInterestDelta(RollResult)` | `Pinder.Core.Rolls` | §5 success deltas |
| `RollResult` constructor + `ComputeRiskTier()` | `Pinder.Core.Rolls` | §5 risk tier classification |
| `RiskTierBonus.GetInterestBonus(RollResult)` | `Pinder.Core.Rolls` | §5 risk bonus values |
| `InterestMeter()` + `Apply(int)` + `GetState()` | `Pinder.Core.Conversation` | §6 interest states |
| `InterestState` enum | `Pinder.Core.Conversation` | §6 state names |
| `ShadowThresholdEvaluator.GetThresholdLevel(int)` | `Pinder.Core.Stats` | §7 shadow tier boundaries |
| `LevelTable.GetLevel(int)` | `Pinder.Core.Progression` | §10 XP→level mapping |
| `LevelTable.GetBonus(int)` | `Pinder.Core.Progression` | §10 level bonuses |

---

## Input/Output Examples

### §5 Failure Scale

| Input (FailureTier, missMargin) | Expected Output (interest delta) |
|--------------------------------|----------------------------------|
| `(Fumble, 2)` | `-1` |
| `(Misfire, 4)` | `-1` |
| `(TropeTrap, 7)` | `-2` |
| `(Catastrophe, 11)` | `-3` |
| `(Legendary, 14)` — Nat 1 | `-4` |

### §5 Success Scale

| Input (beatMargin, isNat20) | Expected Output (interest delta) |
|-----------------------------|----------------------------------|
| `(3, false)` | `+1` |
| `(7, false)` | `+2` |
| `(12, false)` | `+3` |
| `(any, true)` — Nat 20 | `+4` |

### §5 Risk Tier

| Input (need-to-hit = DC − total before d20) | Expected RiskTier |
|----------------------------------------------|-------------------|
| `need ≤ 5` | `Safe` |
| `need 6–10` | `Medium` |
| `need 11–15` | `Hard` |
| `need ≥ 16` | `Bold` |

### §5 Risk Bonus

| Input (RiskTier, isSuccess) | Expected Interest Bonus |
|-----------------------------|------------------------|
| `Safe, success` | `0` |
| `Hard, success` | `+1` |
| `Bold, success` | `+2` |
| `any tier, failure` | `0` |

### §6 Interest States

| Interest Value | Expected InterestState |
|---------------|----------------------|
| `0` | `Unmatched` |
| `3` | `Bored` |
| `7` | `Lukewarm` |
| `12` | `Interested` |
| `18` | `VeryIntoIt` |
| `22` | `AlmostThere` |
| `25` | `DateSecured` |

### §6 Interest Meter Invariants

| Property | Expected Value |
|----------|---------------|
| Default starting value | `10` |
| Maximum (Apply(+100) clamped) | `25` |
| Minimum (Apply(-100) clamped) | `0` |

### §7 Shadow Thresholds

| Shadow Value | Expected Tier |
|-------------|---------------|
| `0` | `0` (no threshold) |
| `5` | `0` |
| `6` | `1` (T1) |
| `11` | `1` |
| `12` | `2` (T2) |
| `17` | `2` |
| `18` | `3` (T3) |
| `25` | `3` |

### §10 Progression

| XP | Expected Level | Level Bonus |
|----|---------------|-------------|
| `0` | `1` | `0` |
| `50` | `2` | `0` |
| `150` | `3` | `1` |
| `500` | `5` | `2` |
| `3500` | `11` | `5` |

---

## Acceptance Criteria

### AC1: 54 generated tests in `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs`

The file must exist at `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs`. It must contain exactly 54 test methods: 37 with `[Fact]` and 17 with `[Fact(Skip = "...")]`. The test class must be in namespace `Pinder.Core.Tests.RulesSpec` and be named `RulesSpecTests`.

### AC2: All 54 compile and pass (37 active + 17 skipped)

Running `dotnet test --filter "FullyQualifiedName~Pinder.Core.Tests.RulesSpec"` must produce 37 passing tests and 17 skipped tests. Zero failures. The 17 skipped tests cover LLM-taint and qualitative narrative effects (shadow taint per type/tier, failure narrative flavor, ghost trigger narrative, LLM prompt taint injection) that cannot be mechanically unit-tested.

### AC3: Source attribution comment at top of file

Lines 1–3 of the file must contain:

```
// Auto-generated from rules/extracted/rules-v3-enriched.yaml
// See: rules/tools/generate_tests.py
// Edit the YAML source, then re-run generation — do not edit this file manually.
```

This prevents accidental manual edits and documents the regeneration pipeline.

### AC4: All existing tests still pass

`dotnet test` at the solution level must show all previously-existing tests passing. The test stubs are purely additive — they import from existing Pinder.Core public API and make no changes to production code. (Note: the issue says 2238, but the actual count at the time of integration may be higher — the requirement is zero regressions.)

### AC5: Build clean

`dotnet build` must produce zero errors and zero warnings from the new test file. All `using` directives must reference real namespaces. All method calls must resolve to existing Pinder.Core public API.

---

## Edge Cases

1. **Boundary values in failure scale**: Miss margin of exactly 1 (low end of Fumble), exactly 10 (threshold of Catastrophe), exactly 6 (threshold of TropeTrap). Each boundary must return the correct delta.

2. **Boundary values in success scale**: Beat margin of exactly 1 (low end of +1), exactly 5 (threshold of +2), exactly 10 (threshold of +3). Nat 20 always returns +4 regardless of margin.

3. **Interest state boundaries**: Interest at exactly 0 (Unmatched), 1 (Bored low), 4 (Bored high), 5 (Lukewarm low), 9 (Lukewarm high), 10 (Interested low), 15 (Interested high), 16 (VeryIntoIt low), 20 (VeryIntoIt high), 21 (AlmostThere low), 24 (AlmostThere high), 25 (DateSecured).

4. **Interest meter clamping**: Applying a large positive delta (e.g., +100) must clamp to 25. Applying a large negative delta (e.g., -100) must clamp to 0.

5. **Shadow threshold boundaries**: Shadow value 5 (just below T1 at 6), value 6 (exactly T1), value 11 (just below T2 at 12), value 12 (exactly T2), value 17 (just below T3 at 18), value 18 (exactly T3).

6. **Nat 1 vs. low-roll distinction**: A Nat 1 is always `Legendary` failure tier regardless of total. A non-Nat-1 roll that misses by the same margin should not be classified as Legendary.

7. **Nat 20 vs. high-roll distinction**: A Nat 20 always grants +4 interest delta regardless of beat margin. A non-Nat-20 roll that beats DC by the same amount follows the normal scale.

8. **Risk bonus on failure**: Risk tier bonus is 0 when the roll fails, regardless of tier. Only successful Bold (+2) and Hard (+1) rolls earn risk bonuses.

---

## Error Conditions

1. **Compilation failure**: If the generated test stubs reference non-existent types or methods (e.g., wrong namespace, renamed enum member), `dotnet build` will fail. Fix: correct the method path/enum reference to match current Pinder.Core public API.

2. **Assertion failure**: If a hardcoded C# constant disagrees with the YAML-specified value (e.g., FailureScale returns -2 for Fumble but YAML says -1), the test will fail. Fix: reconcile the C# implementation with the authoritative YAML value.

3. **Skipped test bodies**: The 17 skipped stubs have empty bodies (`{ }`). They must NOT contain `throw new NotImplementedException()` — xUnit Skip correctly bypasses execution. If a skipped stub's body were changed to throw, it would still be skipped.

4. **RollResult construction edge case**: `MakeFailureResult` and `MakeSuccessResult` helpers must construct `RollResult` values that satisfy the internal invariants of `FailureScale.GetInterestDelta()` and `SuccessScale.GetInterestDelta()`. Incorrect construction (e.g., wrong `UsedDieRoll` for Nat 1) will produce wrong tier classification.

---

## Dependencies

| Dependency | Type | Notes |
|-----------|------|-------|
| `Pinder.Core` (Stats, Rolls, Conversation, Progression) | Project reference | Tests validate existing public API — no changes needed |
| `xunit` + `xunit.runner.visualstudio` | NuGet (test project) | Already referenced by `Pinder.Core.Tests.csproj` |
| `rules/extracted/rules-v3-enriched.yaml` | Data file | Source of truth for test values; produced by #443 + #444 |
| Issue #443 (round-trip fixes) | Predecessor | Must be merged — extract.py/generate.py produce correct YAML |
| Issue #444 (enrichment) | Predecessor | Must be merged — enriched YAML provides condition/outcome fields |
| `rules/tools/generate_tests.py` | Pipeline tool (optional) | Generates test stubs from enriched YAML; re-run to regenerate |

---

## File Layout

```
tests/Pinder.Core.Tests/
└── RulesSpec/
    └── RulesSpecTests.cs    ← 54 test methods (37 active + 17 skipped)
```

No other files are added or modified. The test project's `.csproj` does not need changes — the file is auto-discovered by the wildcard include pattern.
