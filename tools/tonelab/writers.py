"""Writers write a character from the brief, then a chat between two of their own characters.

Profiles: each writer on the panel list is told only who they are, given the Pinder brief, and asked
to write one character for the game from a shuffled handful of the game's own fragments for that
character, as inspiration rather than requirements. No profile rules, no headings, no length. The
physique is left out: the anatomy and the physical-dysmorphia pair. So are the game's consolidated
texts (personality, backstory, diagnosis), which are its own synthesis of the same fragments.

Chats: the same writer gets the brief and the two characters they wrote, and writes how a chat
between them would go on Pinder, with no constraints: no dice, no turns, no length.

    python3 -I tools/tonelab/writers.py --key-stdin --only reuben          # profiles
    python3 -I tools/tonelab/writers.py --key-stdin --chat reuben,brick    # chats from the profiles
    python3 -I tools/tonelab/writers.py --key-stdin --only reuben --chat reuben,brick
"""
import argparse, json, os, random, sys, threading, time

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
    # run 4: comedians who work on dating, sex and identity, with run 3's prompts
    ("ansari", "Aziz Ansari"),
    ("wong", "Ali Wong"),
    ("martin", "Mae Martin"),
    ("booster", "Joel Kim Booster"),
    ("carr", "Jimmy Carr"),
    ("schumer", "Amy Schumer"),
]

PROMPTS = {
    "v1": {
        "system": "You are {name}.",
        "user": """Here's a brief.

{brief}

Write a character for this game: {char} ({pronouns}). Below is a random bunch of tidbits the game has generated for {obj}. Take them as inspiration, not requirements: use what you like, change it, or ignore it.

{tidbits}
""",
        "chat": """Here's a brief.

{brief}

Here are two characters you wrote for this game.

=== {a} ===

{a_profile}

=== {b} ===

{b_profile}

Now write how a chat between these two would go if they were texting on Pinder, without any constraints. {a} writes first. Write it the way you write.
""",
    },
    # v2: the writer is the author and the authority, and the game's material is theirs to throw away.
    "v2": {
        "system": """You are {name}. Not someone writing in your manner: you, with your own ear, your own taste and your own judgement.

The people making Pinder have asked you to write for it. On the characters and the dialogue you are the authority: you decide who these people are, what about them matters, how they talk and how they type. Nobody will hold you to anything else.""",
        "user": """Here's the brief the Pinder team wrote, so you know what the game is and what it's for.

{brief}

Now write a character for it: {char} ({pronouns}).

The game generated the list below about {obj}. It is raw material, nothing more, and most of it may be no use to you. Keep only what interests you, change anything, and throw the rest away. Nothing in it binds you: not the habits, not the history, not the way it says {obj} talks. If you'd rather invent {obj} from scratch, do.

Write the character in whatever form you'd actually hand over, as long or as short as you'd make it.

{tidbits}
""",
        "chat": """Here's the brief the Pinder team wrote.

{brief}

Here are the two characters you wrote for it.

=== {a} ===

{a_profile}

=== {b} ===

{b_profile}

Now write their chat on Pinder. {a} writes first. It's your chat: as long or as short as it should be, and typed the way these two would actually type.
""",
    },
}
# v3: v2 without the game's texting-style fragments, and one sentence asking the writer for their own.
PROMPTS["v3"] = dict(PROMPTS["v2"], user=PROMPTS["v2"]["user"].replace(
    "Write the character in whatever form you'd actually hand over, as long or as short as you'd make it.\n",
    "Write the character in whatever form you'd actually hand over, as long or as short as you'd make it.\n\n"
    "Develop your own texting style for this character, so the character is distinct.\n"))
assert PROMPTS["v3"]["user"] != PROMPTS["v2"]["user"]
# v4: v3 with the chat held to 1,000 words; the profiles are run 4's, unchanged.
PROMPTS["v4"] = dict(PROMPTS["v3"], chat=PROMPTS["v3"]["chat"].replace(
    "It's your chat: as long or as short as it should be,",
    "It's your chat: no longer than 1,000 words,"))
assert PROMPTS["v4"]["chat"] != PROMPTS["v3"]["chat"]
# v5: v3 with the chat held to 500 words; the profiles are run 4's, unchanged.
PROMPTS["v5"] = dict(PROMPTS["v3"], chat=PROMPTS["v3"]["chat"].replace(
    "It's your chat: as long or as short as it should be,",
    "It's your chat: no longer than 500 words,"))
