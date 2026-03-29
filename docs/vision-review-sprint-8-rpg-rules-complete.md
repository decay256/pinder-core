# Vision Review — Sprint 8: RPG Rules Complete

## Alignment: ⚠️

This sprint is **directionally correct and high-leverage**. It's the implementation sprint that follows the architecture/spec work of Sprint 7, turning 14 detailed specs into working code. The scope covers the full breadth of RPG mechanics needed for a playable prototype: shadow growth, combos, callbacks, tells, weakness windows, XP, horniness, multi-session management, and alternative turn actions. The Wave 0 infrastructure (#139) correctly gates everything.

However, **spec drift between #44 and #139 creates a conflicting class hierarchy** that must be resolved before implementation begins — see Data Flow Traces and Gaps below.

## Data Flow Traces

### Wave 0 → All Features: ExternalBonus + dcAdjustment
- `GameSession.ResolveTurnAsync()` → compute callback (#47) + tell (#50) + triple combo (#46) → sum into `externalBonus` → pass to `RollEngine.Resolve(externalBonus:, dcAdjustment:)` → `RollResult.FinalTotal` = Total + ExternalBonus → `IsSuccess` uses `FinalTotal`
- Required: `RollResult.ExternalBonus`, `RollResult.FinalTotal` (both exist from PR #135)
- ⚠️ `RollResult.AddExternalBonus()` still exists as public mutable method — deprecated per #146 but not removed. Dual path risk remains.

### #44 Shadow Growth → SessionShadowTracker vs CharacterState
- **#139 spec**: `SessionShadowTracker(StatBlock)` → `ApplyGrowth()` / `GetEffectiveShadow()` / `GetEffectiveStat()` / `GetDelta()`
- **#44 spec**: `CharacterState(CharacterProfile)` → `ApplyShadowGrowth()` / `GetEffective()` / `DrainGrowthEvents()` / `GetShadowDelta()`
- **⚠️ BLOCKING**: These are two specs for the same responsibility. #43, #45, #51, #56 all reference `SessionShadowTracker`. Only #44 references `CharacterState`. If both are built, GameSession has two competing shadow trackers. See #161.

### #43 Read/Recover/Wait → Fixed DC Roll
- Player → `ReadAsync()` → `RollEngine.ResolveFixedDC(SA, 12)` → success: reveal interest / fail: −1 interest + Overthinking via `SessionShadowTracker`
- Player → `RecoverAsync()` → requires `TrapState.HasActive` → `ResolveFixedDC(SA, 12)` → success: clear trap / fail: −1 interest
- Player → `Wait()` → −1 interest, advance traps
- ✅ Clean flow. No LLM calls needed.
- ⚠️ Minor: Spec doesn't specify whether Read/Recover/Wait append to `_history`. LLM will lack context on next Speak turn.

### #49/#50: Weakness Windows + Tells → LLM Response → Next Turn
- `ILlmAdapter.GetOpponentResponseAsync()` returns `OpponentResponse` → `.WeaknessWindow` / `.Tell` stored in GameSession → next `ResolveTurnAsync`: weakness → `dcAdjustment`, tell → `externalBonus`
- ✅ Types already on OpponentResponse. Flow is clean.

### #51: Horniness → Option Injection
- `SessionShadowTracker.GetEffectiveShadow(Horniness)` + `IGameClock.GetHorninessModifier()` → level → force Rizz options
- ✅ All types exist. Clean flow.

### #56: ConversationRegistry → Multi-Session
- Registry needs continuous read of `GameSession.InterestMeter.Current` for ghost/fizzle checks
- ⚠️ `GameSession` has no public interest accessor (only via `GameStateSnapshot` after turns). Filed as #160.

## Unstated Requirements

- **Read/Recover/Wait history entries**: When player performs a non-Speak action, the LLM needs to know on the next Speak turn. Without history entries, dialogue continuity breaks.
- **Shadow growth event readability**: `ShadowGrowthEvents` strings must be human-readable for UI display (e.g., "Madness +1: Nat 1 on Charm"), not internal identifiers. Both specs do this correctly.
- **Energy system consumers**: #54 builds `IGameClock.ConsumeEnergy()` infrastructure but nothing in this sprint calls it. This is acceptable for prototype — infrastructure is ready for future use.

## Domain Invariants

- `StatBlock` remains immutable — shadow mutation goes exclusively through `SessionShadowTracker` (or whichever class wins the #161 resolution)
- Shadow growth applies AFTER roll resolution — never retroactively changes the current roll
- Interest delta = SuccessScale + RiskTierBonus + momentum + combo bonus (additive, never multiplicative)
- `RollEngine` remains stateless — all context (tells, combos, callbacks) flows through `externalBonus`/`dcAdjustment` params
- Turn counter increments exactly once per player action (Speak, Read, Recover, Wait)
- Tell/weakness window effects are one-turn-only

## Gaps

### ⚠️ BLOCKING
- **#161: `CharacterState` (#44) vs `SessionShadowTracker` (#139)** — Two specs define competing shadow-tracking wrappers. 5 issues reference `SessionShadowTracker`, 1 references `CharacterState`. Must resolve before Wave 2 (#44) implementation. Recommended: keep `SessionShadowTracker`, add `DrainGrowthEvents()` to it, update #44 spec.

### Missing (non-blocking)
- **#162: `previousOpener` constructor param conflicts with `GameSessionConfig` pattern** — #44 adds it as a constructor param while #139 establishes `GameSessionConfig` as the extension point. Should be moved into config.
- **#163: `TurnResult.ShadowGrowthEvents` already exists** — #44 spec treats it as new. Already present in codebase. Spec should note "populate existing field" not "add field."
- **#160: GameSession public interest accessor** — ConversationRegistry (#56) needs it. Trivial to add during implementation.

### Could Defer
- **#56 (ConversationRegistry)**: Deepest dependency chain, most complex. Core RPG mechanics (#43–#51) work without it. Deferring reduces risk. However, spec is well-defined and self-contained — proceeding is acceptable.
- **#55 (PlayerResponseDelay)**: Pure function, no consumer in this sprint unless #56 ships.

### Assumptions to Validate
- #44 "same opener twice in a row" detection requires cross-session memory — host-owned persistence, not engine concern. Correctly scoped.
- #48 XP DC tier thresholds (≤13/14-17/≥18) are derived, not explicitly in rules — PO should confirm.
- Energy system (#54) has no consumers this sprint (#144 still open).

## Wave Plan

```
Wave 0: #139, #38
Wave 1: #54, #43, #46, #47, #49, #50
Wave 2: #44, #55
Wave 3: #45, #48
Wave 4: #51
Wave 5: #56
```

**Rationale:**
- Wave 0: #139 (all features depend on it) + #38 (QA, independent)
- Wave 1: All depend only on #139. Six parallel issues.
- Wave 2: #44 depends on #43. #55 depends on #54.
- Wave 3: #45 depends on #44. #48 depends on #43 + #44.
- Wave 4: #51 depends on #45 + #54.
- Wave 5: #56 depends on #54 + #44. Most complex, most dependencies.

## Role Assignment Check

All 14 issues have correct role assignments:
- 13 backend-engineer issues (pure C# engine work) ✅
- 1 qa-engineer issue (#38 QA audit) ✅

## Recommendations

1. **BLOCKING: Resolve #161 before Wave 2** — Decide `SessionShadowTracker` vs `CharacterState`. Recommended: keep `SessionShadowTracker`, add growth event tracking to it, update #44 spec.
2. **Move `previousOpener` into `GameSessionConfig`** (#162) — Maintain the single extension pattern established by #139.
3. **Update #44 spec for TurnResult** (#163) — Note that `ShadowGrowthEvents` already exists; spec should say "populate" not "add."
4. **Close stale VCs** (#148) — ~12 open vision concerns are now addressed by #139. Closing reduces noise for implementers.
5. **Defer `AddExternalBonus()` removal** to cleanup sprint — it's deprecated (#146) but removing it mid-implementation risks breaking things.

## VERDICT: ADVISORY

Sprint direction is correct. Three new vision concerns filed:
- **#161** (BLOCKING for Wave 2): `CharacterState` vs `SessionShadowTracker` conflict
- **#162** (advisory): `previousOpener` should use `GameSessionConfig` pattern
- **#163** (advisory): `TurnResult.ShadowGrowthEvents` already exists in codebase

#161 must be resolved before #44 implementation begins (Wave 2). Waves 0 and 1 can proceed immediately. The sprint's core direction — implementing all remaining RPG mechanical systems — is the right priority for prototype maturity.
