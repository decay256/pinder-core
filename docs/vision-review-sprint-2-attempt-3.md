# Vision Review — Sprint 2 "RPG Rules Complete" (Attempt 3)

## Alignment: ⚠️ ADVISORY
This sprint correctly targets the remaining RPG mechanics needed for a complete prototype. All 16 feature issues map to specific rules sections (§5, §7, §8, §10, §15, §async-time). The vision concerns raised in attempts 1 and 2 are now in the sprint as well-specified issues. However, several sprint issues still contain stale references and ambiguities that haven't been corrected despite vision concerns existing for them.

## Issues Fixed This Pass

### #53 — Removed Lukewarm reference (per #59)
- Replaced undefined `×2.0 Lukewarm` with the correct 6-value `InterestState` multiplier table
- Added explicit warning: do NOT reference or create `InterestState.Lukewarm`

### #55 — Defined "Chaos ≥ high" threshold + fixed record syntax (per #71, #70)
- "Chaos ≥ high" → "Chaos **base stat** ≥ 4" (not shadow stat)
- `DelayPenalty` changed from `record` to `sealed class` with full constructor
- Clarified other modifiers check **shadow stat** ≥ 6 (threshold 1)

### #47 — Fixed record syntax (per #70)
- `ConversationTopic` changed from `record` to `sealed class` with constructor

### #56 — Fixed record syntax + added IGameClock reference (per #70, #67)
- `ConversationEntry` changed from `record` to `sealed class`
- Added: constructor accepts `IGameClock` (not concrete `GameClock`)

### #60 — Clarified DialogueOption expansion scope
- Made explicit that `HasWeaknessWindow` and `IsHorninessForced` should be added to `DialogueOption` in #63

## New Concerns Filed

### #73 — #49 and #50 missing dependency on #63
#49 (Weakness windows) and #50 (Tells) use `Tell`, `WeaknessWindow`, and `OpponentResponse` types created by #63, but don't list #63 as a dependency. Implementers will hit compile errors or create duplicate types.

### #74 — **BLOCKING** — #51 Horniness roll vs #45 Horniness shadow stat ambiguity
#51 specifies `dice.Roll(10) + timeOfDay` but #45 specifies Horniness as a shadow stat with thresholds at 6/12/18. These produce the **exact same gameplay effects** (forced Rizz options) from two different value sources. PO must clarify before implementation.

### #75 — Energy system ownership split between #54 and #56
Both `GameClock` and `ConversationRegistry` define energy methods. One must own the state; the other must delegate.

## Previously Raised Concerns — Status

| # | Concern | Status |
|---|---|---|
| #57 | Sprint scope massive (16 issues) | Advisory — wave plan mitigates |
| #58 | StatBlock immutability vs shadow growth | Unresolved — architect must decide |
| #59 | #53 Lukewarm reference | ✅ Fixed this pass |
| #60 | ILlmAdapter expansion | ✅ Clarified this pass |
| #61 | #42 re-scoped without PO confirm | Advisory — values marked prototype |
| #62 | #38 QA should run early | Advisory — wave plan decision |
| #64 | TrapState.HasActive missing | Well-specified — trivial fix |
| #65 | DC 12 API mismatch | Well-specified — architect decision |
| #66 | Shadow growth counters | Well-specified — needs architect |
| #67 | IGameClock interface | Well-specified — clear AC |
| #68 | Roll bonus composition | Well-specified — architect decision |
| #69 | TurnResult expansion | Well-specified — prerequisite needed |
| #70 | Record types vs netstandard2.0 | ✅ Fixed #47, #55, #56 this pass |
| #71 | Chaos ≥ high undefined | ✅ Fixed this pass |

## Domain Invariants
- Horniness effects must come from ONE authoritative source (shadow stat OR roll, not both)
- Energy state must have a single owner (GameClock OR ConversationRegistry, not both)
- All types must be `sealed class` (no `record` types — netstandard2.0 constraint)
- `InterestState` has exactly 6 values — no Lukewarm
- All new `TurnResult`/`DialogueOption` fields must have nullable or default values to preserve backward compat

## Gaps
- **Missing dependency**: #49 and #50 need #63 as prerequisite (#73)
- **Ambiguous mechanic**: Horniness dual-source (#74) — needs PO input
- **Unclear ownership**: Energy system split (#75) — needs architect decision
- **Unresolved architectural**: StatBlock mutability (#58), RollEngine fixed DC (#65), roll bonus composition (#68), TurnResult expansion (#69)

## Verdict: ADVISORY

The sprint issues have improved significantly from attempt 1. The major type-safety issues (records, Lukewarm) are now fixed. However:

1. **#74 (Horniness ambiguity) should be resolved before #51 implementation starts** — two contradictory specifications for the same mechanic will produce conflicting code.
2. Four architectural decisions (#58, #65, #68, #69) need the architect to make calls before wave 2+ issues can proceed.
3. Two new dependency gaps (#73, #75) need the sprint planner to update issue dependencies and wave ordering.

These are all addressable by the architect and sprint planner. No fundamental vision misalignment exists.

## Recommendations
1. **Architect**: resolve #58 (StatBlock mutability), #65 (fixed DC overload), #68 (roll bonus composition), #69 (TurnResult expansion) as design decisions before implementation agents start wave 2
2. **Sprint planner**: add #63 as dependency to #49 and #50
3. **PO**: confirm Horniness mechanic source (#74) before #51 is waved
4. **Architect**: decide energy ownership (#75) — recommend GameClock owns, ConversationRegistry delegates
