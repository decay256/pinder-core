# Contract: Issue #210 — Full 8-Turn Integration Test

## Component
`tests/Pinder.Core.Tests/Integration/FullConversationIntegrationTest.cs`

## Maturity: Prototype

---

## Purpose
End-to-end integration test that runs a realistic 8-turn `GameSession` conversation using `NullLlmAdapter`. Verifies the full rules stack fires correctly: interest deltas, momentum, combos, traps, recovery, shadow growth, XP, and game outcome.

## Test Setup

### Characters
Construct via `CharacterProfile` directly (no JSON loading needed):
- **Player ("Gerald")**: Level 5, Charm+13, SA+4, other stats mid-range
- **Opponent ("Velvet")**: Level 7, Chaos+14, Honesty+10, other stats mid-range

### Dice
Use `FixedDice` with predetermined values. **Must include values for both roll resolution AND `TimingProfile.ComputeDelay`** per turn (lesson from #209).

### LLM Adapter
`NullLlmAdapter` — no API calls. Returns 4 hardcoded options (Charm/Honesty/Wit/Chaos).

### Trap Registry
Use a mock/stub `ITrapRegistry` that returns a known trap definition for the stat that triggers TropeTrap.

## Turn Plan

| Turn | Action | Stat | Expected Roll Outcome | Interest Delta | Momentum | Combo | Notes |
|------|--------|------|----------------------|----------------|----------|-------|-------|
| 1 | Speak | CHARM | Success (beat DC by 1-4) | +1 | 1 | — | |
| 2 | Speak | WIT | Success (beat DC by 5-9, Hard tier) | +2 +1(risk) +1(combo) | 2 | The Setup | Charm→Wit fires |
| 3 | Speak | HONESTY | Fail (miss by 3 → MISFIRE) | -2 | 0 (reset) | — | Momentum resets |
| 4 | Speak | SA | Success | +1 +2(combo) | 1 | The Recovery | After fail→SA |
| 5 | Speak | CHAOS | Fail (miss by 7 → TROPE_TRAP) | -3 | 0 | — | Trap activates |
| 6 | Recover | SA | Success (DC 12) | 0* | — | — | Trap clears. *Recover doesn't change interest on success |
| 7 | Speak | CHARM | Success | +1 or +2 | 1 | — | Weakness window if available |
| 8 | Speak | CHARM | Big success | hits 20+ or 25 | 2 | — | DateSecured or high interest |

*Note: Exact dice values must be calculated to produce these outcomes given the character stat blocks and DCs.*

## Assertions Per Turn
1. `TurnResult.InterestDelta` matches expected (including risk tier bonuses)
2. `TurnResult.MomentumStreak` increments on success, resets to 0 on fail
3. `TurnResult.ComboTriggered` matches expected combo name
4. `TurnResult.ShadowGrowthEvents` populated appropriately
5. `TurnResult.XpEarned` > 0 for each turn
6. Trap activation on TROPE_TRAP (turn 5)
7. Trap cleared after Recover (turn 6)

## Final Assertions
- `GameOutcome` is either `DateSecured` (if interest reaches 25) or the correct end state
- `XpLedger` total > 0 and includes expected event types
- No exceptions thrown during the 8-turn sequence

## Implementation Notes

### FixedDice calculation
The implementer must:
1. Read `StatBlock.GetDefenceDC()` to understand DC calculation: `13 + opponent.GetEffective(defenceStat)`
2. Read `RollEngine.Resolve()` to understand: `d20 + attacker.GetEffective(stat) + levelBonus`
3. Calculate exact d20 values needed to produce intended outcomes
4. Read `TimingProfile.ComputeDelay()` to count how many additional dice rolls per turn
5. Queue all values in `FixedDice` in call order

### NullLlmAdapter behavior
- `GetDialogueOptionsAsync` returns 4 options: Charm(0), Honesty(1), Wit(2), Chaos(3)
- `DeliverMessageAsync` echoes text (or prefixes with failure tier)
- `GetOpponentResponseAsync` returns `OpponentResponse("...")` with null Tell/WeaknessWindow
- `GetInterestChangeBeatAsync` returns null

Since NullLlmAdapter returns null Tell/WeaknessWindow, turn 7's weakness window assertion may need adjustment (test verifies the mechanic exists but won't trigger with NullLlmAdapter unless manually set).

## Dependencies
- #209 (combo test fix — ensures FixedDice pattern is established)
- All existing Pinder.Core code (no adapter project dependency)

## Consumers
None (test-only)

## Test Location
`tests/Pinder.Core.Tests/Integration/FullConversationIntegrationTest.cs`

**This test goes in Pinder.Core.Tests** (not LlmAdapters.Tests) because it tests the core engine with NullLlmAdapter — no adapter project dependency needed.
