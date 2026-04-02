# Vision Review — Sprint 11 Architecture Strategic Alignment

## Alignment: ✅ Good

The architect's output is well-aligned with the product vision. Sprint 11 is pure rules-compliance — fixing mechanical bugs and filling implementation gaps against the canonical rules-v3.4 spec. This is exactly the right work for a prototype-maturity RPG engine: get the game rules right before layering on more features. The architecture briefing and per-issue contracts are thorough, source-code-verified, and correctly address all 6 vision concerns from the first pass.

## Evaluation

### 1. Maturity Fit: ✅ Appropriate for prototype

The architect proposes no new abstractions, no structural changes, and no premature optimization. All 10 issues are surgical fixes within existing module boundaries. The recommended implementation order (3 waves) is pragmatic — it sequences independent changes first, then GameSession constructor changes, then logic changes. This minimizes merge conflict risk without introducing unnecessary process overhead.

The `DialogueOption.IsUnhinged` addition (#273) is the only DTO change. Using an explicit boolean property rather than a text marker (`[UNHINGED]` prefix) is the right call — it's consistent with the existing metadata pattern (`HasTellBonus`, `HasWeaknessWindow`) and avoids stringly-typed signaling.

### 2. Coupling Assessment: ✅ No new coupling introduced

All changes stay within existing module boundaries:
- `FailureScale` (Rolls) — isolated static class, no new dependencies
- `RollEngine` (Rolls) — already has trap activation logic, extending it to Catastrophe/Legendary is natural
- `GameSession` (Conversation) — already owns all the state being modified (momentum, horniness, shadow thresholds, crit tracking)
- `DialogueOption` (Conversation) — gains one backward-compatible property

The dependency graph is unchanged: `GameSession → RollEngine`, `GameSession → SessionShadowTracker`, `GameSession → InterestMeter`. No cross-module coupling added.

### 3. Abstraction Longevity: ⚠️ Manageable concern

**GameSession god object**: The file is already 1,176 lines with 57 access-modified members. Sprint 11 adds ~100 more lines and at least 2 new private fields (`_pendingMomentumBonus`, `_pendingCritAdvantage`). This is tracked as arch-concern #87 and the architect correctly notes extraction is planned for MVP maturity. For prototype, this is acceptable — the file is a single-conversation orchestrator and all the state it accumulates is genuinely per-conversation.

The architect's suggestion to extract shadow evaluation into a private helper method (for #260) is good — it creates a natural seam for later extraction without over-engineering now.

**No painful abstractions to undo**: Nothing in this sprint creates abstractions that would need unwinding. The `IsUnhinged` property, `_pendingCritAdvantage` field, and momentum-as-roll-bonus flow are all straightforward and won't resist refactoring when GameSession is eventually decomposed.

### 4. Interface Design: ✅ Correct boundaries

The contracts correctly keep implementation details internal:
- Momentum bonus computation is private to GameSession (not exposed on TurnResult or TurnStart)
- Crit advantage tracking is private state (not a public API)
- Shadow reduction triggers are evaluated internally by GameSession, not pushed to callers
- `IsUnhinged` on `DialogueOption` is the right level of abstraction — it tells the LLM adapter *what* to do without *how*

## Data Flow Traces

### Momentum as Roll Bonus (#268)
- `StartTurnAsync()` → compute `_pendingMomentumBonus` from `_momentumStreak` → pass to `ResolveTurnAsync()`
- `ResolveTurnAsync()` → `externalBonus = tellBonus + callbackBonus + tripleCombo + _pendingMomentumBonus` → `RollEngine.Resolve(externalBonus)` → roll total includes momentum → `SuccessScale`/`FailureScale` → interest delta
- Required: `_momentumStreak` must be incremented AFTER roll, not before (architect correctly specifies this)
- ✅ Data flow is complete — momentum flows through the existing `externalBonus` pipeline

### Crit Advantage Across Action Types (#271 + #280)
- Any action (Speak/Read/Recover) Nat 20 → sets `_pendingCritAdvantage = true`
- Next action of any type → reads `_pendingCritAdvantage` → grants advantage → clears flag
- ✅ Architect correctly addresses vision concern #280 — all action types participate

### Shadow Disadvantage on Read/Recover (#260)
- `_shadowDisadvantagedStats` computed in `StartTurnAsync()` — but Read/Recover don't call it
- Architect recommends extracting threshold computation into a private helper called from all paths
- ✅ This is the correct solution — ensures fresh shadow data regardless of action sequence

## Unstated Requirements

- **Shadow reduction events should appear in TurnResult/ReadResult/RecoverResult**: The architect's contract for #270 specifies `ApplyOffset` returns a description string, but doesn't explicitly state where these reduction event strings surface in the return types. `TurnResult.ShadowGrowthEvents` already exists — reductions should appear there too. The naming is slightly misleading ("growth" events including reductions) but renaming is a cleanup concern, not a blocker.

## Domain Invariants (verified against contracts)

- ✅ `ApplyGrowth()` is never called with negative amounts (vision concern #279 addressed in all per-issue contracts)
- ✅ `AddExternalBonus()` is not used for new features (vision concern #276 addressed)
- ✅ All 1146 existing tests must pass (backward compatibility via optional params with defaults)
- ✅ Trap activation on TropeTrap + Catastrophe + Legendary (all three tiers covered per #275)
- ✅ Sequential implementation of GameSession issues (merge conflict risk mitigated per #277)

## Gaps

- **None blocking**: All 6 vision concerns are addressed in the per-issue contracts with correct guidance.
- **Minor**: The contract doesn't specify the exact return type enrichment for shadow reduction events (#270). `DrainGrowthEvents()` returns both growth and reduction descriptions, and `TurnResult.ShadowGrowthEvents` is the natural destination — but the mapping should be explicit in the contract. This is advisory, not blocking — any reasonable implementer will wire it the same way the existing growth events are wired.
- **Deferred (correct)**: GameSession decomposition (#87) is acknowledged and deferred to MVP. This is the right call for prototype maturity.

## Recommendations

1. **Proceed as-is** — the architecture contracts are thorough, source-code-verified, and correctly address all vision concerns.
2. **Monitor GameSession size post-sprint** — after Sprint 11, the file will be ~1,280 lines. If another sprint adds more GameSession logic before extraction, flag it as blocking.

## Verdict

**VERDICT: CLEAN** — architecture aligns with product vision, proceed.
