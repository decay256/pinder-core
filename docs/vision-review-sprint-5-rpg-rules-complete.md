# Vision Review — Sprint 5: RPG Rules Complete (Attempt 1)

## Alignment: ⚠️

This sprint is the **right work** — implementing the remaining RPG mechanical systems (§7 shadows, §8 non-Speak actions, §10 XP, §15 advanced mechanics, async-time) is the correct next step after the core game loop and prerequisite expansions (#63, #78, #42, #52, #53, #85) shipped. The scope (13 issues) is large but manageable with proper wave sequencing. However, **the sprint is missing its foundation**: no Wave 0 implementation issues exist for the prerequisite infrastructure that 9 of 13 feature issues depend on. Additionally, a data flow integrity bug in `RollResult.ExternalBonus` (PR #135) means external bonuses from callbacks, tells, and combos are cosmetic — they cannot change roll outcomes. These must be fixed before feature work begins.

## Data Flow Traces

### #54 GameClock → Consumers (#51, #55, #56)
- `GameClock.Now` → `GetTimeOfDay()` → `GetHorninessModifier()` → consumed by #51 (Horniness level = shadow + modifier)
- `GameClock.ConsumeEnergy()` → consumed by #56 `ConversationRegistry`
- Required: `IGameClock` interface (doesn't exist), `FixedGameClock` test helper (doesn't exist)
- ✅ Issue body is well-specified with `IGameClock` interface per VC #67
- ⚠️ `IGameClock` must exist before #51 and #55 can be implemented

### #43 Read/Recover/Wait → GameSession
- Player selects Read → `RollEngine.ResolveFixedDC(SA, attacker, dc: 12, ...)` → success/fail → reveal interest or −1
- Player selects Recover → guard `TrapState.HasActive` → `ResolveFixedDC(SA, attacker, dc: 12, ...)` → clear trap or −1
- Player selects Wait → −1 interest, trap duration decrement
- Read fail → `SessionShadowTracker.IncrementShadow(Overthinking, 1)`
- ⚠️ **BLOCKED**: `RollEngine.ResolveFixedDC()` doesn't exist. `TrapState.HasActive` doesn't exist. `SessionShadowTracker` doesn't exist. All three are Wave 0 prerequisites (#130).

### #44 Shadow Growth → SessionShadowTracker
- Roll resolves → detect growth event (e.g., Nat1 on Charm → Madness +1) → `SessionShadowTracker.IncrementShadow(Madness, 1)`
- End of conversation → check "never picked Chaos" → Fixation +1
- Required counters: TropeTrap count, Honesty success flag, stats-used history, SA count, highest-% tracking
- ⚠️ **BLOCKED on Wave 0**: `SessionShadowTracker` doesn't exist. `StatBlock._shadow` is `private readonly` — no mutation API.

### #46 Combo System → Interest Delta + Roll Bonus
- `ComboTracker` records last N stats + last-roll-fail flag
- Roll succeeds → check combo completion → add +1/+2 to interest delta
- The Triple: 3 different stats in 3 turns → +1 to ALL rolls next turn
- The Triple bonus must flow via `externalBonus` → `RollEngine.Resolve()` → `RollResult.Total` → `IsSuccess`
- ⚠️ **#136 DATA FLOW BUG**: `RollResult.AddExternalBonus()` sets `ExternalBonus` and `FinalTotal` but `IsSuccess` (frozen at construction) and `MissMargin` (`DC - Total`) ignore it. The Triple's +1 cannot turn a miss into a hit.

### #47 Callback Bonus → Roll
- `GameSession` maintains topic list → pass `CallbackOpportunities` to LLM → LLM sets `CallbackTurnNumber` on option → compute distance → +1/+2/+3 bonus
- Bonus must flow through `RollEngine.Resolve(externalBonus)` pre-roll
- ⚠️ **Same #136 bug**: bonus is known pre-roll but current `AddExternalBonus()` can't affect outcome

### #50 Tells → Roll
- `OpponentResponse.DetectedTell` stored → next turn, if player picks matching stat → +2 bonus
- Same `externalBonus` flow requirement
- ⚠️ **Same #136 bug**

### #49 Weakness Windows → DC Reduction
- `OpponentResponse` carries `WeaknessWindow?` → GameSession stores it → next turn, reduce DC by 2-3 for matching stat
- ✅ This flows through **DC modification**, not `externalBonus` — not affected by #136
- ⚠️ `GetOpponentResponseAsync` returns structured `OpponentResponse` (merged in PR #114) ✅

### #45 Shadow Thresholds → Gameplay Effects
- `SessionShadowTracker.GetShadow(type)` → `ShadowThresholdEvaluator.GetThresholdLevel()` → 0/1/2/3
- Threshold 2 (≥12): disadvantage on paired stat
- Threshold 3 (≥18): Dread=starting interest 8, Denial=no Honesty options, Fixation=forced stat
- ⚠️ `InterestMeter` has no `startingValue` constructor overload (#79)
- ⚠️ **BLOCKED on Wave 0**: needs `SessionShadowTracker`

### #51 Horniness-Forced Rizz → Option Composition
- `horninessLevel = sessionShadowTracker.GetShadow(Horniness) + gameClock.GetHorninessModifier()`
- ≥6: inject one Rizz option; ≥12: force one; ≥18: all Rizz
- ⚠️ **BLOCKED**: needs both `SessionShadowTracker` AND `IGameClock`

### #48 XP Tracking → XpLedger
- Roll resolves → `XpLedger.Record(source, amount)` → `TurnResult.XpEarned` populated
- DC tier: ≤13 = 5 XP, 14-17 = 10 XP, ≥18 = 15 XP
- Trap recovery XP (15) requires #43 Recover action
- ✅ Straightforward accumulation, minimal blocking dependencies beyond #43

### #55 PlayerResponseDelay → Interest Penalty
- Caller passes `TimeSpan delay` → `PlayerResponseDelayEvaluator.Evaluate(delay, opponentStats, interest)` → `DelayPenalty`
- Pure function, no state
- ✅ Self-contained. Only depends on `IGameClock` if wired through `ConversationRegistry` (async mode)

### #56 ConversationRegistry → Multi-Session Orchestration
- Holds collection of `ConversationEntry` (session + pending reply timestamp)
- `FastForward()`: find earliest reply → advance `IGameClock` → check ghost/fizzle on all sessions → apply interest decay → return
- `ApplyCrossChatEvent()`: propagate shadow bleed across sessions
- ⚠️ **Most complex issue**: orchestrates ghost triggers, fizzle, interest decay, cross-chat shadow bleed, energy
- ⚠️ Needs: `IGameClock`, `SessionShadowTracker`, and access to `GameSession.InterestMeter.Current` (no public property exists)

## Unstated Requirements
- **Shadow persistence across sessions**: #44 implements in-session shadow growth, but shadows are character-level state. No issue addresses serialization of shadow deltas after a session ends. Players expect shadow growth to carry across conversations.
- **Combo UI feedback**: #46 mentions `DialogueOption.ComboName` for ⭐ icon, but the player also needs to understand WHY a combo fired. A brief explanation string (e.g., "The Setup: Wit → Charm") should be in `TurnResult`.
- **XP → Level-up notification**: #48 tracks XP but doesn't specify what happens when cumulative XP crosses a `LevelTable` threshold mid-conversation. Player expects a level-up notification in `TurnResult`.
- **Multiple external bonuses stacking**: #46 Triple (+1), #47 callback (+3), #50 tell (+2) can theoretically all apply to the same roll. The stacking behavior needs to be explicit (additive? capped?).

## Domain Invariants
- `RollResult.IsSuccess` must reflect ALL bonuses that affect the roll — base + external. A roll that beats DC when external bonuses are included MUST be a success.
- Shadow growth must not affect the CURRENT roll's resolution — growth happens AFTER the roll, affecting future rolls only.
- Interest delta = SuccessScale/FailureScale ± momentum ± combo ± risk tier bonus. These compose additively.
- `GameSession` turn sequencing invariant (StartTurn → Resolve alternation) must hold even with Read/Recover/Wait added.
- `SessionShadowTracker` must NOT modify the underlying `StatBlock` — it's an overlay for in-session mutations.
- Energy is owned by `IGameClock`, consumed by `ConversationRegistry` — single owner, no duplication.

## Gaps

### Missing (Critical)
- **Wave 0 implementation issues do not exist** (#137). VC #130 recommends them but no backlog items were created. 9/13 issues are blocked without: `SessionShadowTracker`, `IGameClock`, `RollEngine.ResolveFixedDC`, `TrapState.HasActive`, `GameSession` constructor expansion.
- **#136 ExternalBonus data flow bug**: `IsSuccess`/`MissMargin` ignore `ExternalBonus`. Callback/tell/combo bonuses are cosmetic. Must be fixed in Wave 0.
- **InterestMeter starting value overload** (#79): Needed by #45 (Dread ≥18 → start at 8). Should be in Wave 0.

### Missing (Non-blocking)
- Shadow persistence/serialization: deferred is acceptable for prototype, but should be tracked.
- `GameSession` public access to `InterestMeter.Current`: needed by #56 `ConversationRegistry` for ghost/fizzle checks. Currently only exposed via `GameStateSnapshot`.

### Could Defer
- **#56 ConversationRegistry**: Most complex issue, deepest dependency chain (needs #54, #44, #45, SessionShadowTracker). It's a self-contained async-time subsystem that doesn't block any core RPG mechanic. Deferring to Sprint 6 would significantly reduce risk.
- **#55 PlayerResponseDelay**: Pure function, easy to implement — but only useful when wired through ConversationRegistry. Could ship with #56.

### Assumptions to Validate
- #48 DC tier thresholds (≤13/14-17/≥18) — are these PO-confirmed or inferred from base DC 13?
- Multiple external bonuses stacking — additive with no cap? PO should confirm.
- #44's "highest-% option picked 3 turns in a row" — defined as "highest stat modifier + level bonus" but this doesn't account for DC variance across stats. Is this the intended definition?

## Wave Plan

```
Wave 0: Wave 0 prerequisite PRs (create from #137)
  - 0A: SessionShadowTracker
  - 0B: IGameClock interface + FixedGameClock
  - 0C: RollEngine expansion (externalBonus param fix #136, ResolveFixedDC, TrapState.HasActive)
  - 0D: GameSession constructor expansion + InterestMeter(startingValue) overload

Wave 1: #54, #38, #55
  - #54 GameClock (implements IGameClock from 0B)
  - #38 QA review (no code dependencies)
  - #55 PlayerResponseDelay (pure function, only needs StatBlock)

Wave 2: #43, #46, #47, #49, #50
  - #43 Read/Recover/Wait (needs 0C + 0A)
  - #46 Combo system (needs 0C for externalBonus)
  - #47 Callback bonus (needs 0C for externalBonus)
  - #49 Weakness windows (needs GameSession, already merged)
  - #50 Tells (needs 0C for externalBonus)

Wave 3: #44, #48
  - #44 Shadow growth (needs 0A + #43 for Overthinking-on-Read-fail)
  - #48 XP tracking (needs #43 for trap recovery XP)

Wave 4: #45
  - #45 Shadow thresholds (needs #44 for shadow values to check)

Wave 5: #51
  - #51 Horniness-forced Rizz (needs #45 + #54)

Wave 6: #56 (consider deferring to Sprint 6)
  - #56 ConversationRegistry (needs #54, #44, #45, SessionShadowTracker)
```

## Recommendations
1. **Create Wave 0 implementation issues immediately** — convert #137's recommendations into 2-4 backlog issues. This is the single highest-leverage action for sprint success. Without Wave 0, agents will fail on 9/13 issues.
2. **Fix #136 (ExternalBonus data integrity) in Wave 0C** — add `externalBonus` param to `RollEngine.Resolve()` and include it in `Total` at construction. This is ~20 lines of code but affects 3 features.
3. **Consider deferring #56 to Sprint 6** — ConversationRegistry is the most complex issue with the deepest dependency chain. The 12 other issues deliver a complete single-conversation RPG experience. Multi-session management is a separate concern.
4. **Add `InterestMeter(int startingValue)` overload to Wave 0D** — trivial change, blocks #45 Dread ≥18 effect.
5. **Add `GameSession.CurrentInterest` public property in Wave 0D** — needed by #56 for ghost/fizzle evaluation.

## Vision Concerns Filed
- **#136**: `RollResult.IsSuccess` and `MissMargin` ignore `ExternalBonus` — callback/tell/combo bonuses cannot change roll outcomes
- **#137**: Sprint 5 has no Wave 0 implementation issues — only VC #130 recommending them

## VERDICT: ADVISORY

The sprint direction is correct and the issues are well-specified with good cross-references to existing vision concerns. Two new concerns filed (#136, #137). The **Wave 0 gap (#137)** is the most actionable — creating 2-4 prerequisite implementation issues would unblock the entire sprint. The **ExternalBonus bug (#136)** is a data flow integrity issue that must be fixed before #46, #47, and #50 can work correctly. Neither is BLOCKING — both are solvable within sprint scope — but they must be addressed before spawning feature agents.
