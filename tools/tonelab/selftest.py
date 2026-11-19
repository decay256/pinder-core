#!/usr/bin/env python3
"""Self-check for the tone lab. Calls no model.

1. A --mechanics-only session is reproducible: the same seed and clock give
   byte-identical mechanics.
2. The mechanics are a consistent game: interest chains from turn to turn and
   stays within 0..25.
3. Without --mechanics-only and without an API key the runner refuses to start,
   so the mechanics path is the only one that runs model-free.
4. Every variant renders against real mechanics with no unknown placeholder; a live
   variant renders every call of a few whole games, and its beats never look ahead.
"""

import json
import os
import re
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tonelab  # noqa: E402


def check(cond, msg):
    if not cond:
        sys.exit(f"FAIL: {msg}")
    print(f"ok: {msg}")


def check_live(name, variant, knobs, tmp):
    """A live variant: every prompt renders on every turn of a few real games, one session writes
    every turn, and the story beats never look ahead (a game cut short gives the same beats up
    to the cut)."""
    import live
    table = live.load_table(variant)
    import html as _html
    import situations
    page = situations.page(table)
    texts = list(situations.texts(table))
    missing = [x for x in texts if _html.escape(x) not in page]
    check(len(texts) > 300 and not missing,
          f"the situation page shows every text in the table ({len(texts)} texts; missing: {missing[:2]})")
    with open(situations.PAGE, encoding="utf-8") as f:
        check(f.read() == page, "the committed situation page is rendered from the current table")

    def at(path):
        nodes = [table]
        for seg in path.replace("[]", ".[]").split("."):
            nodes = [x for n in nodes for x in (list(n.values()) if seg == "*" and isinstance(n, dict) else
                                                n if seg == "[]" and isinstance(n, list) else
                                                [n[seg]] if isinstance(n, dict) and seg in n else [])]
        return nodes
    bad = []
    for call in table["assembly"]["calls"]:
        for msg in call["messages"]:
            ph = set()
            for p in msg["template"]:
                got = at(p)
                if not got or not isinstance(got[0], str):
                    bad.append(p)
                    continue
                ph |= set(re.findall(r"\$\{(\w+)\}", got[0]))
            if ph != set(msg["fills"]):
                bad.append(f"{msg['message']}: {sorted(ph ^ set(msg['fills']))}")
            bad += [x for v in msg["fills"].values() for x in v.get("from") or [] if not at(x)]
    check(not bad, f"the page's map of how each prompt is built covers every placeholder of every template and "
                   f"names only table sections that exist ({bad[:3]})")
    for seed in (7, 23, 42):
        path = os.path.join(tmp, f"{name}-{seed}.json")
        m = tonelab.play_mechanics("reuben", "brick", seed, 30, tonelab.DEFAULT_CLOCK, path, knobs.get("agent"))
        names = {"player": m["player"]["display_name"], "datee": m["datee"]["display_name"]}
        prompts, session = [], []

        def caller(system, user, label, effort, ctx):
            prompts.append(system + user)
            if label.startswith("turn ") or label == "tracker end":
                session.append((len(ctx.get("history") or []), label, user, system))
            return live.dry_caller(names)(system, user, label, effort, ctx)
        res = live.play(variant, knobs, m, "reuben", "brick", caller)
        n = len(m["turns"])
        role = lambda lb: "tracker" if lb.endswith("tracker") or lb == "tracker end" else lb.split()[-1]
        by = {r: [(k, u, s) for k, lb, u, s in session if role(lb) == r] for r in ("tracker", "director", "writer")}
        check(len(res["turns"]) == n and len(prompts) == 1 + 3 * n and [len(by[r]) for r in by] == [n, n, n],
              f"{name} seed {seed}: a plan call, then a tracker call before every turn but the first and once after "
              f"the last, and a director and a writer call per played turn ({n} turns)")
        check(all([k for k, _, _ in v] == list(range(0, 2 * len(v), 2)) for v in by.values()),
              f"{name} seed {seed}: each of the three sessions carries only its own earlier messages into each call")
        d_users, w_users, t_users = ([u for _, u, _ in by[r]] for r in ("director", "writer", "tracker"))
        check("If they click" in d_users[0] and not any("If they click" in u for u in d_users[1:]),
              f"{name} seed {seed}: the plan goes into the director's session once, at the start")
        hidden = ("If they click", "dry-origin", "dry-accident-line", "Collision:", "dry-event", "dry-story",
                  "Tonight's events", "dry-tests", "dry-gives", "dry-bad", "Good reason")
        seen = [sy + u for r in ("tracker", "writer") for _, u, sy in by[r]]
        check(not any(h in x for x in seen for h in hidden),
              f"{name} seed {seed}: neither the tracker nor the writer sees the plan's origins, accident, collision "
              "or endings")
        check(all("dry-tonight" in sy and "Voice: dry" in sy for _, _, sy in by["writer"])
              and not any("dry-tonight" in sy + u for _, u, sy in by["tracker"]),
              f"{name} seed {seed}: the writer's system prompt carries each character's sheet, the tracker's doesn't")
        check(all("Wants tonight: dry" in sy and "Needs to know: dry" in sy for _, _, sy in by["writer"])
              and "Bad reason: dry-bad" in d_users[0] and "Gives away: dry-gives" in d_users[0],
              f"{name} seed {seed}: the writer knows what each wants tonight and needs to find out; the director "
              "also gets their tests, what they give away and the two reasons")
        check(all(f"Turn {k}\n" in u for j, u in enumerate(d_users) for k in range(1, j + 1))
              and all(f"Turn {k}\n" in u for j, u in enumerate(w_users) for k in range(1, j + 1))
              and all(f"Turn {k}\n" in u for j, u in enumerate(t_users) for k in range(1, j + 2)),
              f"{name} seed {seed}: every role is shown the whole chat so far, every turn")
        check(all("Unanswered:" in u for u in d_users[1:]) and "Unanswered:" not in d_users[0],
              f"{name} seed {seed}: from the second turn on the director gets the tracker's report")
        check("Build it this turn from the plan's Story" in d_users[0]
              and all("Your arc, as you left it after turn" in u for u in d_users[1:]),
              f"{name} seed {seed}: the director builds its arc on the first turn and gets it back every turn after")
        check(not any("What the chat needs" in u or "has to come out this turn" in u or "plant it this turn" in u
                      or "Still to come:" in u for u in d_users)
              and all("- nothing beyond the template" in u for u in d_users[:-1])
              and not any("How the chat ends:" in u for u in d_users[:-1]) and "How the chat ends:" in d_users[-1],
              f"{name} seed {seed}: the game never says what a turn is for or asks for anything before the last "
              "turn, where it says how the chat ends")
        check(len(res["arc_played"]) == n and all(now == "1. dry: dry" for _, now, _ in res["arc_played"]),
              f"{name} seed {seed}: the beat the director played is recorded every turn")
        quiet = d_users[:min(2, n - 1)]
        check(all("The pace, which the game holds and checks afterwards:" in u for u in d_users)
              and all("Nothing big comes out in the first two turns" in u for u in quiet)
              and "This is the last turn: the pace no longer holds" in d_users[-1]
              and not any("This is the last turn" in u for u in d_users[:-1])
              and len(res["pace"]) == n and not any(p[5] for p in res["pace"]),
              f"{name} seed {seed}: the game tells the director the pace every turn, quiet in the first two turns "
              "and free on the last, and a dry run keeps it")
        check(all("Answer: dry" in u for u in w_users)
              and not any(re.search(r"\bTOLD\b|\bRETIRED\b|\bPLANTED\b|\bPAID\b", u) for u in w_users),
              f"{name} seed {seed}: the writer gets the director's brief, without the notes for the game")
        check(not any(" plays " in u or "What that does to the message" in u or re.search(r"(?i)\binterest\b", u)
                      for u in w_users),
              f"{name} seed {seed}: the writer is never told the move, the roll or the meter")
        check(not res["plan_gaps"] and len(res["coverage"]) == 10,
              f"{name} seed {seed}: by the end every piece of the plan is spent or retired ({res['plan_gaps']})")
        turn_users = d_users
        check(len(turn_users) == n and all(f" plays {t['chosen_stat']}, " in u and "What that does to the message:" in u
                                           and "takes it" in u and "Where the chat stands:" in u
                                           and "Repeated so far, counted by the game:" in u and "Where they stand:" in u
                                           and "Where the story is:" in u and "This turn:" in u
                                           for u, t in zip(turn_users, m["turns"])),
              f"{name} seed {seed}: every director turn reads where they stand, the story, the move, the roll, its "
              "reception and where the chat stands")
        check(all("Last turn you read" in u for u in turn_users[1:]),
              f"{name} seed {seed}: from the second turn on, the director sees how it read each of them last turn")
        check(all(not re.search(r"\brolled \d|natural 20|against \d", u) for u in turn_users),
              f"{name} seed {seed}: no roll is described by its numbers")
        check(all("${" not in p for p in prompts), f"{name} seed {seed}: every prompt renders with no unresolved placeholder")
        if knobs.get("profiles"):
            pc = res["common"]["player_card"]
            check("#### The people in their life" in pc and "Body, as the profile shows it" not in pc
                  and "\n- says:" not in pc.split("Tonight, build")[0] and "says:" in pc.split("Tonight, build")[1],
                  f"{name} seed {seed}: with profiles the card is the profile, and one of its brags is the focus")
            check(all(sy.count("texts, by situation") == 2 and sy.count("**Guard down:**") == 2 for _, _, sy in by["writer"]),
                  f"{name} seed {seed}: the writer is told how each one texts by situation")
        st = m["player"]["effective_stats"]
        best = max(st, key=st.get)
        check(f"strong at {best} ({st[best]})" in res["common"]["player_card"] and "${" not in res["common"]["datee_card"],
              f"{name} seed {seed}: each card says what the character is strong and weak at ({best} {st[best]})")
        full = live.beat_keys(knobs, table, m["turns"], m.get("outcome"))
        same = all(live.beat_keys(knobs, table, m["turns"][:k], None)[:k - 1] == full[:k - 1] for k in range(2, n + 1))
        check(same, f"{name} seed {seed}: beats never depend on later turns ({' '.join(full)})")
    names = {"player": "A", "datee": "B"}
    lim = {"ghost": False}
    check(not live.length_problems([("A", "w " * 400), ("B", "w " * 120), ("B", "a"), ("B", "b"), ("B", "c")], lim, names),
          "no length is checked: a long message, a long turn and many messages all pass")
    check(live.length_problems([("A", "hi"), ("B", "hi")], {"ghost": True}, names) == ["B replied, but they ghost"],
          "a ghost who replies is caught")
    check(live.length_problems([("A", "hi")], lim, names) == ["B sends nothing"], "a match who sends nothing is caught")
    shape = live.shape_text([(1, [("A", "one two three"), ("B", "x")]), (2, [("A", "a"), ("A", "b c")])], names)
    check(shape == "A: 1 message (3 words), then 2 messages (3 words); B: 1 message (1 word), then nothing",
          f"each one's shape over the last turns is counted, a silent turn included ({shape})")
    chat = [(1, [("A", "aa one"), ("B", "bb one")]), (2, [("A", "aa two"), ("A", "aa three"), ("B", "bb two")])]
    for flip in (False, True):
        def sorter(system, user, label, effort, ctx, flip=flip):
            got = re.findall(r"(?m)^(\d+)\. (\w+)", user)
            return "\n".join(f"{k}: {'AB'[(w.startswith('aa')) == flip]}" for k, w in got)
        att = live.attribution_check(sorter, chat, names, 7)
        check(att["score"] == 1.0 and att["messages"] == 5 and att["answered"] == 5,
              f"a reader who sorts every message right scores 1, whichever letter they give whom ({att['score']})")
    chat = [(1, [("A", "hm. posture first"), ("B", "lol posture")]), (2, [("A", "hm. posture again")]),
            (3, [("A", "Hm, posture"), ("B", "the end")])]
    rep = live.repeat_counts(chat, names)
    check(rep["openers"] == [("A", "hm.", 3)] and rep["words"] == [("posture", 3)],
          f"an opener used three times and a word in three turns are counted ({rep})")
    check('"hm."' in live.repeats_text(chat, names) and live.repeats_text(chat[:1], names) == "- nothing yet.",
          "what has repeated is told, and nothing is told before anything repeats")
    turns = [{"turn": 1, "writer_draft": "A: hello there\nB: no", "writer_review": '1. holds\n2. "no" → "never"'},
             {"turn": 2, "director_draft": "A: same", "director_review": "1. holds"}]
    ed = live.review_edits(turns, [(1, [("A", "hello there"), ("B", "never")]), (2, [("A", "same")])], names)
    check(ed["turns_changed"] == 1 and ed["words_changed"] == 1 and ed["replacements_named"] == 1,
          f"what the review changed between the draft and the turn as sent is counted ({ed})")
    plan = ("**A**\n**Fear:** being found out\nHabit marker: hm. | posture\nOrigin, in three pieces:\n1. brag\n"
            "2. truth\nAccident: wrong paste\nVoice: lowercase\n- \"example: one\"\nBreak: caps drop\n\n**B**\n"
            "Fear: x\n\nCollision: both\nTonight's events:\n1. the ex texts\nStory:\n1. Opener: acts\n"
            "If they click: y")
    sh = live.sheets_of(plan, names)
    check(all(w in sh["A"] for w in ("being found out", "lowercase", '"example: one"', "caps drop"))
          and not any(w in sh["A"] for w in ("brag", "truth", "wrong paste")) and sh["B"] == "Fear: x",
          f"the writer's sheets are cut from the plan without the origin, the accident or the story ({sh})")
    got = [live.retired_item(x, names) for x in ("A: 3: never close enough", "setup 2: the chat moved on",
                                                 "collision: they click", "the vibe")]
    check(got == [("piece", "A", 3), ("setup", 2), ("collision",), None], f"a RETIRED note is read ({got})")
    arc = live.arc_of("Chat now: x\nStory: y\nARC:\nNow: 3. The door: she asks\nNext:\n4. The rack: he tells\n"
                      "Held: the coat rack, until 4")
    check(live.arc_line(arc, "Now") == "3. The door: she asks" and live.arc_ok(arc) and not live.arc_ok("Now: x")
          and live.arc_of("Chat now: x\nStory: the arc") == "", f"the director's arc is read from its notes ({arc!r})")
    _, _, g = live.director_parts("NOTES:\nx\nBRIEF:\nAnswer: a\nSNAP: B\nTOLD: A: 1: brag", names)
    check(g["snap"] == ["B"] and g["told"] == [("A", "1: brag")], f"a SNAP note names who snaps ({g})")
    check(live.beat_no("3. The door: she asks") == 3 and live.beat_no("The door") is None
          and live.arc_size("Now: 3. x\nNext:\n4. y\n5. z\nHeld: the rack, until 4") == 5 and live.arc_size("") is None,
          "the beat a turn plays and the size of the arc are read from the arc's numbers")
    st0 = {"pieces": {"A": {1}, "B": set()}, "setups": {1: {"planted": 2}}, "collided": None}
    big, deep = live.big_of({"told": [("A", "2: the dad"), ("B", "dry"), ("A", "1: again")], "collided": True,
                             "paid": ["setup 1"], "snap": ["B"]}, st0)
    check(big == ["piece 2 of A's origin", "the collision", "the payoff of setup 1", "B's snap"] and deep == [],
          f"piece 2 or 3, the collision, a payoff and a snap are big; piece 1 and a piece already out are not ({big})")
    r0 = live.Pace().rules(0, False, 12, None)
    check(r0["furthest"] == 1 and r0["early"] and r0["total"] == 12 and not r0["deep_ok"] and r0["deep_from"] == 9,
          f"on the first turn of an expected twelve the pace allows beat 1, nothing big, and piece 3 from turn 9 ({r0})")
    pace = live.Pace()
    got = [pace.rules(i, False, x - i, 8)["total"] for i, x in enumerate((12, 9, 8, 12, 8, 12, 11, 14))]
    check(got == [12, 11, 10, 10, 10, 10, 10, 11],
          f"the pace runs on the average of the meter's estimates, which jump from 8 to 14 on seed 23 ({got})")

    def replay(plays, bigs, n=11, total=12, beats=8):
        pace, back = live.Pace(), []
        for i in range(n):
            r = pace.rules(i, i == n - 1, total - i, beats if i else None)
            b = bigs.get(i + 1, [])
            if pace.problems(r, plays[i], b, [x for x in b if x.startswith("piece 3")]):
                back.append(i + 1)
            pace.record(i, plays[i], b)
        return back
    v19 = {3: ["piece 2 of R"], 4: ["piece 2 of B", "the payoff of setup 1"], 5: ["the collision"], 6: ["piece 3 of R"],
           7: ["piece 3 of B", "the payoff of setup 2"], 10: ["the payoff of setup 3"]}
    back = replay([1, 2, 3, 4, 5, 6, 7, 8, 8, 8, 8], v19)
    check(back == [3, 4, 5, 6, 7, 8],
          f"v19's seed-23 chat, a beat a turn and the big beats from turn 3 to 7, is sent back on every turn from 3 to 8 "
          f"({back})")
    paced = {3: ["piece 2 of R"], 5: ["piece 2 of B"], 7: ["the collision"], 9: ["piece 3 of R"],
             11: ["piece 3 of B", "the payoff of setup 1", "the payoff of setup 2", "the payoff of setup 3"]}
    back = replay([1, 2, 2, 3, 4, 4, 5, 5, 6, 7, 8], paced)
    check(back == [], f"a chat that lets each beat take its time and breathes after each big one passes ({back})")
    back = replay([1, 2, 2, 3, 4, 4, 5, 5, 6, 7, 8], {6: ["piece 3 of R"], 8: ["a", "b", "c"]})
    check(back == [6, 8], f"piece 3 before its turn, and three big things in one turn, are sent back ({back})")
    rd = live.reader_check(lambda system, user, label, *a: "SCORE: 12" if label == "reader grade" else user,
                           "the plan", "A: hi", names)
    check(rd["count"] == 14 and len(rd["questions"]) == 16 and "go home with A" in rd["answers"]
          and rd["score"] == 12.0,
          f"the reader is asked what the two of them would ask, and all but the last two answers are graded ({rd['count']})")
    rep = "REPORT:\nLast turn: x\nUnanswered:\n- turn 2, A: the job\n- turn 3, B: the ex\nCame out: y"
    check(live.unanswered_count(rep) == 2 and live.unanswered_count(rep.replace("- turn 2, A: the job\n- turn 3, B: "
                                                                                "the ex", "- nothing")) == 0,
          "the offers the tracker lists as unanswered are counted, and '- nothing' counts none")
    rc = live.release_check(lambda *a: "1: yes: the job\n2. **partly** hinted", [(1, [("A", "my dad"), ("B", "oh")])],
                            [(1, "his dad graded him"), (1, "the ex")])
    check(rc["score"] == 0.75 and rc["answered"] == 2, f"the release check counts yes as 1 and partly as a half ({rc})")
    brief = "Answer: the job\nFeeling: A: shame\nReleased: nothing\nHeld back: the father"
    check(live.part_of(brief, "Released", ["Answer", "Feeling", "Released", "Held back"]) == "nothing"
          and live.part_of(brief, "Gives", ["Answer", "Gives"]) is None, "a brief is read heading by heading")
    feel = live.feelings_of("Where they are: x\nFeeling: A: shame, strong, at himself. B: joy, mild, at the joke\nMeant: y", names)
    check(feel["A"]["feeling"] == "shame" and feel["B"]["feeling"] == "joy", f"the Feeling note is read per character ({feel})")
    check(len(live.feeling_problems(feel, names, -3, False)) == 1 and not live.feeling_problems(feel, names, 2, False),
          "a match feeling joy on a loss is caught, and on a gain it passes")
    check(len(live.feeling_problems(live.feelings_of("Feeling: A: tired. B: hurt", names), names, -1, False)) == 1,
          "a feeling that isn't on the list is caught")
    keys = [live.roll_key(r) for r in ({"success": True, "total": 22, "dc": 15}, {"success": True, "total": 27, "dc": 15},
                                       {"success": False, "tier": "Legendary", "final_tier": "Catastrophe"},
                                       {"success": False, "tier": "TropeTrap", "final_tier": "TropeTrap"})]
    check(keys == ["strong", "critical", "nat1", "trope_trap"], f"roll results map to the game's tiers ({keys})")


