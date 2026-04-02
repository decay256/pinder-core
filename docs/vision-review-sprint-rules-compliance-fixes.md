# Vision Review — Rules Compliance Fixes Sprint

## Alignment: ✅ Strong

This sprint is exactly the right work at the right time. After 10 sprints building the RPG engine and LLM adapter layer, the game rules have drifted from the spec (rules-v3.4). Fixing FailureScale values, trap activation gaps, momentum mechanics, horniness initialization, shadow disadvantage coverage, missing shadow reductions, Nat 20 advantage, Denial growth, and Madness T3 are all **correctness fixes** — the engine's output must match the designed game. This is high-leverage: every future sprint builds on correct mechanics. No gold-plating detected.

## Data Flow Traces

### #265 — traps.json creation → JsonTrapRepository → TrapState → RollEngine
- Data file created at `data/traps/traps.json`
- `JsonTrapRepository(json)` → `ParseTrap()` reads: `id`, `stat`, `effect`, `effect_value`, `duration_turns`, `llm_instruction`, `clear_method`, `nat1_bonus`
- `RollEngine.Resolve()` → `trapRegistry.GetTrap(stat)` → `attackerTraps.Activate(trap)`
- ⚠️ **SCHEMA MISMATCH**: Issue #265 body specifies nested JSON (`triggered_by_stat`, `mechanical_effect.type`, `prompt_taint.llm_instruction`) — `JsonTrapRepository` expects flat fields (`stat`, `effect`, `llm_instruction`). Filed as #274.

### #268 — Momentum: interest delta → roll bonus
- Current: `interestDelta += GetMomentumBonus(streak)` — momentum bypasses the roll
- Correct: momentum → `externalBonus` → `RollEngine.Resolve(externalBonus)` → affects `FinalTotal` → changes success/failure tier
- Required fields: `_momentumStreak`, `_pendingMomentumBonus`, `externalBonus` param
- ⚠️ Issue suggests `AddExternalBonus()` (deprecated). Should use `Resolve(externalBonus)` param per ADR #146. Filed as #276.

### #269 — Horniness initialization without clock
- Current: `if (_clock != null) { roll + todModifier }` — no clock = horniness 0
- Correct: always `_dice.Roll(10)`, add `_clock?.GetHorninessModifier() ?? 0`
- Required fields: `_sessionHorniness`, `_dice`, `_clock` (nullable)
- No missing fields — straightforward fix.

### #267 — Catastrophe trap activation
- `RollEngine.ResolveFromComponents()` → miss ≥10 → `tier = Catastrophe` → **no trap activation**
- Fix adds `attackerTraps.Activate(trapRegistry.GetTrap(stat))` in Catastrophe branch
- ⚠️ Legendary (Nat 1) also skips trap activation — same gap. Filed as #275.

### #260 — Read/Recover shadow disadvantage
- `ReadAsync()`/`RecoverAsync()` → `hasDisadvantage = _interest.GrantsDisadvantage` only
- Missing: `|| _shadowDisadvantagedStats?.Contains(StatType.SelfAwareness)` check
- `_shadowDisadvantagedStats` is computed in `StartTurnAsync()` but Read/Recover don't call it
- ⚠️ Fix must also recompute `_shadowDisadvantagedStats` in Read/Recover if `StartTurnAsync()` hasn't been called yet.

## Unstated Requirements

- **#270 shadow reductions must use the same `ApplyGrowth` API with negative deltas** — `SessionShadowTracker.ApplyGrowth(shadow, -1, reason)` is the method. No new API needed but implementer must verify negative deltas are supported (they are — checked code).
- **#273 Madness T3 "unhinged text" needs LLM-side handling** — marking `IsUnhingedReplacement = true` on a `DialogueOption` only matters if the LLM adapter reads it. The `NullLlmAdapter` won't care, but `AnthropicLlmAdapter` prompt templates must eventually include Madness T3 instructions. This is acceptable for prototype — flag the option, wire LLM later.
- **#271 Nat 20 advantage should NOT stack with interest-based advantage** — both sources produce the same effect (roll twice, take higher). D20 systems don't "double advantage." The issue says "stacks correctly" — this means OR logic, not addition. Implementer should verify.

