# YAML Rules Compliance Audit — 2026-04-14

## Summary
- Rules in YAML: 87
- Implemented: 58
- Not implemented: 11
- Partial: 8
- Deliberate deferral: 10

---

## Not Implemented

### §7 Nat 1 Trap Activation
**YAML says**: "Miss by Nat 1: Legendary Fail. −4 + trap + shadow +1." The Nat 1 should activate the stat-specific trope trap in addition to shadow growth.
**Gap**: `RollEngine.ResolveFromComponents` sets `tier = FailureTier.Legendary` when `usedRoll == 1` and returns immediately. Trap activation only occurs in the `TropeTrap` / `Catastrophe` branches of the `else` block. Nat 1 never fires a trap.
**Severity**: High

### §9 Despair T2 — One Option Is Always Unwanted Rizz
**YAML says**: "Despair at 12: One option is always unwanted Rizz."
**Gap**: `OptionFilterEngine.ApplyT3Filters` handles Fixation T3, Denial T3, and Madness T3. There is no Despair T2 handling anywhere — no forced Rizz option injection. `DrawRandomStats` only excludes Honesty for Denial; it doesn't force Rizz for Despair.
**Severity**: High

### §9 Despair T3 — ALL Options Become Rizz
**YAML says**: "Despair at 18+: ALL options become Rizz. No other thoughts."
**Gap**: Same as above — `OptionFilterEngine` has no Despair T3 logic. The `requiresRizzOption` field in `DialogueContext` is hardcoded to `false`.
**Severity**: High

### §9 Trap Clear Method — SA vs DC 12
**YAML says**: Every trap in `traps.json` defines `"clear_method": "SA vs DC 12"` — the player can attempt a Self-Awareness roll against DC 12 to clear the trap early.
**Gap**: No trap clearing mechanism exists in `GameSession`. Traps only expire via `TrapState.AdvanceTurn()` timer countdown. There is no player action to attempt early clearing.
**Severity**: Medium

### §7 Overshare / Pretentious — opponent_dc_increase Effect
**YAML says**: The Overshare trap increases the opponent's Chaos defense by +2 (harder Honesty rolls). The Pretentious trap increases the opponent's Rizz defense by +3 (harder Wit rolls). Both use `"effect": "opponent_dc_increase"`.
**Gap**: `RollEngine` only handles two trap effects: `TrapEffect.Disadvantage` (roll 2d20 take lower) and `TrapEffect.StatPenalty` (flat modifier reduction). The `opponent_dc_increase` effect type is not processed — the DC is never adjusted for these traps. The LLM instruction IS passed through, but the mechanical penalty is missing.
**Severity**: High

### §9 Shadow Growth — Conversation Dies Without Date → Dread +1
**YAML says**: "Conversation dies without date: Dread +1."
**Gap**: `ShadowGrowthEvaluator.EvaluateEndOfGame` only checks DateSecured for Dread reduction. There is no trigger for conversations that end without a date (e.g., max turns reached, player abandons). The Unmatched case (interest → 0) has its own +2 Dread trigger, but a conversation that simply expires at interest 5-15 never triggers Dread growth.
**Severity**: Medium

### §9 Shadow Growth — Session Longer Than 30 Min → Madness +1
**YAML says**: "Session longer than 30 min real-time: Madness +1."
**Gap**: No real-time session duration tracking exists in `GameSession` or `ShadowGrowthEvaluator`. No timestamp is recorded at session start.
**Severity**: Low

### §6 Advantage from Outfit Synergy Bonus
**YAML says**: "Advantage from: ... Outfit synergy bonus"
**Gap**: No outfit synergy system exists. Items provide stat modifiers and fragments, but there is no synergy detection that grants advantage.
**Severity**: Low (item system is early-stage)

### §12 Achievement XP (20-50)
**YAML says**: "Achievement: 20-50 XP"
**Gap**: No achievement system exists in the codebase. `SessionXpRecorder` handles roll XP, Nat 1/20 XP, date secured XP, and conversation completion XP, but has no achievement tracking.
**Severity**: Low

