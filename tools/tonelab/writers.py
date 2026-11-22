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
import argparse, json, os, random, re, sys, threading, time

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
# v7: v6, with the chat task asking first for the joke and why it is the right joke for these two.
PROMPTS["v7"] = dict(PROMPTS["v6"], chat=PROMPTS["v6"]["chat"].replace("""# The chat

Write their chat on the app, no longer than {words} words. {a} writes first.

Write it as a joke. It will be read, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends. And write it in Pinder's spirit: put a finger in the wound of dating apps, through these two.
""", """# The chat

You'll write their chat on the app as a joke. Before you write it, work the joke out.

First, under the heading "## The joke", say what the joke is and why it's the right joke for these two: the setup, where it turns, the punchline, and why it could only land on these two. A few sentences.

Then, under the heading "## The chat", write the chat: no longer than {words} words, {a} first, one message per line, as "{a}: message" or "{b}: message", and nothing else under that heading.

The chat is that joke, and it has to work without the explanation: the reader never sees it. It will be read, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends. And write it in Pinder's spirit: put a finger in the wound of dating apps, through these two.
"""))
assert PROMPTS["v7"]["chat"] != PROMPTS["v6"]["chat"]
TEXTING["v7"] = False
GROUPED = {"v6", "v7"}
# v8: the brief cut to its what and why (brief_short); the chat is one joke with one punchline. The
# joke heading holds the punchline word for word, the joke in one sentence, and why it is Pinder.
PROMPTS["v8"] = dict(PROMPTS["v7"], chat=PROMPTS["v7"]["chat"][:PROMPTS["v7"]["chat"].index("# The chat")] + """# The chat

You'll write their chat on the app as a joke: one joke, with one punchline. Work it out before you write.

First, under the heading "## The joke", write three things:

1. The punchline: the last message of the chat, word for word.
2. The joke, in one sentence: what the reader believes until that message, and what the message makes them see.
3. Why it's Pinder: in two or three sentences, what it says about dating apps and the people on them, and why it lands on these two.

Then, under the heading "## The chat", write the chat: no longer than {words} words, {a} first, one message per line, as "{a}: message" or "{b}: message", ending on the punchline, and nothing else under that heading. Use only what the joke needs.

The chat has to work without the explanation: the reader never sees it. It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.
""")
assert PROMPTS["v8"]["chat"].startswith("{brief}")
TEXTING["v8"] = False
GROUPED = {"v6", "v7", "v8"}
SHORT_BRIEF = {"v8"}  # versions that get brief_short()
JOKE_FIRST = {"v7", "v8"}  # versions whose chat answer is "## The joke" then "## The chat", saved apart
# v9: the short brief for both tasks. The character task gives the game's notes shuffled per writer,
# with no headings and the claims apart from what's true; the writer states their own take first and
# picks the form. The chat is worked out as a bit under "## The bit" (the idea and how they found it,
# why it's funny, why it's Pinder), with no punchline written in advance and no required first speaker.
PROMPTS["v9"] = dict(PROMPTS["v8"], user="""{brief}

# Your task

Pinder needs a character: {char} ({pronouns}). Below are notes about {obj} from the game's files, in no particular order. Some are better than others, and a few contradict each other. They're raw material, not a spec: keep the two or three that make you laugh, change them, or ignore them all and invent {obj} yourself.

Start with your take on {obj}: in a few lines, what's funny about {obj} to you, the thing you'd build a bit on. Then write {obj} in whatever form you'd want in hand later: notes, a list, a monologue in {poss} voice, anything. Make it as long as it's useful to you, and no longer. Somewhere in it, say how {subj} types.

Later you'll write a chat between {obj} and another Pinder character, working from what you write here and nothing else.

## Notes on {char}

{tidbits}
""", chat=PROMPTS["v8"]["chat"][:PROMPTS["v8"]["chat"].index("# The chat")] + """# The chat

Write their chat on the app: a comedy bit to be read on a phone. One idea, played out, ending on the laugh. Use as much or as little of the characters as the bit needs.

First, under the heading "## The bit", work it out. This is for you; the reader never sees it.

1. The idea. What comic premise do you see in these two together, the thing you'd build a bit on? And how did you get there: what in the material caught you, what you're exaggerating or turning around, how you usually find a bit and how you found this one. Don't write the punchline here. Say what kind of turn it will be and why it follows from the premise.
2. Why it's funny. What does the reader expect, what do they get instead, and why does that gap make them laugh? "Because it's true" or "because it's sad" is not an answer: say where the laugh is.
3. Why it's Pinder. What about dating apps does this make fun of (a belief, a habit, a phrase people say with a straight face), and how do these two make it ridiculous? The reader should laugh at it first and recognise themselves second.

Then, under the heading "## The chat", write the chat: no longer than {words} words (shorter is fine), one message per line, as "{a}: message" or "{b}: message", and nothing else under that heading.

It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.
""")
assert PROMPTS["v9"]["chat"].startswith("{brief}") and "{a} first" not in PROMPTS["v9"]["chat"]
TEXTING["v9"] = False
SHORT_BRIEF = {"v8", "v9"}
JOKE_FIRST = {"v7", "v8", "v9"}  # v9's heading is "## The bit"; it is saved as <writer>.joke.md like the joke
LOOSE = {"v9"}  # versions that give the notes ungrouped, shuffled per writer, claims apart from truths
# v10 (--pair): three sessions per writer. The writer first rewrites the short brief in their own words;
# that text is their brief from then on. Then one session sees both characters' notes and writes a
# short take on each (what's funny, what they're afraid of) and how the two clash or click. Then the
# chat, from their own brief and that text alone: one joke or a run of jokes, set up early and paid off
# at the end, chat only.
OWN_BRIEF = """Here's Pinder's brief, as you put it in your own words.

{own}
"""
PROMPTS["v10"] = dict(PROMPTS["v9"], brief_task="""{brief}

# Your task

Rewrite this brief in your own words.
""", user=OWN_BRIEF + """
# Your task

Two of Pinder's characters are about to match: {a} ({a_pron}) and {b} ({b_pron}). Below are notes about each of them from the game's files, in no particular order. Some are better than others, and a few contradict each other. They're raw material, not a spec: keep what makes you laugh, change it, or ignore it and invent.

Write your take on each of them: what's funny about them to you, and the one thing they're afraid of, in no more than about 150 words each. Then say how the two of them clash, or click, in a way nobody would expect: the thing you'd build a bit on. Use the headings "## {a}", "## {b}" and "## The two of them".

Keep it short. This is what you'll have in hand when you write their chat, and nothing else.

# Notes on {a}

{a_notes}

# Notes on {b}

{b_notes}
""", chat=OWN_BRIEF + """
# Your task

Here's what you wrote about the two characters.

{characters}

# The chat

Improvise their chat on the app: one joke, or a run of jokes, no longer than {words} words. Set something up early and pay it off at the end, so the last message picks up something from the beginning and the chat feels rounded. End on the laugh.

Write only the chat: one message per line, as "{a}: message" or "{b}: message".

It's a text chat, read on a phone, not performed: nobody hears your voice or your timing, so the timing has to be in what they type and where each message ends.
""")
TEXTING["v10"] = False
SHORT_BRIEF = {"v8", "v9", "v10"}
PAIRED = {"v10"}  # versions run with --pair: own brief, then both characters in one session, then the chat


