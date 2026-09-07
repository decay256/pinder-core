#!/usr/bin/env python3
"""
Lightweight Character Seed Extractor & Turn-by-Turn Dice Simulation Engine for Pinder.

STRICT CANONICAL ENGINE RULES (C# Pinder.Core / Pinder.Rules SSOT):
1. Primary Option Roll:
   - Total = d20 + Player Stat Mod vs DC.
   - Success Interest Delta (SuccessScale.cs):
     * Nat 20              -> +4
     * Margin >= 10 (Crit) -> +3
     * Margin 5..9         -> +2
     * Margin 1..4         -> +1
   - Failure Interest Delta (FailureScale.cs):
     * Nat 1 (Catastrophe) -> -3
     * Miss margin >= 5    -> -2
     * Miss margin 1..4    -> -1
   - Active Trap Penalty (RollResolutionStage.cs):
     * Active trap applies -25% penalty to positive interest delta.
   - Shadow Taint Cap (DeliveryStage.cs):
     * If shadow check fails on a success, interest gain is CAPPED AT +1.
   - Horniness Slip Halving (DeliveryStage.cs):
     * If horniness check fails, positive interest delta is halved (floor).

2. Paired Shadow Roll (TurnOrchestrator.Helpers.cs):
   - Paired stats: Charm->Madness, Rizz->Despair, Honesty->Denial,
     Chaos->Fixation, Wit->Dread, SelfAwareness->Overthinking.
   - d20 vs DC 12. Miss taints message delivery.

3. Horniness Check:
   - d20 vs DC 14. Miss appends clumsy physical slip / premature thirst.

4. Steering Roll (SteeringEngine.cs):
   - Player Mod = (Charm + Wit + SA) // 3
   - Datee DC = 16 + (Datee SA + Datee Rizz + Datee Honesty) // 3
   - Success appends a date-steering question.

5. Interest Meter Gating (0..25) & States (RuleBookResolver.cs):
   - 0: Unmatched, 1-4: Bored, 5-9: Lukewarm, 10-15: Interested,
     16-20: VeryIntoIt, 21-24: AlmostThere, 25: DateSecured.
   - HOLD-OFF LAW: At Interest < 25 (even 21..24), premature date pushes MUST be rejected.
"""

import argparse
import json
import math
import os
import random
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ITEMS_PATH = os.path.join(REPO_ROOT, "data", "items", "starter-items.json")
ANATOMY_PATH = os.path.join(REPO_ROOT, "data", "anatomy", "anatomy-parameters.json")
CHARACTERS_DIR = os.path.join(REPO_ROOT, "data", "characters")

SHADOW_PAIRS = {
    "Charm": "Madness",
    "Rizz": "Despair",
    "Honesty": "Denial",
    "Chaos": "Fixation",
    "Wit": "Dread",
    "SelfAwareness": "Overthinking",
}


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def get_interest_state(interest):
    if interest <= 0:
        return "Unmatched"
    if interest <= 4:
        return "Bored"
    if interest <= 9:
        return "Lukewarm"
    if interest <= 15:
        return "Interested"
    if interest <= 20:
        return "VeryIntoIt"
    if interest <= 24:
        return "AlmostThere"
    return "DateSecured"


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


