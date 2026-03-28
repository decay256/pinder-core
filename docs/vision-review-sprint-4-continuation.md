# Vision Review — Sprint 4: RPG Rules Complete — Continuation

## Alignment: ⚠️

This sprint continues the right strategic direction — completing the RPG mechanical systems that make Pinder a real game (shadows, combos, XP, async-time). However, **the sprint's foundation is unstable**: 6 of 14 issues (#43, #46, #47, #49, #50, #38) are marked CLOSED but only have spec-writing PRs merged — no implementation code exists. Additionally, #52 has approved code and test PRs (#122, #123) sitting unmerged. The remaining 7 open feature issues depend on architectural prerequisites (#58 shadow mutability, #85 RollEngine externalBonus, #65 fixed-DC overload) that are still unresolved after 3 sprint cycles. The sprint cannot succeed without first: (a) reopening or replacing the 6 closed-but-unimplemented issues, and (b) shipping Wave 0 prerequisite infrastructure.

## Data Flow Traces

### #44: Shadow Growth Events → StatBlock Mutation
- `GameSession.ResolveTurnAsync()` → detect growth trigger (Nat1 on Charm, 3+ TropeTraps, etc.) → increment shadow stat
- **⚠️ BLOCKING**: `StatBlock._shadow` is `private readonly Dictionary`. No `IncrementShadow()` or `SetShadow()` method. No `SessionShadowTracker` exists. See #58, #82, #128.
- Required: mutable shadow tracking + GameSession constructor accepting shadow tracker

### #45: Shadow Thresholds → Gameplay Effects
- `StatBlock.GetShadow(shadow)` → `ShadowThresholdEvaluator.GetThresholdLevel()` → 0/1/2/3 → disadvantage flags, option suppression, starting interest override
- ⚠️ `InterestMeter` starting value is hardcoded to 10 (`StartingValue = 10`). Dread ≥18 needs starting interest 8. See #79.
- Required: `InterestMeter` constructor accepting custom starting value, or `SessionShadowTracker` that adjusts it pre-session

### #48: XP Sources → XpLedger
- `GameSession.ResolveTurnAsync()` → determine XP from roll outcome → `XpLedger.Record(source, amount)` → `TurnResult.XpEarned`
- Required fields: roll success/fail, DC tier (≤13/14-17/≥18), IsNatOne, IsNatTwenty, trap recovery event, game outcome
- ✅ All source data exists in `RollResult`. Clean data flow.

