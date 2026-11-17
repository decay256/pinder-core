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
    for seed in (7, 23, 42):
        path = os.path.join(tmp, f"{name}-{seed}.json")
        m = tonelab.play_mechanics("reuben", "brick", seed, 30, tonelab.DEFAULT_CLOCK, path, knobs.get("agent"))
        names = {"player": m["player"]["display_name"], "datee": m["datee"]["display_name"]}
        prompts, session = [], []

        def caller(system, user, label, effort, ctx):
            prompts.append(system + user)
            if label.startswith("turn "):
                session.append((len(ctx.get("history") or []), label, user))
            return live.dry_caller(names)(system, user, label, effort, ctx)
        res = live.play(variant, knobs, m, "reuben", "brick", caller)
        n = len(m["turns"])
        check(len(res["turns"]) == n and len(prompts) == 1 + n,
              f"{name} seed {seed}: a plan call and one call per played turn, in one session ({n} turns)")
        check([k for k, _, _ in session] == list(range(0, 2 * len(session), 2)),
              f"{name} seed {seed}: the session carries every earlier message into each call")
        check("If they click" in session[0][2] and not any("If they click" in u for _, _, u in session[1:]),
              f"{name} seed {seed}: the plan goes into the session once, at the start")
        turn_users = [u for _, _, u in session]
        check(len(turn_users) == n and all(f" plays {t['chosen_stat']}, " in u and "What that does to the message:" in u
                                           and "takes it" in u and "Limits, checked:" in u and "Where they stand:" in u
                                           and "Where the story is:" in u and "This turn:" in u
                                           for u, t in zip(turn_users, m["turns"])),
              f"{name} seed {seed}: every turn reads where they stand, the story, the move, the roll, its reception and the limits")
        check(all("Last turn you read" in u for u in turn_users[1:]),
              f"{name} seed {seed}: from the second turn on, the director sees how it read each of them last turn")
        check(all(not re.search(r"\brolled \d|natural 20|against \d", u) for u in turn_users),
              f"{name} seed {seed}: no roll is described by its numbers")
        check(all("${" not in p for p in prompts), f"{name} seed {seed}: every prompt renders with no unresolved placeholder")
        st = m["player"]["effective_stats"]
        best = max(st, key=st.get)
        check(f"strong at {best} ({st[best]})" in res["common"]["player_card"] and "${" not in res["common"]["datee_card"],
              f"{name} seed {seed}: each card says what the character is strong and weak at ({best} {st[best]})")
        full = live.beat_keys(knobs, table, m["turns"], m.get("outcome"))
        same = all(live.beat_keys(knobs, table, m["turns"][:k], None)[:k - 1] == full[:k - 1] for k in range(2, n + 1))
        check(same, f"{name} seed {seed}: beats never depend on later turns ({' '.join(full)})")
    names = {"player": "A", "datee": "B"}
    lim = {"message": 45, "long": None, "turn": 150, "match_total": 10, "ghost": False}
    bad = live.length_problems([("A", "w " * 50), ("B", "w " * 12)], lim, names)
    check(len(bad) == 2 and "50 words" in bad[0] and "12 words" in bad[1],
          "a message over the limit, and a match over its limit after a big loss, are caught")
    check(not live.length_problems([("A", "w " * 60), ("B", "no")], {**lim, "long": 70, "match_total": None}, names),
          "one long message is allowed in an accident or reveal")
    check(live.length_problems([("A", "hi"), ("B", "hi")], {**lim, "ghost": True}, names) == ["B replied, but they ghost"],
          "a ghost who replies is caught")
    over = live.length_problems([("A", "hi"), ("B", "a"), ("B", "b"), ("B", "c")], {**lim, "match_messages": 2}, names)
    check(over == ["B sent 3 messages, 2 at most this turn"], f"a match over its message count is caught ({over})")
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
    check(len(table["moves"]) == 6 and full and span and table["limits"]["message_words"] > 0,
          "the situation table covers every move and result, and every interest level from 0 to 25 once")
    lines, stray = live.split_stray(live.parse_lines("A: hi\nSNAP B\nB: no", names))
    check(lines == [("A", "hi"), ("B", "no")] and stray == ["SNAP B"], "a line that is neither a message nor a note is caught")
    notes, chk, final = live.sections("NOTES:\nWhere they are: x\nCHECK:\n1. y\nFINAL:\nA: hi")
    check(notes.startswith("Where") and chk == "1. y" and final == "A: hi", "a director answer splits into notes, checklist and final")
    path = [(3, 10), (2, 13), (-1, 15), (5, 14), (-3, 19), (2, 16), (-1, 18), (-3, 17), (2, 14), (5, 16), (4, 21)]
    est = [live.estimate_left([{"interest_delta": d} for d, _ in path[:k]], path[k][1]) for k in range(len(path))]
    jump = max(abs(a - b) for a, b in zip(est, est[1:]))
    check(jump <= 5, f"along v8's meter the turns-left estimate moves at most 5 a turn ({est})")


def main():
    check_chat_rules()
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
