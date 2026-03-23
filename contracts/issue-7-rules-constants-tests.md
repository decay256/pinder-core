# Contract: Issue #7 — Rules Constants Unit Tests

## Component
`tests/Pinder.Core.Tests/RulesConstantsTests.cs` (new file)

## Maturity
Prototype

## What It Produces
A test file that asserts every rules-v3.4 constant matches the C# implementation. If any rule value drifts, the test fails with a clear name identifying which rule is wrong.

## Test Classes / Sections

### Defence Pairings (§3)
```csharp
[Fact] DefenceTable_Charm_DefendedBy_SelfAwareness()
[Fact] DefenceTable_Rizz_DefendedBy_Wit()
[Fact] DefenceTable_Honesty_DefendedBy_Chaos()
[Fact] DefenceTable_Chaos_DefendedBy_Charm()
[Fact] DefenceTable_Wit_DefendedBy_Rizz()
[Fact] DefenceTable_SelfAwareness_DefendedBy_Honesty()
[Fact] DefenceTable_IsBijection()  // each stat appears exactly once as key and once as value
```

### Base DC (§3)
```csharp
[Fact] BaseDC_WithZeroModifier_Is13()
```

### Fail Tiers (§5)
```csharp
[Theory] with inline data for all boundary values:
  miss 1 → Fumble
  miss 2 → Fumble
  miss 3 → Misfire
  miss 5 → Misfire
  miss 6 → TropeTrap
  miss 9 → TropeTrap
  miss 10 → Catastrophe
  Nat 1 → Legendary (tested via RollEngine)
```

### Success Scale (§5) — DESCOPED
Per vision concern #18, success scale is **not implemented** in the codebase. These tests are **omitted** from this issue. A future issue will add `SuccessMargin` / interest delta to `RollResult` and the corresponding tests.

### Interest Meter (§6)
```csharp
[Fact] InterestMeter_Max_Is25()
[Fact] InterestMeter_Min_Is0()
[Fact] InterestMeter_StartingValue_Is10()
```

### Level Bonuses (§10)
```csharp
[Theory] with inline data:
  Level 1 → +0
  Level 2 → +0
  Level 3 → +1
  Level 4 → +1
  Level 5 → +2
  Level 6 → +2
  Level 7 → +3
  Level 8 → +3
  Level 9 → +4
  Level 10 → +4
  Level 11 → +5
```

### Shadow Pairs (§8)
```csharp
[Fact] ShadowPairs_AllSixMapped()
[Fact] ShadowPenalty_Is_FloorDivBy3()  // e.g. shadow=9 → penalty=3
```

## Behavioural Contract
- Tests are **read-only** — they assert existing constants, they do not modify any code
- Test names encode the rules section so failures are immediately traceable
- No dependency on external files (no JSON loading, no file I/O)
- Uses only `StatBlock`, `RollEngine`, `InterestMeter`, `LevelTable` from Pinder.Core

## Files Changed
- `tests/Pinder.Core.Tests/RulesConstantsTests.cs` (new)

## Dependencies
- Issue #6 must be merged first (for InterestState boundary tests, if included). However, the non-InterestState tests can be written independently.
- **#4 dependency removed** per vision concern #18.
- **§5 success scale removed** per vision concern #18.

## Consumers
- CI pipeline — these tests run on every PR to catch drift
