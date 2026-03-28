# Vision Review — Sprint 3: RPG Rules Complete

## Alignment: ⚠️

This sprint aims to implement ALL remaining RPG rules (§5 risk tiers, §7 shadow growth/thresholds, §8 non-Speak actions, §10 XP, §14 trap taint, §15 combos/callbacks/tells/weakness windows, §async-time timing/clock/registry, plus a QA pass). This IS the right work — it completes the engine's mechanical layer. However, the sprint is enormous (18 issues with deep dependency chains up to 5 levels deep) and several foundational decisions from prior vision concerns (#58 shadow mutation, #65 fixed DC, #68 bonus composition) remain unresolved. The sprint will succeed only if the prerequisite PRs (#63, #78) are landed cleanly and the architect resolves the open design questions before downstream issues start.

## Data Flow Traces

### #42 Risk Tier Bonus
- `RollEngine.Resolve()` → `RollResult` (has `Total`, `DC`, `StatModifier`, `LevelBonus`) → `GameSession.ResolveTurnAsync` computes `need = DC - (StatModifier + LevelBonus)` → maps to `RiskTier` → adds +1 (Hard) or +2 (Bold) to interest delta
- Required fields: `RollResult.DC`, `RollResult.StatModifier`, `RollResult.LevelBonus`, new `RiskTier` enum
- ✅ All source fields exist on `RollResult`

### #43 Read/Recover/Wait
- Player calls `ReadAsync()` → roll SA vs **fixed DC 12** → `RollEngine.Resolve()` → BUT RollEngine has no fixed DC parameter
- ⚠️ **BLOCKING**: `RollEngine.Resolve` always computes DC from defender stats (`13 + GetEffective(defenceStat)`). No fixed DC overload exists. Per #65, this must be resolved architecturally.
- On Read fail: Overthinking shadow +1 → BUT `StatBlock._shadow` is readonly. Per #58, shadow mutation mechanism must exist first.

### #44 Shadow Growth → #45 Threshold Effects
- Various roll outcomes → increment shadow stats → `StatBlock._shadow[shadowType]++` → BUT StatBlock is immutable
- ⚠️ **BLOCKING**: Per #58, shadow mutation is architecturally undefined. All of #44, #45, #43 (Overthinking +1), #51 (Horniness), #56 (cross-chat bleed) depend on this.

### #46 Combo Detection
- `ComboTracker` observes sequence of `(StatType, bool success)` per turn → matches against 8 combo patterns → returns bonus
- `GameSession.ResolveTurnAsync` feeds stat + outcome to tracker, adds bonus to interest delta
- Required: turn history of stats played (not currently tracked in GameSession beyond conversation text)
- ⚠️ Per #66, session tracking counters are unspecified

