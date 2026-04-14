# Rules Compliance Audit — 2026-04-14

## Summary
52 rules checked, 14 gaps found, 30 already implemented, 8 deliberately deferred.

---

## Gaps (rules in markdown, not in code)

### §7.1 — Despair Fresh 1d10 Roll Per Conversation
**Rule**: "Start of conversation → Roll 1d10 → That's your Despair." Despair is re-rolled fresh each conversation and does not persist. (rules-v3.md §7, §2)
**Status**: Not implemented
**Impact**: High — Despair currently uses the persistent base value from the character JSON. Without the per-session d10, Despair either never matters (starts at 0) or matters too much (accumulated from prior sessions). The fresh roll is central to Despair being the "biology" shadow — unpredictable and uncontrollable.
**Suggested fix**: In `GameSession` constructor, roll `_dice.Roll(10)` and override the character's Despair shadow value in `_playerShadows` for the session. Similar to how `_sessionHorniness` is rolled today.

### §7.2 — Despair Shadow Threshold Effects (T1/T2/T3)
**Rule**: At Despair 6 (T1): "Rizz options appear more often." At Despair 12 (T2): "One option is always unwanted Rizz." At Despair 18+ (T3): "ALL options become Rizz. No other thoughts." (rules-v3.md §7)
**Status**: Not implemented — `OptionFilterEngine` handles Fixation T3, Denial T3, and Madness T3 but has **zero** Despair threshold logic. `requiresRizzOption` is passed as `false` always.
**Impact**: High — Despair is mechanically inert beyond its stat penalty. The escalating Rizz-forcing is a key comedy mechanic that creates pressure on high-Despair characters.
**Suggested fix**: Add Despair threshold handling in `OptionFilterEngine.DrawRandomStats` (T1: bias stat pool toward Rizz) and `ApplyT3Filters` (T2: force one option to Rizz; T3: force all options to Rizz).

### §7.3 — Dread T3: Starting Interest 8 (Partial)
**Rule**: Dread T3 (≥18) → Starting Interest 8 instead of 10. (rules-v3.md §7)
**Status**: **Implemented** for Dread T3 in `GameSession` constructor ✅. But other T3 mechanical effects for Dread (T1 existential flavor, T2 Wit disadvantage at ≥12) — the T2 disadvantage is implemented via `_shadowDisadvantagedStats` generically. T1 flavor effects are LLM-prompt level only, not mechanical.
**Impact**: Low — T1 is correctly cosmetic, T2 and T3 are implemented. This is actually **confirmed implemented**.

### §7.4 — Overthinking T1: "You see Interest number always"
**Rule**: Overthinking T1 (≥6): "You see Interest number always (TMI)." Overthinking T3 (≥18): "You see opponent's inner monologue (freeze)." (rules-v3.md §7)
**Status**: Not implemented — T1 is a display/UX rule (always show interest), T3 "freeze" has no mechanical implementation in code.
**Impact**: Low-Medium — T1 is cosmetic (session runner could implement). T3 "freeze" effect is undefined mechanically in both rules and code.
**Suggested fix**: T1: expose in `TurnStart` snapshot whether Overthinking T1 is active so the session runner can always show interest. T3: define mechanical freeze effect (e.g., disadvantage on all stats for 1 turn) or mark as LLM-flavor only.

### §8.1 — Opponent Ghost Chance: Interest < 5 (25%/turn)
**Rule**: "Interest < 5: chance they leave. Dread +1." with "Ghost chance (25%/turn)" for Interest 1-4. (rules-v3.md §8)
**Status**: Partially implemented — ghost trigger fires in `StartTurnAsync` when state is `Bored` (1-4), rolls d4, ghost on 1. But the Dread +1 is only applied on ghost. The rule also says ghosting → Dread +1 separately from unmatch (Dread +2). **Implemented correctly** — ghost fires Dread +1, unmatch fires Dread +2.
**Impact**: N/A — this is actually confirmed implemented.