assert PROMPTS["v5"]["chat"] != PROMPTS["v3"]["chat"]
# v6: a portrait of the writer in the system prompt; the brief first in both tasks; the game's
# fragments grouped under headings ("what we know about him"); the profile written knowing it is for
# a short chat; the chat written as a joke to be read, with a word limit (--words).
PORTRAITS = {
    "wong": """You made Baby Cobra and Hard Knock Wife pregnant, Don Wong married and Single Lady divorced and dating again. You co-wrote Always Be My Maybe and played Amy in Beef. Your material is sex, bodies, marriage and money, told without shame and in very specific physical detail. Your jokes say the thing nobody admits wanting, and the filthiest line always gives away something true about the person saying it. You like to flip who's winning: the one who looks like she's losing is running the deal.""",
    "booster": """You were born in Korea, adopted by white evangelical parents in the Midwest and homeschooled, and you found gay life with no map. Psychosexual is an hour about sex, apps, bodies and being wanted; you wrote and starred in Fire Island, Pride and Prejudice on a gay holiday, about who gets ranked where. Your jokes are vain and honest at once: you admit the shallow thing you want, name the hierarchies of bodies and race the apps run on, and let the hurt under it show for a second before the next joke covers it.""",
    "carr": """You write one-liners, and you've thought hard about how they work: you co-wrote The Naked Jape, a book about jokes. Your rule is the shortest route to the laugh: set up an assumption, cut every word that isn't needed, and put the word that springs the twist last. Your persona is a man with no shame saying the unsayable, deadpan, and the joke is often on you: your image, your vanity, your tax affair. You go dark on purpose, and the darkness is aimed, never random.""",
}
PROMPTS["v6"] = {
    "system": """You are {name}: you, with your own ear and your own taste, not someone imitating you.

{portrait}

You're writing for Pinder, a comedy game. Each time, you'll first read its brief: what the game is, what it wants to say about dating apps, and what a reader should be left with. Then you'll get one task.""",
    "user": """{brief}

# Your task

Write {char} ({pronouns}), one of the game's characters. Below is what we know about {obj}. Keep what interests you, change anything, throw the rest away. If you'd rather invent {obj} differently, do.

Later you'll write a short chat between {obj} and another character, working from what you write here and nothing else. So write {obj} the way you'd want {obj} in hand for that: as long as it needs to be, and no longer. Work out how {subj} types yourself, so {subj} is distinct.

{tidbits}
""",
    "chat": """{brief}

# Your task

Here are the two characters you wrote for Pinder.

## {a}

{a_profile}

## {b}

{b_profile}

# The chat

Write their chat on the app, no longer than {words} words. {a} writes first.

Write it as a joke. It will be read, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends. And write it in Pinder's spirit: put a finger in the wound of dating apps, through these two.
""",
}
TEXTING = {"v1": True, "v2": True, "v3": False, "v4": False, "v5": False, "v6": False}
GROUPED = {"v6"}  # versions that give the fragments under headings instead of one shuffled list  # whether the game's texting-style fragments are in the list
VERSION = os.environ.get("TONELAB_WRITERS_VERSION", "v6")
SYSTEM, USER, CHAT = (PROMPTS[VERSION][k] for k in ("system", "user", "chat"))

OBJ = {"she/her": "her", "he/him": "him", "they/them": "them"}
SUBJ = {"she/her": "she", "he/him": "he", "they/them": "they"}
POSS = {"she/her": "her", "he/him": "his", "they/them": "their"}


def system_for(wid, name):
    return SYSTEM.format(name=name, portrait=PORTRAITS.get(wid, "").strip())


def tidbits_grouped(d, items):
    """The game's fragments under headings, each group shuffled with the seed; no texting fragments."""
    pron = d.get("gender_identity", "they/them")
    s, o, p = SUBJ.get(pron, "they"), OBJ.get(pron, "them"), POSS.get(pron, "their")
    rnd = random.Random(SEED)
    who, past, claims, fear = [], [], [], []
    for iid in d["items"]:
        it = items[iid]
        if (it.get("personality_fragment") or "").strip():
            who.append(it["personality_fragment"].strip())
        if (it.get("backstory_fragment") or "").strip():
            past.append(it["backstory_fragment"].strip())
    for cat, v in d["backstory_categories"].items():
        if cat == "physical_dysmorphia":
            continue
        claims.append(f"{s.capitalize()} says: {v['bio_lie'].strip()}  \n  In fact: {v['tragic_reality'].strip()}")
    for line in d.get("psychological_stake", "").splitlines():
        line = line.strip().lstrip("- ").strip()
        if line and "about my body" not in line:
            fear.append(line)
    fear += [x.strip() for x in d.get("stake_lines", []) if x.strip()]
    out = [f"## What {p} profile says\n\n{d['bio'].strip()}"]
    for head, xs in ((f"Who {s} is", who), (f"What happened to {o}", past),
                     (f"What {s} claims, and what's actually true", claims), (f"What {s} keeps to {o}self", fear)):
        if xs:
            rnd.shuffle(xs)
            out.append(f"## {head}\n\n" + "\n".join("- " + x for x in xs))
    return "\n\n".join(out)


def brief():
    y = yaml.safe_load(open(os.path.join(REPO, "tools/tonelab/variants/live/situations.yaml"), encoding="utf-8"))
    return y["prompts"]["bible"].strip()


