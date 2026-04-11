# Vision Review — Sprint: RPG Rules Complete

## Alignment: ⚠️

This sprint is the right *direction* — implementing the remaining RPG mechanical systems (§7 shadows, §8 non-Speak actions, §10 XP, §15 advanced mechanics, async-time) is the logical next step after the core game loop shipped in Sprint 6. However, the **scope is 4-8x larger than any previous sprint** (16 issues vs. historical 2-4) and introduces two entirely new architectural layers (shadow mutability, async-time clock system) alongside 10+ feature additions to GameSession. This is a "build the entire rest of the game" sprint, not a focused iteration.

## Data Flow Traces

### #42: Risk Tier Bonus (RollResult → GameSession)
- `RollEngine.Resolve()` → `RollResult` (currently no RiskTier) → `GameSession.ResolveTurnAsync` → `SuccessScale.GetInterestDelta(result) + riskBonus` → `InterestMeter.Apply(total)`
- Required new fields: `RollResult.RiskTier` (enum: Safe/Medium/Hard/Bold), computed from `need = dc - (statMod + levelBonus)`
- ⚠️ `RollResult` currently has `MissMargin` (DC - Total) but no "need" concept pre-roll. The risk tier is based on `need = dc - (statMod + levelBonus)` which is `DC - modifiers` WITHOUT the die roll — this is different from miss margin. Implementer must compute this from existing fields: `RiskTier = DC - StatModifier - LevelBonus` (the "need" to roll).

### #44: Shadow Growth (GameSession → StatBlock)
- `GameSession.ResolveTurnAsync()` → detect growth event (e.g., Nat1 on Charm) → increment `ShadowStatType.Madness` on `_player.Stats`
- **⚠️ BLOCKING DATA FLOW GAP**: `StatBlock._shadow` is `private readonly Dictionary`. There is NO public method to modify shadow values. `StatBlock.GetShadow()` is read-only. Shadow growth CANNOT flow through the current API. See #58.

### #52: Trap Taint Injection (TrapState → ILlmAdapter contexts)
- `GameSession` already passes trap names to `DialogueContext.ActiveTraps` and trap instructions to `DeliveryContext.ActiveTraps`
- ⚠️ Inconsistency: `DialogueContext.ActiveTraps` carries **names** (`string[]`), `DeliveryContext.ActiveTraps` carries **LLM instructions** (`string[]`). Issue #52 wants ALL contexts to carry instructions, but `DialogueContext` and `OpponentContext` still carry names. This is partially already done for DeliveryContext.

### #49/#50: Weakness Windows + Tells (LLM response → GameSession state)
- `ILlmAdapter.GetOpponentResponseAsync()` currently returns `string` (just the message text)
- Both #49 and #50 need structured return data: `WeaknessWindow?` and `Tell?`
- **⚠️ INTERFACE GAP**: `GetOpponentResponseAsync` returns `Task<string>`, not a structured response object. Both features need it to return something like `OpponentResponse { Message, WeaknessWindow?, Tell? }`. This requires breaking the ILlmAdapter interface. See #60.

### #56: ConversationRegistry → GameSession orchestration
- `ConversationRegistry.FastForward()` → find earliest pending reply → `GameClock.AdvanceTo()` → check ghost triggers on ALL sessions → check fizzle → apply interest decay → return active session
- Required: every `GameSession` must expose its `InterestMeter.Current` and last activity timestamp for the registry to evaluate ghost/fizzle conditions
- ⚠️ `GameSession` currently has NO public property for `InterestMeter` — only exposed via `GameStateSnapshot` after turns. Registry needs continuous read access.

## Unstated Requirements

- **Shadow persistence across sessions**: #44 implements shadow growth within a session, but shadows are character-level state that persists across conversations. The sprint has no issue for shadow serialization/persistence. After a session ends, shadow growth is lost.
- **UI contract for new TurnResult fields**: Issues #44, #46, #47, #48, #50 all add new fields to `TurnResult` (ShadowGrowthEvents, ComboTriggered, TellReadBonus, XpEarned). Unity consumers will need to handle all these new fields. No documentation issue exists for the expanded UI contract.
- **Test infrastructure for async-time**: Issues #53-#56 introduce time-dependent logic (delays, clocks, scheduling). Tests will need a deterministic clock abstraction. `GameClock` should probably implement an `IGameClock` interface for testability — but no issue mentions this.

