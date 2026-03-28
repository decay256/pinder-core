# Vision Review — Sprint: RPG Rules Complete (Attempt 2)

## Alignment: ⚠️ → Improving

The sprint direction remains correct — completing the RPG mechanical systems is the right next step. Since the first vision pass, 14 vision concerns were filed (#57–#69) and issue #63 was created as a preparatory prerequisite to handle ILlmAdapter expansion. This attempt refined 4 existing concerns (#59, #60, #68, #69) with sharper acceptance criteria and created 2 new concerns (#70, #71). The sprint is materially better-specified than before.

## Vision Concerns — Status After This Pass

### Well-Specified (no edits needed)
| # | Title | Assessment |
|---|---|---|
| #57 | Sprint scope massive | Advisory, wave plan mitigates. No action needed. |
| #58 | StatBlock immutable | 3 clear options. Architect must decide before Wave 3 (#44). |
| #62 | Test gaps before features | Clear scheduling recommendation. |
| #64 | TrapState.HasActive missing | Trivial, well-scoped. |
| #65 | DC 12 for Read/Recover | 3 options with recommendation. |
| #66 | Session tracking counters for #44 | Comprehensive counter list. |
| #67 | IGameClock interface needed | Clear pattern reference (FixedDice). |

### Sharpened This Pass
| # | Title | What changed |
|---|---|---|
| #59 | #53 Lukewarm reference | Added concrete replacement multiplier table with all 6 InterestState values and AC checklist |
| #60 | ILlmAdapter breaking changes | Updated to reflect #63 partially addresses it; flagged remaining gap (DialogueOption expansion for #49/#51) |
| #68 | The Triple roll bonus composition | Added recommended `rollBonus` parameter design, stacking rules, and AC checklist |
| #69 | TurnResult expansion | Added concrete field list with types/defaults, recommended approach, and AC checklist |

### New Concerns Filed This Pass
| # | Title | Why |
|---|---|---|
| #70 | Multiple issues use C# `record` — project is netstandard2.0/C# 8.0 | 5 issues specify `record` types that won't compile. Cross-cutting implementer trap. |
| #71 | #55 'Chaos >= high' undefined threshold | Ambiguous modifier — unclear if base stat or shadow stat, no numeric value. |

## Data Flow Traces

### Risk Tier (#42) → Interest Delta
- `RollEngine.Resolve()` → `RollResult` (needs new `RiskTier` field) → `GameSession.ResolveTurnAsync` → `SuccessScale.GetInterestDelta() + riskBonus` → `InterestMeter.Apply(total)`
- ⚠️ `RiskTier` is based on `need = DC - (statMod + levelBonus)` — computable from existing RollResult fields (`DC`, `StatModifier`, `LevelBonus`) without the die roll
- ⚠️ **#69**: TurnResult needs `RiskTier` field added in preparatory PR

### Shadow Growth (#44) → StatBlock Mutation
- `GameSession.ResolveTurnAsync()` → detect growth event → increment shadow on `_player.Stats`
- **⚠️ BLOCKING (via #58)**: `StatBlock._shadow` is private readonly. No public mutation method. Design decision required (mutable StatBlock vs. SessionShadowTracker vs. snapshot-per-mutation).

### External Roll Bonus (#46/#47/#50) → RollEngine
- `GameSession` accumulates bonuses (The Triple +1, callback +1/+2/+3, tell +2) → passes to `RollEngine.Resolve()`
- **⚠️ Via #68**: `RollEngine.Resolve` has no `rollBonus` parameter. Breaking change needed. Should be designed once for all three consumers.

### Opponent Response (#49/#50) → Structured Return
- `ILlmAdapter.GetOpponentResponseAsync()` → `OpponentResponse { MessageText, Tell?, WeaknessWindow? }`
- ✅ **Addressed by #63**: preparatory PR changes return type and adds stub types.

## Unstated Requirements
- **Shadow persistence across sessions**: #44 implements in-session growth but shadows are character-level. No serialization issue exists. After session ends, growth is lost. (Acceptable at prototype maturity — flag for next sprint.)
- **InterestMeter starting value override**: #45 says Dread ≥18 → start at 8 instead of 10. `InterestMeter` constructor is hardcoded to 10. Minor — implementer can add overload.
- **DialogueOption expansion**: #49 needs `HasWeaknessWindow`, #51 needs `IsHorninessForced`. Neither field exists. Should be added in #63's preparatory PR. (Flagged in updated #60.)

## Domain Invariants
- `RollResult` must remain immutable — `RiskTier` computed at construction, never mutated
- Shadow growth must NOT affect the current roll (growth happens after resolution, affects future rolls)
- Interest deltas compose additively: `SuccessScale + riskBonus + momentum + comboBonus + rollBonus`
- `GameSession` turn sequencing (StartTurn → Resolve alternation) must hold with Read/Recover/Wait added
- All new types must be `sealed class` with readonly properties — no `record` types (netstandard2.0)
- Active trap LLM instructions must be the same text in all context types (DialogueContext, DeliveryContext, OpponentContext)

## Gaps

### Critical (architect must address before implementation)
- **#58**: StatBlock immutability blocks #44 → #45 → #48 → #51 → #56 chain
- **#68**: RollEngine `rollBonus` design blocks #46, #47, #50

### Important (should be in this sprint)
- **#69**: TurnResult expansion — needs preparatory PR before feature PRs
- **#60 remaining**: DialogueOption expansion (`HasWeaknessWindow`, `IsHorninessForced`) — add to #63 scope
- **#70**: Fix `record` references in 5 issue bodies before agents implement

### Minor (can be handled inline)
- **#59**: Fix Lukewarm reference in #53
- **#64**: Add `HasActive` to TrapState
- **#65**: Add fixed-DC overload to RollEngine
- **#71**: Clarify Chaos threshold in #55

### Could defer to next sprint
- **#56** (ConversationRegistry): Most complex issue, deepest dependency chain. Self-contained subsystem.
- **#55** (PlayerResponseDelay): Depends on async-time subsystem. Could ship with #56.

## Recommendations

1. **Architect must resolve #58 (StatBlock mutability) and #68 (rollBonus design) before spawning implementation agents** — these are architectural decisions that cascade into 10+ issues.
2. **Expand #63 scope** to also include: TurnResult field expansion (#69), DialogueOption field expansion (#60 remaining), and `RiskTier` enum. One preparatory PR to rule them all.
3. **Fix issue bodies** for #70 (record→sealed class) and #59 (Lukewarm→real InterestState values) — these are 5-minute edits that prevent wasted agent cycles.
4. **Schedule #38 (QA audit) in Wave 1** — test gaps should be fixed before 14 new features land on top.
5. **Consider deferring #55+#56 to Sprint 3** — async-time is a self-contained subsystem that doesn't block core RPG mechanics.

## VERDICT: ADVISORY

All concerns are now filed as issues with specific acceptance criteria. Two architectural decisions (#58 StatBlock mutability, #68 rollBonus design) must be made by the architect before Wave 3 implementation begins. Two new cross-cutting concerns were identified (#70 record types, #71 vague threshold). The sprint can proceed — the architect should address #58 and #68 in their design phase, and issue body edits for #59/#70/#71 should happen before agents are spawned.