def split_joke(text):
    """("joke", "chat") from an answer with a "## The joke" and a "## The chat" heading, else None."""
    m = re.search(r"^(?:#{1,4}\s*|\*\*)The chat(?:\*\*)?:?\s*$", text, re.I | re.M)
    if not m:
        return None
    joke = re.sub(r"^\s*(?:#{1,4}\s*|\*\*)The (?:joke|bit)(?:\*\*)?:?\s*\n", "", text[:m.start()], flags=re.I).strip()
    chat = text[m.end():].strip()
    return (joke, chat) if joke and chat else None  # versions that give the fragments under headings instead of one shuffled list  # whether the game's texting-style fragments are in the list
VERSION = os.environ.get("TONELAB_WRITERS_VERSION", "v10")
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


def brief_full():
    y = yaml.safe_load(open(os.path.join(REPO, "tools/tonelab/variants/live/situations.yaml"), encoding="utf-8"))
    return y["prompts"]["bible"].strip()


def brief_short():
    """The brief's what and why only: the opening, "What Pinder is" and "What the reader should feel".
    Everything from "How the reader comes to feel it" on (the how, the medium, how they write, your
    job) is cut, and so are the opening's promise of the how and the reading time."""
    b = brief_full()
    b = b[:b.index("\n## How the reader comes to feel it")].rstrip()
    for a, c in (("This brief tells you what Pinder is, what we want a reader to feel, and how a chat makes them feel it.",
                  "This brief tells you what Pinder is and what we want a reader to feel."),
                 ("Someone reads a whole chat on their phone in about five minutes. In that time they get to know two people.",
                  "Someone reads a whole chat on their phone. In that time they get to know two people.")):
        assert b.count(a) == 1, a
        b = b.replace(a, c)
    return b


