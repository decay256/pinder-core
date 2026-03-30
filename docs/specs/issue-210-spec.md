# Spec: Issue #210 — Integration Test: Full 8-Turn GameSession Conversation

## Overview

This feature adds a deterministic end-to-end integration test that exercises a full 8-turn `GameSession` conversation. It verifies the complete rules stack: interest deltas with success/failure scaling, momentum streaks, combo detection, trap activation and recovery, shadow growth events, XP accumulation, and game outcome resolution. All randomness is controlled via `FixedDice`; no real LLM API calls are made.

## Test File Location

`tests/Pinder.Core.Tests/Integration/FullConversationIntegrationTest.cs`

This test belongs in `Pinder.Core.Tests` (not LlmAdapters) since it exercises only core engine code.

---

## 1. Function Signatures

### Test Class

```csharp
namespace Pinder.Core.Tests.Integration
{
    public class FullConversationIntegrationTest
    {
        [Fact]
        public async Task FullEightTurnConversation_VerifiesAllMechanics();
    }
}
```

No new public library APIs are created. The test exercises these existing public APIs:

- `new GameSession(CharacterProfile player, CharacterProfile opponent, ILlmAdapter llm, IDiceRoller dice, ITrapRegistry trapRegistry, GameSessionConfig? config)` — constructor
- `Task<TurnStart> GameSession.StartTurnAsync()` — begin a Speak turn; returns dialogue options
- `Task<TurnResult> GameSession.ResolveTurnAsync(int optionIndex)` — resolve the chosen option
- `Task<RecoverResult> GameSession.RecoverAsync()` — self-contained Recover action (SA vs DC 12)
- `GameStateSnapshot` — accessed via `TurnResult.StateAfter` or `RecoverResult.StateAfter`

### Test Helpers (private to test file)

```csharp
// Already exists in GameSessionTests.cs — reuse or duplicate:
private sealed class FixedDice : IDiceRoller
{
    // Queue<int> _values; constructor takes params int[]; Roll(int sides) dequeues next value
}

private sealed class NullTrapRegistry : ITrapRegistry
{
    // Returns null for all stats (no traps available)
}

// New: returns a known TrapDefinition for a specific stat
private sealed class TestTrapRegistry : ITrapRegistry
{
    // GetTrap(StatType.Chaos) → TrapDefinition("unhinged", StatType.Chaos, TrapEffect.Disadvantage, 0, 3, ...)
    // GetTrap(other) → null
}

// New: controllable LLM adapter that returns stat-specific options per turn
private sealed class ScriptedLlmAdapter : ILlmAdapter
{
    // Accepts a queue/list of DialogueOption[] arrays, one per StartTurnAsync call
    // Allows SA and Rizz options that NullLlmAdapter doesn't provide
    // DeliverMessageAsync, GetOpponentResponseAsync, GetInterestChangeBeatAsync behave like NullLlmAdapter
}
```

---

## 2. Test Setup

### Character Profiles

**Player — "Gerald" (Level 5):**

| Stat | Base Value | Shadow | Effective |
|------|-----------|--------|-----------|
| Charm | 13 | 0 | 13 |
| Wit | 3 | 0 | 3 |
| Honesty | 3 | 0 | 3 |
| Chaos | 2 | 0 | 2 |
| SelfAwareness | 4 | 0 | 4 |
| Rizz | 2 | 0 | 2 |

Level 5 → `LevelTable.GetBonus(5)` = **+2** level bonus on all rolls.

**Opponent — "Velvet" (Level 7):**

| Stat | Base Value | Shadow | Effective |
|------|-----------|--------|-----------|
| Chaos | 14 | 0 | 14 |
| Honesty | 10 | 0 | 10 |
| Charm | 5 | 0 | 5 |
| SelfAwareness | 5 | 0 | 5 |
| Wit | 4 | 0 | 4 |
| Rizz | 4 | 0 | 4 |

Both use `TimingProfile(baseDelay: 10, variance: 0.5f, drySpell: 0.0f, readReceipt: "neutral")`.

Both use placeholder `AssembledSystemPrompt` strings (e.g., `"Test system prompt"`).

### DC Calculations

The DC formula is `13 + opponent.GetEffective(defenceStat)` where the defence stat is looked up from `StatBlock.DefenceTable`:

| Gerald's Attack Stat | Velvet's Defence Stat | Velvet Defence Value | DC |
|---------------------|-----------------------|---------------------|-----|
| Charm | SelfAwareness | 5 | **18** |
| Wit | Rizz | 4 | **17** |
| Honesty | Chaos | 14 | **27** |
| Chaos | Charm | 5 | **18** |
| SelfAwareness | Honesty | 10 | **23** |
| Rizz | Wit | 4 | **17** |

Gerald's roll formula: `d20 + statEffective + 2 (level bonus)`.

For **Recover** action: fixed DC = 12, stat = SelfAwareness. Gerald needs `d20 + 4 + 2 ≥ 12` → `d20 ≥ 6`.

### LLM Adapter

**Critical constraint:** `NullLlmAdapter` returns options for only Charm, Honesty, Wit, and Chaos — it does **not** include SelfAwareness or Rizz. Since the turn plan requires a SelfAwareness Speak action (Turn 4), the implementer must use a **`ScriptedLlmAdapter`** (test-local) that returns the correct stat options per turn.

The `ScriptedLlmAdapter` must:
- Accept a sequence of `DialogueOption[]` arrays (one per `GetDialogueOptionsAsync` call)
- Return the next array in sequence on each call
- Behave identically to `NullLlmAdapter` for `DeliverMessageAsync` (echo text, prefix failure tier on failure), `GetOpponentResponseAsync` (return `OpponentResponse("...")` with null Tell/WeaknessWindow), and `GetInterestChangeBeatAsync` (return null)

Alternatively, the implementer may restructure the turn plan to avoid SelfAwareness Speak actions entirely (see "Alternative Turn Plan" in Edge Cases).

### Trap Registry

Use a `TestTrapRegistry` that returns a trap definition only for `StatType.Chaos`:

```
TrapDefinition(
    id: "unhinged",
    stat: StatType.Chaos,
    effect: TrapEffect.Disadvantage,
    effectValue: 0,
    durationTurns: 3,
    llmInstruction: "You're unhinged now",
    clearMethod: "Recover",
    nat1Bonus: "Extra chaos"
)
```

All other stats return `null` from `GetTrap()`.

### GameSessionConfig

```csharp
var config = new GameSessionConfig(
    clock: null,            // no time-based mechanics in this test
    playerShadows: new SessionShadowTracker(geraldStats),
    opponentShadows: new SessionShadowTracker(velvetStats),
    startingInterest: null, // default 10
    previousOpener: null
);
```

Providing `SessionShadowTracker` instances enables shadow growth event tracking.

---

## 3. Turn Plan with Dice Values

Starting interest: **10** (InterestState.Interested).

### Turn 1 — Speak CHARM (Success)

- **Option index:** 0 (Charm)
- **DC:** 18. Roll: `d20 + 13 + 2 = d20 + 15`.
- **Queue d20 = 5** → total = 20, beat DC by 2 → `SuccessScale` = **+1** interest
- **Risk tier:** need = 18 − 13 − 2 = 3. RiskTier.Safe (need ≤ 5). Bonus: 0.
- **Momentum:** increments to 1 (no bonus at streak=1)
- **Combo:** none (first turn, no previous stat)
- **Interest delta:** +1
- **Interest after:** 11
- **XP:** 5 (Success, beat DC by 1–4 → low-margin success)
- **Dice consumed:** 1 d20 (roll) + 1 d100 (timing) = **2 values**

### Turn 2 — Speak WIT (Success, Hard tier)

- **Option index:** 2 (Wit) — if using NullLlmAdapter ordering; or appropriate index from ScriptedLlmAdapter
- **DC:** 17. Roll: `d20 + 3 + 2 = d20 + 5`.
- **Queue d20 = 18** → total = 23, beat DC by 6 → `SuccessScale` = **+2** interest
- **Risk tier:** need = 17 − 3 − 2 = 12. RiskTier.Hard (11–15). Bonus: **+1**.
- **Momentum:** increments to 2 (no bonus at streak=2)
- **Combo:** previous=Charm, current=Wit. Per `ComboTracker` code: no combo matches Charm→Wit. **However**, the code defines "The Setup" as `prev=Wit, current=Charm`. The issue states this should be "The Setup" but the code contradicts it. **The implementer must verify the actual combo definitions in `ComboTracker.cs` and adjust the turn order accordingly.** See "Revised Turn Plan" below.
- **Interest delta (without combo):** +2 (success) + 1 (Hard risk) = +3
- **Interest after:** 14
- **XP:** 10 (Success, beat DC by 5–9 → mid-margin success)
- **Dice consumed:** 1 d20 + 1 d100 = **2 values**

