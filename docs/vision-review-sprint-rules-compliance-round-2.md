# Vision Review — Rules Compliance Round 2

## Alignment: ✅ Strong

This sprint is pure rules-correctness work — fixing bugs and filling gaps between the engine implementation and rules-v3.4. Every issue either fixes a broken mechanic (traps.json schema crash, shadow taint never firing, external bonuses not affecting outcome quality) or implements a missing rule (Lukewarm state, XP risk multiplier, Madness T3, tell categories). This is exactly the right work at prototype maturity: get the rules engine correct before building higher-level features on top of it.

## Data Flow Traces

### Shadow Taint (#307 → #308)
- GameSession computes shadow values → `ShadowThresholdEvaluator.GetThresholdLevel()` → stores **tier** (0-3) in `shadowThresholds` dict → passes to `DialogueContext` → `SessionDocumentBuilder.BuildShadowTaintBlock()` checks `value > 5` → **always false** since tier max is 3
- Required fields: raw shadow values (0-20+) flowing through to builder
- ⚠️ **BUG**: GameSession stores tier (0-3) instead of raw value. Builder designed for raw values. Taint has never fired.
- After #307 fix: raw values flow to DialogueContext ✅
- After #308 fix: raw values also flow to DeliveryContext + OpponentContext ✅
- Data flow becomes: `SessionShadowTracker.GetEffectiveShadow()` → raw int → `shadowThresholds` dict → all three context DTOs → builder checks `> 5/6/etc.` → taint block injected

### External Bonus → Outcome Quality (#309)
- Player has callback/tell/momentum/triple bonus → `externalBonus` int → `RollEngine.ResolveFromComponents()` → `finalTotal = total + externalBonus` → success/fail check uses `finalTotal` ✅ → **BUT** failure tier uses `dc - total` (ignores bonus) ❌ → SuccessScale uses `result.Total` (ignores bonus) ❌ → `beatDcBy` in GameSession uses `result.Total` ❌
- After #309 fix: all three downstream calculations use FinalTotal ✅

### Triple Combo in Read/Recover (#312)
- Player has Triple combo active → `ReadAsync()` calls `_comboTracker.ConsumeTripleBonus()` → gets `tripleBonus` int → **currently discarded** ❌
- After fix: `tripleBonus` → `RollEngine.ResolveFixedDC(externalBonus: tripleBonus)` → affects roll outcome
- ⚠️ **CONCERN**: Issue #312 suggests using deprecated `AddExternalBonus()` — see concern #315

### Trap Loading (#306)
- `data/traps/traps.json` → `JsonTrapRepository.Parse()` → expects flat fields (`stat`, `effect`, `effect_value`) → JSON has nested structure (`triggered_by_stat`, `mechanical_effect.type`) → **FormatException on load**
- After fix: flat schema matches parser → 6 traps load correctly

## Unstated Requirements

- **Lukewarm state should affect LLM narrative tone** — `SessionDocumentBuilder` already uses raw interest values for display labels (and already shows "Lukewarm 🤔" for 5-9), but consumers of the `InterestState` enum may want to distinguish Lukewarm from Interested for narrative framing
- **XP risk multiplier should be visible in TurnResult** — if the UI shows "3x XP" for Bold, the actual XP earned should match; players will notice if Bold success gives the same XP as Safe
- **Tell categories must be stable across model versions** — providing the explicit mapping table (#311) makes tell generation deterministic regardless of which Claude model is used

## Domain Invariants

- **External bonuses must affect ALL downstream calculations uniformly** — success/fail, failure tier severity, success scale tier, and beatDcBy must all use the same total (FinalTotal)
- **Shadow taint must reach ALL LLM prompt paths** — dialogue options, delivery, and opponent response prompts must all receive shadow state
- **Deprecated APIs must not appear in new code** — `AddExternalBonus()` is deprecated per ADR #146; new code uses `externalBonus` parameter
- **Interest state enum must exactly match rules §6** — 7 states including Lukewarm
- **Trap data must round-trip through parse without loss** — JSON schema matches parser expectations exactly

## Gaps

### Missing: Nothing critical beyond filed concern
The sprint scope is comprehensive for a rules-compliance pass.

### Unnecessary: Nothing
All 9 issues address real bugs or missing rules. No gold-plating.

### Assumptions to validate:
- **#309 backward compatibility**: Changing failure tier to use FinalTotal will change existing test expectations (the code has an explicit "backward compatibility" comment). The implementer must update affected tests — this is intentional and correct, but should be called out.
- **#313 enum addition**: Adding `Lukewarm` to `InterestState` will break any exhaustive switch/if-else chains on the enum. `SessionDocumentBuilder.GetInterestBehaviourBlock()` and any test assertions on `InterestState.Interested` for values 5-9 will need updating.
- **#314 rounding**: `(int)Math.Round(7.5)` rounds to 8 (banker's rounding in .NET). The issue uses `Math.Round` — confirm this matches design intent for edge cases like 5 × 1.5 = 7.5 → 8 XP.

## Requirements Compliance Check

No `REQUIREMENTS.md` file exists in the repo. Cannot perform formal FR/NFR/DC compliance check. All changes are consistent with the architecture doc's constraints:
- ✅ netstandard2.0 + C# 8.0
- ✅ Zero NuGet dependencies for Pinder.Core changes
- ✅ Backward-compatible (optional params with defaults)
- ⚠️ #312's suggested fix uses deprecated `AddExternalBonus()` — violates ADR #146 (filed as #315)

## Vision Concerns Filed

| # | Concern | Severity |
|---|---------|----------|
| #315 | #312 must use `externalBonus` parameter, not deprecated `AddExternalBonus()` | Medium — architectural consistency + correctness after #309 |

## Wave Plan

Based on dependency analysis and GameSession conflict avoidance:

**Wave 1:** #306, #311, #313
- #306: traps.json schema fix (Data layer, independent)
- #311: Tell categories in prompt template (LlmAdapters, independent)
- #313: Lukewarm InterestState (Conversation/InterestMeter + enum, independent)

**Wave 2:** #307, #309, #314
- #307: Shadow taint tier→raw fix (GameSession shadow computation)
- #309: FinalTotal in SuccessScale + RollEngine + GameSession beatDcBy (Rolls + GameSession)
- #314: XP risk-tier multiplier (GameSession RecordRollXp)

**Wave 3:** #308, #310, #312
- #308: Wire shadowThresholds to DeliveryContext/OpponentContext (depends on #307)
- #310: Madness T3 unhinged option (GameSession StartTurnAsync)
- #312: Triple combo in Read/Recover (GameSession ReadAsync/RecoverAsync — must use externalBonus param per #315)

## Recommendations

1. **Ensure #312 implementer reads concern #315** — the suggested fix in the issue body uses the deprecated API and will produce incorrect failure tiers after #309 lands
2. **Update #309 to note that existing tests will change** — the "backward compatibility" comment in RollEngine is intentionally being overridden; test expectations for miss margins with external bonuses will shift
3. **#313 implementer should grep for `InterestState.Interested`** across both projects to find all sites that need updating for the new Lukewarm range

## Verdict: ADVISORY

One concern filed (#315). The sprint is well-scoped and correctly prioritized. The concern is implementation guidance — not a structural issue. Sprint should proceed with the concern added for the #312 implementer to follow.
