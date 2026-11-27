"""Build the writers' reading page with every run: each run's chats and profiles, and how each was made.

"How it was made" is built from the exact prompts saved next to every output (<writer>.prompt.md),
in the order the writer read them: per session, the system prompt, then the user message. Inside a
user message the brief and the pasted profiles are folded, at the place they occur, so the page
stores the brief once per run and each profile once.
"""
import hashlib, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import writers as w  # noqa: E402
import functools  # noqa: E402
# the briefs are parsed from YAML on every call, and the page asks for them per session: read each once
w.brief_full, w.brief_short = functools.cache(w.brief_full), functools.cache(w.brief_short)

BASE = os.path.join(w.REPO, "docs/tone-lab/scratch")
SHOWN_FROM = 16  # Daniel, 2026-11-27: run 16 and later only
FIRST = "Gemini 4 Argon"
# newest first: (run id, label, output folder, about, where its profiles were written)
RUNS = [
    ("v22", "Run 22: no writers", "writers-v22",
     "Run 22. Run 21 again (Reuben and Brick on seed 23, the balance, T42 and M10, template v2) with no writers: Claude writes the two characters once, with no persona, from Pinder's premise and the game's notes, and one chat is written from them. The premise loses its two lines in one comedian's voice.", ""),
    ("v21", "Run 21: the theme and the method, explained", "writers-v21",
     "Run 21. Run 18 again (Reuben and Brick on seed 23, the balance, T42 and M10, each comedian's characters), on template v2: the setup says what the theme and the method are for (two tools, so the chat adds up to one comic idea; how they apply to these two and where they meet is the writer's call), and names the balance where it named the game's interest. The theme's explanation no longer cites its source.", "run 11"),
    ("v20", "Run 20: Zyx & Sable, five in a row", "writers-v20",
     "Run 20. " + "Run 17's prompt with the tone lab's balance in the improvisations instead of the game's interest: each move of the player's lands (+1), does nothing (0) or backfires (-1); every improvisation says which, and where the balance stands, in words, and the other one's reply follows it. Five in a row that land are a date, five that backfire an unmatch, and four in a row are announced as one more; otherwise the chat has eleven improvisations and the balance after the last decides. Theme T42 and method M10, as in run 17." + " Zyx and Sable on another seed (34): the first two moves land, then five in a row backfire, and Sable unmatches Zyx after seven improvisations.", "run 15"),
    ("v19", "Run 19: Zyx & Sable, the balance", "writers-v19",
     "Run 19. " + "Run 17's prompt with the tone lab's balance in the improvisations instead of the game's interest: each move of the player's lands (+1), does nothing (0) or backfires (-1); every improvisation says which, and where the balance stands, in words, and the other one's reply follows it. Five in a row that land are a date, five that backfire an unmatch, and four in a row are announced as one more; otherwise the chat has eleven improvisations and the balance after the last decides. Theme T42 and method M10, as in run 17." + " Zyx and Sable on run 16's seed 23, the first eleven of its fourteen moves: the balance ends at -4, and Sable unmatches Zyx.", "run 15"),
    ("v18", "Run 18: Reuben & Brick, the balance", "writers-v18",
     "Run 18. " + "Run 17's prompt with the tone lab's balance in the improvisations instead of the game's interest: each move of the player's lands (+1), does nothing (0) or backfires (-1); every improvisation says which, and where the balance stands, in words, and the other one's reply follows it. Five in a row that land are a date, five that backfire an unmatch, and four in a row are announced as one more; otherwise the chat has eleven improvisations and the balance after the last decides. Theme T42 and method M10, as in run 17." + " Reuben and Brick on seed 23, run 17's game: the balance ends at +4, a date.", "run 11"),
    ("v17", "Run 17: the frozen prompt, improvised", "writers-v17",
     'Run 17. The one-go prompt that worked by hand on 26 November, frozen and played as improvisations. Claude writes the chat, not the comedians; each of the three chats is written on a different comedian\'s descriptions of Reuben and Brick from run 11, without their take on how the two clash or click. The prompt is Pinder\'s premise, those two descriptions, three acts laid over the improvisations, and one theme and one method from the writers\' pool for the whole chat: T42 "Not recognised by your own people" and M10 "The outrageous ask in the politest voice", the draw of the hand run. Seed 23 as in run 14, eleven improvisations, at most 500 words.', "run 11"),
    ("v16", "Run 16: Zyx & Sable, improvised", "writers-v16",
     'Run 16. Run 15\'s briefs and characters, and the chat as improvisations, as in run 14: one session per comedian, at most 500 words. The game played Zyx_444 and Sable_xo on seed 23, the seed Reuben and Brick were played on; with these two it goes fourteen steps and Sable unmatches Zyx at the end. Two pieces of text are new: verbs agree with they/them ("they try"), and The Disarm, Wit then Honesty, which Reuben never reached. The last step does not say that a shadow carries on.', "run 15"),
    ("v15", "Run 15: Zyx & Sable, in one go", "writers-v15",
     "Run 15. The whole of run 11's workflow for two characters the comedians had not written before, drawn at random from the game's other four: Zyx_444 (they/them), who plays, and Sable_xo. Each comedian rewrites Pinder's short brief in their own words, then writes both characters from the game's notes in a new session, then the chat in one go in a third, at most 500 words: run 11's prompts, unchanged.", None),
    ("v14", "Run 14: improvisations, not game rules", "writers-v14",
     "Run 14. Run 13's set-up (run 11's briefs and characters, seed 23, eleven steps in one session, at most 500 words), with the game's language taken out of what the comedian reads. No Dungeons & Dragons frame, no rolls or numbers, and no \"turn\": each step is \"Improvisation n\". Reuben chooses a stat, and the text says what it means and how it comes out; on a miss, which of his shadows he reacts with; a trap is that shadow carrying on into the next improvisation, two in all (it was three before, and in seed 23 that covered six of the eleven). Brick's interest is said in words only. Each step ends \"what Reuben sends, and what Brick sends back\".", "run 11"),
    ("v13", "Run 13: turn by turn, any number of messages", "writers-v13",
     "Run 13. Run 12 again, with one change: the chat prompt and the end of each turn's account no longer ask for \"Reuben's message and Brick's reply\", which made every turn of run 12 exactly two lines. They now ask for what Reuben sends this turn and what Brick sends back, however many messages that takes. Everything else is run 12's: run 11's briefs and characters, seed 23's dice, the same pieces, eleven turns in one session, at most 500 words in all.", "run 11"),
    ("v12", "Run 12: turn by turn on the dice", "writers-v12",
     "Run 12. Run 11's comedians, with their own briefs and their characters from run 11, unchanged. The chat is now played turn by turn on seed 23's dice, in one session: each turn the comedian gets the game's account of what happened (which stat Reuben rolled and what it means, how well it went, the stat's shadow on a miss, traps, steering, combos, and how Brick's interest moved) and writes Reuben's message and Brick's reply. Every account is put together from fixed pieces of text, so the same dice always give the same words. Eleven turns, at most 500 words in all, set up early and paid off at the end.", "run 11"),
    ("v11", "Run 11: their own brief, both at once", "writers-v11",
     "Run 11. Each comedian first rewrites the short brief in their own words, and that text is their brief from then on. Then one session with the game's notes on both characters: a short take on each (what's funny about them, the one thing they're afraid of, about 150 words each) and how the two clash or click in a way nobody would expect. Then the chat, from their own brief and that text alone: one joke or a run of jokes, at most 500 words, set up early and paid off at the end, so the last message picks up something from the beginning. Only the chat comes back.", None),
    ("v10", "Run 10: the bit, and room", "writers-v10",
     "Run 10. The same three comedians write both characters and the chat again, all from the brief cut to what Pinder is and what the reader should feel. Each writer gets the game's notes about a character in an order of their own, with no headings and with each claim apart from what's true; they start with their own take on the character and write it in whatever form they want. The chat is worked out first as a bit, under \"The bit\": the comic idea and how they found it (without the punchline), why it's funny, and why it's Pinder. Then the chat, at most 500 words, with either character free to start.", None),
    ("v9", "Run 9: one punchline, short brief", "writers-v9",
     "Run 9. Run 7's profiles, unchanged, and a new chat session for the same three comedians. The brief is cut to what Pinder is and what the reader should feel; the how, the medium, how they write and the writer's job are gone. The chat is one joke with one punchline: under \"The joke\" the writer gives the punchline (the chat's last message, word for word), the joke in one sentence, and why it is Pinder; then the chat, at most 500 words, ending on the punchline, using only what the joke needs.", "run 7"),
    ("v8", "Run 8: the joke first", "writers-v8",
     "Run 8. Run 7's profiles, unchanged, and a new chat session for the same three comedians. The chat task asks first for the joke, under its own heading: the setup, where it turns, the punchline, and why it could only land on these two. Then the chat under its own heading, at most 500 words, one message per line as \"Name: message\". The joke is saved apart from the chat; the chat is read and scored without it.", "run 7"),
    ("v7", "Run 7: the writer's own pitch", "writers-v7",
     "Run 7. Three of the comedians (Ali Wong, Joel Kim Booster, Jimmy Carr), each with a system prompt that says what they are known for and how they work, and that the brief comes first. The game's fragments are given as \"what we know\" about each character, under headings; the profile is written knowing it is for a short chat; the chat is written as a joke, to be read, in at most 500 words.", None),
    ("v6", "Run 6: 500 words", "writers-v6",
     "Run 6. As run 5, with the chat prompt saying \"no longer than 500 words\": the six comedians' profiles from run 4, unchanged.", "run 4"),
    ("v5", "Run 5: 1,000 words", "writers-v5",
     "Run 5. The six comedians' profiles from run 4, unchanged; the chat prompt says \"no longer than 1,000 words\" where it said \"as long or as short as it should be\".", "run 4"),
    ("v4", "Run 4: six comedians", "writers-v4",
     "Run 4. Run 3's prompts, unchanged, given to six comedians who work on dating, sex and identity: Aziz Ansari, Ali Wong, Mae Martin, Joel Kim Booster, Jimmy Carr and Amy Schumer.", None),
    ("v3", "Run 3: own texting style", "writers-v3",
     "Run 3. As run 2, but the game's texting-style fragments are removed from the list, and the profile prompt adds one sentence: \"Develop your own texting style for this character, so the character is distinct.\"", None),
    ("v2", "Run 2: the writer decides", "writers-v2",
     "Run 2. The system prompt makes the writer the author and the authority on the characters and the dialogue; the game's material is raw material they may throw away; form and length are theirs.", None),
    ("v1", "Run 1: first prompt", "writers",
     "Run 1. One line of persona (\"You are <writer>.\"); the game's material offered as inspiration, not requirements; the chat \"without any constraints\".", None),
]
REPO = w.REPO
chars = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in ("reuben", "brick")}
NAME = {s: chars[s]["name"] for s in chars}
PRON = {"reuben": "him", "brick": "her"}
items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}