def brief():
    return brief_short() if VERSION in SHORT_BRIEF else brief_full()


def tidbits_loose(d, items, wid):
    """The game's notes as one list with no headings, shuffled with a seed of the writer's own: the app
    profile, personality and backstory fragments, each claim and each truth as separate notes, and the
    fears. No texting fragments and no physique."""
    pron = d.get("gender_identity", "they/them")
    p = POSS.get(pron, "their")
    out = [f"{p.capitalize()} profile on the app: {d['bio'].strip()}"]
    for iid in d["items"]:
        it = items[iid]
        for k in ("personality_fragment", "backstory_fragment"):
            if (it.get(k) or "").strip():
                out.append(it[k].strip())
    for cat, v in d["backstory_categories"].items():
        if cat == "physical_dysmorphia":
            continue
        out += [v["bio_lie"].strip(), v["tragic_reality"].strip()]
    for line in d.get("psychological_stake", "").splitlines():
        line = line.strip().lstrip("- ").strip()
        if line and "about my body" not in line:
            out.append(line)
    out += [x.strip() for x in d.get("stake_lines", []) if x.strip()]
    random.Random(f"{SEED}-{wid}").shuffle(out)
    return "\n".join("- " + x for x in out)


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
        if label == "chat" and VERSION in JOKE_FIRST:
            with open(os.path.join(out, f"{wid}.raw.md"), "w", encoding="utf-8") as f:
                f.write(text + "\n")
            parts = split_joke(text)
            if not parts:
                results[wid]["error"] = "no \"## The joke\" and \"## The chat\" headings in the answer (kept in .raw.md)"
                print(f"{label} {wid}: no bit/chat headings; answer kept in {wid}.raw.md", flush=True)
                return
            joke, text = parts
            results[wid].update(words=len(text.split()), joke_words=len(joke.split()))
            lines = [ln.split(":", 1)[1].strip() for ln in text.splitlines() if ":" in ln]
            if lines and VERSION == "v8":  # only v8 states its punchline word for word
                norm = lambda s: re.sub(r"[^a-z0-9]+", " ", s.lower()).strip()
                results[wid]["punchline_is_last"] = norm(lines[-1]) in norm(joke)
            with open(os.path.join(out, f"{wid}.joke.md"), "w", encoding="utf-8") as f:
                f.write(joke + "\n")
        with open(os.path.join(out, f"{wid}.md"), "w", encoding="utf-8") as f:
            f.write(text + "\n")
        with open(os.path.join(out, f"{wid}.prompt.md"), "w", encoding="utf-8") as f:
            f.write(f"<!-- system prompt -->\n{system_for(wid, name)}\n\n<!-- user prompt -->\n{user}")
        print(f"{label} {wid}: {len(text.split())} words, {seconds}s, ${usage.get('cost')}"
              + (f", joke {results[wid]['joke_words']} words" if "joke_words" in results[wid] else ""), flush=True)

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


