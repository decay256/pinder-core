**Module**: docs/modules/shadow-stats.md

## Overview
This specification addresses test coverage gaps related to shadow pairings and the Defence table. It ensures that a Natural 1 on a Rizz roll correctly increases the Horniness shadow stat, and adds collection count assertions to `StatBlock.ShadowPairs` and `StatBlock.DefenceTable` to prevent silent regressions from missing or duplicate dictionary entries.

## Function Signatures
No new production methods or signatures are introduced. The existing static readonly dictionaries on `StatBlock` are accessed for validation:
```csharp
// In Pinder.Core.Stats.StatBlock
public static readonly IReadOnlyDictionary<StatType, ShadowStatType> ShadowPairs;
public static readonly IReadOnlyDictionary<StatType, StatType> DefenceTable;
```

New test methods added to test classes:
```csharp
// In tests/Pinder.Core.Tests/Stats/ShadowGrowthEventTests.cs
[Fact]
public void Nat1OnRizz_GrowsHorniness();

// In tests/Pinder.Core.Tests/Stats/StatBlockTests.cs
[Fact]
public void ShadowPairs_HasSixEntries();

[Fact]
public void DefenceTable_HasSixEntries();
```

## Input/Output Examples

**ShadowPairs Count Assertion**
- **Input**: Inspect `StatBlock.ShadowPairs.Count`
- **Expected Output**: `6`

**DefenceTable Count Assertion**
- **Input**: Inspect `StatBlock.DefenceTable.Count`
- **Expected Output**: `6`

**Rizz Nat1 Shadow Growth**
- **Input**: A roll using `StatType.Rizz` resulting in a Natural 1.
- **Expected Output**: The `SessionShadowTracker` registers a +1 growth event for `ShadowStatType.Horniness`.

## Acceptance Criteria

### 1. Rizz Nat 1 Growth Tested
- **Given** `ShadowGrowthEventTests.cs`,
- **When** the `Nat1OnRizz_GrowsHorniness` test is executed,
- **Then** it asserts that a Natural 1 on a Rizz roll triggers Horniness growth.

### 2. ShadowPairs Completeness Asserted
- **Given** `StatBlockTests.cs`,
- **When** the `ShadowPairs_HasSixEntries` test is executed,
- **Then** it asserts that `StatBlock.ShadowPairs.Count` is exactly `6`.

### 3. DefenceTable Completeness Asserted
- **Given** `StatBlockTests.cs`,
- **When** the `DefenceTable_HasSixEntries` test is executed,
- **Then** it asserts that `StatBlock.DefenceTable.Count` is exactly `6`.

### 4. Build and Pass
- **Given** the test additions,
- **When** the test suite is run,
- **Then** all tests compile and pass, ensuring a clean build.

## Edge Cases
- **Dictionary Initialization**: A dictionary initialization with duplicate keys would compile but fail at runtime. Ensuring the count matches the number of expected unique mappings guarantees all six core stats are covered.

## Error Conditions
- If the Rizz shadow pairing behavior is ever modified to map to a different shadow stat (or none), the test `Nat1OnRizz_GrowsHorniness` will fail and serve as a regression flag.
- If a new `StatType` is introduced in the future and the mappings are updated, the count tests will fail until the expected counts are updated to match the new stat total.

## Dependencies
- **Pinder.Core.Stats**: Existing `StatBlock` and `SessionShadowTracker` functionality.
- **xUnit**: Used for executing the new assertion methods in `Pinder.Core.Tests`.
