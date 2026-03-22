# Contract: Test Value Updates (Issue #2)

## Purpose
After issue #1 changes `StatBlock.DefenceTable` and base DC, all existing tests with hardcoded DC values and defence pairing assumptions must be updated.

## Files to Modify
- `tests/Pinder.Core.Tests/RollEngineTests.cs`
- `tests/Pinder.Core.Tests/CharacterSystemTests.cs`

## Specific Changes in RollEngineTests.cs

### DC assertions: 10 → 13
Every test that asserts `result.DC == 10` (with zero-stat defenders) must change to `result.DC == 13`.

Affected tests:
- `MissByOne_IsFumble`: `Assert.Equal(10, result.DC)` → `Assert.Equal(13, result.DC)`
  - Roll 8 + 0 + 0 = 8. New DC = 13. Miss by 5 → **Misfire** (not Fumble). Test logic must be reworked.
- `MissByFive_IsMisfire`: Roll 5, DC was 10, miss 5. Now DC 13, miss 8 → **TropeTrap**. Must rework.
- `MissBySeven_IsTropeTrap`: Roll 3, DC was 10, miss 7. Now DC 13, miss 10 → **Catastrophe**. Must rework.
- `LevelBonus_AppliesToRoll`: Roll 9 + 0 + 2 = 11 vs DC 10 → success. Now DC 13 → fail. Must rework.

### Defender stat adjustments for Charm tests
`Charm → SelfAwareness` defence pairing is **unchanged**. The `MakeStats` helper only sets `charm` and `selfAwareness`, so defence pairing changes (Honesty, Wit) don't affect existing tests.

### Strategy
The implementer should recalculate every test's expected values against DC=13 and adjust die roll inputs or stat values to preserve the intended failure tier being tested.

## Specific Changes in CharacterSystemTests.cs

### InterestMeter tests
- `InterestMeter_ClampsAtMax`: asserts `meter.Current == InterestMeter.Max` — no hardcoded 20, so this auto-updates when Max changes. **No change needed.**
- No explicit `== 20` assertions found.

### TimingProfile tests
- `TimingProfile_HighInterest_FasterThanLow`: passes `interestLevel: 20` — should pass `InterestMeter.Max` (25) to test full range. Minor update.

## Invariant
After all updates, `dotnet test` must pass with zero failures.

## Dependencies
- Issue #1 must be implemented first (or simultaneously in the same PR per issue #4 recommendation)
