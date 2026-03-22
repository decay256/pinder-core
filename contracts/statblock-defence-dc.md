# Contract: StatBlock Defence Table & Base DC (Issues #1, #2 tests)

## Component
`Pinder.Core.Stats.StatBlock`

## File
`src/Pinder.Core/Stats/StatBlock.cs`

## Changes Required (v3.4 sync)

### 1. DefenceTable — update two pairings

```csharp
// BEFORE (current):
{ StatType.Honesty, StatType.SelfAwareness }
{ StatType.Wit,     StatType.Wit           }

// AFTER (v3.4):
{ StatType.Honesty, StatType.Chaos  }
{ StatType.Wit,     StatType.Rizz   }
```

All other pairings remain unchanged:
- `Charm → SelfAwareness` ✓
- `Rizz → Wit` ✓
- `Chaos → Charm` ✓
- `SelfAwareness → Honesty` ✓

### 2. GetDefenceDC — base DC 10 → 13

```csharp
// BEFORE:
return 10 + GetEffective(defenceStat);

// AFTER:
public const int BaseDC = 13;
return BaseDC + GetEffective(defenceStat);
```

Extract `BaseDC` as a named constant for testability (issue #7 will assert against it).

## Public Interface (post-change)

```csharp
public sealed class StatBlock
{
    public const int BaseDC = 13;  // NEW — extracted constant

    public static readonly Dictionary<StatType, StatType> DefenceTable;
    public static readonly Dictionary<StatType, ShadowStatType> ShadowPairs;  // unchanged

    public int GetBase(StatType stat);           // unchanged
    public int GetShadow(ShadowStatType shadow); // unchanged
    public int GetEffective(StatType stat);      // unchanged
    public int GetDefenceDC(StatType attackingStat);  // formula changes: 13 + effective
}
```

## Invariants
- `DefenceTable` must contain exactly 6 entries (one per `StatType`)
- `GetDefenceDC` must use `BaseDC` constant, not a magic number
- No stat defends against itself after v3.4 (Wit→Wit is gone)

## Test Impact
- All tests asserting `DC == 10` must change to `DC == 13` (issue #2)
- Tests asserting old defence pairings must update (issue #2)

## Dependencies
- None (StatBlock has no dependencies on other components)

## Consumers
- `RollEngine.Resolve()` — calls `defender.GetDefenceDC(stat)`
- `RollEngineTests` — hardcoded DC assertions
- Future: `RulesConstantsTests` (issue #7)
