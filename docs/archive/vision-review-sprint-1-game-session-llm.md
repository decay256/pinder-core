# Vision Review — Sprint 1: Game Session + LLM Adapter

## Alignment: ⚠️

This sprint is a **major positive direction change**. After 5+ sprints stuck on rules-sync housekeeping, building the GameSession + ILlmAdapter is the highest-leverage work possible — it creates the actual game loop. Players will be able to match, converse, roll dice, and get LLM-generated dialogue. This is the product's core value proposition. However, several sub-mechanics in #27 are under-specified (failure deltas, shadow growth triggers, Hard/Bold bonus), which will force the implementation agent to either invent game design or leave stubs.

## Data Flow Traces

### ILlmAdapter (#26) — Dialogue Generation Pipeline
- Host creates `DialogueContext` {PlayerPrompt, OpponentPrompt, ConversationHistory, OpponentLastMessage, ActiveTraps, CurrentInterest}
- → `ILlmAdapter.GetDialogueOptionsAsync(context)` → `DialogueOption[]` (4 options, one per stat family)
- → Player picks option → host builds `DeliveryContext` {all DialogueContext fields + ChosenOption, Outcome (FailureTier), BeatDcBy, ActiveTraps (full LLM instructions)}
- → `ILlmAdapter.DeliverMessageAsync(delivery)` → degraded message string
- → `ILlmAdapter.GetOpponentResponseAsync(opponent)` → opponent reply string
- Required fields: PlayerPrompt (from PromptBuilder), OpponentPrompt, FailureTier, StatType, InterestState, conversation history
- ✅ All referenced types exist in the codebase: `FailureTier`, `StatType`, `InterestState`, `InterestMeter`
- ⚠️ `DialogueOption.CallbackTurnNumber` and `ComboName` reference callback/combo mechanics that don't exist yet — these should be nullable and ignored by NullLlmAdapter (they are, per the issue spec)
- ⚠️ `DialogueOption.HasTellBonus` references a "tell" mechanic not defined anywhere in the codebase or architecture

### GameSession (#27) — Full Turn Pipeline
- `StartTurnAsync()` → check end conditions → determine adv/disadv from InterestMeter + TrapState → call LLM for options → return `TurnStart`
- `ResolveTurnAsync(optionIndex)` → validate → `RollEngine.Resolve()` → `SuccessScale.GetInterestDelta()` OR **[UNDEFINED: fail tier delta]** → momentum update → trap activation → **[UNDEFINED: shadow growth]** → `InterestMeter.Apply(delta)` → `ILlmAdapter.DeliverMessageAsync()` → threshold check → `ILlmAdapter.GetOpponentResponseAsync()` → return `TurnResult`
- Required fields flowing through: StatBlock (from CharacterProfile), assembled system prompt, TrapState, interest level, dice rolls, turn number
- ✅ Roll → SuccessScale → InterestMeter path is fully wired with existing code
- ⚠️ **BLOCKING: Failure interest deltas undefined** — what delta does a Fumble, Misfire, TropeTrap, Catastrophe, or Legendary apply? (#28)
- ⚠️ Shadow growth triggers undefined — no concrete rules for when shadow stats increase (#29)
- ⚠️ Hard/Bold risk bonus referenced but never defined (#30)

## Unstated Requirements
- Players expect the 4 dialogue options to feel mechanically different (Charm option vs Chaos option should read differently) — this is an LLM prompt engineering concern, but `DialogueContext` must carry enough character identity for the LLM to differentiate
- If GameSession tracks momentum streaks, players expect visual feedback — the host needs `MomentumStreak` in the state snapshot (it IS included in `GameStateSnapshot` ✅)
- Ghost trigger (25% per turn at Bored) means a game can end abruptly — host should get enough warning to play an animation/transition. `GameOutcome.Ghosted` exists for this ✅
- The `NullLlmAdapter` will be used for ALL automated testing — it must be deterministic and predictable for integration tests

## Domain Invariants
- Interest delta must always be applied through `InterestMeter.Apply()` — never raw-set the value (clamping invariant)
- `RollEngine` remains stateless — `GameSession` owns all mutable state (interest, traps, momentum, turn count, history)
- Every LLM call must receive the full assembled system prompt — partial prompts produce incoherent character voice
- Trap state advances exactly once per player turn (not per LLM call)
- Conversation history is append-only within a session — no message rewriting
- Game ends exactly at Interest 0, Interest 25, or ghost trigger — no other exit paths

## Gaps

### Missing (filed as vision concerns)
- **#28 — Failure interest deltas**: SuccessScale handles +1 to +4 for successes, but negative deltas for failure tiers are unspecified. This is **the most critical gap** — without it, failed rolls have no mechanical consequence on interest.
- **#29 — Shadow growth triggers**: Step 6 of #27 references shadow growth events with zero specification. Recommend descoping to a stub or separate issue.
- **#30 — Hard/Bold risk bonus**: Referenced in #27 step 3 but never defined.

### Unnecessary (could defer)
- `DialogueOption.CallbackTurnNumber` and `ComboName` reference callback/combo systems that don't exist. Fine as nullable fields at prototype, but don't let them creep into GameSession logic.

### Assumptions to validate
- **netstandard2.0 + async**: `Task<T>` is available in netstandard2.0, but the test project (net8.0) needs async test support — verify xUnit handles async tests (it does ✅)
- **Ghost trigger uses `dice.Roll(4) == 1`**: This means the IDiceRoller is reused for non-combat randomness. Fine, but tests need a `FixedDice` that can return sequences (not just a single fixed value). The issue references `FixedDice` in its AC — verify this exists or needs to be created.

## Recommendations
1. **Resolve #28 (failure deltas) before implementation** — either PO provides values or use conservative defaults: Fumble=-1, Misfire=-2, TropeTrap=-2, Catastrophe=-3, Legendary=-4. Document the default source.
2. **Descope shadow growth from #27** — mark step 6 as a TODO/stub and create #29 as a follow-up issue with full trigger rules. #27 is already the largest issue in the project.
3. **Descope Hard/Bold bonus from #27** — remove from step 3 per #30. Can be added when the mechanic is designed.
4. **Verify or create `FixedDice`** — #27's integration test AC requires it. If it doesn't exist, #26 or #27 must create it.

## Role Assignments
- **#26 (ILlmAdapter)**: backend-engineer ✅ — pure C# interface + types + null impl
- **#27 (GameSession)**: backend-engineer ✅ — core game loop logic

## VERDICT: ADVISORY

Sprint direction is excellent — this is the right work at the right time. Three under-specified sub-mechanics (failure deltas, shadow growth, Hard/Bold) need resolution. Filed as #28, #29, #30, #31. The sprint should proceed with conservative defaults for #28 and explicit descoping of #29/#30 from #27's AC. None of these are blocking if the orchestrator applies safe defaults before spawning implementation agents.
