"""Live mode for the tone lab: the chat written one turn at a time, the way the game will
play it, instead of all at once after the game is over.

Why: in the real game nobody knows in advance which of the three offered moves the player
will pick or how the dice will land, so a story cannot be written ahead of time, turn by
turn. This mode tests whether a story still holds under that constraint:

1. One planning call at match start writes the story plan for the pair: cravings, lies,
   quirks, secrets, where their wounds collide, setups and both endings.
2. Each turn, one call writes the three options before the roll: what the player
   character means to send for each move on offer.
3. The engine's result for that turn is then revealed (which move the player picked, how
   the dice landed) and a second call writes what was actually sent and the match's reply.

The per-turn calls see the plan and the chat so far, never the character files: the plan
is the only channel from the files to the chat, which keeps file wording (a texting trait
asking for long, formal replies, say) from overriding the brief. The shape of each turn,
how many messages each side sends and how long each is, is drawn here from the beat and
the seed, and a turn that breaks it is asked for once more. Variety in rhythm has to come
from outside the model: each call reads every earlier turn and copies its shape.

The story underneath is the two of them getting to know each other. The delivery writer
ends each turn with notes (TOLD, HOOK, PLANTED, CALLBACK) that are stripped from the chat
and kept here: what each has told the other and which hooks are still hanging go into
every later prompt, a middle turn that hands over nothing real is asked for again, and the
setup has to come back once in the middle. Each character's habit marker is read from the
plan and rationed: one message a turn at most, and none after two turns in a row.

The engine still plays the whole session first (--mechanics-only, normally with
--agent random so the choices are as unpredictable as a person's), but the writer only
ever sees the turns up to the current one, so it knows no more than the live game would.
Which story beat a turn carries is decided here from the game so far, never from later
turns; selftest.py checks that.
"""

import json
import os
import random
import re

import tonelab as tl

FILES = ("bible.md", "plan.md", "plan_input.md", "options.md", "options_input.md",
         "deliver.md", "deliver_input.md", "character.md")
LONG_WORDS = 40  # the one message an accident may take
BAD_TIERS = {"catastrophe", "legendary"}  # miss tiers bad enough to shake a secret out
REAL_STATS = {"Honesty", "SelfAwareness"}  # moves that can carry a breakthrough
ENDINGS = {"DateSecured": "ending_win", "Unmatched": "ending_loss", "Ghosted": "ending_ghost"}
SPENT = {  # how a fired beat is described to the writer afterwards
    "breakthrough": "the breakthrough (something real got through)",
    "forced_breakthrough": "the breakthrough (something real got through)",
    "catastrophe": "the player character's secret fell out (the planned accident is spent)",
    "secret_miss": "the player character's secret slipped out (the planned accident is spent)",
    "discovery": "the match found out the player character's secret",
    "match_reveal": "the match's reveal (their secret is out)",
    "callback": "the setup came back in the middle (its callback is spent)",
    "player_snap": "the player character snapped (no second snap)",
    "match_snap": "the match snapped (no second snap)",
}


def read_files(variant_dir):
    files = {}
    for name in FILES:
        path = os.path.join(variant_dir, name)
        if not os.path.exists(path):
            tl.die(f"live variant {variant_dir} is missing {name}")
        with open(path, encoding="utf-8") as f:
            files[name] = f.read()
    return files


