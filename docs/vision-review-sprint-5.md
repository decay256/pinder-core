# Vision Review — Sprint 5: Rules v3.4 Sync (Attempt 2)

## Alignment: ⚠️

This sprint remains the right work — documenting the sync process (#3), adding interest state mechanics (#6), and locking constants with tests (#7) are all critical for a rules-driven game engine at prototype maturity. However, **the same three PO decisions that blocked sprints 2–4 remain unresolved**: Lukewarm range (#9), success scale scope (#8/#15), and #7's unresolvable dependency on #4 (#16). Issue #18 consolidates these clearly but has received no PO response. The sprint can proceed for #3 (pure docs) and partially for #6 and #7 — but only if agents apply the safe defaults recommended below.

## Data Flow Traces

### Issue #6: InterestState → Advantage/Disadvantage
- `InterestMeter.Current` (0–25) → `GetState()` → `InterestState` enum → `GrantsAdvantage` / `GrantsDisadvantage` → consumed by caller as booleans for `RollEngine.Resolve()`
- Required fields: Current value, state boundary ranges (0, 1–4, 5–15, 16–20, 21–24, 25)
- ⚠️ **Lukewarm enum value has no range mapping** — rules v3.4 §6 defines 6 states over 6 ranges; Lukewarm is a 7th value with no home. See #9.
- No other data flow issues. `InterestMeter` already has `Current`, `Min`, `Max` on main.

### Issue #7: RulesConstants Tests
- Test reads static constants → asserts against hardcoded rules values
- Required: `DefenceTable` entries, `GetDefenceDC(base=13)`, `FailureTier` boundaries, `InterestMeter.Max/Min/StartingValue`, `LevelTable` bonuses, **§5 success scale margins**
- ⚠️ **BLOCKING for §5 section only**: `RollResult` has no `SuccessMargin` property. No code computes interest delta from successful rolls. Agent cannot write assertions for values that don't exist.
- All other sections (§3, §5 fail tiers, §6, §10) have corresponding code and can be tested.

### Issue #3: Architecture Documentation
- No runtime data flow — pure documentation.
- References `design/systems/rules-v3.md` as source of truth, but **this file does not exist in the repo**. Either it lives in the parent `pinder` repo or it needs to be created/linked.

## Unstated Requirements
- If #3 references `design/systems/rules-v3.md` as the authoritative source, that file must be accessible to the agent writing the doc — it's not in this repo
- If #6 adds `GetState()`/`GrantsAdvantage`, the Unity game loop will expect `InterestMeter` to be the single source of truth for advantage — no manual bool management
- After this sprint ships, `docs/architecture.md` and `README.md` must not contradict each other (#5 still open)

## Domain Invariants
- Every `InterestState` enum value must map to exactly one non-overlapping range in [0, Max]
- `DefenceTable` must remain a bijection (each stat appears once as attacker, once as defender)
- `FailureTier` boundaries must be exhaustive and non-overlapping for all miss margins ≥ 1
- Success and failure mechanics should be symmetric — if failures have tiers, successes should too (currently violated; acceptable to defer but must be tracked)

## Gaps
- **Missing (PO decision needed)**: Lukewarm resolution (#9). Safe default: remove it from #6 AC.
- **Missing (PO decision needed)**: Success scale scope in #7 (#8/#15). Safe default: remove §5 success scale from #7 AC, track separately.
- **Missing (PO action needed)**: Remove `#4` from #7 dependency list (#16). #4 is a vision-concern issue that will never merge; #1 and #2 are already on main.
- **Missing**: `design/systems/rules-v3.md` not in repo. #3 agent needs access to write the sync table.
- **Missing**: #5 (README update) not in sprint — will leave stale docs alongside new architecture doc.
- **Stale**: #4 and #10 are moot now that #1 and #2 are merged. PO should close them.
- **Unnecessary**: Nothing — all three implementation issues are well-scoped.

## Vision-Concern Issue Quality Assessment

| Issue | Specificity | Action Taken |
|-------|------------|--------------|
| #18 (meta-escalation) | ✅ Now has concrete AC and safe defaults | Edited to add acceptance criteria and workaround section |
| #9 (Lukewarm) | ✅ Two clear options, well-specified | No edit needed |
| #8 (success scale code) | ✅ Concrete code suggestion included | No edit needed |
| #15 (success scale tests) | ✅ Three options clearly laid out | No edit needed |
| #16 (#7 → #4 dependency) | ✅ Fix is one line edit | No edit needed |
| #17 (role reassignment) | ✅ Clear recommendation | No edit needed |
| #5 (README stale) | ✅ Lists exact lines to update | No edit needed |

## New Concern Identified

**`design/systems/rules-v3.md` does not exist in this repo.** Issue #3's task is to document the mapping between this file and C# constants, but the file isn't here. The agent writing #3 will either need to:
- (a) Reference the parent `pinder` repo where rules may live
- (b) Create a stub rules reference in this repo
- (c) Document the mapping based solely on code comments and issue descriptions

This is not blocking (the C# constants are self-documenting enough), but it weakens the value of #3 if the "source of truth" document isn't accessible.

## Recommendations
1. **Apply safe defaults from #18 if PO doesn't respond**: Remove Lukewarm from #6, remove §5 success scale from #7, remove #4 dependency from #7. These are scope reductions, not design decisions — they can be reversed.
2. **Add #5 (README update) to this sprint** — it's 5 minutes of work and prevents doc contradiction.
3. **Close #4 and #10** — both are moot with #1 and #2 merged.
4. **Clarify where `design/systems/rules-v3.md` lives** before #3 agent starts — or tell the agent to document the mapping from code inspection alone.
5. **Create a success scale implementation issue** for a future sprint — this is the game's positive feedback loop and has been deferred for 4 sprints.

## VERDICT: ADVISORY

All vision-concern issues are well-specified and actionable. The blocker is PO inaction on 3 small decisions, not issue quality. #18 now includes safe defaults that agents can apply if PO doesn't respond. The sprint can proceed with these defaults:
- **#3**: Proceed as-is (note: rules doc may not be in repo)
- **#6**: Proceed with 6 states (no Lukewarm) unless PO says otherwise
- **#7**: Proceed with all sections except §5 success scale unless PO adds implementation first
- **#18**: Resolved by applying safe defaults above

One retry recommended to confirm PO has seen the safe defaults before agents spawn.