def pair(a, chosen, key):
    """v10: per writer, their own brief, then both characters in one session, then the chat."""
    x, y = a.pair.split(",")
    items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
    ds = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in (x, y)}
    P = PROMPTS[VERSION]
    ok = fan_out([(w, n, P["brief_task"].format(brief=brief())) for w, n in chosen], key, os.path.join(a.out, "brief"), "brief")

    def done(folder, w):
        p = os.path.join(a.out, folder, f"{w}.md")
        return open(p, encoding="utf-8").read().strip() if os.path.exists(p) else None

    jobs = []
    for w, n in chosen:
        own = done("brief", w)
        if not own:
            print(f"characters {w}: no brief of their own, skipped", flush=True)
            ok = False
            continue
        jobs.append((w, n, P["user"].format(own=own, a=ds[x]["name"], b=ds[y]["name"],
                                            a_pron=ds[x].get("gender_identity", "they/them"),
                                            b_pron=ds[y].get("gender_identity", "they/them"),
                                            a_notes=tidbits_loose(ds[x], items, w), b_notes=tidbits_loose(ds[y], items, w))))
    ok = fan_out(jobs, key, os.path.join(a.out, "characters"), "characters") and ok
    jobs = []
    for w, n in chosen:
        own, chars = done("brief", w), done("characters", w)
        if not (own and chars):
            print(f"chat {w}: missing their brief or characters, skipped", flush=True)
            ok = False
            continue
        jobs.append((w, n, P["chat"].format(own=own, characters=chars, a=ds[x]["name"], b=ds[y]["name"], words=a.words)))
    return fan_out(jobs, key, os.path.join(a.out, f"chat-{x}-{y}"), "chat") and ok


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key-stdin", action="store_true")
    ap.add_argument("--only", default="", help="write this character's profiles")
    ap.add_argument("--chat", default="", help="a,b: each writer writes a chat between their a and b")
    ap.add_argument("--pair", default="", help="a,b: v10's three sessions per writer (own brief, both characters, chat)")
    ap.add_argument("--writers", default="")
    ap.add_argument("--words", type=int, default=500, help="the chat's word limit, for prompt versions that have one")
    ap.add_argument("--out", default=os.path.join(REPO, "docs/tone-lab/scratch/writers"))
    a = ap.parse_args()
    if not a.key_stdin:
        tl.die("writers: the key comes on stdin (--key-stdin)")
    key = sys.stdin.readline().strip().lstrip("﻿").strip()
    chosen = [w for w in WRITERS if not a.writers or w[0] in a.writers.split(",")]
    ok = True
    if a.pair:
        if VERSION not in PAIRED:
            tl.die(f"writers: --pair needs a paired prompt version, not {VERSION}")
        ok = pair(a, chosen, key)
    if a.only:
        items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
        slugs = a.only.split(",")
        done = {}

        def one(slug):
            d = json.load(open(os.path.join(REPO, "data/characters", f"{slug}.json"), encoding="utf-8"))
            pron = d.get("gender_identity", "they/them")
            fill = dict(brief=brief(), char=d["name"], pronouns=pron, obj=OBJ.get(pron, "them"),
                        subj=SUBJ.get(pron, "they"), poss=POSS.get(pron, "their"))
            if VERSION in LOOSE:  # each writer gets the notes in an order of their own
                jobs = [(w, n, USER.format(tidbits=tidbits_loose(d, items, w), **fill)) for w, n in chosen]
            else:
                user = USER.format(tidbits=tidbits(d, items), **fill)
                jobs = [(w, n, user) for w, n in chosen]
            done[slug] = fan_out(jobs, key, os.path.join(a.out, slug), slug)

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
