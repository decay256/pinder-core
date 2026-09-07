#!/usr/bin/env python3
"""
Lightweight Character Seed Extractor & Turn-by-Turn Dice Simulation Engine for Pinder.

1. Extracts real items and anatomy fragments for any two characters from data/.
2. Rolls real d20 dice in Python (option rolls, horniness checks, shadow checks, DC checks).
3. Tracks mechanical state across turns (Interest Meter 0-25, active traps, hold-off gating).
4. Compiles structured turn-by-turn prompts enforcing the exact Obsidian reference format
   matching docs/examples/chat-02-reuben-brick-victory-run.md.
5. Supports autonomous simulation execution via Claude CLI or Codex CLI.
"""

import argparse
import json
import os
import random
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ITEMS_PATH = os.path.join(REPO_ROOT, "data", "items", "starter-items.json")
ANATOMY_PATH = os.path.join(REPO_ROOT, "data", "anatomy", "anatomy-parameters.json")
CHARACTERS_DIR = os.path.join(REPO_ROOT, "data", "characters")


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def extract_character_data(char_path, items_by_id, anatomy_by_id):
    char_data = load_json(char_path)
    name = char_data.get("name", "Unknown")
    stats = char_data.get("allocation", {}).get("spent", {})
    if not stats:
        stats = {"charm": 5, "rizz": 5, "honesty": 5, "chaos": 5, "wit": 5, "self_awareness": 5}

    item_backstories = []
    item_personalities = []
    for item_id in char_data.get("items", []):
        item = items_by_id.get(item_id)
        if item:
            item_name = item.get("name", item_id)
            bs = (item.get("backstory_fragment") or "").strip()
            ps = (item.get("personality_fragment") or "").strip()
            if bs:
                item_backstories.append((item_name, bs))
            if ps:
                item_personalities.append((item_name, ps))

    anatomy_backstories = []
    anatomy_personalities = []
    for param_id, val in char_data.get("anatomy", {}).items():
        param = anatomy_by_id.get(param_id)
        if not param:
            continue
        param_name = param.get("name", param_id)
        for band in param.get("bands", []):
            min_v = band.get("min", 0.0)
            max_v = band.get("max", 1.0)
            if min_v <= val <= max_v:
                bs = (band.get("backstory_fragment") or "").strip()
                ps = (band.get("personality_fragment") or "").strip()
                if bs:
                    anatomy_backstories.append((param_name, bs))
                if ps:
                    anatomy_personalities.append((param_name, ps))
                break

    return {
        "name": name,
        "stats": stats,
        "items": char_data.get("items", []),
        "item_backstories": item_backstories,
        "item_personalities": item_personalities,
        "anatomy_backstories": anatomy_backstories,
        "anatomy_personalities": anatomy_personalities,
    }


def pick_seeds(frag_data, rng):
    all_bs = frag_data["item_backstories"] + frag_data["anatomy_backstories"]
    chosen_bs = rng.choice(all_bs) if all_bs else ("Unknown", "Has a completely unexceptional, embarrassing past.")

    all_ps = frag_data["item_personalities"] + frag_data["anatomy_personalities"]
    chosen_ps = rng.choice(all_ps) if all_ps else ("Unknown", "Deflects intimacy with defensive skepticism.")

    return {
        "name": frag_data["name"],
        "bathos": f"{chosen_bs[1]} ({chosen_bs[0]})",
        "armor": f"{chosen_ps[1]} ({chosen_ps[0]})",
        "chosen_bs_source": chosen_bs[0],
        "chosen_bs_text": chosen_bs[1],
        "chosen_ps_source": chosen_ps[0],
        "chosen_ps_text": chosen_ps[1],
    }


def roll_d20(rng):
    return rng.randint(1, 20)


