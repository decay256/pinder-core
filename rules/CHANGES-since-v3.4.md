# Rule Changes Since v3.4 (2026-04-04 to 2026-04-11)

This document catalogues every rule change made to the Pinder game engine since issue #442 was filed (2026-04-04). Source of truth: `GameSession.cs` code, cross-referenced against the git log.

---

## Shadow Growth Changes

### CHARM / Madness

- **Changed:** Every TropeTrap failure now grows Madness +1 (was: only on 3rd TropeTrap in one conversation). Commit `014b5ce`.
- **Added:** CHARM used 3+ times in one conversation → Madness +1 (once per conversation). Commit `5637b25`.
- **Added:** Combo success → Madness −1. Commit `5637b25`.
- **Added:** Tell option selected → Madness −1. Commit `5637b25`.
- **Added:** Nat 20 on CHAOS → Madness −1. Commit `4445294`.
- **Removed:** "Déjà vu (same opener twice in a row)" trigger — never implemented in code, only in YAML summary table.
- **Note:** "5+ conversations in one session without a date" and "Session longer than 30 min real-time" remain in YAML but are **not** in per-conversation `GameSession.cs` — they are multi-conversation/session-level rules tracked elsewhere or not yet implemented.

### RIZZ / Despair

- **Renamed:** Shadow renamed from Horniness → Despair. Commit `bcbe22a`.
- **Added:** Nat 1 on RIZZ → +2 Despair (all other stats: Nat 1 → +1 to paired shadow). Commit `bcbe22a`, refined in `bdd0e28`.
- **Added:** RIZZ TropeTrap failure → +1 Despair. Commit `bdd0e28`.
- **Added:** Every 3rd cumulative RIZZ failure in session → +1 Despair (uses running count, not consecutive). Commit `bdd0e28`.
- **Added:** SA or HONESTY success at Interest >18 → Despair −1. Commit `bdd0e28`.

### HONESTY / Denial

- (no changes — all 3 triggers verified correct in code and YAML)
  - Date secured without Honesty success → +1 Denial ✅
  - Choosing non-Honesty when Honesty available → +1 Denial ✅ (note: not yet implemented in `GameSession.cs` — the YAML rule exists but no code enforces it)
  - Nat 1 on Honesty → +1 Denial ✅
  - Honesty success at Interest ≥15 → Denial −1 ✅

### CHAOS / Fixation

- **Added:** CHAOS combo trigger → Fixation −1. Commit `0a1ba63`.
- **Reverted:** Commit `b22fd57` temporarily removed "Never picked Chaos → +1 Fixation" and replaced it with "Chaos never used → Madness −1". Subsequent shadow rules commits (`0a1ba63`, `5637b25`) **restored** the original Fixation +1 trigger. Current code: Never picked Chaos in whole conversation → **+1 Fixation** (original rule stands).
- **Note:** The Madness −1 for Chaos-never-used (from `b22fd57`) was removed in the restoration. It is **not** in the current code.

### WIT / Dread

- **Added:** Any Nat 20 (any stat) → Dread −1. Commit `de12c74`.
- **Added:** Date secured → Dread −1 (end-of-game). Already in code, newly codified.
- Existing triggers unchanged:
  - Catastrophic Wit failure (miss by 10+) → Dread +1 ✅
  - Interest hits 0 → Dread +2 ✅
  - Nat 1 on Wit → Dread +1 ✅

### SA / Overthinking

- **Added:** Success at Interest ≥20 → Overthinking −1. Commit `eba04fe`.
- **Added:** Winning despite Overthinking disadvantage → Overthinking −1 (already in code, verified).
- **Not applicable:** "Read action fail → Overthinking +1" and "Recover action fail → Overthinking +1" — these never existed in code. Read and Recover were separate actions, not shadow triggers.
- Existing triggers unchanged:
  - SA used 3+ times → Overthinking +1 ✅
  - Nat 1 on SA → Overthinking +1 ✅

---

## Mechanic Changes

### Horniness (ambient overlay, no longer a shadow stat)

Horniness is no longer a growing shadow stat. It was replaced by Despair as RIZZ's paired shadow.

- **Session Horniness** = d10 + time-of-day modifier (from `GameClock.GetHorninessModifier()`). Commit `da32a5d`, `50d83c4`, `4f928e4`.
- **Per-turn check:** d20 vs DC (20 − sessionHorniness). On miss, an overlay instruction is applied to the delivered message.
- **Tiers:** Fumble / Misfire / TropeTrap / Catastrophe (from `horniness_overlay` in `delivery-instructions.yaml`). Commit `beda823`.
- **Session header** shows 🌶️ Session Horniness roll.
- **Per-turn check result** shown in turn log.
- **GameClock required:** `GameSession` now throws if no clock is provided. Configurable time-of-day modifiers in `game-definition.yaml`. Commit `50d83c4`.