### #49 Weakness Windows + #50 Tells
- `ILlmAdapter.GetOpponentResponseAsync` returns `OpponentResponse` with optional `WeaknessWindow?` and `Tell?`
- `GameSession` stores these → applies DC reduction (#49) or +2 roll bonus (#50) on next turn
- Required: `OpponentResponse` type from #63 prerequisite
- ✅ Data flow is clean IF #63 lands first

### #52 Trap Taint Injection
- `TrapState.AllActive` → `.Select(t => t.Definition.LlmInstruction)` → injected into `DialogueContext.ActiveTrapInstructions`
- Currently `GameSession` already collects trap instructions for `DeliveryContext` but passes only names to `DialogueContext`
- ✅ Straightforward — just wire instructions instead of names in all three context types

### #53 OpponentTimingCalculator + #54 GameClock + #55 PlayerResponseDelay → #56 ConversationRegistry
- `OpponentTimingCalculator.ComputeDelayMinutes(profile, interest, shadows, dice)` → delay value
- `GameClock.Advance(delay)` → advances simulated time → `GetTimeOfDay()` → horniness modifier
- `ConversationRegistry.FastForward()` → finds earliest pending reply → advances clock → checks ghost/fizzle/decay
- ⚠️ Per #81: PlayerResponseDelay input source (wall-clock vs game-clock) is undefined
- ⚠️ Per #75: Energy system ownership between GameClock and ConversationRegistry is ambiguous

## Unstated Requirements

- **Combo UI feedback**: If combos exist (#46), the player expects to see WHICH stat sequence would complete a combo before choosing. `DialogueOption.ComboName` exists but the LLM needs combo context in `DialogueContext` to generate appropriate options — not specified in #46.
- **Shadow visibility**: If shadows grow (#44) and have threshold effects (#45), the player needs to see their shadow stat values somewhere. No issue addresses shadow stat display/reporting.
- **Save/load**: With 18 new stateful components (ComboTracker, XpLedger, SessionShadowTracker, GameClock, ConversationRegistry), session serialization becomes critical. No issue addresses persistence.
- **End-of-game shadow events**: #44 specifies several end-of-game shadow triggers (Denial +1 if no Honesty successes, Fixation +1 if never picked Chaos). These require GameSession to have a `EndGame()` or equivalent that evaluates deferred shadow events. Not specified.

## Domain Invariants

- `StatBlock` must remain immutable for roll resolution — any shadow mutation must happen via a separate tracker that doesn't affect mid-roll calculations
- `RollEngine.Resolve` must remain stateless — roll bonuses from combos/tells/callbacks must be accumulated BEFORE calling Resolve, not injected into it
- Interest delta composition must be deterministic and auditable: `base delta (SuccessScale/FailureScale) + risk bonus + momentum + combo bonus = total` — each component should be visible in TurnResult
- All `sealed class` types (not records) per netstandard2.0/C# 8.0 constraint
- `InterestMeter` range [0, 25] is absolute — no feature should bypass clamping

## Gaps

### Missing (should be in sprint or addressed as prerequisite)
- **Shadow mutation mechanism** (#58): Architectural decision needed before #43, #44, #45, #51, #56 can proceed. This is the single biggest blocker.
- **RollEngine fixed DC overload** (#65): Needed for #43 Read/Recover. Must be resolved before or within #43.
- **Bonus composition pattern** (#68): Tells (+2), callbacks (+1/+2/+3), The Triple (+1) all need to compose with the roll. No unified approach specified.
- **GameSession constructor extensibility** (#82): 4-5 new injectable components need a planned approach, not ad-hoc parameter additions.
- **InterestMeter starting value override** (#79): Needed for #45 Dread ≥18 → start at 8.

### Unnecessary (could defer)
- **#56 ConversationRegistry**: This is the most complex piece and depends on almost everything else. At prototype maturity, single-session gameplay is sufficient. Multi-session management could be Sprint 4.
- **#55 PlayerResponseDelay**: Requires integration with the host's real-time measurement. At prototype maturity, the engine can function without penalizing slow replies.

### Assumptions needing validation
- **#42 risk tier values are final**: Previously descoped per #30. Re-introduced without PO confirmation per #61.
- **#74 Horniness roll vs shadow**: Two different mechanisms produce the same gameplay effect. Which one is canonical?
- **#75 Energy ownership**: GameClock or ConversationRegistry?

## Wave Plan

```
Wave 1: #63 (ILlmAdapter expansion), #38 (QA audit)
Wave 2: #78 (TurnResult expansion — depends on #63)
Wave 3: #42 (risk tier bonus), #52 (trap taint), #53 (timing calculator), #54 (GameClock)
Wave 4: #43 (Read/Recover/Wait — needs #42 done + #58/#65 resolved), #46 (combos), #47 (callbacks), #49 (weakness windows), #50 (tells), #55 (player delay — needs #54)
Wave 5: #44 (shadow growth — needs #43), #48 (XP tracking — needs #42, #43, #44)
Wave 6: #45 (shadow thresholds — needs #44), #51 (horniness mechanic — needs #45)
Wave 7: #56 (ConversationRegistry — needs #53, #54, #44)
```

## Recommendations

1. **Resolve #58 (shadow mutation) as a prerequisite** — add a `SessionShadowTracker` class in #63 or create a tiny Wave 0 issue. Without this, Waves 4-7 are blocked.
2. **Resolve #65 (fixed DC) as part of #43** — add a `RollEngine.ResolveFixedDC` overload. Document in #43's AC.
3. **Adopt a unified bonus accumulation pattern** (per #68) — define a `RollBonusAccumulator` or simply add an `int externalBonus` parameter to `RollEngine.Resolve`. This unblocks #46, #47, #50 cleanly.
4. **Defer #56 to next sprint** — ConversationRegistry is the most complex piece with the deepest dependency chain. Single-session gameplay works without it.
5. **Confirm #42 risk tier values with PO** — they were explicitly descoped before. A one-line PO confirmation closes #61.
6. **Plan GameSession constructor evolution** (per #82) — use optional parameters or a `GameSessionOptions` object in #63/#78 to avoid merge conflicts across 10+ issues.

## VERDICT: ADVISORY

Four new vision concerns filed (#79, #80, #81, #82). Fourteen prior concerns remain open and relevant (#57, #58, #61, #62, #64, #65, #66, #67, #68, #70, #74, #75 plus the new ones). The sprint's ambition is appropriate for the product — completing all RPG rules IS the right goal. But the prerequisite design decisions (shadow mutation, fixed DC, bonus composition, constructor extensibility) must be resolved by the architect before Waves 3+ can proceed. The sprint should add these resolutions and retry.