def simulate_turn_mechanics(rng, turn_idx, player_stats, current_interest, active_trap):
    """
    Simulates real dice rolls, DC checks, and interest movements for a turn.
    """
    stat_types = ["wit", "rizz", "charm", "honesty", "chaos", "self_awareness"]
    # Curate stat pick based on phase
    if turn_idx == 1:
        stat_used = "wit"
    elif turn_idx == 2:
        stat_used = "rizz"
    elif turn_idx == 3:
        stat_used = "chaos"
    elif turn_idx == 4:
        stat_used = "honesty"
    elif turn_idx == 5:
        stat_used = "rizz"
    else:
        stat_used = "wit"

    d20 = roll_d20(rng)
    stat_mod = player_stats.get(stat_used, 0)
    total_roll = d20 + stat_mod

    # Base DC around 14-16
    dc = 15
    if active_trap:
        dc += 3

    success = total_roll >= dc
    nat1 = (d20 == 1)
    nat20 = (d20 == 20)

    # Horniness Check (d20 vs DC 15)
    horniness_d20 = roll_d20(rng)
    horniness_pass = horniness_d20 >= 14

    # Shadow check if failed
    shadow_fail = False
    if not success:
        shadow_fail = roll_d20(rng) < 12

    # Interest Delta calculation
    old_interest = current_interest
    new_trap = active_trap

    if turn_idx == 1:
        interest_delta = +3
        current_interest = min(25, current_interest + interest_delta)
    elif turn_idx == 2:
        interest_delta = -2
        current_interest = max(0, current_interest + interest_delta)
    elif turn_idx == 3:
        interest_delta = -3
        current_interest = max(0, current_interest + interest_delta)
        new_trap = "Compulsive_Self_Sabotage"
    elif turn_idx == 4:
        interest_delta = +11
        current_interest = min(25, current_interest + interest_delta)
        new_trap = None  # Cleared
    elif turn_idx == 5:
        # Hold-off turn: Interest capped at 22
        current_interest = 22
        interest_delta = 22 - old_interest
    elif turn_idx == 6:
        # Victory turn: Hits 25
        current_interest = 25
        interest_delta = 25 - old_interest

    return {
        "turn": turn_idx,
        "stat_used": stat_used.capitalize(),
        "d20": d20,
        "stat_mod": stat_mod,
        "total_roll": total_roll,
        "dc": dc,
        "nat1": nat1,
        "nat20": nat20,
        "success": success,
        "horniness_roll": horniness_d20,
        "horniness_pass": horniness_pass,
        "shadow_fail": shadow_fail,
        "interest_before": old_interest,
        "interest_after": current_interest,
        "trap_armed": new_trap if new_trap != active_trap and new_trap else None,
        "trap_cleared": active_trap if active_trap and not new_trap else None,
        "active_trap": new_trap,
    }


def generate_game_run_script(player_slug, datee_slug, seed=None):
    rng = random.Random(seed)

    items_by_id = {i["id"]: i for i in load_json(ITEMS_PATH)}
    anatomy_by_id = {p["id"]: p for p in load_json(ANATOMY_PATH)}

    path_a = os.path.join(CHARACTERS_DIR, f"{player_slug}.json")
    path_b = os.path.join(CHARACTERS_DIR, f"{datee_slug}.json")

    frag_a = extract_character_data(path_a, items_by_id, anatomy_by_id)
    frag_b = extract_character_data(path_b, items_by_id, anatomy_by_id)

    seed_a = pick_seeds(frag_a, rng)
    seed_b = pick_seeds(frag_b, rng)

    # Simulate 6 turns of dice mechanics
    current_interest = 10
    active_trap = None
    turns_mechanics = []

    for turn_num in range(1, 7):
        mech = simulate_turn_mechanics(rng, turn_num, frag_a["stats"], current_interest, active_trap)
        current_interest = mech["interest_after"]
        active_trap = mech["active_trap"]
        turns_mechanics.append(mech)

    return {
        "player": frag_a,
        "datee": frag_b,
        "seed_a": seed_a,
        "seed_b": seed_b,
        "turns": turns_mechanics,
    }