def check_chat_rules():
    """The director's version is checked for lines a dating-app chat can't have."""
    import live
    names = {"player": "A", "datee": "B"}
    bad = live.chat_problems(["meet downstairs so you can wince first?", "I am putting my coat back in the closet",
                              "[B's past: the firm]", "TOLD B 2"], False)
    check(len(bad) == 3 and "one place" in bad[0], "lines that put them in one place, placeholders and labels are caught")
    check(not live.chat_problems(["In the interest of transparency, I am losing", "I'm in bed with my schedule"], False),
          "ordinary lines, a habit marker with 'interest' in it and one's own place pass")
    check(not live.chat_problems(["come over"], True), "after the match agrees to meet, meeting is allowed")
    check(not live.chat_problems(["The heels expose terrible doorway engineering."], False),
          "a doorway as a topic is not a doorway they share (v9 turn 1 flagged one)")
    import yaml
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "variants", "live", "situations.yaml"),
              encoding="utf-8") as f:
        table = yaml.safe_load(f)
    full = all(len(m["tiers"]) == 10 and all(x["message"] and x["received"] and x["length"] for x in m["tiers"].values())
               for m in table["moves"].values())
    span = sorted(v for s in table["interest"] for v in range(s["from"], s["to"] + 1)) == list(range(26))
    check(len(table["moves"]) == 6 and full and span and table["limits"]["chat_words"] > 0,
          "the situation table covers every move and result, and every interest level from 0 to 25 once")
    lines, stray = live.split_stray(live.parse_lines("A: hi\nSNAP B\nB: no", names))
    check(lines == [("A", "hi"), ("B", "no")] and stray == ["SNAP B"], "a line that is neither a message nor a note is caught")
    notes, chk, final, draft, review = live.sections("NOTES:\nWhere they are: x\nCHECK:\n1. y\nFINAL:\nA: hi")
    check(notes.startswith("Where") and chk == "1. y" and final == "A: hi" and not draft and not review,
          "a director answer splits into notes, checklist and final")
    _, chk, final, draft, review = live.sections("NOTES:\nx\nCHECK:\n1. y\nDRAFT:\nA: hey\nREVIEW:\n1. stale: hey\nFINAL:\nA: hi")
    check(chk == "1. y" and draft == "A: hey" and review == "1. stale: hey" and final == "A: hi",
          "the draft and its review are read apart from the checklist and the turn as sent")
    path = [(3, 10), (2, 13), (-1, 15), (5, 14), (-3, 19), (2, 16), (-1, 18), (-3, 17), (2, 14), (5, 16), (4, 21)]
    est = [live.estimate_left([{"interest_delta": d} for d, _ in path[:k]], path[k][1]) for k in range(len(path))]
    jump = max(abs(a - b) for a, b in zip(est, est[1:]))
    check(jump <= 5, f"along v8's meter the turns-left estimate moves at most 5 a turn ({est})")


