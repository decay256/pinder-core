**Module**: docs/modules/testing.md (create new)

## Overview
This specification details a new integration test that plays a full multi-turn session (5-8 turns) from a starting interest of 10 to a `DateSecured` victory condition (interest 25+). Using a deterministic `FixedDice` sequence and `NullLlmAdapter`, the test executes continuous successful rolls to verify that interest deltas, momentum streaks, combos, and XP are accurately accumulated through the `GameSession` lifecycle.

## Function Signatures
```csharp
namespace Pinder.Core.Tests.Integration;

public class GameSessionIntegrationTests
{
    [Fact]
    public async Task FullSession_ContinuousSuccess_ToDateSecured()
    {
        // Integration test logic
    }
}
```

## Input/Output Examples

**Setup**:
- **Starting Interest**: 10
- **Max Interest**: 25
- **Characters**: Both Player and Opponent configured with +2 in all stats (making DC = 13 + 2 = 15, and player roll modifier = +2).
- **Dice**: `FixedDice` pre-loaded with `(5, 15, 50, 15, 50, 15, 50, 15, 50, 15, 50, 15, 50)`. The first value `5` handles the `GameSession` constructor horniness roll. The subsequent `15, 50` pairs represent the `RollEngine.Resolve` d20 and `TimingProfile.ComputeDelay` d100 rolls for each turn.

**Simulation Sequence**:
- **Turn 1 (Option 2 - Wit)**: Roll 15 + 2 Mod = 17 vs DC 15. Risk Need 13: Hard (+1). Margin: 2 (+1). Total Delta: +2. Interest: 12. Momentum Streak: 1.
- **Turn 2 (Option 0 - Charm)**: Roll 15 + 2 Mod = 17 vs DC 15. Risk: Hard (+1). Margin: 2 (+1). Combo: "The Setup" (+1). Total Delta: +3. Interest: 15. Momentum Streak: 2.
- **Turn 3 (Option 1 - Honesty)**: Roll 15 + 2 Mod = 17 vs DC 15. Risk: Hard (+1). Margin: 2 (+1). Combo: "The Reveal" (+1). Total Delta: +3. Interest: 18. Momentum Streak: 3.
- **Turn 4 (Option 3 - Chaos)**: Roll 15 + 2 Mod + 2 Momentum = 19 vs DC 15. Risk: Hard (+1). Margin: 4 (+1). Combo: "The Pivot" (+1). Total Delta: +3. Interest: 21. Momentum Streak: 4.
- **Turn 5 (Option 0 - Charm)**: Roll 15 + 2 Mod + 2 Momentum = 19 vs DC 15. Risk: Hard (+1). Margin: 4 (+1). Total Delta: +2. Interest: 23. Momentum Streak: 5.
- **Turn 6 (Option 1 - Honesty)**: Roll 15 + 2 Mod + 3 Momentum = 20 vs DC 15. Risk: Hard (+1). Margin: 5 (+2). Combo: "The Reveal" (+1). Total Delta: +4. Interest: 27 -> caps at 25.
- **End State**: Upon successfully reaching 25 interest, the final operation ends with a `GameEndedException` carrying `GameOutcome.DateSecured`.

## Acceptance Criteria
- **Integration test plays 5-8 turns to DateSecured**: The test must play a continuous sequence of exactly 5 to 8 successful turns resulting in the DateSecured outcome.
- **Asserts interest delta per turn**: Verify `TurnResult.InterestDelta` matches the expected deterministic calculation step-by-step.
- **Asserts at least one combo fires**: Asserts `TurnResult.ComboTriggered` is true and `TurnResult.ComboName` matches expected combos (e.g., "The Setup") at least once.
- **Asserts final outcome DateSecured**: Upon hitting or exceeding 25 interest, asserting `session.Outcome` (or catching the `GameEndedException`) confirms `DateSecured`.
- **Asserts XP includes 50 for date**: Extract `session.DrainXpLedger().Events` or verify `session.TotalXpEarned` correctly includes the 50 XP award specifically flagged as `"DateSecured"`.
- **Test is deterministic (FixedDice)**: Exclusively use a scripted `FixedDice` queue and `NullLlmAdapter` to ensure zero unpredictability or LLM API calls.
- **Clean build**: The codebase compiles successfully, and all (new and existing) tests pass.

## Edge Cases
- **Over-capping Interest**: The final turn's calculation naturally pushes the total past 25. The session must handle this gracefully, cleanly clamping at exactly 25.
- **Dice Queue Exhaustion**: If the `FixedDice` queue exhausts prematurely, it signifies an unexpected additional roll in the game loop and should correctly fail the test. The test provides precisely the number of rolls needed (1 for constructor, 2 per turn).
- **Simultaneous Combos**: Ensuring that exactly the expected combo takes precedence based on highest interest bonus (handled natively by `ComboTracker.PickBest`).

## Error Conditions
- If the test throws `InvalidOperationException` due to `FixedDice` running out of queued values.
- If the game outcome evaluates to anything other than `DateSecured` (such as `Ghosted` or `Unmatched`).
- If the XP ledger events are missing the final `"DateSecured"` XP award or the total XP mismatch the step-by-step accumulation.

## Dependencies
- `Pinder.Core.Conversation.GameSession`
- `Pinder.Core.Conversation.ComboTracker`
- `Pinder.Core.Conversation.InterestMeter`
- `Pinder.Core.Progression.XpLedger`
- `NullLlmAdapter` (test double)
- `FixedDice` (test double)
