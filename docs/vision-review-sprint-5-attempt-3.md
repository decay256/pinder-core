# Vision Review — Sprint 5: Rules v3.4 Sync (Attempt 3)

## Alignment: ⚠️

This sprint remains correctly targeted — #3 (sync docs), #6 (interest states), and #7 (rules constants tests) are the right next steps after merging the core value changes (#1, #2). Adding #18 (consolidated vision concerns) to the sprint is the right call. However, the fundamental blocker persists: **no PO decisions have been made on the three questions raised since Sprint 2**. The safe defaults in #18 are well-defined and conservative — the orchestrator should apply them to unblock implementation.

## Step 1 — Vision-Concern Issue Review

### #18: Three sprints of unresolved vision concerns (PO action required)
**Status: Well-specified ✅**

Acceptance criteria are clear and actionable: three binary PO decisions with explicit options. The "Workaround if PO is unavailable" section provides safe defaults that are conservative (remove scope, don't invent it). No edits needed.

**One process recommendation:** The safe defaults should be applied directly to #6 and #7 issue bodies by the orchestrator NOW, rather than hoping implementation agents read #18 and infer the right behavior. Specifically:
1. Edit #6 body: remove `Lukewarm` from the enum list (6 states, 6 ranges)
2. Edit #7 body: remove `§5 Success scale` section entirely from "What to test"
3. Edit #7 body: change `Depends on: #1, #2, #4` to `Depends on: #1, #2` (both already merged)

These edits make #6 and #7 self-contained and implementable without cross-referencing vision concerns.

## Step 2 — Remaining Gaps

### Resolved vision concerns (can be closed by PO)
- **#4** (Issue #1 will break test suite): Moot. PRs #12 and #13 merged #1 and #2 atomically enough that main stayed green. No action needed.
- **#5** (README documents stale DC formula): **Resolved.** README now shows `DC = 13`, RollEngine docstring says `DC = 13`, InterestMeter docstring says `0–25`. All three fixes landed via PRs #12/#13. PO should close.
- **#10** (Dependency chain serializes sprint): Partially moot. #1 and #2 are merged. The remaining chain is just #3, #6, #7 — and #3 has no real code dependency on anything (it documents what's already on main). Only #6→#7 has a logical dependency if #7 wants to test InterestState boundaries.

### No new gaps identified
The codebase on main is consistent with rules v3.4 for all implemented sections (§3, §5 fail tiers, §6 meter range, §10 levels). The only unimplemented rules section is §5 success scale, which is correctly deferred by #18's safe defaults.

## Data Flow Traces

### Issue #6: InterestState → Advantage/Disadvantage
- `InterestMeter.Current` (0–25) → `GetState()` → `InterestState` enum → `GrantsAdvantage` / `GrantsDisadvantage` → consumed by caller as booleans for `RollEngine.Resolve(hasAdvantage, hasDisadvantage)`
- Required fields: `Current`, boundary constants for 6 ranges
- ✅ No missing fields — `InterestMeter.Current`, `Min`, `Max` all exist on main
- ⚠️ If Lukewarm is NOT removed from #6 AC, agent must invent a range — apply safe default

### Issue #7: RulesConstants Tests
- Tests read static constants → assert against hardcoded rules values
- §3 (DefenceTable, DC base): ✅ All constants exist on main
- §5 fail tiers: ✅ `FailureTier` enum and `RollEngine` miss-margin logic exist
- §5 success scale: ❌ No `SuccessMargin` on `RollResult`, no interest delta computation — **must be descoped per #18 safe default**
- §6 (InterestMeter): ✅ `Max=25`, `Min=0`, `StartingValue=10` exist
- §10 (LevelTable): ✅ `LevelTable.GetBonus()` exists
- ⚠️ #7 depends on #4 (a vision-concern, not a PR) — must be removed per #18 safe default

### Issue #3: Architecture Docs
- Pure documentation. No data flow. Reads existing code on main and describes the sync mapping.
- ✅ No blockers. All constants to document are already merged.

## Unstated Requirements
- If #6 adds `GrantsAdvantage`/`GrantsDisadvantage`, callers will expect `InterestMeter` to be the single source of truth — no manual bool management
- If #3 documents the sync table, the team will expect it to stay current as new sections are implemented (success scale, items, prestige)
- Success scale (§5 positive feedback) remains the most important unbuilt mechanic — should be Sprint 6's top priority

## Domain Invariants
- Every `InterestState` enum value must map to exactly one non-overlapping range in [0, Max]
- `DefenceTable` must be a bijection (each stat appears once as attacker, once as defender)
- `FailureTier` boundaries must be exhaustive and non-overlapping for all miss margins ≥ 1
- Constants in test file must match constants in production code AND rules doc — triangulated truth

## Gaps
- **Missing (non-blocking):** Success scale implementation deferred to future sprint. Correct decision for now — you can't test what doesn't exist.
- **Missing (minor):** #5 and #4 should be closed as resolved — stale open issues create noise.
- **Unnecessary:** Nothing — all four sprint issues are justified.
- **Assumption:** Agents will apply #18's safe defaults without explicit issue edits. **Recommendation: edit the issue bodies directly.**

## Recommendations
1. **Apply #18 safe defaults to issue bodies NOW** — edit #6 to remove Lukewarm, edit #7 to remove §5 success scale and #4 dependency. Don't rely on agents cross-referencing vision concerns.
2. **Close resolved vision concerns** — #4 and #5 are moot after PRs #12/#13. Reducing noise helps future sprints.
3. **Plan Sprint 6 around success scale** — it's the core positive feedback loop and the only major rules section without implementation.
4. **#17 (role reassignment)** is valid — #3 is technical writing, not backend engineering. But at prototype maturity, any agent can write docs. Low priority.

## VERDICT: CLEAN

All vision concerns are now consolidated in #18 with clear, actionable safe defaults. The sprint can proceed IF the orchestrator applies those defaults to issue bodies before spawning implementation agents. No new concerns identified. The codebase is consistent with implemented rules sections. Sprint 5 should finally land #3, #6, and #7.