def compute_canonical_turn(rng, turn_idx, player_stats, datee_stats, current_interest, active_trap):
    """
    Computes turn outcomes strictly according to C# Pinder engine rules.
    """
    turn_stat_plan = {
        1: "wit",
        2: "rizz",
        3: "chaos",
        4: "honesty",
        5: "rizz",
        6: "wit",
    }
    stat_used = turn_stat_plan.get(turn_idx, "wit")

    # 1. Option Roll
    d20 = rng.randint(1, 20)
    stat_mod = player_stats.get(stat_used, 0)
    total_roll = d20 + stat_mod

    # Base DC
    base_dc = 15
    dc = base_dc + (3 if active_trap else 0)

    is_nat20 = (d20 == 20)
    is_nat1 = (d20 == 1)
    is_success = (total_roll >= dc or is_nat20) and not is_nat1

    # Base Interest Delta (SuccessScale.cs & FailureScale.cs)
    breakdown = []
    if is_success:
        if is_nat20:
            base_delta = 4
            breakdown.append("Nat 20 (+4)")
        else:
            margin = total_roll - dc
            if margin >= 10:
                base_delta = 3
                breakdown.append(f"Crit margin {margin} (+3)")
            elif margin >= 5:
                base_delta = 2
                breakdown.append(f"Strong margin {margin} (+2)")
            else:
                base_delta = 1
                breakdown.append(f"Margin {margin} (+1)")
    else:
        if is_nat1:
            base_delta = -3
            breakdown.append("Nat 1 Catastrophe (-3)")
        else:
            miss_margin = dc - total_roll
            if miss_margin >= 5:
                base_delta = -2
                breakdown.append(f"Miss margin {miss_margin} (-2)")
            else:
                base_delta = -1
                breakdown.append(f"Miss margin {miss_margin} (-1)")

    interest_delta = base_delta

    # Active Trap Penalty (-25% on positive delta)
    new_trap = active_trap
    if is_nat1:
        new_trap = "Compulsive_Self_Sabotage"

    # Turn 4 Honesty clears trap if successful
    if turn_idx == 4 and is_success and active_trap:
        new_trap = None

    if active_trap and interest_delta > 0:
        penalized = round(interest_delta * 0.75)
        breakdown.append(f"Active trap penalty: {interest_delta} -> {penalized}")
        interest_delta = penalized

    # 2. Horniness Check
    horniness_d20 = rng.randint(1, 20)
    horniness_pass = horniness_d20 >= 14
    if not horniness_pass and interest_delta > 0:
        halved = math.floor(interest_delta / 2.0)
        breakdown.append(f"Horniness slip halving: {interest_delta} -> {halved}")
        interest_delta = halved

    # 3. Shadow Check
    paired_shadow = SHADOW_PAIRS.get(stat_used.capitalize(), "Dread")
    shadow_d20 = rng.randint(1, 20)
    shadow_pass = shadow_d20 >= 12
    if not shadow_pass and is_success and interest_delta > 1:
        breakdown.append(f"Shadow taint cap ({paired_shadow}): {interest_delta} -> 1")
        interest_delta = 1

    # 4. Steering Roll
    player_charm = player_stats.get("charm", 0)
    player_wit = player_stats.get("wit", 0)
    player_sa = player_stats.get("self_awareness", 0)
    steering_mod = (player_charm + player_wit + player_sa) // 3

    datee_sa = datee_stats.get("self_awareness", 0)
    datee_rizz = datee_stats.get("rizz", 0)
    datee_honesty = datee_stats.get("honesty", 0)
    steering_dc = 16 + (datee_sa + datee_rizz + datee_honesty) // 3

    steering_d20 = rng.randint(1, 20)
    steering_total = steering_d20 + steering_mod
    steering_success = steering_total >= steering_dc

    old_interest = current_interest
    new_interest = max(0, min(25, current_interest + interest_delta))

    return {
        "turn": turn_idx,
        "stat_used": stat_used.capitalize(),
        "d20": d20,
        "stat_mod": stat_mod,
        "total_roll": total_roll,
        "dc": dc,
        "nat1": is_nat1,
        "nat20": is_nat20,
        "success": is_success,
        "base_delta": base_delta,
        "final_delta": interest_delta,
        "breakdown": breakdown,
        "horniness_roll": horniness_d20,
        "horniness_pass": horniness_pass,
        "paired_shadow": paired_shadow,
        "shadow_roll": shadow_d20,
        "shadow_pass": shadow_pass,
        "steering_d20": steering_d20,
        "steering_mod": steering_mod,
        "steering_total": steering_total,
        "steering_dc": steering_dc,
        "steering_success": steering_success,
        "interest_before": old_interest,
        "interest_after": new_interest,
        "state_before": get_interest_state(old_interest),
        "state_after": get_interest_state(new_interest),
        "trap_armed": new_trap if new_trap != active_trap and new_trap else None,
        "trap_cleared": active_trap if active_trap and not new_trap else None,
        "active_trap": new_trap,
    }


