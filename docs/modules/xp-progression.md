# XP & Progression

## Overview
The `Pinder.Core.Progression` namespace and related XP logic in `GameSession` handle experience point awards, levelling, and the XP ledger. XP is earned each turn based on roll outcomes and accumulated in an immutable event log (`XpLedger`). The `LevelTable` maps cumulative XP to levels, roll bonuses, build points, and item slots.

## Key Components

| File / Class | Description |
|---|---|
| `src/Pinder.Core/Progression/XpLedger.cs` | Append-only event log accumulating XP events with source labels and amounts. Supports `DrainTurnEvents()` for per-turn consumption. |
| `src/Pinder.Core/Progression/LevelTable.cs` | Static lookup table mapping XP → level, level → roll bonus, build points, item slots, and failure pool tier. Matches rules §10. |
| `src/Pinder.Core/Conversation/GameSession.cs` | Contains `RecordXp()` (per-turn XP awards) and `ApplyRiskTierMultiplier()` (risk-tier XP scaling). Owns the `XpLedger` instance. |
| `src/Pinder.Core/Conversation/TurnResult.cs` | Exposes `XpEarned` — the total XP awarded for a single turn, and `XpBreakdown` — the itemized list of XP events for the turn. |
| `tests/Pinder.Core.Tests/XpRiskTierMultiplierSpecTests.cs` | Tests for risk-tier XP multiplier (issue #314): all four tiers, Nat20/Nat1/failure flat values, rounding, ledger consistency. |

## API / Public Interface

### XpLedger

```csharp
public sealed class XpLedger
{
    public int TotalXp { get; }
    public IReadOnlyList<XpEvent> Events { get; }

    public void Record(string source, int amount);
    public IReadOnlyList<XpEvent> DrainTurnEvents();

    public sealed class XpEvent
    {
        public string Source { get; }
        public int Amount { get; }
    }
}
```

### LevelTable

**Single Source of Truth Configuration:** All XP thresholds, level roll bonuses, build point awards, item slots, and failure pool tiers are defined inside `game-definition.yaml` (the Single Source of Truth). Hardcoded static C# default arrays and `??` cascades have been completely removed. If any required progression or XP configuration keys are missing in the configuration file, the game engine will fail fast and loud on startup/loading by throwing an exception.

```csharp
public static class LevelTable
{
    public const int CreationBudget = 12;
    public const int CreationStatCap = 4;
    public const int BaseStatCap = 6;

    public static int GetLevel(int xp);
    public static int GetBonus(int level);
    public static int GetBuildPointsForLevel(int level);
    public static int GetItemSlots(int level);
    public static FailurePoolTier GetFailurePoolTier(int level);
}
```

### XP Award Rules (per-turn, in `GameSession.RecordXp`)

| Outcome | XP Awarded |
|---|---|
| Nat 20 | 25 (flat, no multiplier) |
| Nat 1 | 10 (flat, no multiplier) |
| Success (DC ≤ 13) | 5 × risk-tier multiplier |
| Success (DC 14–17) | 10 × risk-tier multiplier |
| Success (DC ≥ 18) | 15 × risk-tier multiplier |
| Failure (non-Nat1) | 2 (flat, no multiplier) |

### Risk-Tier XP Multiplier (`GameSession.ApplyRiskTierMultiplier`)

```csharp
private static int ApplyRiskTierMultiplier(int baseXp, RiskTier riskTier);
```

| RiskTier | Multiplier |
|---|---|
| Safe | 1.0× |
| Medium | 1.5× |
| Hard | 2.0× |
| Bold | 3.0× |

Rounding: `(int)Math.Round(baseXp * multiplier)` — midpoint rounds to nearest even (banker's rounding), so 7.5 → 8.

### End-of-Game XP (in `GameSession`)

| Outcome | XP |
|---|---|
| DateSecured | 3.0x |
| Unmatched / Ghosted | 1.0x |

### Level Thresholds

| Level | XP Required | Roll Bonus | Build Points | Item Slots |
|---|---|---|---|---|
| 1 | 0 | +0 | 0 (12 at creation) | 2 |
| 2 | 50 | +0 | 2 | 2 |
| 3 | 150 | +1 | 2 | 3 |
| 4 | 300 | +1 | 2 | 3 |
| 5 | 500 | +2 | 3 | 4 |
| 6 | 750 | +2 | 3 | 4 |
| 7 | 1100 | +3 | 3 | 5 |
| 8 | 1500 | +3 | 4 | 5 |
| 9 | 2000 | +4 | 4 | 6 |
| 10 | 2750 | +4 | 5 | 6 |
| 11 | 3500 | +5 | 0 (prestige reset) | 6 |

## Architecture Notes

- **XpLedger is append-only**: events cannot be removed. `TotalXp` is a running sum. `DrainTurnEvents()` returns new events since last drain without modifying the ledger.
- **Risk-tier multiplier is applied only to normal successes**: Nat20, Nat1, and non-Nat1 failures use flat XP values with no multiplier.
- **The multiplier is computed in `GameSession`** (private static method `ApplyRiskTierMultiplier`), not in the `Progression` namespace. The multiplied value is what gets recorded in the ledger.
- **DC buckets determine base XP**: low (≤13) = 5, mid (14–17) = 10, high (≥18) = 15. Risk tier determines the multiplier independently of DC.
- **`TurnResult.XpEarned`** equals the sum of all ledger events recorded during that turn, ensuring consistency between the return value and the ledger.
- **`TurnResult.XpBreakdown`** exposes the itemized `IReadOnlyList<XpLedger.XpEvent>` of XP events earned during the turn.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #314 | Initial creation — documented XP risk-tier multiplier (Safe=1×, Medium=1.5×, Hard=2×, Bold=3×) applied to successful rolls. Multiplier uses `Math.Round`. Flat XP for Nat20 (25), Nat1 (10), failure (2). Added `XpRiskTierMultiplierSpecTests.cs` (344 lines, 14 tests). |
| 2026-07-01 | #1281 | Added `XpBreakdown` property to `TurnResult` to expose itemized XP events. |
| 2026-07-03 | #1283 | Migrated all XP and progression thresholds to game-definition.yaml. Removed hardcoded C# arrays and cascades. Added strict failure-fast loading validations. |
