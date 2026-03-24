# Vision Review — Sprint 1 (Attempt 1): GameSession

## Alignment: ✅

This sprint is the right work at the right time. After 5+ sprints of rules-sync housekeeping, #27 creates the **actual game loop** — the first time a player can match, converse, roll dice, and experience consequences. The issue has been well-refined: previous vision concerns (#28 failure deltas, #29 shadow growth, #30 Hard/Bold bonus) are addressed — #29 and #30 are explicitly descoped, and #28 is resolved via `FailureScale` with prototype defaults. The AC is concrete, the data flow is traceable, and the scope is appropriate for a single backend-engineer issue at prototype maturity.

## Data Flow Traces

### StartTurnAsync — Option Generation
- `GameSession.StartTurnAsync()` → check `InterestMeter.IsZero` / `IsMaxed` → ghost check via `dice.Roll(4)` → derive advantage from `InterestMeter.GrantsAdvantage` + `TrapState` disadvantage → build `DialogueContext(playerPrompt, opponentPrompt, history, lastMessage, trapNames, interest)` → `ILlmAdapter.GetDialogueOptionsAsync()` → return `TurnStart(options, snapshot)`
- Required fields: `CharacterProfile.AssembledSystemPrompt` (both), `InterestMeter.Current`, `TrapState.AllActive` (names), conversation history
- ✅ All context types exist from #26 merge — `DialogueContext` constructor matches available data

### ResolveTurnAsync — Full Turn Resolution
- `ResolveTurnAsync(optionIndex)` → validate index → extract `DialogueOption.Stat` → `RollEngine.Resolve(stat, player.StatBlock, opponent.StatBlock, trapState, player.Level, trapRegistry, dice, advantage, disadvantage)` → `RollResult`
- → if success: `SuccessScale.GetInterestDelta(result)` → +1/+2/+3/+4
- → if failure: `FailureScale.GetInterestDelta(result)` → -1/-2/-3/-4/-5 (NEW, prototype defaults)
- → momentum: streak++ on success, reset on fail; +2 at 3-streak, +2 at 4-streak, +3 at 5+ streak
- → `InterestMeter.Apply(delta + momentumBonus)` → clamped to [0, 25]
- → `TrapState.AdvanceTurn()` → expire old traps
- → build `DeliveryContext` → `ILlmAdapter.DeliverMessageAsync()` → delivered text
- → detect threshold crossing (compare `InterestState` before vs after) → if crossed: `ILlmAdapter.GetInterestChangeBeatAsync()` → narrative beat
- → build `OpponentContext` (needs `TimingProfile.ComputeDelay()` for `ResponseDelayMinutes`) → `ILlmAdapter.GetOpponentResponseAsync()` → opponent reply
- → append both to history → increment turn → return `TurnResult`
- Required fields: `StatBlock` (both profiles), `Level` (player), `AssembledSystemPrompt` (both), `DisplayName` (opponent, for InterestChangeContext), `TimingProfile` (opponent, for delay calc)
- ✅ All required data is available through the `CharacterProfile` constructor as specified
- ✅ `RollResult` carries all fields needed by `SuccessScale`, `FailureScale`, and `DeliveryContext`

### Ghost Trigger
- In `StartTurnAsync`: if `InterestMeter.GetState() == Bored` → `dice.Roll(4) == 1` → 25% chance → set `_ended = true`, return `GameOutcome.Ghosted`
- ⚠️ Minor: AC says "throw `GameEndedException`" for end conditions but ghost trigger is checked at start of turn — the implementer needs to decide whether ghost returns a special `TurnStart` indicating game over, or throws. The AC says "throws GameEndedException when Interest hits 0 or 25" but ghost is a separate end path. The implementation should handle both consistently.

## Unstated Requirements
- **Threshold crossing detection**: The AC says "check Interest threshold crossing" without defining it. The obvious interpretation is: compare `InterestMeter.GetState()` before and after `Apply(delta)` — if the `InterestState` enum value changed, a threshold was crossed. The implementer should use this interpretation.
- **FixedDice for integration tests**: The AC requires an integration test with `FixedDice`, but the existing `FixedDice` implementations are private inner classes in other test files. The implementer will need to create their own or extract a shared test helper. Not a blocker — standard test refactoring.
- **`GameEndedException` doesn't exist yet**: Must be created as part of this issue. Straightforward sealed exception class.
- **History format**: `_history` is `List<(string sender, string text)>` — the `sender` field should use `CharacterProfile.DisplayName` for both player and opponent entries to keep history human-readable and LLM-useful.

## Domain Invariants
- Interest delta is always applied through `InterestMeter.Apply()` — never raw-set (clamping invariant preserved)
- `RollEngine` remains stateless — `GameSession` owns all mutable state
- Trap state advances exactly once per player turn, after roll resolution
- Conversation history is append-only within a session
- Game ends at exactly three conditions: Interest ≤ 0 (Unmatched), Interest ≥ 25 (DateSecured), or ghost trigger (Bored + 25% dice)
- Momentum streak resets to 0 on any failure — never carries across failures
- No shadow growth, no Hard/Bold risk bonus in this implementation (descoped per #29, #30)

## Gaps

### Missing — None critical
All previous gaps (#28, #29, #30, #31) have been addressed in the updated issue body. The AC is now explicit about what's in and what's out.

### Unnecessary — None
The scope is tight for prototype maturity. No gold-plating detected.

### Assumptions to validate
- **`TrapState` disadvantage stacking**: If a trap grants disadvantage AND interest state is Bored (also disadvantage), disadvantage doesn't "double" in d20 systems — it's just disadvantage. The implementer should pass `hasDisadvantage = true` in either case, which `RollEngine` already handles correctly (rolls twice, takes lower).
- **Momentum bonus applies to BOTH success and failure deltas?**: The AC says momentum adds +2/+2/+3 to interest delta on streaks, but streaks only build on success. So momentum bonus only ever adds to positive deltas. The implementer should be clear: momentum bonus is added to the success delta only (since streak resets on fail, there's never a momentum bonus on a failure turn). This is logically consistent but worth the implementer being explicit about.

## Recommendations
1. **No new vision concerns to file** — the issue is well-specified after incorporating #28-31 feedback
2. **Proceed to implementation** — the backend-engineer has clear AC, explicit descoping, and all prerequisite types from #26

## Role Assignments
- **#27 (GameSession)**: backend-engineer ✅ — pure C# game loop logic, correct role

## VERDICT: CLEAN

The issue is well-refined after the previous vision review cycle. Previous concerns (#28-31) are resolved: failure deltas have prototype defaults via `FailureScale`, shadow growth and Hard/Bold are explicitly descoped, and the AC clearly delineates in-scope vs out-of-scope. All prerequisite types from #26 (ILlmAdapter, context types, NullLlmAdapter) are merged and available. No blocking concerns remain. Sprint proceeds automatically.