def generate_game_run_script(player_slug, datee_slug, seed=None, num_turns=6, starting_interest=10):
    rng = random.Random(seed)

    items_by_id = {i["id"]: i for i in load_json(ITEMS_PATH)}
    anatomy_by_id = {p["id"]: p for p in load_json(ANATOMY_PATH)}

    path_a = os.path.join(CHARACTERS_DIR, f"{player_slug}.json")
    path_b = os.path.join(CHARACTERS_DIR, f"{datee_slug}.json")

    frag_a = extract_character_data(path_a, items_by_id, anatomy_by_id)
    frag_b = extract_character_data(path_b, items_by_id, anatomy_by_id)

    seed_a = pick_seeds(frag_a, rng)
    seed_b = pick_seeds(frag_b, rng)

    current_interest = starting_interest
    active_trap = None
    turns_mechanics = []

    for turn_num in range(1, num_turns + 1):
        mech = compute_canonical_turn(
            rng, turn_num, frag_a["stats"], frag_b["stats"], current_interest, active_trap
        )
        current_interest = mech["interest_after"]
        active_trap = mech["active_trap"]
        turns_mechanics.append(mech)

    return {
        "player": frag_a,
        "datee": frag_b,
        "seed_a": seed_a,
        "seed_b": seed_b,
        "turns": turns_mechanics,
        "final_interest": current_interest,
        "final_state": get_interest_state(current_interest),
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

        horn_desc = f"Horniness Check = {t['horniness_roll']} ({'Passed' if t['horniness_pass'] else 'FAILED - Thirst Slip'})"
        shadow_desc = f"Shadow Roll ({t['paired_shadow']}) = {t['shadow_roll']} ({'Resisted' if t['shadow_pass'] else 'FAILED - Neurosis Taint'})"
        steer_desc = f"Steering Roll = {t['steering_total']} (d20={t['steering_d20']} + mod={t['steering_mod']} vs DC {t['steering_dc']}) -> {'SUCCEEDED (appends question)' if t['steering_success'] else 'FAILED (no steering)'}"

        extra_mech = [horn_desc, shadow_desc, steer_desc]
        if t["trap_armed"]:
            extra_mech.append(f"Trap Armed: `{t['trap_armed']}`")
        if t["trap_cleared"]:
            extra_mech.append(f"Trap Cleared: `{t['trap_cleared']}`")
        if t["interest_after"] in (21, 22, 23, 24):
            extra_mech.append("HOLD-OFF LAW ACTIVATED (Interest < 25 Date Gating: Datee MUST reject premature date push)")
        if t["interest_after"] >= 25:
            extra_mech.append("DATE SECURED (Interest 25/25)")

        mech_str = " · ".join([f"`{roll_desc}`"] + [f"`{m}`" for m in extra_mech])
        sign = "+" if t["final_delta"] > 0 else ""
        delta_str = f"{sign}{t['final_delta']}" if t["final_delta"] != 0 else "0"

        turns_doc.append(f"""### Turn {t_num}
* **Mechanics:** {mech_str}
* **Interest Movement:** {t['interest_before']} ({t['state_before']}) -> {t['interest_after']} ({t['state_after']}) [{delta_str}: {', '.join(t['breakdown'])}]
* **Required Dramatic Beat:**
  - Delivered message must strictly reflect the roll result, horniness slip (if failed), and shadow neurosis (if failed).
  - Datee reaction must respond in character, reflecting interest change and steering question if succeeded.""")

    turns_block = "\n\n".join(turns_doc)

    prompt = f"""You are simulating an authentic, R-rated Pinder dating app session in the exact Markdown format of the canonical Obsidian reference transcripts.

---
title: "Simulated Run: {sa['name']} × {sb['name']}"
characters:
  player: {sa['name']}
  datee: {sb['name']}
outcome: {sim_data['final_state']} ({sim_data['final_interest']}/25)
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
3. Strict adherence to the pre-rolled mechanical dice outcomes below for every turn. Notice that interest changes strictly obey the canonical engine rules (typical deltas are +1, +2, +3 on success; -1, -2, -3 on failure).

### PRE-ROLLED TURN-BY-TURN MECHANICS

{turns_block}

### TASK
Simulate the complete turn-by-turn dialogue now matching the exact Obsidian reference structure:
For each turn, output:
- **Mechanics line**
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
    parser.add_argument("--turns", type=int, default=6, help="Number of turns to simulate")
    parser.add_argument("--starting-interest", type=int, default=10, help="Starting interest (default 10)")
    parser.add_argument("--show-provenance", action="store_true", help="Print fragment provenance")
    parser.add_argument("--run-cli", choices=["claude", "codex"], default=None, help="Execute via CLI")
    args = parser.parse_args()

    sim_data = generate_game_run_script(
        args.player, args.datee, args.seed, args.turns, args.starting_interest
    )
    prompt = build_obsidian_simulation_prompt(sim_data)

    if args.show_provenance:
        print("=" * 78)
        print("REAL CHARACTER FRAGMENT PROVENANCE & CANONICAL ENGINE DICE")
        print("=" * 78)
        print(f"Player: {sim_data['seed_a']['name']} | Selected Bathos: {sim_data['seed_a']['bathos']}")
        print(f"                                | Selected Armor:  {sim_data['seed_a']['armor']}")
        print(f"Datee:  {sim_data['seed_b']['name']} | Selected Bathos: {sim_data['seed_b']['bathos']}")
        print(f"                                | Selected Armor:  {sim_data['seed_b']['armor']}")
        print("\nPre-Rolled Canonical Engine Turn Mechanics:")
        for t in sim_data["turns"]:
            sign = "+" if t["final_delta"] > 0 else ""
            delta_str = f"{sign}{t['final_delta']}" if t["final_delta"] != 0 else "0"
            print(
                f"  Turn {t['turn']}: {t['stat_used']} total={t['total_roll']} vs DC {t['dc']} "
                f"({'PASS' if t['success'] else 'FAIL'}) | Thirst={t['horniness_roll']} "
                f"| Shadow={t['paired_shadow']} ({'OK' if t['shadow_pass'] else 'FAIL'}) "
                f"| Delta={delta_str} ({', '.join(t['breakdown'])}) "
                f"| Interest: {t['interest_before']}->{t['interest_after']} ({t['state_after']})"
            )
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