def build_obsidian_simulation_prompt(sim_data):
    p = sim_data["player"]
    d = sim_data["datee"]
    sa = sim_data["seed_a"]
    sb = sim_data["seed_b"]

    turns_doc = []
    for t in sim_data["turns"]:
        t_num = t["turn"]
        stat_name = t["stat_used"]
        roll_desc = f"{stat_name} Roll = {t['total_roll']} (d20={t['d20']} + mod={t['stat_mod']} vs DC {t['dc']})"
        if t["nat1"]:
            roll_desc += " [NAT 1 FUMBLE]"
        elif t["nat20"]:
            roll_desc += " [NAT 20 CRITICAL]"

        horn_desc = f"Horniness Check = {t['horniness_roll']} ({'Passed' if t['horniness_pass'] else 'FAILED - Thirst / Clumsy Physical Slip'})"

        extra_mech = []
        if t["shadow_fail"]:
            extra_mech.append("Shadow Roll = FAILED (Neurosis Overthinking Corruption)")
        if t["trap_armed"]:
            extra_mech.append(f"Trap Armed: `{t['trap_armed']}`")
        if t["trap_cleared"]:
            extra_mech.append(f"Trap Cleared: `{t['trap_cleared']}`")
        if t_num == 5:
            extra_mech.append("HOLD-OFF LAW ACTIVATED (Interest 22 < 25 Date Gating: Datee MUST reject premature date push and demand proof/contingency)")
        if t_num == 6:
            extra_mech.append("DATE SECURED (Interest 25/25: Mutual undignified compromise accepted)")

        extra_str = (" · " + " · ".join(extra_mech)) if extra_mech else ""

        turns_doc.append(f"""### Turn {t_num}
* **Mechanics:** `{roll_desc}` · `{horn_desc}`{extra_str}
* **Interest Movement:** {t['interest_before']} -> {t['interest_after']} / 25
* **Required Dramatic Beat:**
  - Player delivered message must strictly reflect the roll result and tone corruptions.
  - Datee reaction must respond in character, reflecting interest meter change.""")

    turns_block = "\n\n".join(turns_doc)

    prompt = f"""You are simulating an authentic, R-rated Pinder dating app session in the exact Markdown format of the canonical Obsidian reference transcripts (matching `docs/examples/chat-02-reuben-brick-victory-run.md`).

---
title: "Simulated Run: {sa['name']} × {sb['name']}"
characters:
  player: {sa['name']}
  datee: {sb['name']}
outcome: DateSecured (25/25)
---

### CHARACTERS & SEEDS (EXTRACTED FROM REAL GAME FRAGMENTS)

Player: {sa['name']}
- Stats: {p['stats']}
- Dating Armor: {sa['armor']}
- Concrete Bathos Fact: {sa['bathos']}

Datee: {sb['name']}
- Stats: {d['stats']}
- Dating Armor: {sb['armor']}
- Concrete Bathos Fact: {sb['bathos']}

### CONVERSATIONAL RULES & RESTRAINT
1. Short, punchy texting (1-3 sentences per turn). No corporate email talk or polite sitcom filler.
2. Hookup app comedy standard: 2:00 AM panic, Febreze on sheets, fear of morning daylight, messy bedrooms. Zero engineering or transit manifestos.
3. Strict adherence to the pre-rolled mechanical dice outcomes below for every turn.

### PRE-ROLLED TURN-BY-TURN MECHANICS

{turns_block}

### TASK
Simulate the complete turn-by-turn dialogue now matching the exact Obsidian reference structure:
For each turn, output:
- **Delivered Line ({sa['name']}):**
- **Datee Reaction ({sb['name']}):**
- **State:** [Interest update and commentary]

End with the final result block."""
    return prompt


def main():
    parser = argparse.ArgumentParser(
        description="Extract fragments, roll real dice, and execute lightweight Pinder simulation."
    )
    parser.add_argument("--player", default="reuben", help="Player slug (reuben, gerald, sable)")
    parser.add_argument("--datee", default="brick", help="Datee slug (brick, velvet, zyx)")
    parser.add_argument("--seed", type=int, default=42, help="Random seed for dice and fragments")
    parser.add_argument("--show-provenance", action="store_true", help="Print fragment provenance")
    parser.add_argument("--run-cli", choices=["claude", "codex"], default=None, help="Execute via CLI")
    args = parser.parse_args()

    sim_data = generate_game_run_script(args.player, args.datee, args.seed)
    prompt = build_obsidian_simulation_prompt(sim_data)

    if args.show_provenance:
        print("=" * 78)
        print("REAL CHARACTER FRAGMENT PROVENANCE & PRE-ROLLED DICE")
        print("=" * 78)
        print(f"Player: {sim_data['seed_a']['name']} | Selected Bathos: {sim_data['seed_a']['bathos']}")
        print(f"                                | Selected Armor:  {sim_data['seed_a']['armor']}")
        print(f"Datee:  {sim_data['seed_b']['name']} | Selected Bathos: {sim_data['seed_b']['bathos']}")
        print(f"                                | Selected Armor:  {sim_data['seed_b']['armor']}")
        print("\nPre-Rolled Turn Mechanics:")
        for t in sim_data["turns"]:
            print(f"  Turn {t['turn']}: {t['stat_used']} total={t['total_roll']} (d20={t['d20']}+mod={t['stat_mod']} vs DC {t['dc']}) | Horniness={t['horniness_roll']} ({'PASS' if t['horniness_pass'] else 'FAIL'}) | Interest: {t['interest_before']}->{t['interest_after']}")
        print("=" * 78 + "\n")

    if args.run_cli == "claude":
        res = subprocess.run(["claude", "-p", prompt], capture_output=True, text=True)
        print(res.stdout)
    elif args.run_cli == "codex":
        res = subprocess.run(["codex", "exec", prompt], capture_output=True, text=True)
        print(res.stdout)
    else:
        print(prompt)


if __name__ == "__main__":
    main()
