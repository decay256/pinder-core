# Conversation Game Session — Shadow Reductions

## Overview
This document covers the shadow stat reduction events implemented within `GameSession` per rules §7. Shadow reductions decrease a shadow stat by −1 as a reward for specific player behavior, complementing the existing shadow growth triggers. The primary module doc for `GameSession` is [`game-session.md`](game-session.md).

## Key Components

| File / Class | Description |
|---|---|
| `src/Pinder.Core/Conversation/GameSession.cs` | Contains all shadow reduction logic within existing methods (`ResolveTurnAsync`, `EvaluatePerTurnShadowGrowth`, `EvaluateEndOfGameShadowGrowth`, `RecoverAsync`). |
| `src/Pinder.Core/Stats/SessionShadowTracker.cs` | Provides `ApplyOffset(ShadowStatType, int, string)` — the method used for all reductions (accepts negative deltas, unlike `ApplyGrowth`). |
| `tests/Pinder.Core.Tests/ShadowReductionTests.cs` | Core positive/negative tests for each of the 4 new reduction events. |
| `tests/Pinder.Core.Tests/ShadowReductionSpecTests.cs` | Spec-driven tests — boundary values, edge cases (negative deltas, null shadows, stacking), and coexistence with other shadow events. |

## API / Public Interface

No new public methods or types were introduced. All changes are internal to existing `GameSession` private/public methods. The reductions use existing `SessionShadowTracker` API:

```csharp
// Used for all reductions (delta is negative, e.g. -1)
public string ApplyOffset(ShadowStatType shadow, int delta, string reason);

// Used in tests to verify reductions
public int GetDelta(ShadowStatType shadow);
```

## Shadow Reduction Events (§7)

| # | Trigger | Shadow | Delta | Location | Reason String |
|---|---------|--------|-------|----------|---------------|
| 1 | `outcome == GameOutcome.DateSecured` | Dread | −1 | `EvaluateEndOfGameShadowGrowth()` | `"Date secured"` |
| 2 | Honesty success at interest ≥ 15 | Denial | −1 | `EvaluatePerTurnShadowGrowth()` (trigger 6 block) | `"Honesty success at high interest"` |
| 3 | Successful `RecoverAsync()` | Madness | −1 | `RecoverAsync()` success branch | `"Recovered from trope trap"` |
| 4 | Success despite Overthinking disadvantage | Overthinking | −1 | `ResolveTurnAsync()` after roll | `"Succeeded despite Overthinking disadvantage"` |
| 5 | 4+ different stats used | Fixation | −1 | `EvaluateEndOfGameShadowGrowth()` (trigger 13) | *(pre-existing, not changed)* |

### Implementation Details

- **All reductions use `ApplyOffset()`**, not `ApplyGrowth()`. `ApplyGrowth` throws `ArgumentOutOfRangeException` on negative amounts.
- **Null-safety**: All reduction code checks `_playerShadows != null` (or uses `?.` operator for `RecoverAsync`).
- **Negative deltas are valid**: A shadow delta can go below 0 (e.g., Dread at 0 → −1 after DateSecured).
- **No per-session cap**: Reductions stack across turns (e.g., Denial −1 fires on every qualifying Honesty success).
- **Overthinking reduction (AC-4)** has an extra guard: checks `StatBlock.ShadowPairs[chosenOption.Stat] == ShadowStatType.Overthinking` in addition to `_shadowDisadvantagedStats.Contains(chosenOption.Stat)`. This ensures only Overthinking-specific disadvantage triggers the reduction.
- **Co-existence**: Reductions fire independently of growth triggers. E.g., DateSecured can trigger both Dread −1 and Denial +1 (for no Honesty success) on the same outcome.

## Architecture Notes

- Shadow reductions follow the same event recording pattern as growth triggers — `ApplyOffset` logs the event string, which is later drained via `DrainGrowthEvents()` and surfaced in `TurnResult.ShadowGrowthEvents`.
- The Overthinking reduction is placed in `ResolveTurnAsync()` (not in `EvaluatePerTurnShadowGrowth()`) because it depends on `_shadowDisadvantagedStats`, which is computed during turn resolution.
- See [`game-session.md`](game-session.md) for the full `GameSession` module documentation.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-03 | #270 | Initial creation — documented 4 new shadow reduction events (Dread, Denial, Madness, Overthinking) added to `GameSession`. Two new test files (1202 lines). |