def check_first_look(variant, knobs):
    """run --stop-after: only the first turns of the full game are written, and none of them is the ending."""
    import live
    m = tonelab.play_mechanics("reuben", "brick", 23, 30, tonelab.DEFAULT_CLOCK,
                               os.path.join(tempfile.mkdtemp(prefix="tonelab-first-"), "m.json"), knobs.get("agent"))
    names = {"player": m["player"]["display_name"], "datee": m["datee"]["display_name"]}
    res = live.play(variant, {**knobs, "stop_after": 2}, m, "reuben", "brick", live.dry_caller(names))
    check(len(m["turns"]) > 2 and [t["turn"] for t in res["turns"]] == [1, 2]
          and not any((t.get("clock_beat") or "").startswith("ending") for t in res["turns"]),
          f"a first look writes turns 1 and 2 of a {len(m['turns'])}-turn game, and neither is the ending")


def check_profiles():
    """v22: the profiles the live cards are built from."""
    import profiles as pf
    T = pf.table()
    chars = {x: pf.character(x) for x in pf.ORDER}
    got = pf.read_profiles()
    check(sorted(got) == sorted(pf.ORDER), f"every bundled character has a profile ({sorted(got)})")
    bad = {k: pf.problems(md, chars[k], T, pf.shared_passages(k, chars)) for k, md in got.items()}
    check(not any(bad.values()), f"every committed profile passes the generator's own checks "
                                 f"({ {k: v[:1] for k, v in bad.items() if v} })")
    check(any("portrait" in x for x in pf.shared_passages("brick", chars))
          and not any("portrait" in x for x in pf.shared_passages("reuben", chars)),
          "a passage copied from another character's fragments is marked shared, and its owner keeps it")
    portrait = lambda x: any(m["name"] == "the cropped family portrait" for m in pf.barred_motifs(chars[x], T))
    check(portrait("brick") and portrait("gerald") and not portrait("reuben"),
          "a spread motif is barred from every profile but the one whose game fragments it comes from")
    broken = got["reuben"].replace("**Guard down:**", "**Guard:**").replace("## How they got here", "## How he got here")
    p = pf.problems(broken, chars["reuben"], T, [])
    check(len(p) == 2, f"a profile with a renamed heading and a missing situation is caught ({p})")
    cv = pf.card_values("reuben")
    check(4 <= len(cv["pairs"]) <= 6 and "**Baseline:**" in cv["texting_by_situation"] and "\n- says:" not in cv["profile"],
          f"a card takes the profile without its brags, and the brags apart ({len(cv['pairs'])} brags)")