## Domain Invariants

- **Failure scale deltas must match rules-v3.4 §5 exactly** — these are balance constants, not approximations
- **Trap activation must occur on TropeTrap, Catastrophe, AND Legendary tiers** — worst tiers include all lesser effects
- **Momentum is a roll modifier, not an interest modifier** — it affects WHETHER you succeed, not how much you gain
- **Shadow disadvantage applies to ALL SA rolls** — Speak, Read, and Recover use the same stat; disadvantage must apply uniformly
- **Horniness exists in every session** — the 1d10 roll is session-intrinsic; only the time-of-day modifier requires a clock
- **Shadow reductions are the counterbalance to shadow growth** — without them, shadows only increase, creating an inevitable death spiral

## Gaps

### Missing from sprint
- **Legendary trap activation**: #267 only specifies Catastrophe. Legendary (Nat 1) has the same gap. Filed #275.
- **`_shadowDisadvantagedStats` recomputation in Read/Recover**: #260's fix needs to handle the case where `StartTurnAsync()` was never called (Read/Recover are self-contained actions). The field might be null or stale.

### Unnecessary
- Nothing identified. All 10 issues are justified rules-compliance fixes.

### Assumptions to validate
- **#265 JSON schema**: The issue body's JSON will NOT parse with existing `JsonTrapRepository`. Implementer must use the flat schema. Filed #274.
- **#268 `AddExternalBonus()` vs `externalBonus` param**: Issue suggests deprecated path. Filed #276.
- **#273 `DialogueOption` DTO change**: Adding `IsUnhingedReplacement` is a public API change. Existing callers (tests, LLM adapters) must compile with the new optional param.

## Wave Plan

**Wave 1** (independent, different files/areas):
- #265 — data file creation (data/traps/)
- #266 — FailureScale.cs (Rolls/)
- #267 — RollEngine.cs (Rolls/)
- #269 — GameSession constructor

**Wave 2** (GameSession method changes, builds on Wave 1):
- #268 — ResolveTurnAsync momentum refactor
- #271 — StartTurnAsync/ResolveTurnAsync Nat 20 advantage
- #260 — ReadAsync/RecoverAsync shadow disadvantage

**Wave 3** (shadow mechanics, builds on Wave 2 patterns):
- #270 — Shadow reductions across multiple methods
- #272 — Denial growth in ResolveTurnAsync
- #273 — Madness T3 in StartTurnAsync + DTO change

## Recommendations

1. **BLOCKING: Fix #265 JSON schema before implementation** — the issue body's JSON format will cause `FormatException`. Either update the issue body with correct flat schema, or ensure the implementer knows to use `JsonTrapRepository`'s expected format. (#274)
2. **Add Legendary trap activation to #267 scope** — or address via #275 in the same wave. A Nat 1 that doesn't activate a trap while a miss-by-10 does is counter-intuitive and rules-incorrect.
3. **#268 implementer must use `Resolve(externalBonus)` param, not `AddExternalBonus()`** — per ADR #146. (#276)
4. **Strict wave sequencing per #277** — 8 issues touch GameSession.cs. Merge conflicts are inevitable without sequencing.
5. **#260 must recompute shadow thresholds inline** — Read/Recover don't call `StartTurnAsync()`, so `_shadowDisadvantagedStats` may be null. The fix must compute it fresh.

## Verdict: ADVISORY

Four concerns filed (#274, #275, #276, #277). None are sprint-blocking — they're implementation guidance that prevents the most likely failure modes. The sprint scope is correct and high-priority. Proceed with wave sequencing.