### #54: GameClock → Time-of-Day Effects
- `GameClock.Now` → `GetTimeOfDay()` → `GetHorninessModifier()` → fed to horniness system (#51)
- `GameClock.Advance()` / `AdvanceTo()` → midnight crossing → `ReplenishEnergy()`
- ⚠️ No `IGameClock` interface exists — concrete `GameClock` blocks deterministic testing. See #67.

### #55: PlayerResponseDelay → Interest Penalty
- `TimeSpan delay` → `PlayerResponseDelayEvaluator.Evaluate(delay, opponentStats, interest)` → `DelayPenalty`
- ⚠️ Input source unclear: wall-clock (real-time) vs game-clock (simulated). See #81. For pure engine, this should be game-clock delta, not real time.

### #56: ConversationRegistry → Multi-Session Orchestration
- `ConversationRegistry.FastForward()` → find earliest pending → `IGameClock.AdvanceTo()` → ghost/fizzle checks → interest decay → return active session
- ⚠️ `GameSession` has no public accessor for `InterestMeter.Current` outside of `GameStateSnapshot` (only available after turns). Registry needs continuous read access.
- ⚠️ `ApplyCrossChatEvent()` needs to modify shadows across sessions — requires shadow tracker on every session.
- Deepest dependency chain: depends on #54 (GameClock), #44 (shadow growth), #53 (timing, merged).

## Unstated Requirements

- **Prerequisite infrastructure PR**: Just as #63 (ILlmAdapter expansion) and #78 (TurnResult expansion) were created as prerequisites for the previous sprint wave, this continuation needs equivalent prerequisite PRs for `SessionShadowTracker`, `IGameClock`, `RollEngine.externalBonus`, and `TrapState.HasActive`.
- **Implementation of closed-but-unimplemented issues**: The specs for #43, #46, #47, #49, #50, #38 are done but the code isn't. Agents assigned to #44 (depends on #43) will fail immediately.
- **Shadow persistence story**: Shadow growth within a session (#44) is meaningless if shadows reset between sessions. `ConversationRegistry` (#56) propagates cross-chat shadow events, but there's no serialization story. At prototype maturity this can be deferred, but should be on the roadmap.

## Domain Invariants

- `StatBlock` must remain immutable for roll resolution — shadow mutation must happen via a separate tracker that doesn't modify the StatBlock used during `RollEngine.Resolve()`
- `RollResult.Total` must include ALL bonuses (stat + level + external) — `IsSuccess` and `MissMargin` are derived from Total and must be consistent
- Interest delta composition must be additive: `SuccessScale + RiskTierBonus + Momentum + ComboBonus + CallbackBonus` — never multiplicative
- `GameSession` turn sequencing (StartTurn → Resolve alternation) must hold for Speak, Read, Recover, and Wait actions
- `GameClock` must be injectable (via `IGameClock`) for deterministic testing
- Energy is per in-game day, not per session or per turn

## Gaps

### Missing (Critical — filed as #126, #127, #128, #129, #130)
- **#126**: 6 issues closed with only spec PRs — no implementation code exists
- **#127**: #52 has approved PRs sitting unmerged
- **#128**: Shadow mutation architecture still unresolved after 3 sprints
- **#129**: `RollResult.Total` excludes external bonuses — data flow integrity gap
- **#130**: No Wave 0 prerequisites defined for this continuation sprint

### Still Open from Previous Reviews
- **#58**: StatBlock immutability (3 sprints old)
- **#64**: `TrapState.HasActive` missing
- **#65**: `RollEngine.Resolve` fixed-DC overload missing
- **#67**: `IGameClock` interface missing
- **#79**: `InterestMeter` starting value not configurable
- **#82**: GameSession constructor lacks injection points
- **#85**: `RollResult.Total` excludes externalBonus
- **#87**: GameSession god-object trajectory (tech debt, not blocking)

### Could Defer
- **#56 (ConversationRegistry)**: Most complex issue, deepest dependency chain. Self-contained multi-session subsystem. Deferring to a dedicated sprint reduces risk significantly.
- **#55 (PlayerResponseDelay)**: Depends on #54 (GameClock). Pure function, no dependencies on it. Low urgency.

### Unnecessary
- Nothing — all remaining issues are justified for a complete RPG rules implementation.

## Role Assignment Check

All roles are correctly assigned:
- #52, #54, #43, #46, #47, #49, #50, #44, #48, #55, #45, #51, #56: backend-engineer ✅ (pure C# engine)
- #38: qa-engineer ✅ (test audit)

No corrections needed.

## Wave Plan

**Wave 0 (Prerequisites — MUST ship first):**
- New prerequisite issue: `SessionShadowTracker` + `IGameClock` + `GameSessionConfig` + `RollEngine.externalBonus` + `TrapState.HasActive` + `RollEngine.ResolveFixedDC`
- Merge #122 + #123 (approved #52 implementation)

**Wave 1 (No internal dependencies — closed issues need reopening):**
- #38 (QA audit — reopen or new issue)
- #43 (Read/Recover/Wait — reopen or new issue; needs prereq for fixed-DC + shadow mutation)
- #49 (Weakness windows — reopen or new issue)
- #50 (Tells — reopen or new issue)
- #54 (GameClock)

**Wave 2 (Depends on Wave 1):**
- #46 (Combos — reopen or new issue; depends on working GameSession actions)
- #47 (Callbacks — reopen or new issue)
- #55 (PlayerResponseDelay — depends on #54)
- #44 (Shadow growth — depends on #43 + shadow tracker prereq)

**Wave 3 (Depends on Wave 2):**
- #48 (XP tracking — depends on #43, #44)
- #45 (Shadow thresholds — depends on #44)

**Wave 4 (Depends on Wave 3):**
- #51 (Horniness-forced Rizz — depends on #45)

**Wave 5 (Depends on Waves 2-4):**
- #56 (ConversationRegistry — depends on #54, #44; consider deferring)

## Recommendations

1. **Merge #52's approved PRs (#122, #123) immediately** — this is done work sitting idle.
2. **Create a Wave 0 prerequisite issue** combining: `SessionShadowTracker`, `IGameClock` interface, `GameSessionConfig`, `RollEngine.Resolve` with `externalBonus` + fixed-DC overload, `TrapState.HasActive` property. This is the same pattern that worked for #63 and #78.
3. **Reopen or replace #43, #46, #47, #49, #50, #38** — these have specs but no code. The orchestrator must not treat them as done.
4. **Consider deferring #56 (ConversationRegistry) and #55 (PlayerResponseDelay)** to a dedicated async-time sprint. They're self-contained and depend on the deepest chain. Removing them reduces this sprint from 14 to 12 issues, which is still large but more manageable.
5. **Resolve #58 once and for all** — recommend `SessionShadowTracker` wrapper (option b from #58). This has been the top concern for 3 consecutive vision reviews.

## VERDICT: ADVISORY

The sprint direction is correct and issues are individually well-specified. Five new vision concerns filed (#126–#130). The most critical finding is **#126: six "closed" issues have no implementation code** — the sprint plan assumes they're done when they aren't. The **shadow mutation architecture (#58/#128)** remains the longest-standing unresolved concern. Both are addressable: reopen the issues and ship a Wave 0 prerequisite PR. No single concern is sprint-blocking, but the sprint will fail if these are not addressed before agents start implementing.