def main():
    check_chat_rules()
    check_profiles()
    check_first_look(os.path.join(tonelab.VARIANTS, "live"),
                     json.load(open(os.path.join(tonelab.VARIANTS, "live", "variant.json"), encoding="utf-8")))
    with tempfile.TemporaryDirectory(prefix="tonelab-selftest-") as tmp:
        a, b = os.path.join(tmp, "a.json"), os.path.join(tmp, "b.json")
        ma = tonelab.play_mechanics("reuben", "brick", 7, 30, tonelab.DEFAULT_CLOCK, a)
        tonelab.play_mechanics("reuben", "brick", 7, 30, tonelab.DEFAULT_CLOCK, b)
        with open(a, "rb") as fa, open(b, "rb") as fb:
            check(fa.read() == fb.read(), "same seed and clock give identical mechanics")

        prev = ma["starting_interest"]
        for t in ma["turns"]:
            check(t["interest_before"] == prev, f"turn {t['turn']} starts where the last ended ({prev})")
            check(0 <= t["interest_after"] <= 25, f"turn {t['turn']} interest within 0..25")
            prev = t["interest_after"]
        check(ma["final_interest"] == prev, "final interest matches the last turn")

        env = tonelab.model_free_env()
        env["PINDER_PLAYTESTS_PATH"] = tmp
        res = subprocess.run(["dotnet", tonelab.RUNNER_DLL, "--player", "reuben", "--datee", "brick", "--turns", "1"],
                             capture_output=True, text=True, env=env, timeout=120)
        check(res.returncode != 0 and "ANTHROPIC_API_KEY not set" in res.stderr,
              "without --mechanics-only and without a key the runner refuses to start")

        for name in sorted(os.listdir(tonelab.VARIANTS)):
            variant = os.path.join(tonelab.VARIANTS, name)
            with open(os.path.join(variant, "variant.json"), encoding="utf-8") as f:
                knobs = json.load(f)
            if knobs.get("mode") == "live":
                check_live(name, variant, knobs, tmp)
                continue
            _, system_prompt, prompt, parts = tonelab.build_prompt(variant, knobs, ma, "reuben", "brick")
            check(len(parts["turn_briefs"]) == len(ma["turns"]), f"{name}: one prompt brief per played turn")
            check(bool(system_prompt) and "Turn 1." in prompt and "${" not in system_prompt + prompt,
                  f"{name}: renders with no unresolved placeholder")
    print("selftest passed")


if __name__ == "__main__":
    main()