class Story:
    """Which beat each turn carries. It only ever looks at the current turn and what has
    already fired, so it decides exactly as it could in a live game. A snap needs a few
    turns of build-up first: nobody snaps at a stranger in their second message."""

    def __init__(self, knobs):
        self.beats, self.needs_text = knobs["beats"], knobs["needs"]
        self.fired, self.secret_turn, self.log = set(), None, []

    def so_far(self):
        done = "; ".join(f"turn {n}: {SPENT[k]}" for n, k in self.log) or "nothing named yet"
        ahead = []
        if "breakthrough" not in self.fired:
            ahead.append("the breakthrough")
        if "secret" not in self.fired:
            ahead.append("the player character's secret, which stays in until its moment, with the planned accident saved for it")
        if "match_reveal" not in self.fired:
            ahead.append("the match's reveal, which stays hidden until then, only cracks showing")
        return f"Already happened: {done}. Still to come: {', '.join(ahead) or 'the ending'}."

    def needs(self, i, interest):
        n = self.needs_text
        parts = [n["opener"] if i == 0 else n["early"] if i <= 2 else n["middle"]]
        if i > 0 and "breakthrough" not in self.fired:
            parts.append(n["breakthrough"])
        if i > 0 and "secret" not in self.fired:
            parts.append(n["secret"])
        if "secret" in self.fired:
            parts.append(n["reveal"])
        if interest >= 18:
            parts.append(n["close"])
        return " ".join(parts)

    def beat(self, i, t, is_last, outcome):
        r = t["roll"] or {}
        ok = bool(r.get("success"))
        tier = (r.get("final_tier") or r.get("tier") or "").lower()
        delta, before, state = t["interest_delta"], t["interest_before"], t["state_after"]
        shadow_miss = bool((t.get("shadow") or {}).get("miss"))
        bad = not ok and (tier in BAD_TIERS or bool(r.get("nat1")))
        f = self.fired
        if is_last:
            key = ENDINGS.get(outcome, "ending_open")
        elif i == 0:
            key = "opener"
        elif "secret" in f and "match_reveal" not in f and self.secret_turn == i - 1:
            key = "match_reveal"
        elif shadow_miss and "player_snap" not in f and i >= 3:
            key = "player_snap"
        elif bad and "secret" not in f and i >= 2:
            key = "catastrophe"
        elif ok and "breakthrough" not in f and i >= 2 and (t["chosen_stat"] in REAL_STATS or delta >= 3):
            key = "breakthrough"
        elif "match_snap" not in f and delta <= -2 and before >= 15 and i >= 3:
            key = "match_snap"
        elif "breakthrough" not in f and (state in ("VeryIntoIt", "AlmostThere") or i >= 6):
            key = "forced_breakthrough"
        elif "secret" not in f and (state == "AlmostThere" or i >= 8):
            key = "discovery" if ok else "secret_miss"
        elif i <= 2:
            key = "setup"
        elif "callback" not in f:
            key = "callback"
        else:
            key = "early" if before < 12 else "warming" if before < 18 else "close"
        fires = {"catastrophe": "secret", "secret_miss": "secret", "discovery": "secret",
                 "forced_breakthrough": "breakthrough"}.get(key, key)
        f.add(fires)
        if fires == "secret":
            self.secret_turn = i
        if key in SPENT:
            self.log.append((t["turn"], key))
        if key not in self.beats:
            tl.die(f"variant.json beats has no '{key}'")
        text = self.beats[key]
        if key.startswith("ending") and "secret" in f and "match_reveal" not in f:
            text += " " + self.beats["reveal_pending"]
        return key, text


EXCHANGE_BEATS = {"early", "warming", "close", "callback"}  # middle turns that must hand over something real
SNAPS = {"player_snap": "player", "match_snap": "datee"}  # the snapper's habit is the weapon, so it isn't rationed
NOTE = re.compile(r"^[\s*_>\-]*(TOLD|HOOK)\s*[:\-]\s*([^:]+?)\s*:\s*(.+?)\s*$", re.I)
FLAG = re.compile(r"^[\s*_>\-]*(PLANTED|CALLBACK)[\s*_.!]*$", re.I)


def draw_shape(knobs, key, seed, turn):
    spec = knobs["shapes"].get(key) or knobs["shapes"]["default"]
    rng = random.Random(f"{seed}-{turn}-shape")
    shape = {side: [rng.choice(spec["words"]) for _ in range(rng.randint(*spec[side]))]
             for side in ("player", "datee")}
    shape["long"], shape["long_words"] = spec.get("long"), spec.get("long_words", LONG_WORDS)
    return shape


