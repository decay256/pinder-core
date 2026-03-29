# Vision Review — Sprint 7: Architecture Strategic Alignment

## Alignment: ✅

The architect's output is **well-aligned** with the product vision. Sprint 7 correctly consolidates all remaining RPG mechanical systems into a single coherent sprint, and the architecture follows the established patterns (stateless engine, interface injection, fragment assembly). The Wave 0 infrastructure (#139) is the correct lynchpin — `SessionShadowTracker`, `IGameClock`, `RollEngine` extensions, and `GameSessionConfig` are genuine prerequisites that unblock everything else. The 14 contracts are specific, backward-compatible, and traceable to rules sections.

---

## Maturity Fit Assessment

**Verdict: Appropriate for prototype maturity.**

The architecture makes the right tradeoffs:
- **Sealed classes** over complex hierarchies — correct for prototype
- **Optional config** (`GameSessionConfig?`) over mandatory injection — lets existing callers work unchanged
- **Static evaluators** (`ShadowThresholdEvaluator`, `PlayerResponseDelayEvaluator`) over stateful services — pure functions are easy to test and replace
- **SessionShadowTracker wrapping immutable StatBlock** — this is the right architectural decision. It preserves `StatBlock` immutability for rolls while enabling session-scoped mutation. Clean separation of concerns.