### Read and Recover Actions

- **Removed:** `ReadAsync()` and `RecoverAsync()` are gone — 238 lines of dead code plus ~3000 lines of tests removed. Commit `714e830`.
- Only **Speak** and **Wait** remain as player actions.

### TrapState Activation

- **Fixed:** `TrapState.Activate()` was called by `RollEngine.Resolve` but `_traps.AdvanceTurn()` ran BEFORE LLM calls, so duration-1 traps expired before any LLM context saw them. Commit `83c2e3c`.
- **Fix:** `AdvanceTurn()` moved to AFTER delivery and opponent LLM calls, so traps are visible on activation turn.
- Trap instructions now properly flow into delivery, dialogue, and opponent LLM contexts.

### Pivot Directive

- **Added:** At turn 3+, if the conversation has stayed on the same topic since the opener, Option C must bridge to a new character dimension. Commit `8309199`.
- Injected via `PromptTemplates.PivotDirective` into `SessionDocumentBuilder`.

### Stat-Specific Failure Corruption

- **Added:** Failure delivery instructions are now stat-specific: RIZZ failures use Despair-framing, WIT failures use Dread-framing, etc. Commit `5053359`, `a31353c`, `588b9c9`.
- Source: `delivery-instructions.yaml` per-stat × per-tier entries now reach the LLM.

### Steering Roll

- **Added:** At turn end, a steering roll is attempted: average of player's (SA + CHARM + WIT) vs DC 16 + average of opponent's (SA + RIZZ + HONESTY) effective modifiers. On success, a date-steering question is appended. Commit `20a1c27`.

### Session Log Enhancements

- Level bonus shown in roll display. Commit `a4df952`.
- Nat 1/20 shows "always fails" / "always succeeds" explanation. Commit `49afecd`.
- Shadow events shown as 📊 summary each turn. Commit `8f149a4`.
- Active traps shown with penalty and turns remaining 🪤. Commit `8f149a4`.
- Momentum streak shown ⚡. Commit `8f149a4`.
- Interest tier shown 💡. Commit `df65c81`.
- Interest delta breakdown shown (roll success + risk bonus + combo). Commit `df65c81`.
- Shadow hints shown per option (⚠️ growth, ✨ reduction). Commit `72ef48e`.

### Failure/Success Delivery

- Failure delivery rewritten from first principles — corruption from within, no appending. Commit `40467b0`.
- Success delivery must rewrite, not extend — hard prohibition on appending. Commit `a484dad`.
- Improvement loop added (two-stage). Commit `6132f12`.

### Options & Display

- Options reduced from 4 to 3 random stats per turn. Commit `cb73247`.
- Risk interest bonus shown on each option. Commit `9c47fc5`.
- DC reference table shows opponent's defending stat + modifier. Commit `c1a70b1`.
- Structured outputs via Anthropic tool_use for all LLM calls. Commit `a8228ec`.

---

## DC / Roll Changes

