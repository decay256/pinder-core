# Vision Review — Sprint 12 Architecture Strategic Alignment

## Alignment: ✅ Strong

This sprint is correctly prioritized rules-compliance work at prototype maturity. The architect's output is thorough, well-sourced against actual code, and proposes no structural changes — which is exactly right for a bug-fix/gap-filling sprint. The wave ordering respects real data dependencies (particularly #307 → #308 → #310 shadow chain). No over-engineering detected.

## Evaluation of Architect's Output

### 1. Maturity Fit: ✅ Appropriate for Prototype

The architecture makes zero structural changes. All 10 issues are surgical fixes within existing module boundaries. The contracts include exact line numbers and code snippets verified against the actual source. This is the right level of specificity for prototype — no premature abstraction, no new interfaces, no new projects.

**One positive note**: The architect correctly identified that `data/traps/traps.json` already has the correct flat schema matching the parser — avoiding unnecessary rework. The contract correctly recommends verifying with a test rather than rewriting.

### 2. Coupling Assessment: ✅ No New Coupling

- All Rolls changes are internal to the Rolls module (FinalTotal usage)
- GameSession changes are orchestration-level (wiring existing data to existing context DTOs)
- LlmAdapters changes are prompt content only (tell categories in PromptTemplates)
- The shadow raw-value change (#307) is a data representation change, not a coupling change — the `Dictionary<ShadowStatType, int>` type stays the same

No cross-boundary violations introduced. The strictly one-way dependency `LlmAdapters → Core` is preserved.

### 3. Abstraction Reversibility: ✅ No Painful Lock-ins

- **Lukewarm enum insertion (#313)**: Shifts ordinal values, but at prototype maturity this is acceptable. No persistence layer exists. If the interest state model changes at MVP, the enum can be freely restructured.
- **Raw shadow values (#307)**: Moving from tier (0-3) to raw (0-30+) is actually a relaxation of abstraction — more data flows through, giving downstream consumers more flexibility. This is the right direction and won't be painful to extend.
- **XP multiplier (#314)**: Simple float multiplication with `Math.Round`. Easy to swap to a lookup table or configuration-driven approach at MVP.

### 4. Interface Design: ✅ User-Facing Boundaries Correct

- `DialogueOption.IsUnhingedReplacement` (#310) is a clean marker for the host/LLM to act on — it doesn't leak implementation details about shadow thresholds
- Context DTOs (`DeliveryContext`, `OpponentContext`) carry `shadowThresholds` as opaque dictionaries — consumers interpret, they don't modify
- `InterestState.Lukewarm` is a user-facing concept from the rules — correct to model as a first-class enum value

## Data Flow Traces

### Shadow Taint Pipeline (#307 → #308)
- `SessionShadowTracker.GetEffectiveShadow(shadow)` → raw int (0-30+)
- → stored in `shadowThresholds` dict (currently stores tier via `GetThresholdLevel()` — **bug**)
- → `DialogueContext.ShadowThresholds` → `SessionDocumentBuilder.BuildShadowTaintBlock()` checks `> 5`
- ⚠️ **Confirmed BUG**: Tier max is 3, threshold check is `> 5` → taint never fires. Architect's fix is correct.
- After #307: raw values flow → builder fires correctly ✅
- After #308: raw values also reach DeliveryContext + OpponentContext ✅
- **Side effect correctly documented**: Fixation T3 (line 322) and Denial T3 (line 336) checks must change from `>= 3` to `>= 18`

### External Bonus → Outcome Quality (#309)
- `externalBonus` → `RollEngine.ResolveFromComponents()` → `finalTotal = total + externalBonus` (line 156)
- → `IsSuccess = finalTotal >= dc` ✅
- → `miss = dc - total` (line 169) — **ignores bonus** ❌
- → `SuccessScale.GetInterestDelta()` uses `result.Total` (line 25) — **ignores bonus** ❌
- → `beatDcBy` in GameSession uses `result.Total` — **ignores bonus** ❌
- Architect's three-point fix is correct and complete

### Triple Combo in Read/Recover (#312)
- `_comboTracker.ConsumeTripleBonus()` returns int → currently **discarded** (lines 971, 1083)
- Architect correctly specifies: pass as `externalBonus` parameter to `ResolveFixedDC`
- `ResolveFixedDC` already accepts `externalBonus` parameter (verified) ✅
- Vision concern #315 correctly prevents use of deprecated `AddExternalBonus()`

## Domain Invariants Verified

- ✅ External bonuses affect ALL downstream calculations uniformly (after #309)
- ✅ Shadow taint reaches ALL LLM prompt paths (after #307 + #308)
- ✅ Interest state enum matches rules §6 exactly (after #313)
- ✅ Deprecated APIs not introduced in new code (#315 guards #312)
- ✅ Backward compatibility maintained when externalBonus=0

## Gaps

### Missing: Nothing Critical
The sprint scope comprehensively addresses the identified rules gaps. All 10 issues trace back to specific rules sections.

### Unnecessary: Nothing
All issues fix real bugs or implement missing rules. No gold-plating.

### Assumptions to Validate
- **#307 side effect coverage**: The Dread T3 check at line 118 goes through `ShadowThresholdEvaluator.GetThresholdLevel()` first, so it's NOT affected by the tier→raw change. Only lines 321-322 and 335-336 need updating. Architect correctly identified this.
- **#313 test impact**: Tests asserting `InterestState.Interested` for values 5-9 will break and need updating. This is intentional, not accidental breakage.
- **#306 may already be fixed**: traps.json already has the correct flat schema. The implementer should write a verification test rather than blindly rewriting.

## Unstated Requirements

- **Madness T3 unhinged option needs LLM prompt guidance**: #310 marks the option with `IsUnhingedReplacement = true`, but the LLM adapter needs to know what "unhinged" means for this character. The existing `PromptTemplates` or `SessionDocumentBuilder` may need a future addition to instruct the LLM on unhinged tone. This is acceptable as a follow-up — the marker is the right first step.

## Requirements Compliance Check

No `REQUIREMENTS.md` exists. Checked against architecture doc constraints:
- ✅ netstandard2.0 + C# 8.0 — no violations
- ✅ Zero NuGet dependencies in Pinder.Core
- ✅ Backward-compatible optional parameters
- ✅ ADR #146 respected (deprecated API not used in new code, per #315)
- ✅ Existing 1718 tests expected to pass (with intentional updates for Lukewarm and raw shadow values)

## Recommendations

1. **Proceed as specified** — the wave plan, contracts, and dependency ordering are sound
2. **#312 implementer must read #315** — this is the only architectural concern and is already documented in the contract
3. **#307 implementer should add a regression test** — specifically for Fixation T3 and Denial T3 triggering with raw values ≥ 18, to prevent the "taint never fires" class of bug from recurring

**VERDICT: CLEAN**
