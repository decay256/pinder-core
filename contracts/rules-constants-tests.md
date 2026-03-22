# Contract: Rules Constants Test File (Issue #7)

## Purpose
A dedicated test file that asserts every numeric constant from rules v3.4. If a rule value changes and code is updated but this file is not (or vice versa), tests fail — making drift impossible to miss.

## File
`tests/Pinder.Core.Tests/RulesConstantsTests.cs`

## Test Structure

One test (or test class section) per rules section:

### §3 Defence Pairings
```csharp
[Fact] void DefenceTable_CharmDefendedBySelfAwareness()
[Fact] void DefenceTable_RizzDefendedByWit()
[Fact] void DefenceTable_HonestyDefendedByChaos()       // v3.4 change
[Fact] void DefenceTable_ChaosDefendedByCharm()
[Fact] void DefenceTable_WitDefendedByRizz()             // v3.4 change
[Fact] void DefenceTable_SelfAwarenessDefendedByHonesty()
```

### §3 Base DC
```csharp
[Fact] void BaseDC_Is13()
// Assert StatBlock.BaseDC == 13
```

### §4 Interest Meter
```csharp
[Fact] void InterestMax_Is25()
[Fact] void InterestMin_Is0()
[Fact] void InterestStart_Is10()
```

### §5 Success Scale
```csharp
[Fact] void SuccessMargin1to4_InterestDelta1()
[Fact] void SuccessMargin5to9_InterestDelta2()
[Fact] void SuccessMargin10Plus_InterestDelta3()
[Fact] void Nat20_InterestDelta4()
```

### §6 Interest State Boundaries
```csharp
[Fact] void Interest0_IsUnmatched()
[Fact] void Interest1to4_IsBored()
[Fact] void Interest5to15_IsInterested()
[Fact] void Interest16to20_IsVeryIntoIt()
[Fact] void Interest21to24_IsAlmostThere()
[Fact] void Interest25_IsDateSecured()
[Fact] void Bored_HasDisadvantage()
[Fact] void VeryIntoIt_HasAdvantage()
[Fact] void AlmostThere_HasAdvantage()
```

### §7 Failure Tiers (already tested but repeated here for completeness)
```csharp
[Fact] void MissBy1to2_IsFumble()
[Fact] void MissBy3to5_IsMisfire()
[Fact] void MissBy6to9_IsTropeTrap()
[Fact] void MissBy10Plus_IsCatastrophe()
[Fact] void Nat1_IsLegendary()
```

## Dependencies
- `StatBlock.BaseDC` constant (from issue #1)
- `InterestMeter.Max/Min/StartingValue` (from issue #2)
- `InterestState` enum and `GetState()` (from issue #6)
- `RollResult.SuccessMargin` and `InterestDelta` (from issue #8 / success scale)

## Note
Issue #7 depends on #1, #2, #6, and the success scale code existing. Per issue #10 (dependency concern), #7 should be implemented **last** in the sprint.
