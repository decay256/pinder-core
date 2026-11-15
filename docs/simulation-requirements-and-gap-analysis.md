# Simulation Requirements & Gap Analysis

## 1. Context & Starting Point
We want a **minimal simulation prompt** to one-shot an authentic, funny dialogue between a custom Avatar and a Datee.
We already established:
- **Ruled out:** Raw fragment dumps (causes the LLM to checklist-recite items like an inventory).
- **Ruled out:** Single random seeds (discards 95% of the player's custom build).
- **Ruled out:** Overly sophisticated steering engines (too complex, hard to tune in one-shot prompts).
- **Strictly retained:** The core RPG rules (stats, d20 option rolls, horniness checks, shadow checks, interest progression, and threshold gating).

---

## 2. What Elements We Have vs. What Doesn't Work

### Assets We Have
1. **Consolidated Character Profiles:**
   - 6 bundled characters with consolidated backstories, consolidated personality armor, public bios, and 15 stake lines.
2. **Deterministic RPG Mechanics:**
   - Stats (Wit, Chaos, Honesty, Charm, Self-Awareness, Rizz).
   - Core rolls: Primary option roll, Horniness check (DC 14), Shadow check (DC 12), Steering check.
   - Bounded deltas (-3 to +4), shadow taint cap (+1), horniness halving, Hold-Off Law (< 25 gating).
3. **Simulation Harness:**
   - Python generator (`generate_lightweight_simulation_prompt.py`) pre-rolling dice and building prompts.
   - Claude Code CLI (`claude -p`) runner.

### What Fails in Prompt Execution
1. **Premature Oversharing (False Intimacy):**
   - Characters dump their deep biographical secrets in Turn 1 or 2 instead of starting guarded and defensive.
2. **Stand-up Banter Trap:**
   - Characters cooperate to be witty and perform sitcom setups/punchlines rather than texting like flawed, suspicious adults.
3. **Mechanical Disconnect:**
   - The LLM outputs dice rolls as metadata, but the dialogue ignores the mechanical failure or thirst slip.
4. **Formatting Noise:**
   - Markdown tables and LaTeX formulas breaking mobile chat readability.

---

## 3. The Core Challenge for the Minimal Simulator Prompt
How do we feed the LLM:
1. The **consolidated character identity** (representing the player's full build without a raw dump),
2. The **pre-rolled RPG turn sequence** (so mechanics dictate when slips happen),
3. A **strict phase boundary** (forbidding personal lore early, forcing it only on stumbles/turning points),

...using the absolute minimum number of tokens and instructions to produce a hilarious, authentic one-shot exchange in plain text?
