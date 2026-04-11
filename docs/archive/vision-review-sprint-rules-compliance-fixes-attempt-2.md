# Vision Review — Rules Compliance Fixes (Attempt 2)

## Alignment: ✅ Strong

This sprint remains well-aligned. All 10 issues are correctness fixes bringing the engine in line with rules-v3.4. The 4 vision concerns from attempt 1 (#274, #275, #276, #277) are well-specified with clear, actionable acceptance criteria. Two new gaps were identified in this pass.

## Step 1: Review of Existing Vision Concerns

### #274 — traps.json schema mismatch ✅ Well-specified
- Clear: lists exact field mapping between issue schema and parser expectations
- AC verifiable: round-trip parse test, 6 TrapDefinitions with correct stat/effect
- No changes needed

### #275 — Legendary (Nat 1) should also activate trap ✅ Well-specified
- Correctly identifies Nat 1 skipping trap activation while Catastrophe gets it
- Code snippet is accurate to the codebase (verified: lines 158-161 of RollEngine.cs)
- AC covers all 3 trap-activating tiers
- No changes needed

### #276 — Momentum via externalBonus param, not AddExternalBonus ✅ Well-specified
- ADR reference is correct (#146)
- AC covers the canonical path, combined computation, and reporting
- No changes needed

### #277 — Wave sequencing for GameSession conflicts ✅ Well-specified
- Correct method-level conflict analysis
- Wave groupings are sound
- AC is process-level (wave plan), appropriate for this concern type
- No changes needed

## Step 2: New Gaps Found

### Gap 1: #270 states `ApplyGrowth` accepts negative deltas — it doesn't (filed #279)
`SessionShadowTracker.ApplyGrowth()` throws `ArgumentOutOfRangeException` on `amount <= 0`. Shadow reductions must use `ApplyOffset(shadow, -1, reason)`. The existing Fixation −1 reduction (trigger 13, line 846) already correctly uses `ApplyOffset`. Issue #270's note would mislead the implementer into calling `ApplyGrowth(Dread, -1, ...)` which crashes at runtime.

### Gap 2: #271 Nat 20 advantage doesn't cover Read/Recover paths (filed #280)
Issue #271 only wires `_pendingCritAdvantage` in the Speak path (set in `ResolveTurnAsync`, consumed in `StartTurnAsync`). Read/Recover are self-contained — they don't call `StartTurnAsync()`. Two problems:
1. Speak Nat 20 → Read next → advantage not applied (never consumed)
2. Read Nat 20 → Speak next → advantage not set (never recorded)

Both Read and Recover use `ResolveFixedDC` which supports advantage and can produce Nat 20s.

## Data Flow Traces (unchanged from attempt 1)

All traces from attempt 1 remain valid. No new data flow issues identified.

## Domain Invariants (refined)

- **Failure scale deltas must match rules-v3.4 §5 exactly** — balance constants
- **Trap activation on TropeTrap, Catastrophe, AND Legendary** — worst tiers include all lesser effects
- **Momentum is a roll modifier** — flows through `RollEngine.Resolve(externalBonus)`, not interest delta
- **Shadow disadvantage applies to ALL SA rolls** — Speak, Read, Recover uniformly
- **Shadow reductions use `ApplyOffset`, not `ApplyGrowth`** — the API distinction is enforced at runtime
- **Nat 20 advantage applies to the next roll regardless of action type** — Speak, Read, Recover all participate in the crit-advantage chain

## Full Concern Summary

| # | Concern | Status | Severity |
|---|---------|--------|----------|
| #274 | traps.json schema mismatch | ✅ Well-specified | High — blocks #265 |
| #275 | Legendary trap activation | ✅ Well-specified | High — rules inconsistency |
| #276 | Momentum via externalBonus param | ✅ Well-specified | Medium — architectural consistency |
| #277 | Wave sequencing | ✅ Well-specified | Medium — process risk |
| #279 | ApplyGrowth vs ApplyOffset for reductions | ✅ NEW — well-specified | High — runtime crash |
| #280 | Nat 20 advantage in Read/Recover | ✅ NEW — well-specified | Medium — mechanic gap |

## Verdict: CLEAN

All 6 vision concerns are now well-specified with clear, actionable acceptance criteria. The sprint scope is correct, high-priority, and complete. No blocking issues remain — all concerns are implementation guidance that the backend-engineer can address during development.

**Recommendations:**
1. Address #279 (ApplyOffset) as part of #270 implementation — it's a one-word fix in the method call
2. Address #280 (Read/Recover crit advantage) as part of #271 implementation — add ~6 lines to each of ReadAsync/RecoverAsync
3. Maintain wave sequencing per #277 to minimize merge conflicts
4. Ensure #274's schema guidance reaches the #265 implementer before they write the JSON file
