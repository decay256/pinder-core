# Vision Review — Sprint 3 Architecture Strategic Alignment

## Alignment: ⚠️ ADVISORY

The architecture is fundamentally sound for prototype maturity. The 6 ADRs resolve all open vision concerns, the 7-wave implementation strategy is well-ordered, and deferring #56 (ConversationRegistry) is the right call. However, three concerns need attention: a **data flow gap** in RollResult that will cause incorrect roll outcomes when externalBonus is non-zero (BLOCKING for Waves 4+), a premature `OpponentShadows` field on GameSessionConfig, and GameSession's trajectory toward a god object.

## Data Flow Traces

### Combo/Callback/Tell → externalBonus → RollEngine → RollResult
- Player selects option → GameSession computes externalBonus (callback +1/+2/+3, tell +2, Triple +1) → passes to `RollEngine.Resolve(..., externalBonus: N)`
- RollEngine uses `total = usedRoll + statMod + levelBonus + externalBonus` for tier determination
- RollEngine creates `RollResult(usedDieRoll, stat, statModifier, levelBonus, dc, tier)`
- ⚠️ **BLOCKING GAP**: `RollResult.Total` = `usedDieRoll + statModifier + levelBonus` — **externalBonus is lost**
- `RollResult.IsSuccess` computed from wrong Total — may disagree with tier
- `SuccessScale.GetInterestDelta(rollResult)` reads `rollResult.Total - rollResult.DC` — **wrong beat-DC-by value**
- **Filed as #85**

### Shadow Growth → SessionShadowTracker → effective stats for rolls
- Player rolls → GameSession calls `SessionShadowTracker.RecordStatUsed()` → growth events recorded
- Next turn, GameSession needs shadow-adjusted effective modifiers for RollEngine
- Contract says "creates a temporary StatBlock with adjusted shadow values OR passes the shadow-adjusted effective modifier" — this is vague
- RollEngine takes `StatBlock attacker` and calls `attacker.GetEffective(stat)` internally
- ⚠️ **Minor gap**: No contract specifies HOW GameSession creates a shadow-adjusted StatBlock view. StatBlock constructor takes `Dictionary<StatType, int>` base values and `Dictionary<ShadowStatType, int>` shadow values — so GameSession would need to merge base shadows + session growth into a new StatBlock per turn. This works but should be explicit in the #44 contract.

### GameSessionConfig → GameSession → feature activation
- Host creates `GameSessionConfig(llm, dice, trapRegistry, gameClock?, playerShadows?, opponentShadows?)`
- GameSession checks `config.PlayerShadows != null` before shadow logic
- ⚠️ `OpponentShadows` has no consumer — no contract describes opponent shadow growth. **Filed as #86**

## Unstated Requirements
- **Bonus provenance**: When multiple bonuses stack (callback +2, tell +2, Triple +1 = externalBonus 5), the player/host will want to know WHERE the bonus came from. TurnResult should expose a breakdown, not just the final delta.
- **Shadow-adjusted StatBlock caching**: If GameSession creates a new StatBlock per turn for shadow adjustment, this should be done once per turn, not per-roll (Read/Recover also roll).
- **Combo reset on non-Speak actions**: ComboTracker records stats from Speak turns. What happens to the sequence buffer when the player uses Read/Recover/Wait? The #46 contract doesn't specify. Likely should preserve the buffer (Read/Recover aren't offensive actions).

## Domain Invariants
- `RollResult.IsSuccess` MUST agree with `RollResult.Tier == FailureTier.None` — currently guaranteed, but externalBonus gap (#85) breaks this
- `InterestMeter` value MUST stay in [0, 25] regardless of how many bonuses stack (momentum + combo + risk tier could theoretically push a +10 delta)
- Shadow growth is **monotonic within a session** (can only grow, never shrink) — except Fixation -1 for 4+ distinct stats, which is an end-of-session offset
- `StatBlock` immutability must be preserved — SessionShadowTracker wraps but never mutates

## Gaps

### Missing (should be addressed)
- **#85 (BLOCKING)**: RollResult.Total excludes externalBonus — must be fixed before Wave 4 issues (#46, #47, #50) can be implemented correctly
- **Shadow-adjusted StatBlock creation**: Contract #44 should explicitly specify the mechanism (construct new StatBlock with merged shadow values)

### Unnecessary (could be deferred)
- **OpponentShadows on GameSessionConfig** (#86): No growth loop exists. Remove for Sprint 3.
- **OpponentTimingCalculator** (#53): Nice-to-have at prototype. The existing `TimingProfile.ComputeDelay()` works. Could defer to Sprint 4 with ConversationRegistry.

### Assumptions to validate
- **Combo buffer behavior during non-Speak turns**: Does Read/Recover/Wait break a combo sequence? Rules don't specify.
- **Multiple bonus cap**: Is there a maximum externalBonus? If callback (+3) + tell (+2) + Triple (+1) = +6, that's huge. Rules should confirm this stacks.
- **Shadow growth timing**: Growth events fire "after roll" — but does this mean after the current turn's roll affects the CURRENT turn's SuccessScale, or only future turns? Contract says future turns (shadow-adjusted StatBlock built at turn start), which seems correct.

## Maturity Fit Assessment
- **Appropriate for prototype**: Yes. The contracts are detailed enough to prevent cross-issue confusion across 18 parallel work items.
- **Over-engineered?**: Slightly — OpponentShadows and OpponentTimingCalculator are building for a future that isn't yet specified. But the cost is minimal (a nullable field, a pure function).
- **Painful to undo?**: No. All new types are additive. GameSessionConfig's optional params mean nothing breaks if features are removed. Good.

## Coupling Assessment
- **RollEngine stays stateless**: ✅ Good — externalBonus and fixedDc are passed in, not stored
- **SessionShadowTracker depends on StatBlock**: ✅ Good — reads only, never mutates
- **GameSession depends on everything**: ⚠️ Expected for an orchestrator, but nearing limits. See #87.
- **ComboTracker is self-contained**: ✅ Good — pure state machine, no external dependencies
- **XpLedger is self-contained**: ✅ Good — just accumulates events

## Recommendations
1. **Fix #85 before Wave 4**: Add `externalBonus` param to RollResult constructor. This is a 5-minute change but prevents cascading bugs in 4 issues.
2. **Remove OpponentShadows from GameSessionConfig** for Sprint 3. Add it when the gameplay loop justifies it.
3. **Specify shadow-adjusted StatBlock creation** in the #44 contract explicitly — don't leave it as "OR".
4. **Add a note to #46** (combo system) about buffer behavior during Read/Recover/Wait turns.
5. **Accept GameSession growth** for prototype but file #87 as Sprint 4 tech debt.

## Verdict

**VERDICT: ADVISORY**

The architecture is strategically aligned. The one blocking issue (#85 — externalBonus not flowing through RollResult) is a contract gap, not a design flaw — easily fixed by the architect adding an `externalBonus` parameter to RollResult's constructor. Filed as arch-concern. Two additional advisory concerns filed (#86, #87). Sprint can proceed once #85 is addressed in the contract.