def read(p):
    return open(p, encoding="utf-8").read().strip()


def split_prompt(path):
    t = open(path, encoding="utf-8").read()
    head, user = t.split("<!-- user prompt -->\n", 1)
    system = head.split("<!-- system prompt -->\n", 1)[1]
    return system.strip(), user.rstrip("\n")


def fold(text, inserts):
    """Split text at each insert's exact occurrence: [{"t": "md", "s"}, {"t": kind, ...}]."""
    segs = [{"t": "md", "s": text}]
    for needle, seg in inserts:
        if not needle:
            continue
        out = []
        for s in segs:
            if s["t"] == "md" and needle in s["s"]:
                a, b = s["s"].split(needle, 1)
                out += [{"t": "md", "s": a}, dict(seg), {"t": "md", "s": b}]
            else:
                out.append(s)
        segs = out
    return [s for s in segs if s["t"] != "md" or s["s"].strip()]


briefs, runs, stats = {}, [], {"brief folded": 0, "brief not found": 0, "profile folded": 0, "profile not found": 0}
for rid, label, sub, about, profiles_from in RUNS:
    if int(rid[1:]) < SHOWN_FROM:  # the page shows the current runs; older ones stay in the repo
        continue
    O = os.path.join(BASE, sub)
    # the pair is the chat folder's name, chat-<player>-<match>; runs 1 to 14 are Reuben and Brick
    chs = sorted(d for d in os.listdir(O) if d.startswith("chat-")) if os.path.isdir(O) else []
    CH = chs[0] if chs else "chat-reuben-brick"
    pair = CH[len("chat-"):].split("-")
    pnames = [json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8"))["name"] for s in pair]
    if not os.path.exists(os.path.join(O, CH, "run.json")):
        print(f"{rid}: no chats yet, left out")
        continue
    meta = json.load(open(os.path.join(O, CH, "run.json"), encoding="utf-8"))
    tpl = bool(meta.get("template"))  # run 17 on: Claude writes from a frozen template on a comedian's characters
    rec = meta["writers"]
    neutral = meta.get("characters_by") == w.NEUTRAL_NAME  # v17 on: no writers, Claude's characters and one chat
    writers = []
    for wid, name in ([(w.NEUTRAL_ID, w.NEUTRAL_NAME)] if neutral else w.WRITERS):
        if wid not in rec or "error" in rec[wid]:
            continue
        paired = os.path.isdir(os.path.join(O, "characters"))
        if paired:  # run 11 on: their own brief, then both characters in one session
            texts = {"ownbrief": read(os.path.join(O, "brief", f"{wid}.md")), "characters": read(os.path.join(O, "characters", f"{wid}.md")),
                     "reuben": None, "brick": None}
        else:
            texts = {s: read(os.path.join(O, s, f"{wid}.md")) for s in ("reuben", "brick")}
            texts.update(ownbrief=None, characters=None)
        by = name
        if tpl:  # the brief folder holds Pinder's premise, not the comedian's own brief
            premise, texts["ownbrief"] = texts["ownbrief"], None
            name = name if neutral else f"{name}'s characters"
        texts["chat"] = read(os.path.join(O, CH, f"{wid}.md"))
        tj = os.path.join(O, CH, f"{wid}.turns.json")
        tlog = json.load(open(tj, encoding="utf-8")) if os.path.exists(tj) else None
        jp = os.path.join(O, CH, f"{wid}.joke.md")
        texts["joke"] = read(jp) if os.path.exists(jp) else None
        steps = []
        order = (("ownbrief", "brief"), ("characters", "characters"), ("chat", CH)) if paired else \
            (("reuben", "reuben"), ("brick", "brick"), ("chat", CH))
        if tpl:  # the characters' sessions are run 11's, described on the How it was made tab
            order = (("characters", "characters"), ("chat", CH)) if neutral else (("chat", CH),)
        for i, (slug, folder) in enumerate(order):
            pp = os.path.join(O, folder, f"{wid}.prompt.md")
            note = None
            if tlog and slug != "chat" and not neutral:  # run 12: these two sessions are run 11's
                src = meta["from"].rstrip("/").split("/")[-1]  # the scratch name, or the repo's name without "-out"
                src = src if os.path.isdir(os.path.join(BASE, src)) else src.replace("-out", "")
                pp = os.path.join(BASE, src, folder, f"{wid}.prompt.md")
            if tlog and slug == "chat":
                system, user = tlog["system"].strip(), tlog["turns"][0]["prompt"].rstrip("\n")
            elif os.path.exists(pp):
                system, user = split_prompt(pp)
            else:
                # run 1's Brick came from the first script, which kept no prompt: rebuild it from run 1's template
                assert rid == "v1" and slug == "brick", pp
                d = chars[slug]
                pron = d.get("gender_identity", "they/them")
                system = w.PROMPTS["v1"]["system"].format(name=name)
                user = w.PROMPTS["v1"]["user"].format(brief=w.brief_full(), char=d["name"], pronouns=pron,
                                                      obj=w.OBJ.get(pron, "them"), tidbits=w.tidbits(d, items, texting=True))
                note = "This session's prompt was not saved: the first script kept none. Shown is run 1's profile template filled with Brick's fragments, rebuilt the same way as Reuben's, which was saved."
                # the check that makes the rebuild credible: the same rebuild for Reuben gives his saved prompt
                rd = chars["reuben"]
                rs, ru = split_prompt(os.path.join(O, "reuben", f"{wid}.prompt.md"))
                rb = w.PROMPTS["v1"]["user"].format(brief=w.brief_full(), char=rd["name"], pronouns=rd["gender_identity"],
                                                    obj=w.OBJ.get(rd["gender_identity"], "them"), tidbits=w.tidbits(rd, items, texting=True))
                stats["v1 rebuild matches Reuben's saved prompt"] = stats.get("v1 rebuild matches Reuben's saved prompt", 0) + (rb.rstrip("\n") == ru and rs == system)
            inserts = []
            # the brief: the longest stored brief that occurs in this prompt, else the current one
            cur = premise if tpl else next((x for x in (w.brief_full(), w.brief_short()) if x in user), w.brief_full())
            if cur in user:
                bid = hashlib.sha256(cur.encode()).hexdigest()[:10]
                briefs[bid] = cur
                inserts.append((cur, {"t": "brief", "id": bid}))
                stats["brief folded"] += 1
            else:
                stats["brief not found"] += 1
            if paired and slug != "ownbrief" and not tpl:
                inserts.append((texts["ownbrief"], {"t": "ownbrief"}))
                stats["own brief folded"] = stats.get("own brief folded", 0) + (texts["ownbrief"] in user)
            if paired and slug == "chat":
                inserts.append((texts["characters"], {"t": "chars"}))
                stats["characters folded"] = stats.get("characters folded", 0) + (texts["characters"] in user)
            if slug == "chat" and not paired:
                for s in ("reuben", "brick"):
                    if texts[s] in user:
                        inserts.append((texts[s], {"t": "profile", "who": s}))
                        stats["profile folded"] += 1
                    else:
                        stats["profile not found"] += 1
            steps.append({"slug": slug, "system": system, "user": fold(user, inserts), "note": note})
        turns = [{"n": e["turn"], "game": ((e["prompt"].split("# Turn 1\n\n", 1) if "# Turn 1\n\n" in e["prompt"] else e["prompt"].split("# Improvisation 1\n\n", 1))[1] if e["turn"] == 1 else e["prompt"].split("\n\n", 1)[1]).strip(),
                  "lines": e["lines"]} for e in tlog["turns"]] if tlog else None
        writers.append({"id": wid, "name": name, "by": by, "chat": texts["chat"], "joke": texts["joke"], "reuben": texts["reuben"],
                        "brick": texts["brick"], "ownbrief": texts["ownbrief"], "characters": texts["characters"], "steps": steps,
                        "turns": turns})
    mp = os.path.join(O, "mechanics.json")
    tally = None
    if os.path.exists(mp):  # the tone lab's balance, worked out from the game's results for every improvised run
        mech = json.load(open(mp, encoding="utf-8"))
        score, date = w.tally(mech)
        tally = {"points": [s for s, _ in score], "balance": [b for _, b in score], "date": date, "game": mech.get("outcome")}
        if (meta.get("tally") or {}).get("why"):  # v15 on: the tone lab's rule ended the chat, not the game
            tally = dict(meta["tally"], game=None)
    runs.append({"id": rid, "label": label, "about": about, "profiles_from": profiles_from, "tally": tally,
                 "paired": os.path.isdir(os.path.join(O, "characters")),
                 "turns": os.path.exists(os.path.join(O, "mechanics.json")),
                 "seed": (meta.get("mechanics") or {}).get("seed"),
                 "nturns": (meta.get("mechanics") or {}).get("turns_played"),
                 "model": FIRST if "argon" in (meta.get("model") or "") else
                 "Claude (opus) through the Claude CLI" if tpl else meta.get("model"),
                 "template": tpl, "neutral": neutral,
                 "reasoning": meta.get("reasoning"), "writers": writers,
                 "player": pnames[0], "match": pnames[1], "title": " & ".join(s.capitalize() for s in pair),
                 "pair_text": " and ".join(s.capitalize() for s in pair)})
data = {"player": NAME["reuben"], "match": NAME["brick"], "names": NAME, "pron": PRON, "briefs": briefs, "runs": runs}
t = read(os.path.join(os.path.dirname(os.path.abspath(__file__)), "writers_page.html"))
out = t.replace("__DATA__", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(os.path.join(BASE, "writers", "index.html"), "w", encoding="utf-8").write(out + "\n")
print(", ".join(f"{r['id']}: {len(r['writers'])} writers" for r in runs), "|", len(out.encode()) // 1024, "KB |",
      len(briefs), "brief(s) |", stats)