## Domain Invariants

- `RollResult` must be immutable — risk tier is computed at construction time, not mutated later
- Shadow growth must not affect the current roll's resolution (growth happens AFTER the roll, affecting future rolls only)
- Interest delta = SuccessScale/FailureScale + risk bonus + momentum + combo bonus — these must compose additively, never multiplicatively
- `GameSession` turn sequencing invariant (StartTurn → Resolve alternation) must hold even with Read/Recover/Wait added (#43)
- `ConversationRegistry` must never call `GameSession.StartTurnAsync` — it schedules, the host executes (stated in #56 but must be enforced)
- A trap's LLM instruction must be the same text regardless of which context type carries it

## Gaps

### Missing
- **Shadow stat mutability solution** (#58): Architectural decision needed before #44 can be implemented. Currently BLOCKING.
- **ILlmAdapter structured response**: #49 and #50 both need `GetOpponentResponseAsync` to return structured data, not just a string. No preparatory issue exists for this interface change.
- **Shadow persistence**: No issue addresses saving shadow growth between sessions.
- **IGameClock interface**: #54 introduces `GameClock` as a concrete class. For testability, it should be injectable. No mention in the issue.

### Unnecessary (could defer)
- **#56 (ConversationRegistry)**: This is the most complex issue in the sprint and introduces multi-session orchestration. It depends on #53, #54, #44 — three issues that are themselves complex. Deferring #56 to a separate sprint would reduce risk without blocking any other feature.
- **#55 (PlayerResponseDelay)**: Depends on #54 (GameClock) which introduces a new subsystem. Could be deferred with the async-time cluster.

### Assumptions to validate
- #42 assumes risk tier thresholds (Need ≤5/6-10/11-15/≥16) are final — these were previously descoped as undefined (#30)
- #53 references `InterestState.Lukewarm` which doesn't exist (#59)
- #44 assumes StatBlock shadow stats can be made mutable — this breaks the immutable snapshot contract (#58)
- #43 assumes SA is the stat for Read and Recover — the issue says "Roll SA vs DC 12" but doesn't specify which SA (attacker's or a fixed value)

## Wave Plan

Wave 1: #38, #42, #49, #50, #52, #53, #54
Wave 2: #43, #46, #47, #55
Wave 3: #44
Wave 4: #45, #48, #51
Wave 5: #56

Rationale:
- Wave 1: No internal dependencies. #38 (QA) runs early to fix test gaps before new features land. #42 (risk tier) is the root dependency. #49, #50 depend only on merged #27. #52, #53, #54 are independent new subsystems.
- Wave 2: #43 depends on #42. #46, #47 depend on #42. #55 depends on #54.
- Wave 3: #44 depends on #43. Also needs #58 (shadow mutability) resolved.
- Wave 4: #45 depends on #44. #48 depends on #42+#43+#44. #51 depends on #44+#45.
- Wave 5: #56 depends on #53+#54+#44. Most complex issue, most dependencies.

## Recommendations

1. **Resolve #58 (StatBlock mutability) before Wave 3** — this is a design decision that affects #44, #45, #48, #51, #56. The architect should decide: mutable StatBlock vs. SessionShadowTracker wrapper vs. snapshot-per-mutation.
2. **Fix #53's Lukewarm reference** (#59) before it hits an implementer — trivial edit, prevents confusion.
3. **Plan the ILlmAdapter expansion** (#60) as a preparatory task in Wave 1 — add all needed fields/return types at once rather than N sequential breaking changes.
4. **Confirm #42's risk tier values with PO** (#61) — this was explicitly descoped in Sprint 6 and is now the root dependency for 7+ issues.
5. **Consider deferring async-time cluster** (#53, #54, #55, #56) to a separate sprint — it's a self-contained subsystem that doesn't block core RPG mechanics.

## VERDICT: ADVISORY

The sprint direction is correct and the issues are well-specified. Five vision concerns filed (#57-#62). The **StatBlock immutability gap (#58)** is the most serious — it's an architectural decision that blocks the shadow growth chain (#44→#45→#48→#51). The ILlmAdapter interface expansion (#60) and Lukewarm reference (#59) are fixable with issue edits. The scope concern (#57) is mitigated by the wave plan but remains a risk. No single issue is individually blocking, but the shadow mutability question should be resolved before Wave 3 begins.