### ⚠️ Combo Discrepancy — Revised Turn Plan

The issue says Charm→Wit triggers "The Setup", but `ComboTracker` code defines:
- **The Setup:** prev=**Wit**, current=**Charm** (i.e., Wit→Charm)
- **The Recovery:** prev=any fail, current=**SelfAwareness** success

**Recommended revised turn order to match actual combo code:**

| Turn | Action | Stat | Intended Outcome | Expected Combo |
|------|--------|------|-----------------|----------------|
| 1 | Speak | WIT | Success | — |
| 2 | Speak | CHARM | Success (Hard tier) | **The Setup** (Wit→Charm) |
| 3 | Speak | HONESTY | Fail (miss by 3 → Misfire) | — |
| 4 | Speak | SA | Success | **The Recovery** (fail→SA) |
| 5 | Speak | CHAOS | Fail (miss by 7 → TropeTrap) | — |
| 6 | Recover | SA | Success (DC 12) | — |
| 7 | Speak | CHARM | Success | — |
| 8 | Speak | CHARM | Big success | DateSecured or high interest |

### Revised Dice Calculations

**Turn 1 — Speak WIT (Success, beat DC by 1–4):**
- DC: 17. `d20 + 3 + 2`. **Queue d20 = 14** → total = 19, beat by 2 → +1 interest.
- Risk: need = 12 → Hard. Bonus: +1.
- Interest delta: +1 (success) + 1 (Hard) = **+2**. Interest: 10 → 12.
- Momentum: 1. XP: 5.
- Dice: d20(14), d100(50).

**Turn 2 — Speak CHARM (Success, beat by 1–4, The Setup combo):**
- DC: 18. `d20 + 13 + 2`. **Queue d20 = 5** → total = 20, beat by 2 → +1 interest.
- Risk: need = 3 → Safe. Bonus: 0.
- Combo: Wit→Charm = **"The Setup"** → +1 interest bonus.
- Interest delta: +1 (success) + 0 (Safe) + 1 (The Setup) = **+2**. Interest: 12 → 14.
- Momentum: 2. XP: 5.
- Dice: d20(5), d100(50).

**Turn 3 — Speak HONESTY (Fail, miss by 3 → Misfire):**
- DC: 27. `d20 + 3 + 2 = d20 + 5`. Need total < 27 with miss=3 → total=24 → d20=19.
  Wait: miss = DC − total = 27 − (d20+5). For miss=3: d20+5=24, d20=19. FailureTier: miss 3 is in range 3–5 → **Misfire** → −2 interest.
- **Queue d20 = 19** → total = 24, DC = 27, miss = 3 → Misfire.
- Interest delta: **−2**. Interest: 14 → 12.
- Momentum resets to **0**. XP: 2 (failure).
- Dice: d20(19), d100(50).

**Turn 4 — Speak SA (Success, The Recovery combo):**
- Requires SA option from LLM adapter → use ScriptedLlmAdapter.
- DC: 23. `d20 + 4 + 2 = d20 + 6`. Need `d20 + 6 ≥ 23` → d20 ≥ 17.
- **Queue d20 = 18** → total = 24, beat by 1 → +1 interest (SuccessScale: beat 1–4).
- Risk: need = 23 − 4 − 2 = 17. RiskTier.Bold (need ≥ 16). Bonus: **+2**.
- Combo: previous turn failed, current is SA success → **"The Recovery"** → +2 interest bonus.
- Interest delta: +1 (success) + 2 (Bold) + 2 (The Recovery) = **+5**. Interest: 12 → 17.
- Momentum: 1. XP: 5.
- Dice: d20(18), d100(50).

