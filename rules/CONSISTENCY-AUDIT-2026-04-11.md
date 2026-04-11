# Consistency Audit — 2026-04-11

## Summary
**8 discrepancies found, 3 notable, 5 minor/cosmetic.**

---

## Verified Consistent

### DC Formula
- **Code** (`StatBlock.GetDefenceDC`): `DC = 16 + defending stat's effective modifier` ✅
- **Rules YAML** (`§3.3-the-other-character-opponent`): `Defense DC = 16 + their stat modifier` ✅
- **Defence table pairings** (Charm→SA, Rizz→Wit, Honesty→Chaos, Chaos→Charm, Wit→Rizz, SA→Honesty) match in code, rules YAML, and game-definition.yaml world description ✅

### Failure Tiers (miss margins)
- **Code** (`RollEngine.ResolveFromComponents`): Nat1 → Legendary, miss ≤2 → Fumble, ≤5 → Misfire, ≤9 → TropeTrap, 10+ → Catastrophe ✅
- **Rules YAML** (`§7.fail-tier.*`): miss 1-2 → Fumble, 3-5 → Misfire, 6-9 → Trope Trap, 10+ → Catastrophe, Nat 1 → Legendary ✅
- Match confirmed ✅

### Success Scale (interest deltas)
- **Code** (`SuccessScale.GetInterestDelta`): beat 1-4 → +1, 5-9 → +2, 10+ → +3, Nat20 → +4 ✅
- **Rules YAML** (`§7.success-scale.*`): identical ✅