def shape_text(shape, names):
    parts, lw = [], shape["long_words"]
    for side in ("player", "datee"):
        caps = shape[side]
        if not caps:
            parts.append(f"{names[side]} sends nothing: no reply ever comes.")
            continue
        each = "; ".join("one word" if c == 1 else f"up to {c} words" for c in caps)
        line = f"{names[side]} sends {len(caps)} message{'s' if len(caps) > 1 else ''} ({each})."
        if shape.get("long") == side:
            line += f" One of them may be the accident itself and run to {lw} words."
        parts.append(line)
    if shape.get("long") == "either":
        parts.append(f"One message in this turn, from whichever of them is telling something real, may run to {lw} words.")
    return " ".join(parts)


def shape_problems(lines, shape, names):
    problems, long_used = [], False
    for side in ("player", "datee"):
        msgs = [m for who, m in lines if who == names[side]]
        caps = shape[side]
        if len(msgs) != len(caps):
            problems.append(f"{names[side]} sent {len(msgs)} messages, the shape says {len(caps)}")
        for j, (m, cap) in enumerate(zip(msgs, caps)):
            n = len(m.split())
            if n <= cap + max(2, cap // 3):
                continue
            if shape.get("long") in (side, "either") and not long_used and n <= shape["long_words"]:
                long_used = True
                continue
            problems.append(f"{names[side]}'s message {j + 1} has {n} words, the shape says up to {cap}")
    return problems


def strip_notes(text, names):
    """Take the writer's notes for the game out of the chat: TOLD and HOOK lines
    (name: what), and PLANTED and CALLBACK flags."""
    kept, notes = [], {"planted": False, "callback": False, "told": [], "hooks": []}
    for ln in text.splitlines():
        m = FLAG.match(ln)
        if m:
            notes[m.group(1).lower()] = True
            continue
        m = NOTE.match(ln)
        if m:
            who = next((names[x] for x in ("player", "datee") if names[x].lower() in m.group(2).lower()),
                       m.group(2).strip())
            notes["told" if m.group(1).upper() == "TOLD" else "hooks"].append((who, m.group(3).strip().strip("*_ ").strip()))
            continue
        kept.append(ln)
    return "\n".join(kept).strip(), notes


def setup_note(planted, i):
    if planted:
        n, lines = planted
        said = "\n".join(f"{who}: {msg}" for who, msg in lines)
        return (f"The setup was planted on turn {n}:\n{said}\nIt comes back once in the middle, when the game says so, "
                "and pays off at the end. Leave it alone otherwise.")
    return "The setup from the plan hasn't been planted yet." + (" Plant it soon." if i else "")


def exchange_text(told, hooks):
    if told:
        out = "What they've told each other so far:\n" + "\n".join(f"- turn {n}, {who}: {fact}" for n, who, fact in told)
    else:
        out = "Neither of them has told the other anything real yet."
    if hooks:
        out += "\nHooks still hanging, that nobody has pulled on yet:\n" + "\n".join(
            f"- turn {n}, {who}: {h}" for n, who, h in hooks)
    return out


def habit_markers(plan, names):
    """Each character's habit marker, the literal text their habit always uses, read from
    the plan so the game can ration it."""
    found, current = {}, None
    for ln in plan.splitlines():
        bare = ln.strip().strip("#*_ ").strip()
        for side in ("player", "datee"):
            if re.match(re.escape(names[side]) + r"(?![\w:])(?!\s*:)", bare):
                current = names[side]
        m = re.match(r"(?i)^[\s*_\-]*habit marker[\s*_]*:\s*(.+)$", ln)
        if m and current and current not in found:
            marks = [x.strip().strip("\"'`*\u201c\u201d\u2018\u2019").strip() for x in m.group(1).split("|")]
            found[current] = [x for x in marks if x and x.lower().rstrip(".") not in ("none", "n/a", "-") and len(x) <= 30]
    return {k: v for k, v in found.items() if v}


def habit_used(msg, markers):
    low = msg.lower()
    return any(re.search(r"(?<![a-z0-9])" + re.escape(mk.lower()), low) for mk in markers)


def habit_banned(chat, habits):
    """A habit that showed in each of the last two turns stays out of this one."""
    if len(chat) < 2:
        return set()
    return {name for name, marks in habits.items()
            if all(any(w == name and habit_used(m, marks) for w, m in lines) for _, lines in chat[-2:])}


def habit_note(habits, banned):
    out = []
    for name, marks in habits.items():
        shown = " / ".join(f'"{m}"' for m in marks)
        out.append(f"{name}'s habit ({shown}) showed in each of the last two turns, so it stays out of this turn completely."
                   if name in banned else f"{name}'s habit ({shown}) can show in one message this turn at most, or not at all.")
    return "\n".join(out)


def habit_problems(lines, habits, banned, exempt):
    problems = []
    for name, marks in habits.items():
        if name == exempt:
            continue
        n = sum(1 for w, m in lines if w == name and habit_used(m, marks))
        if name in banned and n:
            problems.append(f"{name}'s habit showed although it had to stay out of this turn")
        elif n > 1:
            problems.append(f"{name}'s habit showed in {n} messages, one at most")
    return problems


def beat_keys(knobs, turns, outcome):
    story = Story(knobs)
    return [story.beat(i, t, i == len(turns) - 1, outcome)[0] for i, t in enumerate(turns)]


def _norm(s):
    return re.sub(r"[^a-z]", "", s.lower())


def parse_options(text, offered):
    blocks = re.split(r"(?im)^[\s*#]*OPTION[\s:\-]*([A-Za-z][A-Za-z \-]*?)[\s*]*$", text)
    found = {_norm(blocks[j]): blocks[j + 1].strip() for j in range(1, len(blocks), 2)}
    return {stat: found.get(_norm(stat)) for stat in offered}


def parse_lines(text, names):
    chat = tl.parse_chat("Turn 0\n" + text, names)
    return [line for k in sorted(chat) for line in chat[k]]


def history(chat):
    if not chat:
        return "(nothing yet: this is the first message)"
    return "\n\n".join(f"Turn {n}\n" + "\n".join(f"{who}: {msg}" for who, msg in lines) for n, lines in chat)


def tonight(c, values, rng, n_drawer):
    """Each match draws its own material, so one character does not tell the same story in
    every match: one backstory pair to build the secret around, and a few drawer lines."""
    cats = list((c.get("backstory_categories") or {}).values())
    pair = rng.choice(cats) if cats else None
    lines = [ln.strip().lstrip("-•*").strip() for ln in (c.get("psychological_stake") or "").splitlines() if ln.strip()]
    if n_drawer and len(lines) > n_drawer:
        lines = rng.sample(lines, n_drawer)
    return {**values,
            "focus": f"- says: {pair['bio_lie']}\n  truth: {pair['tragic_reality']}" if pair else "- (no backstory pairs)",
            "drawer": "\n".join(f"- {ln}" for ln in lines)}


def play(variant_dir, knobs, mech, player, datee, caller):
    """Write the whole chat turn by turn. caller(system, user, label, effort, ctx) -> text."""
    files = read_files(variant_dir)
    names = {"player": mech["player"]["display_name"], "datee": mech["datee"]["display_name"]}
    rng = random.Random(f"{mech.get('seed')}-{player}-{datee}")
    pc, _, pv = tl.character_values(player, mech["player"], knobs)
    dc, datee_diag, dv = tl.character_values(datee, mech["datee"], knobs)
    n_drawer = int(knobs.get("drawer_sample", 0))
    pv, dv = tonight(pc, pv, rng, n_drawer), tonight(dc, dv, rng, n_drawer)
    common = {
        "player_name": names["player"], "datee_name": names["datee"],
        "time_of_day": mech.get("time_of_day", ""),
        "player_card": tl.render(files["character.md"], {**pv, "role": "the player"}, "character.md").strip(),
        "datee_card": tl.render(files["character.md"], {**dv, "role": "the match"}, "character.md").strip(),
    }
    bible = tl.render(files["bible.md"], common, "bible.md").strip()
    systems = {k: bible + "\n\n" + tl.render(files[k + ".md"], common, k + ".md").strip()
               for k in ("plan", "options", "deliver")}
    plan_user = tl.render(files["plan_input.md"], common, "plan_input.md").strip() + "\n"
    plan = caller(systems["plan"], plan_user, "plan", knobs.get("plan_reasoning"), {})

    story, cons, turns, chat, out = Story(knobs), tl.load_consequences(), mech["turns"], [], []
    planted, callback_turn, told, hooks = None, None, [], []
    habits = habit_markers(plan, names)
    for i, t in enumerate(turns):
        is_last = i == len(turns) - 1
        tv = tl.turn_values(t, cons, names, datee_diag, is_last, mech.get("outcome"))
        needs = story.needs(i, t["interest_before"])
        banned = habit_banned(chat, habits)
        h_note = habit_note(habits, banned)
        base = {**common, "plan": plan, "history": history(chat), "n": t["turn"], "story_so_far": story.so_far(),
                "setup_note": setup_note(planted, i), "exchange": exchange_text(told, hooks), "habit_note": h_note}
        o_user = tl.render(files["options_input.md"],
                           {**base, "interest": t["interest_before"], "state": t["state_before"],
                            "options": ", ".join(t["options"]), "needs": needs}, "options_input.md").strip() + "\n"
        o_text = caller(systems["options"], o_user, f"turn {t['turn']} options", knobs.get("turn_reasoning"),
                        {"offered": t["options"]})
        intended = parse_options(o_text, t["options"]).get(t["chosen_stat"])
        if not intended:
            tl.die(f"turn {t['turn']}: the options output has no block for the picked move {t['chosen_stat']}:\n{o_text[:1500]}")
        key, beat = story.beat(i, t, is_last, mech.get("outcome"))
        ghost = key == "ending_ghost"
        shape = draw_shape(knobs, key, mech.get("seed"), t["turn"])
        d_user = tl.render(files["deliver_input.md"],
                           {**base, **tv, "intended": intended, "beat": beat, "shape": shape_text(shape, names)},
                           "deliver_input.md").strip() + "\n"
        ctx = {"ghost": ghost, "shape": shape, "beat": key}
        exempt = names[SNAPS[key]] if key in SNAPS else None

        def attempt(prompt, label):
            raw = caller(systems["deliver"], prompt, label, knobs.get("turn_reasoning"), ctx)
            text, notes = strip_notes(raw, names)
            lines = parse_lines(text, names)
            problems = shape_problems(lines, shape, names) + habit_problems(lines, habits, banned, exempt)
            if key in EXCHANGE_BEATS and not notes["told"]:
                problems.append("nothing real from either true story was told this turn (no TOLD note)")
            if key == "callback" and not notes["callback"]:
                problems.append("the setup didn't come back (no CALLBACK note)")
            return raw, notes, lines, problems

        d_text, notes, lines, problems = attempt(d_user, f"turn {t['turn']} delivery")
        retried = bool(problems)
        if problems:
            again = (d_user + "\nYour answer was:\n\n" + d_text + "\n\nIt broke this turn's rules: " +
                     "; ".join(problems) + f". Write turn {t['turn']} again so it keeps all of them.\n")
            d_text, notes, lines, problems = attempt(again, f"turn {t['turn']} delivery retry")
        if notes["planted"] and planted is None:
            planted = (t["turn"], lines)
        if notes["callback"] and callback_turn is None:
            callback_turn = t["turn"]
        for who_told, fact in notes["told"]:
            told.append((t["turn"], who_told, fact))
            j = next((k for k, h in enumerate(hooks) if h[1] == who_told), None)
            if j is not None:
                hooks.pop(j)
        hooks.extend((t["turn"], w, h) for w, h in notes["hooks"])
        who = {w for w, _ in lines}
        if names["player"] not in who or (names["datee"] not in who and shape["datee"]):
            tl.die(f"turn {t['turn']}: the delivery output is missing a speaker:\n{d_text[:1500]}")
        chat.append((t["turn"], lines))
        brief = (f"Before the roll. What the story needs next: {needs}\n\n"
                 f"The three options written:\n{o_text}\n\n"
                 f"Picked {t['chosen_stat']}. {tv['result']}\n{tv['checks']}\n"
                 f"Interest {t['interest_before']} → {t['interest_after']} ({t['state_after']}).\n\n"
                 f"This turn's beat ({key}): {beat}\n\nShape: {shape_text(shape, names)}"
                 + (f"\n\nHabits: {h_note}" if h_note else "")
                 + (f"\nRules still broken after a retry: {'; '.join(problems)}" if problems else
                    "\n(kept the rules after a retry)" if retried else "")
                 + "".join(f"\nTold, {w}: {f}" for w, f in notes["told"])
                 + "".join(f"\nHook dropped, {w}: {h}" for w, h in notes["hooks"])
                 + ("\nThe setup was planted on this turn." if planted and planted[0] == t["turn"] else "")
                 + ("\nThe setup came back on this turn." if notes["callback"] else ""))
        out.append({"turn": t["turn"], "beat": key, "needs": needs, "options_user": o_user, "options": o_text,
                    "deliver_user": d_user, "delivered": d_text, "brief": brief, "shape": shape,
                    "retried": retried, "shape_problems": problems, "notes": notes, "habit_note": h_note})
    output = "\n\n".join(f"Turn {n}\n" + "\n".join(f"{w}: {m}" for w, m in lines) for n, lines in chat) + "\n"
    return {"names": names, "common": common, "systems": systems, "plan_user": plan_user, "plan": plan,
            "turns": out, "output": output, "planted_turn": planted[0] if planted else None,
            "callback_turn": callback_turn, "told": told, "hooks_open": hooks, "habits": habits}


def dry_caller(names):
    def caller(system, user, label, effort, ctx):
        if label == "plan":
            return "(dry run: no plan)"
        if label.endswith("options"):
            return "\n\n".join(f"OPTION {s}\n{names['player']}: (dry run, {s})" for s in ctx["offered"])
        shape = ctx["shape"]
        lines = [f"{names['player']}: dry"] * len(shape["player"]) + [f"{names['datee']}: dry"] * len(shape["datee"])
        if ctx.get("beat") in EXCHANGE_BEATS:
            lines.append(f"TOLD: {names['player']}: dry")
        if ctx.get("beat") == "callback":
            lines.append("CALLBACK")
        return "\n".join(lines)
    return caller


def run_live(args, knobs, variant_dir, mech, run_dir, run_id, stamp, provider, model, key, turn_cap):
    names = {"player": mech["player"]["display_name"], "datee": mech["datee"]["display_name"]}
    calls, models = [], []
    log_path = os.path.join(run_dir, "live_calls.json")

    def save():
        with open(log_path, "w", encoding="utf-8") as f:
            json.dump(calls, f, indent=2, ensure_ascii=False)

    def caller(system, user, label, effort, ctx):
        if args.dry_run:
            text = dry_caller(names)(system, user, label, effort, ctx)
            calls.append({"label": label, "system_sha256": tl.sha256(system), "user": user, "output": text})
            save()
            return text
        if provider == "openrouter":
            k = {x: knobs[x] for x in ("temperature", "top_p", "max_tokens") if x in knobs}
            if effort:
                k["reasoning"] = {"effort": effort}
            data, seconds = tl.call_openrouter(system, user, model, key, k)
            text = ((data.get("choices") or [{}])[0].get("message") or {}).get("content") or ""
            usage, served = data.get("usage") or {}, data.get("model")
        else:
            data, seconds = tl.call_claude(system, user, model)
            text = data.get("result") or ""
            usage = {"cost": data.get("total_cost_usd")}
            served = ",".join(sorted((data.get("modelUsage") or {}).keys()))
        models.append(served)
        calls.append({"label": label, "system_sha256": tl.sha256(system), "user": user, "output": text,
                      "model": served, "seconds": seconds, "usage": usage})
        save()
        if not text.strip():
            tl.die(f"{label}: the model returned nothing")
        return text.strip()

    result = play(variant_dir, knobs, mech, args.player, args.datee, caller)
    systems = result["systems"]
    with open(os.path.join(run_dir, "prompt.md"), "w", encoding="utf-8") as f:
        f.write("".join(f"<!-- system prompt: {k} -->\n{v}\n\n" for k, v in systems.items()) +
                "<!-- plan user prompt -->\n" + result["plan_user"] +
                "\n<!-- every per-turn user prompt is in live_calls.json -->\n")
    parts = {"system": systems["plan"], "user": result["plan_user"], "plan": result["plan"],
             "options_system": systems["options"], "deliver_system": systems["deliver"],
             "player_card": result["common"]["player_card"], "datee_card": result["common"]["datee_card"],
             "turn_briefs": [t["brief"] for t in result["turns"]], "beats": [t["beat"] for t in result["turns"]]}
    with open(os.path.join(run_dir, "prompt_parts.json"), "w", encoding="utf-8") as f:
        json.dump(parts, f, indent=2, ensure_ascii=False)
    sent = "".join(systems.values()) + "".join(c["user"] for c in calls)
    totals = {"cost": 0.0, "prompt_tokens": 0, "completion_tokens": 0, "reasoning_tokens": 0}
    for c in calls:
        u = c.get("usage") or {}
        totals["cost"] += u.get("cost") or 0
        totals["prompt_tokens"] += u.get("prompt_tokens") or 0
        totals["completion_tokens"] += u.get("completion_tokens") or 0
        totals["reasoning_tokens"] += (u.get("completion_tokens_details") or {}).get("reasoning_tokens") or 0
    files = read_files(variant_dir)
    record = {
        "run_id": run_id, "created_utc": stamp, "variant": args.variant, "mode": "live",
        "variant_sha256": {name: tl.sha256(text) for name, text in files.items()},
        "knobs": knobs,
        "prompt_sha256": tl.sha256(sent), "prompt_chars": len(sent), "model_calls": len(calls),
        "repo_commit": tl.git("rev-parse", "--short", "HEAD"),
        "repo_dirty": bool(tl.git("status", "--porcelain", "--", "data", "session-runner", "src", "tools/tonelab")),
        "player": args.player, "datee": args.datee, "seed": args.seed, "clock": args.clock,
        "agent": knobs.get("agent") or "score",
        "turn_cap": turn_cap, "turns_played": mech["turns_played"],
        "final_interest": mech["final_interest"], "outcome": mech.get("outcome") or "NoTerminalOutcome",
        "provider": None if args.dry_run else provider,
        "model_requested": None if args.dry_run else model,
        "models_reported": sorted(set(m for m in models if m)),
        "seconds": round(sum(c.get("seconds") or 0 for c in calls), 1),
        "usage": None if args.dry_run else totals,
        "beats": parts["beats"],
        "plan_words": len(result["plan"].split()),
        "planted_turn": result["planted_turn"],
        "shape_retries": sum(t["retried"] for t in result["turns"]),
        "turns_off_shape": [t["turn"] for t in result["turns"] if t["shape_problems"]],
        "callback_turn": result["callback_turn"],
        "told": [list(x) for x in result["told"]],
        "hooks_open": [list(x) for x in result["hooks_open"]],
        "habits": result["habits"],
        "format_checks": tl.format_checks(result["output"], names, mech["turns_played"]),
    }
    with open(os.path.join(run_dir, "output.md"), "w", encoding="utf-8") as f:
        f.write(result["output"])
    with open(os.path.join(run_dir, "run.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=2, ensure_ascii=False)
    tl.write_report(run_dir)
    print(os.path.relpath(run_dir, tl.REPO))