**Turn 5 — Speak CHAOS (Fail, miss by 7 → TropeTrap):**
- Interest is 17 → InterestState.VeryIntoIt → grants **advantage** (roll twice, take higher).
- DC: 18. `d20 + 2 + 2 = d20 + 4`. For miss=7: total=11, d20=7. But we're rolling with advantage.
- With advantage: two d20s, take the higher. To get a final usedRoll of 7, both rolls must be ≤ 7.
- **Queue d20_1 = 5, d20_2 = 7** → used = max(5,7) = 7 → total = 7 + 4 = 11. Miss = 18 − 11 = 7 → **TropeTrap** (miss 6–9).
- Trap "unhinged" activates on Chaos stat (from TestTrapRegistry).
- Interest delta: **−3**. Interest: 17 → 14.
- Momentum resets to **0**. XP: 2 (failure).
- Dice: d20(5), d20(7), d100(50).

**Turn 6 — Recover (SA vs DC 12, Success):**
- `RecoverAsync()` is self-contained. Uses `RollEngine.ResolveFixedDC(SA, 12)`.
- `d20 + 4 + 2 = d20 + 6`. Need ≥ 12 → d20 ≥ 6.
- **Queue d20 = 10** → total = 16, success.
- Trap "unhinged" is cleared.
- Interest: no interest change on successful Recover (per rules §8, Recover success clears trap only). **However**, per `GameSession.RecoverAsync()` implementation: on failure, interest −1. On success, no interest change.
- Verify: interest stays at **14**.
- XP: 15 (Recovery success).
- Dice: d20(10). No d100 for timing (Recover doesn't call GetOpponentResponseAsync timing).

**Turn 7 — Speak CHARM (Success):**
- DC: 18. `d20 + 13 + 2 = d20 + 15`. Need d20 ≥ 3.
- **Queue d20 = 8** → total = 23, beat by 5 → SuccessScale: +2 interest.
- Risk: need = 3 → Safe. Bonus: 0.
- NullLlmAdapter returns null WeaknessWindow, so no DC adjustment.
- Interest delta: **+2**. Interest: 14 → 16.
- Momentum: 1. XP: 10.
- Dice: d20(8), d100(50).

**Turn 8 — Speak CHARM (Big Success → push toward DateSecured):**
- Interest is 16 → InterestState.VeryIntoIt → grants **advantage**.
- DC: 18. `d20 + 13 + 2 = d20 + 15`.
- With advantage: two d20s, take higher.
- **Queue d20_1 = 20, d20_2 = 5** → used = 20 → **Nat 20** → auto-success → SuccessScale: **+4** interest.
- Risk: need = 3 → Safe. Bonus: 0.
- Momentum: 2 (no bonus at streak=2).
- Interest delta: **+4**. Interest: 16 → 20.
- Interest: 20 → InterestState.VeryIntoIt (20 is the boundary). Not DateSecured (need 25).
- XP: 25 (Nat20).
- Dice: d20(20), d20(5), d100(50).

### Interest Tracking Summary

| Turn | Start | Delta | End | State |
|------|-------|-------|-----|-------|
| 1 | 10 | +2 | 12 | Interested |
| 2 | 12 | +2 | 14 | Interested |
| 3 | 14 | −2 | 12 | Interested |
| 4 | 12 | +5 | 17 | VeryIntoIt |
| 5 | 17 | −3 | 14 | Interested |
| 6 | 14 | 0 | 14 | Interested |
| 7 | 14 | +2 | 16 | VeryIntoIt |
| 8 | 16 | +4 | 20 | VeryIntoIt |

With this plan, interest reaches 20, not 25. The game does not end with DateSecured. If the issue requires DateSecured, the implementer must either:
- Add more turns, or
- Increase dice values to produce larger deltas, or
- Start with higher interest via `GameSessionConfig.StartingInterest`

**Option to achieve DateSecured:** Set `startingInterest: 15` in `GameSessionConfig`. Then:

| Turn | Start | Delta | End |
|------|-------|-------|-----|
| 1 | 15 | +2 | 17 |
| 2 | 17 | +2 | 19 |
| 3 | 19 | −2 | 17 |
| 4 | 17 | +5 | 22 |
| 5 | 22 | −3 | 19 |
| 6 | 19 | 0 | 19 |
| 7 | 19 | +2 | 21 |
| 8 | 21 | +4 | 25 → **DateSecured** |

This is the **recommended approach**: use `startingInterest: 15` so the final turn pushes interest to exactly 25 and triggers `GameOutcome.DateSecured`.

**Note on advantage:** When interest is in VeryIntoIt (16–20) or AlmostThere (21–24), `InterestMeter.GrantsAdvantage` returns true. With starting interest 15, VeryIntoIt triggers from Turn 1 onward when interest reaches 17. This means Turns 2+ may grant advantage. The dice queue must account for the extra d20 consumed when advantage applies.

### Final Dice Queue (with startingInterest=15)

The implementer must carefully construct the full dice queue by tracing every `dice.Roll()` call through the entire 8-turn sequence. Key sources of dice consumption:

1. **`RollEngine.Resolve()`** — 1 d20 (normal) or 2 d20 (advantage/disadvantage)
2. **`TimingProfile.ComputeDelay()`** — 1 d100 per Speak turn (called in `ResolveTurnAsync`)
3. **`StartTurnAsync()` ghost check** — 1 d4 when interest is in Bored state (unlikely with startingInterest=15)
4. **`RecoverAsync()` via `ResolveFixedDC()`** — 1 d20

The implementer should:
1. Write the dice queue
2. Run the test
3. If `FixedDice` throws "no more values", add more values
4. If assertions fail, adjust d20 values

---

## 4. Acceptance Criteria

### AC1: Integration test file exists in `tests/Pinder.Core.Tests/Integration/`

The file `FullConversationIntegrationTest.cs` must exist at the specified path with at least one `[Fact]` test method.

### AC2: All 8 turns execute without exception

The test must complete all 8 turns (7 `StartTurnAsync`/`ResolveTurnAsync` cycles + 1 `RecoverAsync` call) without throwing any exception. `FixedDice` must have enough queued values for all dice consumption.

### AC3: Interest delta matches rules (including Hard/Bold tier bonuses)

For each turn, assert `TurnResult.InterestDelta` (or verify `RecoverResult.StateAfter.Interest`) matches the calculated value including:
- `SuccessScale.GetInterestDelta()` for successes
- `FailureScale.GetInterestDelta()` for failures
- `RiskTierBonus.GetInterestBonus()` for Hard/Bold tiers
- Combo interest bonuses
- Momentum bonus (only at streak ≥ 3)

### AC4: Momentum streak increments on success, resets on fail

Assert `TurnResult.StateAfter.MomentumStreak` after each turn:
- Increments by 1 on each success
- Resets to 0 on each failure

### AC5: Combo bonus applied correctly (The Setup, The Recovery)

- Turn 2: `TurnResult.ComboTriggered == "The Setup"` (assuming revised Wit→Charm order)
- Turn 4: `TurnResult.ComboTriggered == "The Recovery"` (fail→SA success)
- All other turns: `TurnResult.ComboTriggered == null`

### AC6: TropeTrap fires on miss 6–9, correct trap type for stat

- Turn 5: `TurnResult.Roll.Tier == FailureTier.TropeTrap`
- Turn 5: `TurnResult.Roll.ActivatedTrap != null`
- Turn 5: `TurnResult.Roll.ActivatedTrap.Id == "unhinged"`
- Turn 5: `TurnResult.StateAfter.ActiveTraps` contains "unhinged"

### AC7: Trap clears after Recover action

- Turn 6: `RecoverResult.Success == true`
- Turn 6: `RecoverResult.ClearedTrapName == "unhinged"`
- Turn 6: `RecoverResult.StateAfter.ActiveTraps` is empty (or does not contain "unhinged")

### AC8: ShadowGrowthEvents populated when shadow grows

With `SessionShadowTracker` configured:
- Shadow growth events should appear on appropriate turns (implementation-dependent based on which growth triggers fire). At minimum, verify the `ShadowGrowthEvents` property is not null and is a valid list on each `TurnResult`.
- Specific growth triggers to check: Honesty success may trigger Denial growth; SA usage may trigger Overthinking. The implementer must verify which triggers actually fire given the turn sequence.

### AC9: XP accumulated correctly

Assert `TurnResult.XpEarned` or `RecoverResult.XpEarned` for each turn:
- Success with beat margin 1–4: 5 XP
- Success with beat margin 5–9: 10 XP
- Success with beat margin 10+: 15 XP
- Nat 20: 25 XP
- Failure: 2 XP
- Recovery success: 15 XP
- DateSecured (if reached): 50 XP

After all turns, verify the cumulative XP is consistent with per-turn awards.

### AC10: Final outcome is DateSecured (or appropriate end state)

If `startingInterest: 15` is used per the recommended plan, Turn 8's Nat 20 should push interest to 25, resulting in:
- `TurnResult.IsGameOver == true`
- `TurnResult.Outcome == GameOutcome.DateSecured`
- `TurnResult.StateAfter.Interest == 25`
- `TurnResult.StateAfter.State == InterestState.DateSecured`

### AC11: Test is deterministic

All randomness comes from `FixedDice` with predetermined values. No `Random`, no system clock, no external calls. Running the test 100 times must produce identical results.

### AC12: Test completes in < 2 seconds

The test must not rely on real delays. `NullLlmAdapter`/`ScriptedLlmAdapter` returns synchronously wrapped `Task` results. No `Task.Delay` or `Thread.Sleep`.

### AC13: All existing tests still pass

Running the full test suite (`dotnet test`) must report zero failures across all existing tests plus the new integration test.

---

## 5. Edge Cases

### NullLlmAdapter stat limitation

`NullLlmAdapter` only returns options for Charm, Honesty, Wit, Chaos (indices 0–3). It does not provide SelfAwareness or Rizz options. If the turn plan requires SA Speak actions, the implementer must create a `ScriptedLlmAdapter` test helper.

### Advantage/Disadvantage dice consumption

When advantage or disadvantage applies, `RollEngine.Resolve()` calls `dice.Roll(20)` **twice**. The `FixedDice` queue must include both values. Advantage takes the higher; disadvantage takes the lower.

Interest ≥ 16 (VeryIntoIt) grants advantage. Interest ≤ 4 (Bored) grants disadvantage. Track interest state carefully to know when extra d20 values are needed.

### Ghost check in Bored state

`StartTurnAsync()` checks for ghost trigger when interest is in Bored state (1–4). This calls `dice.Roll(4)` — if result is 1, the game ends as Ghosted. With `startingInterest: 15`, interest should never drop to Bored, so this shouldn't trigger. If it can, queue a d4 value ≠ 1.

### Trap already active

`RollEngine` only activates a trap via `attackerTraps.Activate()` if no trap is already active on that stat (`!attackerTraps.IsActive(stat)`). If the same stat triggers TropeTrap twice, the second activation is skipped. In this test plan, only one TropeTrap fires (Turn 5), and it's cleared before any repeat.

### Interest clamping at 0 and 25

`InterestMeter.Apply()` clamps to [0, 25]. If a large negative delta would drop below 0, interest stays at 0 (Unmatched). If a large positive delta exceeds 25, interest caps at 25 (DateSecured). The test should verify clamping on the final turn if DateSecured is reached.

### RecoverAsync without active trap

`RecoverAsync()` when no trap is active: the implementer should check whether GameSession handles this gracefully (likely returns a result with `ClearedTrapName: null`). In this test plan, Turn 6 always has the "unhinged" trap active, so this edge case doesn't arise.

### Momentum bonus only at streak ≥ 3

Momentum bonus is 0 for streaks 1–2. The bonus kicks in at streak 3 (+2), streak 4 (+2), streak 5+ (+3). In the recommended 8-turn plan, the longest success streak is 2, so momentum bonus is always 0. This is intentional — the test verifies momentum tracking without the bonus adding complexity to interest delta calculations.

### Alternative Turn Plan (NullLlmAdapter only)

If the implementer prefers to avoid a custom adapter, a plan using only Charm/Honesty/Wit/Chaos:

| Turn | Stat | Combo |
|------|------|-------|
| 1 | Charm | — |
| 2 | Honesty | The Reveal (Charm→Honesty) |
| 3 | Chaos | The Pivot (Honesty→Chaos) |
| 4 | Wit | Fail |
| 5 | Honesty | Fail → Misfire or TropeTrap |
| 6 | Recover | — |
| 7 | Charm | — |
| 8 | Charm | — |

This avoids SelfAwareness but loses The Recovery combo. The issue specifically requests The Recovery, so the ScriptedLlmAdapter approach is preferred.

---

## 6. Error Conditions

### FixedDice exhaustion

If the dice queue runs out, `FixedDice.Roll()` throws `InvalidOperationException("FixedDice: no more values in queue.")`. This manifests as a test failure. Resolution: add more values to the queue.

### Invalid option index

`ResolveTurnAsync()` throws `InvalidOperationException` if the index is out of bounds relative to the options array returned by `StartTurnAsync()`. The test must use valid indices (0-based, within array length).

### StartTurnAsync not called before ResolveTurnAsync

`ResolveTurnAsync()` throws `InvalidOperationException("Must call StartTurnAsync before ResolveTurnAsync.")` if called without a preceding `StartTurnAsync()`. The test must always call `StartTurnAsync()` first for Speak actions.

### Game already ended

If the game ends (DateSecured, Unmatched, Ghosted) mid-sequence, subsequent `StartTurnAsync()` calls should check `_ended` and throw or return an ended state. The test should not call additional turns after the game ends.

### RecoverAsync when game is ended

If called after game end, behavior depends on implementation. The test should ensure `RecoverAsync` is called only when the game is still active.

---

## 7. Dependencies

### Code dependencies

- **`Pinder.Core`** — all game engine types (GameSession, RollEngine, InterestMeter, ComboTracker, etc.)
- **`Pinder.Core.Tests`** project — test infrastructure, xUnit references
- **No dependency on `Pinder.LlmAdapters`** — this test uses NullLlmAdapter or a test-local adapter

### Issue dependencies

- **#209** (combo test fix) — must be merged first to ensure FixedDice patterns are established and existing tests pass cleanly. The failing test in #209 may cause the "all existing tests still pass" criterion to fail if #209 is not yet merged.

### External dependencies

- None. No API calls, no file I/O, no network access.

---

## 8. Input/Output Examples

### Example assertion sequence (Turn 1 — Speak WIT):

```
Input:
  StartTurnAsync() → returns TurnStart with options
  ResolveTurnAsync(witOptionIndex) with d20=14 queued

Expected Output:
  TurnResult.Roll.IsSuccess == true
  TurnResult.Roll.Stat == StatType.Wit
  TurnResult.Roll.Total == 19  (14 + 3 + 2)
  TurnResult.Roll.DC == 17
  TurnResult.InterestDelta == 2  (+1 success + 1 Hard risk)
  TurnResult.StateAfter.Interest == 17  (15 + 2, with startingInterest=15)
  TurnResult.StateAfter.MomentumStreak == 1
  TurnResult.ComboTriggered == null
  TurnResult.RiskTier == RiskTier.Hard
  TurnResult.XpEarned == 5
  TurnResult.IsGameOver == false
```

### Example assertion (Turn 6 — Recover):

```
Input:
  RecoverAsync() with d20=10 queued

Expected Output:
  RecoverResult.Success == true
  RecoverResult.ClearedTrapName == "unhinged"
  RecoverResult.Roll.IsSuccess == true
  RecoverResult.StateAfter.Interest == 19  (unchanged from Turn 5 end)
  RecoverResult.XpEarned == 15
```

### Example assertion (Turn 8 — DateSecured):

```
Input:
  ResolveTurnAsync(charmIndex) with d20=20 queued (Nat20, advantage)

Expected Output:
  TurnResult.Roll.IsNatTwenty == true
  TurnResult.InterestDelta == 4  (Nat20)
  TurnResult.StateAfter.Interest == 25
  TurnResult.StateAfter.State == InterestState.DateSecured
  TurnResult.IsGameOver == true
  TurnResult.Outcome == GameOutcome.DateSecured
  TurnResult.XpEarned == 25  (Nat20 XP, plus potentially 50 for DateSecured)
```

---

## 9. Implementation Notes

### Dice queue construction strategy

1. Map out every turn's action type (Speak vs Recover)
2. For each Speak turn, determine if advantage/disadvantage applies based on interest state at that point
3. Count dice consumed: d20 × (1 or 2) + d100 × 1 per Speak turn; d20 × 1 per Recover
4. Build the queue left-to-right in chronological order
5. Run the test; if FixedDice throws, add missing values

### Verifying GameSession internals

The test can only assert on public return types (`TurnResult`, `RecoverResult`, `GameStateSnapshot`). Internal fields like `_momentumStreak` are exposed via `GameStateSnapshot.MomentumStreak`. Active traps are exposed via `GameStateSnapshot.ActiveTraps` (string array of trap IDs).

### Test structure recommendation

Use a single `[Fact]` method that runs all 8 turns sequentially with assertions after each turn. This ensures the full sequence is validated as a unit. Avoid splitting into separate test methods since turn state is cumulative.