One concern: the sheer number of new responsibilities on `GameSession` (shadow growth, combo tracking, XP recording, tell/weakness windows, horniness injection, Read/Recover/Wait) is turning it into a god object. This is **acceptable at prototype** but must be addressed at hardening (already tracked as arch-concern #87).

---

## Data Flow Traces

### Wave 0: ExternalBonus Flow (Critical Path)
- `GameSession.ResolveTurnAsync()` → compute callback bonus (#47) + tell bonus (#50) + triple combo bonus (#46) → sum into single `externalBonus` int → pass to `RollEngine.Resolve(externalBonus:)` → flows into `RollResult` constructor → `FinalTotal = Total + ExternalBonus` → `IsSuccess` uses `FinalTotal`
- Required fields: `RollResult.ExternalBonus`, `RollResult.FinalTotal`, `RollResult.IsSuccess`
- ✅ All fields exist. Contract correctly routes all bonuses through the single `externalBonus` parameter.
- ⚠️ **Minor**: `RollResult.AddExternalBonus()` remains as dead code. The contract says "deprecated" but it's still public and mutable. An implementer could accidentally use it, creating a dual-path bug. Not blocking — but cleanup issue should be filed post-sprint.

### Wave 0: dcAdjustment Flow (Weakness Windows)
- `GameSession.ResolveTurnAsync()` → check `_activeWeaknessWindow` → if stat matches, pass `dcAdjustment = window.DcReduction` → `RollEngine.Resolve(dcAdjustment:)` → `dc = defender.GetDefenceDC(stat) - dcAdjustment` → normal resolution
- ✅ Clean single path. No ambiguity.

### Shadow Growth → Threshold → Gameplay Effect
- `SessionShadowTracker.ApplyGrowth()` in ResolveTurnAsync (after roll) → `GetEffectiveShadow()` checked at next `StartTurnAsync` → `ShadowThresholdEvaluator.GetThresholdLevel()` → disadvantage flags / option filtering
- ✅ Growth is correctly post-roll (invariant preserved).
- ⚠️ **One gap**: The contract for #45 says "Dread ≥ 18 → StartingInterest = 8" checked at constructor. But `SessionShadowTracker` growth happens during the session. If Dread *reaches* 18 mid-session, this effect doesn't retroactively lower interest. This is likely intentional (starting interest is a one-time check) but should be explicit in the contract.

### Read/Recover/Wait → No LLM calls
- `ReadAsync()` → `RollEngine.ResolveFixedDC(SA, 12)` → interest/shadow effects → return `ReadResult`
- `RecoverAsync()` → same roll → clear trap on success → return `RecoverResult`
- `Wait()` → `interest.Apply(-1)` → advance traps → return void
- ✅ These correctly bypass the LLM. No `DeliverMessageAsync` or `GetOpponentResponseAsync` calls.
- ⚠️ **Missing from contract**: Do Read/Recover/Wait add entries to `_history`? The existing `ResolveTurnAsync` appends to history. If non-Speak actions don't append, the conversation history has gaps the LLM might find confusing on the next Speak turn. Recommend: add a system-level entry like `("[System]", "Player chose to Read/Recover/Wait")`.

### ConversationRegistry Fast-Forward
- `FastForward()` → find earliest `PendingReplyAt` → `clock.AdvanceTo()` → check ghost/fizzle on other entries → interest decay
- ✅ Registry correctly does NOT call `GameSession.StartTurnAsync` — separation preserved.
- ⚠️ **Access gap** (known): Registry needs `GameSession.InterestMeter.Current` to check ghost/fizzle conditions. `GameSession` currently exposes this only via `GameStateSnapshot`. The contract doesn't specify how the registry reads interest. This will need a `public int CurrentInterest` property on `GameSession` or the registry accesses `ConversationEntry.Session.CreateSnapshot().Interest`.

---

## Unstated Requirements

1. **History entries for non-Speak actions**: When a player Reads, Recovers, or Waits, the LLM needs to know this happened on the next Speak turn. Without history entries, the LLM generates dialogue as if no time passed.

2. **Energy consumption rules**: #144 correctly identifies that nothing calls `ConsumeEnergy()`. The contracts define the plumbing but not the rules. For prototype, this is acceptable — the infrastructure is there for future sprints.

3. **GameSession public interest accessor**: Multiple Sprint 7 components need continuous read access to interest (ConversationRegistry, PlayerResponseDelay caller). Currently only available via snapshot.

---

## Domain Invariants (verified against contracts)

- ✅ Shadow growth happens AFTER roll resolution (contracts #44, #45 are explicit)
- ✅ Interest delta is additive composition: SuccessScale + RiskTierBonus + momentum + combo bonus
- ✅ `StatBlock` remains immutable — `SessionShadowTracker` wraps it
- ✅ `RollEngine` remains stateless — all new params have backward-compatible defaults
- ✅ Nat1/Nat20 override all external bonuses (IsNatOne → auto-fail regardless of FinalTotal)
- ✅ Tell/Weakness window are one-turn-only effects
- ✅ ConversationRegistry does not call GameSession action methods

---

## Gaps

### Missing (non-blocking)
- **`DialogueOption.HasWeaknessWindow`** property: Contract #49 mentions adding it but `DialogueOption.cs` currently lacks it. The constructor will need a new optional parameter. Not blocking — implementer will add it.
- **History entries for Read/Recover/Wait**: See unstated requirement #1 above.

### Unnecessary (could defer but acceptable)
- **`ConversationRegistry` (#56)**: Most complex component, most dependencies, least tested integration surface. Could be Sprint 8. However, the contract is well-defined and self-contained. Proceeding is acceptable.

### Assumptions to validate
- **Energy formula**: Contract says `dice.Roll(6) + 14` for 15–20 range, but also notes ambiguity. PO should confirm.
- **Dread T3 starting interest = 8**: Is this checked only at session start, or does it apply retroactively if Dread grows to T3 mid-session? Contracts imply "constructor only" which is correct but should be explicit.
- **`AddExternalBonus()` removal timeline**: Contracts say deprecated. When is it actually removed? File a cleanup issue.

---

## Coupling Assessment

The architecture introduces **minimal new coupling**:
- `SessionShadowTracker` depends only on `StatBlock`, `StatType`, `ShadowStatType` — all stable
- `GameSessionConfig` is a plain data carrier with no behavior — easy to extend
- `IGameClock` is an interface — implementation swappable
- `ComboTracker` and `XpLedger` are self-contained trackers owned by `GameSession`
- `ShadowThresholdEvaluator` and `PlayerResponseDelayEvaluator` are static pure functions — zero coupling

The only coupling concern is `GameSession` accumulating responsibilities, but this is tracked (#87) and appropriate for prototype.

---

## Recommendations

1. **Clarify history behavior for Read/Recover/Wait** in the #43 contract — should these actions append a system entry to `_history`?
2. **Add `public int CurrentInterest` property to GameSession** as part of Wave 0 (#139) — multiple consumers need it, and it's trivial.
3. **File a cleanup issue for `AddExternalBonus()` removal** to be done immediately post-Sprint 7.
4. **Confirm energy consumption rules with PO** — or explicitly mark energy as "infrastructure only, no consumers yet" in the contract.

---

**VERDICT: CLEAN** — architecture aligns with product vision, proceed
