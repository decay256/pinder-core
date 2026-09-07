#!/usr/bin/env python3
"""
Lightweight Character Seed Extractor & Simulation Runner for Pinder.

Extracts real items and anatomy fragments for any two characters from data/,
shows the full breakdown of all available fragments and how seeds were selected,
formats the clean simulation prompt, and supports running through Claude CLI or Codex CLI.
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


def extract_character_fragments(char_path, items_by_id, anatomy_by_id):
    char_data = load_json(char_path)
    name = char_data.get("name", "Unknown")

    # Extract item fragments
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

    # Extract anatomy band fragments
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
        "items": char_data.get("items", []),
        "item_backstories": item_backstories,
        "item_personalities": item_personalities,
        "anatomy_backstories": anatomy_backstories,
        "anatomy_personalities": anatomy_personalities,
    }


def pick_seeds(frag_data, seed=None):
    rng = random.Random(seed)

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


def format_provenance(frag_a, frag_b, seed_a, seed_b):
    out = []
    out.append("=" * 78)
    out.append("FRAGMENT PROVENANCE BREAKDOWN")
    out.append("=" * 78)

    for role, frag, seed in [("PLAYER", frag_a, seed_a), ("DATEE", frag_b, seed_b)]:
        out.append(f"\n[{role}] {frag['name']}")
        out.append("-" * 40)
        out.append(f"• Equipped Items ({len(frag['items'])}): {', '.join(frag['items'])}")
        out.append(f"• Item Backstory Fragments ({len(frag['item_backstories'])} available):")
        for src, text in frag["item_backstories"]:
            marker = " -> [SELECTED AS SEED]" if text == seed["chosen_bs_text"] else ""
            out.append(f"    - [{src}] {text}{marker}")
        out.append(f"• Item Personality Fragments ({len(frag['item_personalities'])} available):")
        for src, text in frag["item_personalities"]:
            marker = " -> [SELECTED AS SEED]" if text == seed["chosen_ps_text"] else ""
            out.append(f"    - [{src}] {text}{marker}")
        out.append(f"• Anatomy Backstory Fragments ({len(frag['anatomy_backstories'])} active bands):")
        for src, text in frag["anatomy_backstories"]:
            marker = " -> [SELECTED AS SEED]" if text == seed["chosen_bs_text"] else ""
            out.append(f"    - [{src}] {text}{marker}")
        out.append(f"• Anatomy Personality Fragments ({len(frag['anatomy_personalities'])} active bands):")
        for src, text in frag["anatomy_personalities"]:
            marker = " -> [SELECTED AS SEED]" if text == seed["chosen_ps_text"] else ""
            out.append(f"    - [{src}] {text}{marker}")

        out.append(f"\n  >>> COMPILED SEED FOR {frag['name']}:")
        out.append(f"      Dating Armor: {seed['armor']}")
        out.append(f"      Concrete Bathos: {seed['bathos']}")

    out.append("\n" + "=" * 78)
    return "\n".join(out)


def build_simulation_prompt(seed_a, seed_b):
    return f"""You are simulating a 5-to-6 turn dating app chat on Pinder (an R-rated, satirical hookup app like Tinder/Grindr).
The protagonists are sentient, deeply flawed characters with conflicting dating agendas.

### CORE FRICTION
- One character is desperate for genuine emotional safety / tenderness, but disguises it as a casual hookup.
- The other character is hiding behind cynical, detached, horny armor to avoid getting hurt.
- The tone is adult, funny, and unhinged: messy bedrooms, Febreze on sheets, fear of being seen in morning daylight, fragile egos, and bad decisions at 2:00 AM.
- BANNED: Corporate politeness, sitcom clichés ("not like other girls"), wholesome moralizing, and technical/civil engineering manifestos.

### CHARACTER SEEDS (EXTRACTED FROM REAL CHARACTER FRAGMENTS)

Character A (Player):
- Name: {seed_a['name']}
- Dating Armor: {seed_a['armor']}
- Concrete Bathos Fact: {seed_a['bathos']}

Character B (Datee):
- Name: {seed_b['name']}
- Dating Armor: {seed_b['armor']}
- Concrete Bathos Fact: {seed_b['bathos']}

### CONVERSATIONAL RULES
1. Texting Style: Short, fast lines (1–3 sentences max). Casual, deadpan, lowercase text allowed.
2. The Hold-Off Law: If Character A pushes prematurely to meet up or asks for an address before mutual vulnerability is established, Character B MUST deflect, tease, or demand proof/contingencies.
3. The Micro-Negotiation: The conversation culminates in a funny, pathetic micro-compromise where both agree to meet.

Simulate the 5-to-6 turn dialogue now. Provide only the dialogue with speaker tags (e.g. {seed_a['name']}:, {seed_b['name']}:)."""


def main():
    parser = argparse.ArgumentParser(
        description="Generate lightweight simulation prompts from real character fragments."
    )
    parser.add_argument(
        "--player",
        default="gerald",
        help="Player character slug (e.g. gerald, reuben, sable).",
    )
    parser.add_argument(
        "--datee",
        default="velvet",
        help="Datee character slug (e.g. velvet, brick, zyx).",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=None,
        help="Random seed for deterministic fragment selection.",
    )
    parser.add_argument(
        "--show-provenance",
        action="store_true",
        help="Print the complete fragment provenance breakdown.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output structured JSON payload.",
    )
    args = parser.parse_args()

    items_by_id = {i["id"]: i for i in load_json(ITEMS_PATH)}
    anatomy_by_id = {p["id"]: p for p in load_json(ANATOMY_PATH)}

    path_a = os.path.join(CHARACTERS_DIR, f"{args.player}.json")
    path_b = os.path.join(CHARACTERS_DIR, f"{args.datee}.json")

    if not os.path.exists(path_a):
        print(f"Error: Character file not found: {path_a}", file=sys.stderr)
        sys.exit(1)
    if not os.path.exists(path_b):
        print(f"Error: Character file not found: {path_b}", file=sys.stderr)
        sys.exit(1)

    frag_a = extract_character_fragments(path_a, items_by_id, anatomy_by_id)
    frag_b = extract_character_fragments(path_b, items_by_id, anatomy_by_id)

    seed_a = pick_seeds(frag_a, args.seed)
    seed_b = pick_seeds(frag_b, args.seed + 1 if args.seed is not None else None)

    prompt = build_simulation_prompt(seed_a, seed_b)
    provenance = format_provenance(frag_a, frag_b, seed_a, seed_b)

    if args.json:
        payload = {
            "player": {"fragments": frag_a, "seed": seed_a},
            "datee": {"fragments": frag_b, "seed": seed_b},
            "prompt": prompt,
        }
        print(json.dumps(payload, indent=2))
        return

    if args.show_provenance:
        print(provenance)
        print("\n" + "=" * 78)
        print("COMPILED SIMULATION PROMPT")
        print("=" * 78 + "\n")

    print(prompt)


if __name__ == "__main__":
    main()