def tidbits(d, items, texting=None):
    if texting is None and VERSION in GROUPED:
        return tidbits_grouped(d, items)
    if texting is None:
        texting = TEXTING[VERSION]
    out = [f"Profile bio: {d['bio']}"]
    for iid in d["items"]:
        it = items[iid]
        for k in ("personality_fragment", "backstory_fragment"):
            if it.get(k):
                out.append(it[k].strip())
        t = (it.get("texting_style_fragment") or "").replace("SYNTAX:", "").strip() if texting else ""
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


def fan_out(jobs, key, out, label):
    """jobs: [(writer id, writer name, user prompt)]. One call per writer, in parallel."""
    os.makedirs(out, exist_ok=True)
    results = {}

    def run(wid, name, user):
        try:
            data, seconds = tl.call_openrouter(system_for(wid, name), user, MODEL, key,
                                               {"reasoning": {"effort": "high"}})
        except (SystemExit, Exception) as e:
            results[wid] = {"writer": name, "error": str(e)}
            print(f"{label} {wid}: failed: {e}", flush=True)
            return
        msg = ((data.get("choices") or [{}])[0].get("message") or {})
        text = (msg.get("content") or "").strip()
        usage = data.get("usage") or {}
        results[wid] = {"writer": name, "model": data.get("model"), "seconds": seconds,
                        "cost": usage.get("cost"), "usage": usage, "words": len(text.split())}
        with open(os.path.join(out, f"{wid}.md"), "w", encoding="utf-8") as f:
            f.write(text + "\n")
        with open(os.path.join(out, f"{wid}.prompt.md"), "w", encoding="utf-8") as f:
            f.write(f"<!-- system prompt -->\n{system_for(wid, name)}\n\n<!-- user prompt -->\n{user}")
        print(f"{label} {wid}: {len(text.split())} words, {seconds}s, ${usage.get('cost')}", flush=True)

    threads = [threading.Thread(target=run, args=j) for j in jobs]
    for t in threads:
        t.start()
        time.sleep(1)
    for t in threads:
        t.join()
    for wid, r in results.items():
        if "error" not in r and not (r.get("usage") or {}).get("completion_tokens"):
            r["error"] = "no completion tokens reported: the response was cut off"
            print(f"{label} {wid}: cut off ({r['words']} words, no usage)", flush=True)
    path = os.path.join(out, "run.json")
    old = json.load(open(path, encoding="utf-8")).get("writers", {}) if os.path.exists(path) else {}
    old.update(results)
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"model": MODEL, "seed": SEED, "reasoning": "high", "version": VERSION, "system": SYSTEM,
                   "portraits": {w: PORTRAITS[w] for w in results if w in PORTRAITS} or None,
                   "writers": old}, f, indent=1)
    return not any("error" in r for r in results.values())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key-stdin", action="store_true")
    ap.add_argument("--only", default="", help="write this character's profiles")
    ap.add_argument("--chat", default="", help="a,b: each writer writes a chat between their a and b")
    ap.add_argument("--writers", default="")
    ap.add_argument("--words", type=int, default=500, help="the chat's word limit, for prompt versions that have one")
    ap.add_argument("--out", default=os.path.join(REPO, "docs/tone-lab/scratch/writers"))
    a = ap.parse_args()
    if not a.key_stdin:
        tl.die("writers: the key comes on stdin (--key-stdin)")
    key = sys.stdin.readline().strip().lstrip("﻿").strip()
    chosen = [w for w in WRITERS if not a.writers or w[0] in a.writers.split(",")]
    ok = True
    if a.only:
        items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
        slugs = a.only.split(",")
        done = {}

        def one(slug):
            d = json.load(open(os.path.join(REPO, "data/characters", f"{slug}.json"), encoding="utf-8"))
            pron = d.get("gender_identity", "they/them")
            user = USER.format(brief=brief(), char=d["name"], pronouns=pron, obj=OBJ.get(pron, "them"),
                               subj=SUBJ.get(pron, "they"), tidbits=tidbits(d, items))
            done[slug] = fan_out([(w, n, user) for w, n in chosen], key, os.path.join(a.out, slug), slug)

        ts = [threading.Thread(target=one, args=(s,)) for s in slugs]
        for t in ts:
            t.start()
        for t in ts:
            t.join()
        ok = all(done.get(s) for s in slugs) and ok
    if a.chat:
        x, y = a.chat.split(",")
        names = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8"))["name"] for s in (x, y)}
        jobs = []
        for w, n in chosen:
            px, py = (os.path.join(a.out, s, f"{w}.md") for s in (x, y))
            if not (os.path.exists(px) and os.path.exists(py)):
                print(f"chat {w}: missing a profile, skipped", flush=True)
                ok = False
                continue
            jobs.append((w, n, CHAT.format(brief=brief(), a=names[x], b=names[y], words=a.words,
                                           a_profile=open(px, encoding="utf-8").read().strip(),
                                           b_profile=open(py, encoding="utf-8").read().strip())))
        ok = fan_out(jobs, key, os.path.join(a.out, f"chat-{x}-{y}"), "chat") and ok
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