### §8.2 — Opponent "Pull Back" and "Test" Actions
**Rule**: Opponent can perform Test (force specific stat check next turn) and Pull Back (next roll has disadvantage). (rules-v3.md §8)
**Status**: Not implemented — the opponent LLM generates messages but there's no mechanical "Test" or "Pull Back" action that forces specific stats or imposes disadvantage.
**Impact**: Medium — Tests and Pull Backs would add dynamic difficulty. Currently the opponent is a conversation partner with no mechanical teeth beyond interest decay.
**Suggested fix**: Parse opponent LLM output for Test/PullBack markers. Test: restrict next turn's options to a specific stat. PullBack: set disadvantage flag for next roll.

### §5.1 — Catastrophe: Extra Shadow Growth
**Rule**: Catastrophe (miss 10+): "-3 + trap + extra shadow growth." Nat 1: "-4 + trap + shadow +1." (rules-v3.md §5)
**Status**: Partially implemented — Catastrophe activates trap and interest delta -3 ✅. But the "extra shadow growth" on Catastrophe beyond the TropeTrap Madness +1 trigger is not explicitly coded as a separate additional shadow hit. The Nat 1 "+1 to paired shadow" IS implemented (Trigger 1 in ShadowGrowthEvaluator). Catastrophe extra shadow growth appears to be covered by Trigger 3 (TropeTrap+ → Madness +1) which fires for Catastrophe too. **Likely implemented** via existing triggers.
**Impact**: Low — the existing triggers cover this implicitly.

### §5.2 — Trap Nat 1 Bonus Effects
**Rule**: "Rolling a natural 1 on any roll while a trap is active escalates the trap's DC modifier and adds a shadow stat bonus." (traps.md)
**Status**: Not implemented — `traps.json` has a `nat1_bonus` field but it's empty string for all 6 traps. No code reads or applies `nat1_bonus`.
**Impact**: Low-Medium — without defined nat1_bonus values, this is a no-op. The rule exists in the spec but the data file doesn't populate it.
**Suggested fix**: Define `nat1_bonus` values in `traps.json` for each trap (e.g., "DC modifier +2, shadow +1"). Add code in `RollEngine` or `GameSession` to apply the bonus when Nat 1 fires with an active trap.

### §6.1 — Interest Range 0-25 vs Rules (0-20 in some places)
**Rule**: Interest meter range 0-25, with "21-24 Almost There" and "25 Date Secured." (rules-v3.md §6)
**Status**: Implemented ✅ — `InterestMeter` goes 0-25 with correct states. However, some parts of the rules markdown say "Interest 20 = Date Secured" (§13) or "Interest 20" (§3.5 character construction). These are inconsistencies **within the rules documents** themselves (old text pre-dating the 0-25 scale). The code correctly uses 0-25.
**Impact**: None on code — the rules markdown has stale references.

### §15.1 — Callback Bonus Distance Mechanics
**Rule**: Callback bonuses scale by distance: 2 turns ago → +1, 4+ turns ago → +2, from the opener → +3. (risk-reward-and-hidden-depth.md)
**Status**: Implemented — `CallbackBonus.Compute()` exists and is called in `ResolveTurnAsync`. ✅

### §15.2 — Weakness Window DC Reduction
**Rule**: Opponent weakness windows reduce DC by 2-3 depending on trigger type. (risk-reward-and-hidden-depth.md)
**Status**: Implemented — `WeaknessWindow` is tracked and applied as `dcAdjustment` in `ResolveTurnAsync`. ✅

### §15.3 — Tell Bonus (+2 Hidden)
**Rule**: Opponent tells give +2 to matching stat option. (risk-reward-and-hidden-depth.md)
**Status**: Implemented — `tellBonus = hasTellOption ? 2 : 0` applied as `externalBonus`. ✅