### Failure Scale (interest deltas)
- **Code** (`FailureScale.GetInterestDelta`): Fumble -1, Misfire -1, TropeTrap -2, Catastrophe -3, Legendary -4 ✅
- **Rules YAML** (`§7.fail-tier.*`): Fumble -1, Misfire -1, TropeTrap -2, Catastrophe -3, Legendary -4 ✅
- Match confirmed ✅ (Note: rules YAML §6.natural-1 says interest_delta: -5 in the structured block but the fail severity table says -4. The code uses -4 via FailureScale, which matches the §7 table. See Discrepancy #1.)

### Risk Tier Bonuses
- **Code** (`RiskTierBonus`): Safe +1, Medium +2, Hard +3, Bold +5, Reckless +10 ✅
- **Code** (`RiskTier` enum comments): Safe need 1-7, Medium 8-11, Hard 12-15, Bold 16-19, Reckless 20+ ✅
- **Code** (`RollResult.ComputeRiskTier`): need ≤7 Safe, ≤11 Medium, ≤15 Hard, ≤19 Bold, 20+ Reckless ✅
- **Rules YAML**: Risk tier bonuses are not explicitly enumerated in the enriched YAML (they come from the risk-reward design spec, not the core rules doc). No contradiction — just not documented in rules-v3-enriched.yaml.

### Shadow Thresholds
- **Code** (`ShadowThresholdEvaluator.GetThresholdLevel`): ≥6 → T1, ≥12 → T2, ≥18 → T3 ✅
- **Rules YAML** (`§9.shadow-threshold.*`): All 6 shadows at thresholds 6/12/18 ✅
- Match confirmed ✅

### Shadow Pairs
- **Code** (`StatBlock.ShadowPairs`): Charm→Madness, Rizz→Despair, Honesty→Denial, Chaos→Fixation, Wit→Dread, SA→Overthinking ✅
- **Rules YAML** (`§4.stat-pair.*`): Identical ✅
- **game-definition.yaml** world_description: Identical ✅

### Shadow Penalty Formula
- **Code** (`StatBlock.GetEffective`): penalty = floor(shadowValue / 3) ✅
- **Rules YAML** (`§4.shadow-penalty`): -1 per 3 points ✅

### Level Bonuses
- **Code** (`LevelTable.LevelBonuses`): L1-2 +0, L3-4 +1, L5-6 +2, L7-8 +3, L9-10 +4, L11+ +5 ✅
- **Rules YAML** (`§4.level-bonus.*`): Identical ✅

### Interest Meter States
- **Code** (`InterestMeter.GetState`): 0=Unmatched, 1-4=Bored, 5-9=Lukewarm, 10-15=Interested, 16-20=VeryIntoIt, 21-24=AlmostThere, 25=DateSecured ✅
- **Rules YAML** (`§6.interest-state.*`): Identical ✅

### Advantage/Disadvantage from Interest
- **Code** (`InterestMeter.GrantsAdvantage`): VeryIntoIt or AlmostThere → advantage ✅
- **Code** (`InterestMeter.GrantsDisadvantage`): Bored → disadvantage ✅
- **Rules YAML** (`§6.advantage-disadvantage`): Interest 15+ advantage, Interest ≤4 disadvantage ✅
- Note: Code uses state-based (16-20 + 21-24), rules say "15+". Interest 15 is `Interested` in code (no advantage). See Discrepancy #3.

### Nat 1 / Nat 20 Handling
- **Code** (`RollEngine`): Nat 1 = auto-fail (Legendary tier), Nat 20 = auto-success ✅
- **Rules YAML**: Nat 1 auto-fail, Nat 20 auto-success + crit ✅

### Trap Definitions
- **Code** (traps.json): 6 traps matching rules — cringe/charm/disadvantage/1t, creep/rizz/stat_penalty(-2)/2t, overshare/honesty/opponent_dc_increase(+2)/1t, unhinged/chaos/disadvantage/1t, pretentious/wit/opponent_dc_increase(+3)/1t, spiral/sa/disadvantage/2t ✅
- **Rules YAML** (`§7.trope-trap.*`): The Cringe (Charm, disadvantage, 1t), The Creep (Rizz, -2, 2t), The Overshare (Honesty, opponent Chaos +2, 1t), The Unhinged (Chaos, disadvantage, 1t), The Pretentious (Wit, opponent Rizz +3, 1t), The Spiral (SA, disadvantage, 2t) ✅
- Match confirmed ✅

### Horniness Time Modifiers
- **game-definition.yaml**: morning 3, afternoon 0, evening 2, overnight 5 ✅
- **Rules YAML** (`§9.horniness-ambient-overlay`): morning_09_11 +3, afternoon_12_17 +0, evening_18_23 +2, overnight_00_08 +5 ✅
- **Code** (`GameClock.GetHorninessModifier`): 09-11 → Morning, 12-17 → Afternoon, 18-23 → Evening, 00-08 → Overnight ✅
- Match confirmed ✅

### Horniness Overlay Tiers
- **Code** (`GameSession.DetermineHorninessTier`): miss ≤2 Fumble, ≤5 Misfire, ≤9 TropeTrap, 10+ Catastrophe ✅
- **Rules YAML** (`§9.horniness-ambient-overlay`): Same ✅
- **delivery-instructions.yaml**: Has `horniness_overlay` section with fumble, misfire, trope_trap, catastrophe — all 4 tiers ✅

### Delivery Instructions Coverage
- **delivery-instructions.yaml**: Has entries for all 6 stats: charm, rizz, honesty, chaos, wit, sa ✅
- Each stat has: clean, strong, critical, exceptional, nat20, fumble, misfire, trope_trap, catastrophe, nat1 — **10 tiers each** ✅
- `horniness_overlay` has: fumble, misfire, trope_trap, catastrophe — **4 tiers** ✅

### Steering Roll
- **Code** (`GameSession.AttemptSteeringRollAsync`): modifier = (player.Charm + Wit + SA) / 3, DC = 16 + (opponent.SA + Rizz + Honesty) / 3, separate RNG ✅
- **Rules YAML** (`§9.steering-roll`): Identical formula ✅

### XP Sources
- **Code** (`GameSession.RecordRollXp`): Nat20→25, Nat1→10, success by DC tier → 5/10/15, failure→2 ✅
- **Code** (`GameSession.RecordEndOfGameXp`): DateSecured→50, Unmatched/Ghosted→5 ✅
- **Rules YAML** (`§12.xp-sources`): Successful check 5/10/15, Failed 2, Nat20 25, Nat1 10, Date secured 50, Conversation completed 5 ✅

### Combo Definitions
- **Code** (`ComboTracker`): 8 combos — The Setup (Wit→Charm +1), The Reveal (Charm→Honesty +1), The Read (SA→Honesty +1), The Pivot (Honesty→Chaos +1), The Escalation (Chaos→Rizz +1), The Disarm (Wit→Honesty +1), The Recovery (any fail→SA success +2), The Triple (3 different stats in 3 turns, +1 next roll) ✅
- **Rules YAML**: Combos are referenced in `§12.build-synergies` by name but not fully defined in rules-v3-enriched.yaml ✅ (combo definitions come from risk-reward spec, not core rules)
- No `data/combos.json` file exists — combos are hardcoded in `ComboTracker.cs`. Not a discrepancy per se, but noted.

### Character JSONs
- All 6 characters have `shadows` with all 6 shadow types (madness, despair, denial, fixation, dread, overthinking) ✅
- Starting Despair = 0 for all characters except Reuben (1). This is correct per rules: "Despair rolls fresh each conversation (1d10)" — the base value in JSON is the persistent accumulated despair, session despair is added at runtime. ✅
- `build_points` values for Gerald sum to 21 at level 5 (12 creation + 2+2+3 = 9 from L2-5 = 21). Actual allocation: 6+5+2+4+2+2 = 21 ✅

### Dread T3 Starting Interest
- **Code** (`GameSession` constructor): If Dread T3 (≥18), starting interest = 8 instead of 10 ✅
- **Rules YAML** (`§9.shadow-threshold.dread.t3`): "Starting Interest 8 instead of 10" ✅

---

## Discrepancies

### 1. Rules YAML Internal Inconsistency: Nat 1 Interest Delta (-4 vs -5)
- **Rules YAML `§6.natural-1`** structured block says `interest_delta: -5`
- **Rules YAML `§7.fail-tier.legendary-fail`** says `interest_delta: -4`
- **Rules YAML `§7.fail-severity-scale`** table says `-4 + trap + shadow +1`
- **Code** (`FailureScale.GetInterestDelta`): Legendary → **-4**
- **Assessment**: The `-5` in `§6.natural-1` is a stale value from an earlier rules version. The table in §7 and the code agree on **-4**. The YAML has an internal contradiction. **Action: Update `§6.natural-1` outcome `interest_delta` from -5 to -4.**

### 2. RollEngine Comment Says Base DC = 13, Code Uses 16
- **Code** (`RollEngine` class doc comment): `DC = 13 + opponent defending stat's effective modifier`
- **Code** (`StatBlock.GetDefenceDC`): `return 16 + GetEffective(defenceStat);`
- **Rules YAML** (`§3.3-the-other-character-opponent`): `Defense DC = 16 + their stat modifier`
- **Assessment**: The comment in `RollEngine.cs` is stale from a previous design iteration. The actual formula in `StatBlock.GetDefenceDC` correctly uses **16**. **Action: Update the RollEngine doc comment from 13 to 16.**

### 3. Advantage Threshold: Rules Say "Interest 15+" but Code Uses 16+
- **Rules YAML** (`§6.advantage-disadvantage`): "Interest at 15+ (they're warm)" → advantage
- **Code** (`InterestMeter.GrantsAdvantage`): Advantage only for `VeryIntoIt` (16-20) or `AlmostThere` (21-24)
- **Assessment**: Interest 15 falls in the `Interested` state (10-15) in code, which does NOT grant advantage. Rules YAML says 15+ grants advantage. The interest state table in §6 says advantage starts at 16 ("😍 Very Into It → Your rolls have advantage"), so the §6 advantage list is slightly misleading — it should say "Interest 16+" to match the state table. **Action: Clarify §6 advantage text from "Interest at 15+" to "Interest at 16+" (Very Into It state).**

### 4. Rules YAML `§5.dc-examples` Outcome Has `base_dc: 13`
- **Rules YAML** (`§5.dc-examples` structured data): `outcome.base_dc: 13`
- **Code**: Base DC is 16 everywhere
- **Assessment**: The structured `outcome` block in the DC examples section carries a stale `base_dc: 13` from an earlier rules version. The actual text examples in the same section correctly demonstrate DC = 16 math (e.g., "SA 12 (mod +1) → DC to Charm her = 14" only works with base 13, which is WRONG — it should be DC 17). Wait — checking: SA mod +1, base 13 → DC 14, but with base 16 → DC 17. The text examples use the old base 13 formula too!
- **Full breakdown**: The examples (Velvet SA 12 mod +1 → DC 14, Gerald SA 4 mod -3 → DC 10) only work with `DC = 13 + mod`, not `DC = 16 + mod`. These examples are stale and predate the DC change to 16.
- **Action: Rewrite the §5.dc-examples text to use base DC 16.** With the corrected formula: Velvet SA mod +1 → DC 17. Gerald SA mod -3 → DC 13.

### 5. Despair Fresh Roll (1d10) Not Implemented in Code
- **Rules YAML** (`§9.despair-penalizes-rizz`): "Despair rolls fresh each conversation (1d10)."
- **Code** (`GameSession` constructor): Rolls `_dice.Roll(10)` for **horniness**, not for Despair. There is no per-session Despair roll in the constructor or anywhere in GameSession.
- **`SessionShadowTracker`**: Has no Despair-specific logic; just tracks deltas from base.
- **Assessment**: The "Despair rolls fresh each conversation" mechanic from the rules is **not implemented** in code. Characters use their persistent `shadows.despair` value from JSON directly. This is a known design gap, not a config inconsistency.
- **Action: File issue — implement per-session Despair roll (1d10) that replaces the base Despair value at session start.**

### 6. game-definition.yaml Has No DC Bias Value; Code Supports It
- **Code** (`GameSession`): Accepts `GlobalDcBias` from `GameSessionConfig`, applies it as `dcAdjustment -= _globalDcBias`
- **game-definition.yaml**: No `dc_bias` or equivalent key
- **`GameDefinition.cs`** (YAML parser): Does not parse any DC bias field
- **Assessment**: The global DC bias is a runtime config parameter passed via `GameSessionConfig`, not from game-definition.yaml. It exists in code but is not exposed as a YAML-configurable value. Not a contradiction — it's an intentional runtime-only tuning knob. No action needed unless we want to make it YAML-configurable.

### 7. Horniness Time Band Mismatch Between GetTimeOfDay and GetHorninessModifier
- **Code** (`GameClock.GetTimeOfDay`): Morning = 6-11, Afternoon = 12-17, Evening = 18-21, LateNight = 22-01, AfterTwoAm = 2-5
- **Code** (`GameClock.GetHorninessModifier`): Morning = 09-11, Afternoon = 12-17, Evening = 18-23, Overnight = 00-08
- **game-definition.yaml**: morning 09-11, afternoon 12-17, evening 18-23, overnight 00-08
- **Assessment**: `GetTimeOfDay()` and `GetHorninessModifier()` use **different** time boundaries. `GetTimeOfDay` has a 5-bucket system (Morning starts at 6, Evening ends at 21, then LateNight 22-01, AfterTwoAm 2-5). `GetHorninessModifier` has a 4-bucket system (Morning starts at 9, Evening covers 18-23 including late night). These serve different purposes — `GetTimeOfDay` is for game state display, `GetHorninessModifier` is for the horniness mechanic — so the discrepancy is by design. However, **hours 6-8 fall in "Morning" for `GetTimeOfDay` but "Overnight" for `GetHorninessModifier`**. This is intentional (early morning = high horniness), but worth documenting.

### 8. Rules YAML References "Horniness" as Shadow; game-definition.yaml Uses "Despair"
- **Rules YAML `§4.stat-pair.rizz`**: Rizz paired with shadow **Despair**
- **game-definition.yaml** `world_description`: Lists `Rizz / Despair`
- **Older rules YAML text** (§4 table): Lists shadow as **Despair** with "What Causes It: Random each conversation (1d10)"
- **Code**: `ShadowStatType.Despair` paired with `StatType.Rizz`
- **Assessment**: All sources consistently use "Despair" as the shadow name. However, the game-definition.yaml `world_description` in the older inline text says "Horniness from late-night sessions" in the vision field. The vision field in game-definition.yaml says: "Madness from repeated failures, Horniness from late-night sessions, Dread from rejection, Overthinking from reading too much into everything." This uses the **pre-rename** shadow name "Horniness" instead of "Despair".
- **Action: Update game-definition.yaml `vision` field to say "Despair from desperate neediness" instead of "Horniness from late-night sessions".**

---

## Items Not in Scope (No Data File Exists)
- `data/combos.json` — does not exist. Combos are hardcoded in `ComboTracker.cs`. If data-driven combos are desired, file needs to be created.

---

## Summary Table

| # | Category | Severity | Action |
|---|----------|----------|--------|
| 1 | Nat 1 interest delta | Rules YAML internal inconsistency | Update §6.natural-1 from -5 to -4 |
| 2 | RollEngine DC comment | Stale code comment | Update comment from 13 to 16 |
| 3 | Advantage threshold | Misleading rules text | Clarify §6 from "15+" to "16+" |
| 4 | DC examples in rules | Stale examples using old base DC | Rewrite examples with base DC 16 |
| 5 | Despair fresh 1d10 roll | Missing implementation | File issue |
| 6 | DC bias in YAML | Not exposed (by design) | No action needed |
| 7 | Time band boundaries | Different systems, by design | Document the difference |
| 8 | "Horniness" vs "Despair" naming | Stale text in vision field | Update vision field |