- **DC base:** 16 (was 13 — changed pre-#442 but test fixtures updated in `548eb4d`).
- **Risk tier bonuses (interest delta on success):**
  - Safe (need 1–7): +1
  - Medium (need 8–11): +2
  - Hard (need 12–15): +3
  - Bold (need 16–19): +5
  - Reckless (need 20+): +10

---

## Complete Shadow Trigger Reference (Current Code)

### Per-Turn Triggers (EvaluatePerTurnShadowGrowth)

| # | Trigger | Shadow | Delta | Notes |
|---|---------|--------|-------|-------|
| 1 | Nat 1 on any stat | Paired shadow | +1 (+2 for RIZZ) | RIZZ Nat 1 → +2 Despair |
| 2 | Catastrophic Wit failure (miss by 10+) | Dread | +1 | |
| 3 | Every TropeTrap+ failure (excl Legendary) | Madness | +1 | Changed from "3rd TropeTrap" |
| 3b | RIZZ TropeTrap+ failure (excl Legendary) | Despair | +1 | Stacks with Trigger 3 if stat is RIZZ |
| 3c | Every 3rd cumulative RIZZ failure | Despair | +1 | Running count, resets per session |
| 4 | Same stat 3 turns in a row | Fixation | +1 | Every 3 consecutive |
| 5 | Highest-% option 3 turns in a row | Fixation | +1 | Every 3 consecutive |
| 6 | Honesty success at Interest ≥15 | Denial | −1 | Reduction |
| — | SA/Honesty success at Interest >18 | Despair | −1 | Reduction |
| 7 | Interest hits 0 | Dread | +2 | |
| 9 | SA used 3+ times | Overthinking | +1 | Once per conversation |
| 15 | CHARM used 3+ times | Madness | +1 | Once per conversation |
| — | Combo success (any) | Madness | −1 | Reduction |
| — | CHAOS combo | Fixation | −1 | Reduction (stacks with Madness −1) |
| — | Tell option selected | Madness | −1 | Reduction |

### Nat 20 Triggers (in main turn flow, before EvaluatePerTurnShadowGrowth)

| Trigger | Shadow | Delta |
|---------|--------|-------|
| Nat 20 on CHAOS | Madness | −1 |
| Nat 20 on any stat | Dread | −1 |

### Post-Turn Triggers (after EvaluatePerTurnShadowGrowth)

| Trigger | Shadow | Delta |
|---------|--------|-------|
| Success despite Overthinking disadvantage | Overthinking | −1 |
| Success at Interest ≥20 | Overthinking | −1 |

### End-of-Game Triggers (EvaluateEndOfGameShadowGrowth)

| Trigger | Shadow | Delta |
|---------|--------|-------|
| Date secured | Dread | −1 |
| Date secured without Honesty success | Denial | +1 |
| Never picked Chaos | Fixation | +1 |
| 4+ different stats used | Fixation | −1 |

---

## YAML Gaps

The following discrepancies exist in `rules/extracted/rules-v3-enriched.yaml` and need patching:

### Missing from YAML (in code, not in YAML)

1. **Nat 20 on CHAOS → Madness −1** — no YAML entry exists for this trigger.
2. **Steering roll mechanic** — no YAML documentation of the steering roll (SA+CHARM+WIT averaged vs opponent defences, DC 16 + opponent avg).
3. **Horniness ambient overlay system** — the full per-turn d20 check mechanic is not documented in rules YAML (only delivery-instructions.yaml has the overlay text).
4. **Pivot directive** — exists in YAML (added in `8309199`) but only as a prompt instruction entry, not as a formal rule.

### Incorrect in YAML (code differs from YAML)

5. **Madness summary table** (`§9.madness-penalizes-charm`): Still shows "3+ trope traps in one conversation → Madness +1" — should be "Every TropeTrap failure → Madness +1".
6. **Madness summary table**: Still shows "Déjà vu (same opener twice in a row) → Madness +1" — this trigger does not exist in code and should be removed.
7. **Madness summary table**: Missing entries for CHARM 3+ usage, combo reduction, tell reduction.
8. **Denial trigger** (`§9.shadow-growth.choosing-a-non-honesty-option-when-hones`): "Choosing a non-Honesty option when Honesty was available → Denial +1" exists in YAML but is **not implemented in code**. Either implement or mark as design-only.
9. **Despair table** (`§9.despair-penalizes-rizz`): Correct for the 3 triggers but missing the SA/Honesty success at >18 reduction in the same table block.
10. **Shadow reduction table** (`§9.shadow-reduction`): Missing Nat 20 on CHAOS → Madness −1 and combo success → Madness −1 entries.
11. **Dread table** (`§9.dread-penalizes-wit`): Missing "Date secured → Dread −1" and "Nat 20 (any) → Dread −1" reductions (these are in individual entries but not in the summary table).
12. **Overthinking table** (`§9.overthinking-penalizes-self-awareness`): Missing "Success at Interest ≥20 → Overthinking −1" and "Winning despite Overthinking disadvantage → Overthinking −1" entries.

### Stale in YAML (removed from code)

13. **Read/Recover references**: The YAML was cleaned in commit `714e830` (52 line diff in rules-v3-enriched.yaml), but verify no stale Read/Recover shadow triggers remain in related YAML files.
14. **"Getting ghosted → Dread +1"** and **"Conversation dies without date → Dread +1"** and **"3 consecutive failed conversations → Dread +1"**: These are multi-conversation rules not in `GameSession.cs` per-conversation scope. Verify whether they're tracked elsewhere or are design-only.

---

*Generated 2026-04-11 by code audit of `src/Pinder.Core/Conversation/GameSession.cs` against `rules/extracted/rules-v3-enriched.yaml`.*