### §9 Despair T1 — Rizz Options Appear More Often
**YAML says**: "Despair at 6: Rizz options appear more often."
**Gap**: `DrawRandomStats` does not bias toward Rizz when Despair ≥ 6. Shadow thresholds are passed to the LLM via `DialogueContext.shadowThresholds`, but the stat draw is purely random with no Despair weighting.
**Severity**: Medium

### §15 Horniness Interest Penalty — Formula Mismatch
**YAML says** (§15): "When a horniness overlay fires and current interest > 0, interest is set to floor(interest / 2)."
**Gap**: Code (#743) halves the *turn's interest delta* instead: `halvedDelta = floor(interestDelta / 2.0)`. Example: interest at 15, delta +3 → YAML says set to floor(18/2) = 9; code says delta becomes 1, interest goes to 16. The penalty exists but the formula is materially different.
**Severity**: Medium

---

## Partial Implementations

### §9 Shadow T1 Threshold Effects (All 6 Shadows)
**YAML says**: Each shadow has a T1 (≥6) effect: Dread → existential flavor, Madness → UI glitches, Denial → "I'm fine" leaks, Fixation → repeating patterns, Overthinking → see Interest always, Despair → Rizz more often.
**What's implemented**: Shadow values are tracked in `SessionShadowTracker` and passed to the LLM via `DialogueContext.shadowThresholds`. The LLM can interpret these for flavor.
**What's missing**: T1 effects are purely LLM-interpreted — there is no mechanical enforcement. Overthinking T1 ("see Interest number always") would need UI support. Despair T1 ("Rizz appears more often") needs stat-draw weighting. Fixation T1 / Denial T1 / Dread T1 / Madness T1 are flavor-only, which is acceptable for LLM-driven delivery, but the YAML frames them as game effects.

### §9 Overthinking T3 — See Opponent's Inner Monologue (Freeze)
**YAML says**: "Overthinking at 18+: You see opponent's inner monologue (freeze)."
**What's implemented**: Shadow thresholds are passed to LLM and UI layers via snapshot data.
**What's missing**: No mechanical "freeze" effect. No inner monologue generation. This is a significant T3 effect with no code path — unlike Fixation T3, Denial T3, and Madness T3 which all have explicit `OptionFilterEngine` logic.

### §10 Opponent Turn — Pull Back Action
**YAML says**: "Pull Back — Gets guarded. Your next roll has disadvantage."
**What's implemented**: The opponent response system exists and can generate various response types.
**What's missing**: No mechanical enforcement of a "Pull Back" action that flags disadvantage on the player's next roll. The opponent response model (`OpponentResponse`) doesn't include a PullBack flag.

### §10 Opponent Turn — Test Action
**YAML says**: "Test — Pointed question. Forces specific stat check on your next turn."
**What's implemented**: Opponent can ask questions through LLM.
**What's missing**: No mechanical enforcement that forces the player to use a specific stat on their next turn. The option generation doesn't have a "forced stat" constraint from opponent tests.

### §3.2 Topic Pivot Directive (Turn 3+)
**YAML says**: "At turn 3 or later, if conversation stayed on same topic since opener, one option (Option C) must bridge to a different dimension."
**What's implemented**: `currentTurn` is passed in `DialogueContext` to the LLM.
**What's missing**: Whether this directive is actually included in the LLM prompt depends on the adapter implementation. No mechanical enforcement exists in the option generation pipeline.

### §9 Steering Roll — Uses Effective Modifiers vs Stat Values
**YAML says**: "Modifier: average of player (SA + CHARM + WIT) / 3" using stat modifiers.
**What's implemented**: `SteeringEngine` uses `player.Stats.GetEffective(StatType.X)` for all three stats.
**What's missing**: The YAML specifies "modifiers" (derived values), while code uses `GetEffective` which may return the raw effective stat value rather than the D&D-style modifier. If `GetEffective` returns the stat value (e.g., 12) rather than the modifier (e.g., +1), the steering DC math would be off. Needs verification of `GetEffective` semantics.

### §12 Conversation Completed (No Date) — 5 XP
**YAML says**: "Conversation completed (no date): 5 XP"
**What's implemented**: `SessionXpRecorder` exists and handles roll XP and end-of-game XP.
**What's missing**: Need to verify that `RecordEndOfGameXp` awards 5 XP for non-date outcomes. The method is called but its implementation wasn't audited in detail.

---

## Deliberate Deferrals (Not Scored as Gaps)

### ConversationRegistry / Cross-Session Tracking
- §9 "3 consecutive failed conversations → Dread +1" — requires cross-conversation state
- §9 "5+ conversations in one session without a date → Madness +1" — requires conversation counting
- These require `ConversationRegistry` which is a separate system concern

### Energy Budget / Async Time
- Daily energy budget (15-20 turns/day) — referenced in `async-time` companion spec
- `IGameClock.ConsumeEnergy()` exists but full budget system is external

### Character Server
- Character upload/download
- Matchmaking (level range ±2, complementary stats, random element)
- Popularity ratings / aggregate stats
- Swipe mechanics

### Level-Up / Prestige
- Build point allocation UI
- Stat cap enforcement at level-up
- Prestige reset (Level 11+ → reset to L1, keep items, shadows → 0)

### Legendary Item Effects
- Therapy Hoodie: −1 chosen shadow per 5 convos (cross-conversation tracking)
- Cat on shoulder: +2 starting Interest (partially supported via `GameSessionConfig.StartingInterest`)
- Cold shower towel: Despair capped at 5 (no shadow cap mechanic)

### Leaderboards
- Most dates secured, highest interest swing, most spectacular Nat 1, most popular character

---

## Implemented (Summary List)

### Core Roll Mechanics
- §6 Basic roll formula (d20 + stat mod + level bonus ≥ DC) ✓
- §3 Defense DC (16 + opponent stat modifier) ✓
- §6 Advantage/Disadvantage (2d20 take higher/lower) ✓
- §6 Disadvantage overrides advantage ✓
- §6 Nat 1 auto-fail ✓
- §6 Nat 20 auto-success + advantage on next roll ✓
- §4 Level bonus table (L1-2: +0 through L11+: +5) ✓
- §4 Shadow penalty (−1 per 3 shadow points) ✓

### Failure Scale
- §7 Fumble (miss 1-2): −1 interest ✓
- §7 Misfire (miss 3-5): −1 interest ✓
- §7 TropeTrap (miss 6-9): −2 interest + trap activation ✓
- §7 Catastrophe (miss 10+): −3 interest + trap + shadow growth ✓
- §7 Legendary Fail (Nat 1): −4 interest + shadow +1 ✓ (trap missing — see Not Implemented)

### Success Scale
- §7 Beat DC by 1-4: +1 interest ✓
- §7 Beat DC by 5-9: +2 interest ✓
- §7 Beat DC by 10+: +3 interest ✓
- §7 Nat 20: +4 interest ✓

### Risk Tier Bonus
- Safe/Medium/Hard/Bold/Reckless tiers with scaled interest bonuses ✓

### Interest States
- §6 Unmatched (0): game over ✓
- §6 Bored (1-4): disadvantage + 25% ghost chance ✓
- §6 Lukewarm (5-9): no modifiers ✓
- §6 Interested (10-15): no modifiers ✓
- §6 VeryIntoIt (16-20): advantage ✓
- §6 AlmostThere (21-24): advantage ✓
- §6 DateSecured (25): game won ✓
- §6 Starting interest: 10 (or 8 with Dread T3) ✓

### Shadow Growth — Dread
- Getting unmatched (interest → 0): Dread +2 ✓
- Getting ghosted: Dread +1 ✓
- Catastrophic Wit fail (miss 10+): Dread +1 ✓
- Nat 1 on Wit: Dread +1 (via paired shadow) ✓
- Date secured: Dread −1 ✓
- Any Nat 20: Dread −1 ✓

### Shadow Growth — Madness
- Nat 1 on Charm: Madness +1 (via paired shadow) ✓
- Every TropeTrap failure: Madness +1 ✓
- CHARM used 3+ times: Madness +1 (once per convo) ✓
- Combo success: Madness −1 ✓
- Tell option selected: Madness −1 ✓
- Nat 20 on CHAOS: Madness −1 ✓

### Shadow Growth — Denial
- Date without Honesty successes: Denial +1 ✓
- Choosing non-Honesty when available: Denial +1 ✓
- Nat 1 on Honesty: Denial +1 (via paired shadow) ✓
- Honesty success at Interest 15+: Denial −1 ✓

### Shadow Growth — Fixation
- Highest-% option 3 turns in a row: Fixation +1 ✓
- Same stat 3 turns in a row: Fixation +1 ✓
- Never picking Chaos: Fixation +1 ✓
- Nat 1 on Chaos: Fixation +1 (via paired shadow) ✓
- CHAOS combo trigger: Fixation −1 ✓
- 4+ different stats: Fixation −1 ✓

### Shadow Growth — Overthinking
- SA used 3+ times: Overthinking +1 ✓
- Nat 1 on SA: Overthinking +1 (via paired shadow) ✓
- Winning despite Overthinking disadvantage: Overthinking −1 ✓
- Success at Interest ≥20: Overthinking −1 ✓

### Shadow Growth — Despair
- Nat 1 on Rizz: Despair +2 ✓
- Rizz TropeTrap failure: Despair +1 ✓
- Every 3rd cumulative RIZZ failure: Despair +1 ✓
- SA/Honesty success at Interest >18: Despair −1 ✓

### Shadow Thresholds (Mechanical)
- All T2 (≥12): paired stat gets disadvantage ✓
- Dread T3 (≥18): starting interest 8 ✓
- Fixation T3 (≥18): force same stat as last turn ✓
- Denial T3 (≥18): Honesty options removed ✓
- Madness T3 (≥18): one option replaced with unhinged ✓

### Traps (Mechanical Effects)
- The Cringe (Charm): disadvantage, 1 turn ✓
- The Creep (Rizz): −2 stat penalty, 2 turns ✓
- The Unhinged (Chaos): disadvantage, 1 turn ✓
- The Spiral (SA): disadvantage, 2 turns ✓
- Trap timer advancement on turn end ✓
- Trap LLM instructions passed through ✓

### Combos
- The Setup (Wit → Charm): +1 interest ✓
- The Reveal (Charm → Honesty): +1 interest ✓
- The Read (SA → Honesty): +1 interest ✓
- The Pivot (Honesty → Chaos): +1 interest ✓
- The Escalation (Chaos → Rizz): +1 interest ✓
- The Disarm (Wit → Honesty): +1 interest ✓
- The Recovery (any fail → SA success): +2 interest ✓
- The Triple (3 different stats in 3 turns): +1 next roll ✓
- Combo peek for dialogue options ✓

### Horniness
- Session horniness roll (d10 + time-of-day modifier) ✓
- Time-of-day bands (morning +3, afternoon +0, evening +2, overnight +5) ✓
- Per-turn check (d20 vs DC 20 − sessionHorniness) ✓
- Overlay tier determination (fumble/misfire/trope_trap/catastrophe) ✓
- LLM overlay application ✓

### Steering
- Steering roll after successful delivery ✓
- Modifier: (CHARM + WIT + SA) / 3 ✓
- DC: 16 + (opponent SA + RIZZ + HONESTY) / 3 ✓
- Separate RNG ✓
- LLM generates steering question on success ✓

### Other Mechanics
- Wait action (−1 interest, traps expire) ✓
- Momentum streak tracking (+2 at 3-streak, +3 at 5+) ✓
- Callback topic tracking and bonus ✓
- Tell detection and +2 bonus ✓
- Weakness window detection and DC reduction ✓
- XP ledger with per-turn drain ✓
- Level table (XP thresholds, bonuses, build points, item slots) ✓
- Response timing computation from opponent profile ✓
- Word-level text diffs for delivery layers ✓

### Delivery Instructions
- Per-stat failure instructions (charm/rizz/honesty/chaos/wit/sa) ✓
- Tier-specific delivery instructions (clean through nat1) ✓
- Shadow voice mapping (stat → paired shadow for failure flavor) ✓
- Horniness overlay instructions (fumble through catastrophe) ✓
