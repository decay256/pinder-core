"""Writers write a character from the brief.

Each writer on the panel list is told only who they are, given the Pinder brief, and asked to write
one character for the game from a shuffled handful of the game's own fragments for that character,
as inspiration rather than requirements. No profile rules, no headings, no length. The physique is
left out: the anatomy and the physical-dysmorphia pair. So are the game's consolidated texts
(personality, backstory, diagnosis), which are its own synthesis of the same fragments.

    python3 -I tools/tonelab/writers.py --key-stdin [--only brick] [--out DIR] [--writers id,id]
"""
import argparse, json, os, random, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = "/root/projects/pinder-core"
sys.path.insert(0, os.path.join(REPO, "tools", "tonelab"))
import tonelab as tl  # noqa: E402
import yaml  # noqa: E402

MODEL = "google/gemini-4-argon"
SEED = 7

WRITERS = [
    ("armstrong-bain", "Jesse Armstrong and Sam Bain"),
    ("waller-bridge", "Phoebe Waller-Bridge"),
    ("coel", "Michaela Coel"),
    ("saunders", "George Saunders"),
    ("david", "Larry David"),
    ("leonard", "Elmore Leonard"),
    ("pinter", "Harold Pinter"),
    ("parker", "Dorothy Parker"),
]

SYSTEM = "You are {name}."

USER = """Here's a brief.

{brief}

Write a character for this game: {char} ({pronouns}). Below is a random bunch of tidbits the game has generated for {obj}. Take them as inspiration, not requirements: use what you like, change it, or ignore it.

{tidbits}
"""

OBJ = {"she/her": "her", "he/him": "him", "they/them": "them"}


def brief():
    y = yaml.safe_load(open(os.path.join(REPO, "tools/tonelab/variants/live/situations.yaml"), encoding="utf-8"))
    return y["prompts"]["bible"].strip()


def tidbits(d, items):
    out = [f"Profile bio: {d['bio']}"]
    for iid in d["items"]:
        it = items[iid]
        for k in ("personality_fragment", "backstory_fragment"):
            if it.get(k):
                out.append(it[k].strip())
        t = (it.get("texting_style_fragment") or "").replace("SYNTAX:", "").strip()
        for line in t.splitlines():
            line = line.strip().lstrip("- ").strip()
            if line:
                out.append("Texting: " + line.split(":", 1)[-1].strip())
    for cat, v in d["backstory_categories"].items():
        if cat == "physical_dysmorphia":
            continue
        out.append(f"Claims: {v['bio_lie']} In fact: {v['tragic_reality']}")
    for line in d.get("psychological_stake", "").splitlines():
        line = line.strip().lstrip("- ").strip()
        if line and "about my body" not in line:
            out.append(line)
    out += [s.strip() for s in d.get("stake_lines", []) if s.strip()]
    random.Random(SEED).shuffle(out)
    return "\n".join("- " + t for t in out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key-stdin", action="store_true")
    ap.add_argument("--only", default="brick")
    ap.add_argument("--writers", default="")
    ap.add_argument("--out", default=os.path.join(REPO, "docs/tone-lab/scratch/writers"))
    a = ap.parse_args()
    if not a.key_stdin:
        tl.die("writers: the key comes on stdin (--key-stdin)")
    key = sys.stdin.readline().strip().lstrip("﻿").strip()
    items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
    d = json.load(open(os.path.join(REPO, "data/characters", f"{a.only}.json"), encoding="utf-8"))
    pron = d.get("gender_identity", "they/them")
    user = USER.format(brief=brief(), char=d["name"], pronouns=pron, obj=OBJ.get(pron, "them"),
                       tidbits=tidbits(d, items))
    out = os.path.join(a.out, a.only)
    os.makedirs(out, exist_ok=True)
    chosen = [w for w in WRITERS if not a.writers or w[0] in a.writers.split(",")]
    results = {}

    def run(wid, name):
        system = SYSTEM.format(name=name)
        try:
            data, seconds = tl.call_openrouter(system, user, MODEL, key, {"reasoning": {"effort": "high"}})
        except (SystemExit, Exception) as e:
            results[wid] = {"error": str(e)}
            print(f"{wid}: failed: {e}", flush=True)
            return
        msg = ((data.get("choices") or [{}])[0].get("message") or {})
        text = (msg.get("content") or "").strip()
        usage = data.get("usage") or {}
        results[wid] = {"writer": name, "model": data.get("model"), "seconds": seconds,
                        "cost": usage.get("cost"), "usage": usage, "words": len(text.split())}
        with open(os.path.join(out, f"{wid}.md"), "w", encoding="utf-8") as f:
            f.write(text + "\n")
        print(f"{wid}: {len(text.split())} words, {seconds}s, ${usage.get('cost')}", flush=True)

    threads = [threading.Thread(target=run, args=w) for w in chosen]
    for t in threads:
        t.start()
        time.sleep(1)
    for t in threads:
        t.join()
    with open(os.path.join(out, "prompt.md"), "w", encoding="utf-8") as f:
        f.write("<!-- system prompt: 'You are {writer}.' -->\n\n<!-- user prompt -->\n" + user)
    with open(os.path.join(out, "run.json"), "w", encoding="utf-8") as f:
        json.dump({"model": MODEL, "seed": SEED, "character": a.only, "reasoning": "high",
                   "system": SYSTEM, "writers": results}, f, indent=1)
    if any("error" in r for r in results.values()):
        sys.exit(1)


if __name__ == "__main__":
    main()
