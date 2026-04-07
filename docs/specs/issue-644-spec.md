**Module**: docs/modules/session-runner.md

## Overview
The playtest runner currently prints dialogue options with expected value, risk, and bonuses (like tell, callback, combo). However, it does not display deterministic shadow growth risks or reduction opportunities, making them invisible to the player during manual playtests. This feature adds distinct warning badges (`⚠️` and `✨`) to the option display to show when an option will grow Fixation (same stat 3x) or Denial (skipping Honesty), or when it can reduce Denial (Honesty at high Interest). It also ensures the `PlayerAgentContext` receives the correct turn history so the scoring agent can evaluate these risks accurately.

## Function Signatures
No new methods or classes are required. The changes occur entirely within the `Main` turn loop of `session-runner/Program.cs`.

The existing `PlayerAgentContext` constructor will now receive actual values instead of relying on default `null` parameters:
```csharp
public PlayerAgentContext(
    StatBlock playerStats,
    StatBlock opponentStats,
    int currentInterest,
    InterestState interestState,
    int momentumStreak,
    string[] activeTrapNames,
    int sessionHorniness,
    Dictionary<ShadowStatType, int>? shadowValues,
    int turnNumber,
    StatType? lastStatUsed = null,
    StatType? secondLastStatUsed = null,
    bool honestyAvailableLastTurn = false)
```

## Input/Output Examples

**Input (Game State):**
- Turn: 3
- `lastStatUsed`: `StatType.Chaos`
- `secondLastStatUsed`: `StatType.Chaos`
- `turnStart.Options`: `[Charm, Chaos, Honesty, Rizz]`
- Current Interest: 16

**Output (Console Display):**
```text
**A)** CHARM +10 | 60% 🟡 Medium | ⚠️ Denial +1
**B)** CHAOS +6 | 45% 🟠 Hard | ⚠️ Denial +1, ⚠️ Fixation +1 (same stat 3x)
**C)** HONESTY +3 | 5% 🔴 Bold | ✨ Denial -1 (if success at Interest≥15)
**D)** RIZZ +9 | 55% 🟡 Medium | ⚠️ Denial +1
```

## Acceptance Criteria

1. **Denial Growth Risk**: If `StatType.Honesty` is present in the current turn's `Options` array, any printed option where `Stat != StatType.Honesty` must include the badge `⚠️ Denial +1`.
2. **Fixation Growth Risk**: If the current option's `Stat` equals both `lastStatUsed` and `secondLastStatUsed`, it must include the badge `⚠️ Fixation +1 (same stat 3x)`.
3. **Denial Reduction Opportunity**: If the current option is `StatType.Honesty` and the session's current interest is `≥ 15`, it must include the badge `✨ Denial -1 (if success at Interest≥15)`.
4. **Context Wiring**: The `session-runner/Program.cs` file must initialize and track `lastStatUsed`, `secondLastStatUsed`, and `honestyAvailableLastTurn` variables across turns, and pass them into the `PlayerAgentContext` constructor so that `ScoringPlayerAgent` can evaluate Fixation rules.
5. **Clean Build**: The solution must compile without errors or warnings. All existing tests must pass.

## Edge Cases
- **Multiple Warnings**: An option can trigger multiple warnings (e.g., picking Chaos for the 3rd time when Honesty is available). Both warnings should be appended to the `badges` list.
- **First/Second Turn**: `lastStatUsed` and `secondLastStatUsed` are null during the first two turns. The Fixation warning must handle nulls and gracefully skip evaluation.
- **No Honesty Available**: If the LLM does not generate an Honesty option for the current turn, picking other stats does not trigger the Denial growth risk. The code must explicitly check `turnStart.Options.Any(o => o.Stat == StatType.Honesty)` before applying the `⚠️ Denial +1` badge.

## Error Conditions
- If the `badges` list is null, it should be instantiated (it already is in `Program.cs`).
- No new failure modes are introduced as this relies on pure data already available in memory. 

## Dependencies
- Modifies `session-runner/Program.cs` local state tracking and console output block.
- Relies on `Pinder.Core.Conversation.DialogueOption` and `Pinder.Core.Stats.StatType`.