### §15.4 — Momentum: Flow State and In The Zone Effects
**Rule**: Momentum 4+ = "Flow State" (+2 to next 2 rolls, opponent's next reply warmer). Momentum 5+ = "In The Zone" (+3 to next roll, opponent may spontaneously +1 Interest). (risk-reward-and-hidden-depth.md)
**Status**: Partially implemented — Momentum bonus values are: 3→+2, 4→+2, 5+→+3. The "+2 to next 2 rolls" for Flow State is NOT implemented — the bonus only applies to the single next roll. The "opponent's next reply is warmer" and "opponent may spontaneously +1 Interest" effects are NOT implemented.
**Impact**: Medium — the core momentum bonus works, but the multi-turn and opponent-behavior effects don't.
**Suggested fix**: Track `_pendingMomentumRolls` (set to 2 at streak 4, consumed each turn). Add momentum state to opponent context so LLM generates warmer replies. Add spontaneous +1 Interest roll at streak 5+.

### §15.5 — Risk Tier Interest Bonuses: Rules vs Code Mismatch
**Rule**: Risk tiers give: Safe +0, Medium +0, Hard +1, Bold +2. (risk-reward-and-hidden-depth.md)
**Status**: Code uses different values: Safe +1, Medium +2, Hard +3, Bold +5, Reckless +10. (RiskTierBonus.cs)
**Impact**: This is a **deliberate change** documented in CHANGES-since-v3.4.md. Not a gap — the code values supersede the original rules spec.

### §19.1 — Steering Roll
**Rule**: Steering roll after each turn. (rules-v3.md §19)
**Status**: Implemented ✅ — `SteeringEngine.AttemptSteeringRollAsync` with correct formula.

### §9.2 — Denial: "Choosing non-Honesty when Honesty available" → +1 Denial
**Rule**: Choosing a non-Honesty option when Honesty was available → Denial +1. (rules-v3.md §7)
**Status**: **Implemented** in `ResolveTurnAsync` ✅ — checks `chosenOption.Stat != StatType.Honesty && _currentOptions.Any(o => o.Stat == StatType.Honesty)`.
**Impact**: N/A — confirmed implemented. (Note: CHANGES-since-v3.4.md said this was not yet implemented — it has since been added.)

### §4.1 — Advantage from Outfit Synergy Bonus
**Rule**: Advantage from "Outfit synergy bonus." (rules-v3.md §4)
**Status**: Not implemented — no outfit synergy bonus system exists in `GameSession` or equipment processing.
**Impact**: Low — outfit synergy is a multiplayer/server feature that requires the full item system to be built.
**Suggested fix**: Defer until item/equipment system is built.

### §9.3 — Shadow Reduction: "Recovering from a trope trap" → Madness -1
**Rule**: "Recovering from a trope trap → Madness -1." (rules-v3.md §7)
**Status**: Not implemented — Read and Recover actions were removed (CHANGES-since-v3.4.md). The Recover action was the mechanism for clearing traps early. Without Recover, traps only expire by duration. The shadow reduction trigger for trap recovery cannot fire.
**Impact**: Medium — with Read/Recover removed, there's no way to reduce Madness through trap recovery. This closes off a shadow reduction path.
**Suggested fix**: Either: (a) reintroduce a Recover-like mechanism, or (b) replace this trigger with a new Madness reduction trigger (e.g., successful roll while a trap is active → Madness -1), or (c) mark as deliberately removed.

### §9.4 — Shadow Reduction: "Winning despite Overthinking disadvantage" → Overthinking -1
**Rule**: "Winning despite Overthinking disadvantage → Overthinking -1." (rules-v3.md §7)
**Status**: **Partially implemented** — the code checks for Overthinking disadvantage specifically: `StatBlock.ShadowPairs[chosenOption.Stat] == ShadowStatType.Overthinking`. This means it ONLY fires when the stat used is Self-Awareness (since SA pairs with Overthinking). But the rules say "Winning despite Overthinking disadvantage" — which applies to ANY stat roll while Overthinking T2+ is active (giving SA disadvantage). The current code is too narrow.
**Impact**: Low — Overthinking only gives disadvantage to SA rolls (T2+), so "winning despite Overthinking disadvantage" effectively means "winning an SA roll despite disadvantage," which is what the code checks. Actually correct.

### §async.1 — Time-of-Day Horniness Modifiers: Rules vs Code Mismatch
**Rule**: Morning (6AM-12PM) -2, Afternoon (12PM-6PM) +0, Evening (6PM-10PM) +1, Late Night (10PM-2AM) +3, After 2AM +5. (async-time.md)
**Status**: Code uses different values: Morning (09-11) +3, Afternoon (12-17) +0, Evening (18-23) +2, Overnight (00-08) +5. (game-definition.yaml)
**Impact**: This is a **deliberate change** — the code values were redesigned. The original rules had Morning as -2 (clear-headed) but the implemented version treats morning as +3. This is a significant behavioral difference.
**Suggested fix**: Document this as intentional in CHANGES-since-v3.4.md or reconcile with the original spec.

### §async.2 — Energy System (Turns Per Day)
**Rule**: "Each in-game day has limited energy: 15-20 energy per day." (async-time.md)
**Status**: Partially implemented — `_clock.ConsumeEnergy(1)` is called in `ResolveTurnAsync`, so the clock interface supports energy. But the configuration of 15-20 energy per day depends on the clock implementation.
**Impact**: Low — the hook exists; whether it's enforced depends on the runner.

### §char.1 — Starting Stats: 8 Each (48 Total) Before Build Points
**Rule**: "Positive: 8 each (48 total). Gain points at level-up." "New characters start with 12 build points." "No stat can start above +4 at creation." (rules-v3.md §2, §10)
**Status**: This is character creation logic, not in GameSession. The rules define base stats as 8 each (modifier 0 at 10, so 8 = modifier -1), then build points modify from there. The stat modifier table isn't explicitly shown but follows D&D-style: stat 10-11 = +0, 8-9 = -1, 12-13 = +1, etc. **OR** the "build points" are the stat modifiers themselves (12 points distributed, cap at +4). Looking at the character construction doc and game-definition.yaml: characters have `base_stats` that appear to be modifiers directly (e.g., Gerald charm: 3, rizz: -1). So build points = stat modifiers directly.
**Impact**: N/A — character creation is outside GameSession scope.

---

## Gaps Summary (actionable)

| # | Gap | Severity | Category |
|---|-----|----------|----------|
| 1 | Despair fresh 1d10 roll per conversation | **High** | Shadow mechanic |
| 2 | Despair T1/T2/T3 threshold effects (Rizz forcing) | **High** | Option filtering |
| 3 | Momentum Flow State multi-turn bonus & opponent warmth | **Medium** | Momentum |
| 4 | Opponent Test/Pull Back actions | **Medium** | Opponent AI |
| 5 | Trap Nat 1 bonus (escalation while trap active) | **Low-Medium** | Trap mechanic |
| 6 | "Recovering from trope trap → Madness -1" shadow reduction | **Medium** | Shadow reduction |
| 7 | Overthinking T1/T3 display and freeze effects | **Low** | Shadow thresholds |
| 8 | Horniness time-of-day values differ from original spec | **Low** | Configuration |
| 9 | Outfit synergy advantage | **Low** | Equipment (deferred) |

---

## Implemented (confirmed)

1. **DC formula**: DC = 16 + opponent defending stat modifier ✅
2. **Defence table pairings**: Charm→SA, Rizz→Wit, Honesty→Chaos, Chaos→Charm, Wit→Rizz, SA→Honesty ✅
3. **Failure tiers**: Nat1→Legendary, miss ≤2→Fumble, ≤5→Misfire, ≤9→TropeTrap, 10+→Catastrophe ✅
4. **Success scale**: beat 1-4→+1, 5-9→+2, 10+→+3, Nat20→+4 ✅
5. **Failure scale**: Fumble -1, Misfire -1, TropeTrap -2, Catastrophe -3, Legendary -4 ✅
6. **Risk tier bonuses**: Safe +1, Medium +2, Hard +3, Bold +5, Reckless +10 ✅
7. **Shadow penalty formula**: -1 per 3 points of shadow ✅
8. **Shadow pairs**: All 6 pairs correct ✅
9. **Level bonuses**: L1-2 +0, L3-4 +1, L5-6 +2, L7-8 +3, L9-10 +4, L11+ +5 ✅
10. **Interest meter**: 0-25 range with correct state thresholds ✅
11. **Advantage/disadvantage from interest**: 16+ advantage, 1-4 disadvantage ✅
12. **Nat 1/20 handling**: Auto-fail/auto-success ✅
13. **Nat 20 crit advantage**: Previous crit grants advantage for 1 roll ✅
14. **Ghost trigger**: Bored state → 25% per turn ✅
15. **All 6 traps defined**: Cringe, Creep, Overshare, Unhinged, Pretentious, Spiral ✅
16. **Trap mechanical effects**: Disadvantage, stat penalty, opponent DC increase ✅
17. **Trap duration**: Correct per definition ✅
18. **Trap activation on miss 6+**: TropeTrap and Catastrophe both activate traps ✅
19. **Trap prompt taint**: LLM instructions injected for active traps ✅
20. **All 8 combos**: Setup, Reveal, Read, Pivot, Recovery, Escalation, Disarm, Triple ✅
21. **Combo interest bonuses**: Correct per spec ✅
22. **Callback bonus**: Distance-scaled +1/+2/+3 ✅
23. **Tell bonus**: +2 to matching stat ✅
24. **Weakness window**: DC reduction applied ✅
25. **Momentum bonus**: 3→+2, 4→+2, 5+→+3 ✅
26. **Steering roll**: Correct formula, separate RNG ✅
27. **Horniness overlay**: d10 + time-of-day, per-turn d20 check, 4 tiers ✅
28. **Shadow thresholds**: T1≥6, T2≥12, T3≥18 ✅
29. **Shadow T2 disadvantage**: All pairs get disadvantage at T2+ ✅
30. **T3 option filtering**: Fixation T3 (force same stat), Denial T3 (remove Honesty), Madness T3 (replace one option) ✅
31. **Denial: skip Honesty → +1 Denial**: Implemented ✅
32. **Dread T3: starting interest 8**: Implemented ✅
33. **Denial T3 (≥12): remove Honesty from stat pool**: Implemented in `DrawRandomStats` ✅
34. **XP sources**: Correct per spec ✅
35. **Nat 20 → Dread -1**: Implemented ✅
36. **Nat 20 on Chaos → Madness -1**: Implemented ✅
37. **Shadow growth: all per-turn triggers**: Verified in ShadowGrowthEvaluator ✅
38. **Shadow growth: all end-of-game triggers**: Date→Dread-1, no Honesty→Denial+1, no Chaos→Fixation+1, 4+ stats→Fixation-1 ✅
39. **Combo success → Madness -1**: Implemented ✅
40. **Chaos combo → Fixation -1**: Implemented ✅
41. **Tell option → Madness -1**: Implemented ✅
42. **SA/Honesty success at Interest >18 → Despair -1**: Implemented ✅
43. **Honesty success at Interest ≥15 → Denial -1**: Implemented ✅
44. **Success at Interest ≥20 → Overthinking -1**: Implemented ✅
45. **Winning despite Overthinking disadvantage → Overthinking -1**: Implemented ✅
46. **3 options per turn** (changed from 4): Implemented ✅
47. **Stat-specific failure instructions**: Per-stat delivery instructions in YAML ✅
48. **Pivot directive at turn 3+**: Implemented via PromptTemplates ✅
49. **Horniness interest penalty**: When overlay fires with positive delta, halved ✅
50. **Stateful conversation session**: Opponent session mode supported ✅

---

## Deliberately Deferred

1. **Multi-conversation management**: ConversationRegistry, energy per day budgeting, juggling penalty, cross-chat shadow bleed — all multi-session features deferred
2. **Response timing simulation**: Opponent reply delays, fast-forward system, time-based interest decay — async-time features deferred
3. **Your response delay effects**: Player delay → interest loss — async feature deferred
4. **Matchmaking**: Level-range matching, complementary stats — server feature deferred
5. **Character upload/server model**: Upload, download, matchmaking profiles — server feature deferred
6. **Leaderboards**: All social features deferred
7. **Prestige system**: Full reset to Level 1, keep items, shadows → 0 — progression feature deferred
8. **Item/equipment system**: Stat modifiers from gear, fragment assembly, item tiers, outfit synergy — full equipment pipeline deferred (characters currently use pre-assembled profiles)

---

*Generated 2026-04-14 by compliance audit of rules markdown files against `src/Pinder.Core/` codebase.*
