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

v5: the brief says what Pinder is and what the reader should feel; what each turn has to do
comes from here, per turn. The game keeps a story clock (turns left, estimated from the
meter), tracks each character's origin in three pieces, the collision of their habits and
three setups, and tells each turn what is due. Each roll gets a meaning (a natural 20 has to
cash something in). Turn length comes from a word budget for the whole chat (an average,
not a cap). After the chat a reader who sees only the chat answers what they should know
by the end, and a second call grades that against the plan.

v6: a director. The per-turn writers used to get the whole plan, the game's counters and a
fixed paragraph per kind of turn, and they obeyed the countable parts while missing what had
just happened in the chat (a hook dropped in the last message went unanswered while the turn
dutifully steered toward a date). Now a director is called twice a turn, before the options
and after the roll. It sees everything: the plan, the chat, the game's state, the template
for this kind of turn, the roll and what else fired. It reads the last exchange first and
writes a short direction: what just happened that matters, what this turn does with it, what
has to come out and how. The writers see only who the two characters are (cut from the plan:
what they show, fear, habit, lie, voice), the chat and the direction, never the origins,
setups or endings still to come. A dropped hook stays open until the other one picks it up.

v7: the director is one persistent session for the whole chat instead of a fresh call each
time. The plan goes in once, at the start; after that each message adds only what is new
(the turn the writer delivered, the game's state, the roll), and the director keeps every
earlier message and its own replies. Before each roll it has to write notes looking back
over the whole chat (where they are, what each knows, what's hanging, what's working,
what's stale, whether its last direction landed); notes missing a heading are asked for
again. The report shows every prompt exactly as sent, labelled by who received it, and the
game's own per-turn log separately, labelled as not a prompt.

v8: the director edits meaning, not material. Its directions had become inventories ("tips
his top hat ... (origin piece 1) ... (Setup 1) ... (+3 interest)") that crammed several plan
items into one short message, and the writers delivered sentences nobody would send. Now
every message is one thing one person means; before the roll the director says what the
turn should do to the reader and what each option means, after the roll it first says
what the meant message actually says and whether a person would send it. Game labels go
only in a last "For the notes" line; a direction that uses them elsewhere, or runs long,
is asked for again. The cast sheet now recognises a long-form heading ("Origin, in three
pieces, ...") and the run stops if any origin or accident line reaches the writers.

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
         "deliver.md", "deliver_input.md", "character.md", "direct.md", "direct_options_input.md",
         "direct_turn_input.md")
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
BIG_BEATS = {"breakthrough", "forced_breakthrough", "catastrophe", "secret_miss", "discovery", "match_reveal",
             "player_snap", "match_snap"}
STRONG = {"VeryIntoIt", "AlmostThere", "DateSecured"}
NOTE = re.compile(r"^[\s*_>\-]*(TOLD|HOOK)\s*[:\-]\s*([^:]+?)\s*:\s*(.+?)\s*$", re.I)
SETUP = re.compile(r"^[\s*_>\-]*(PLANTED|CALLBACK|PAID)\s*(?:[:\-]\s*(.*?))?[\s*_]*$", re.I)
FLAG = re.compile(r"^[\s*_>\-]*COLLIDED[\s*_.!]*$", re.I)
STAT_HIT = {
    "Charm": "warm and easy, and it puts them at ease",
    "Rizz": "plainly wanting them, and it works",
    "Honesty": "something real, and it gets through",
    "Chaos": "a swerve that somehow makes perfect sense",
    "Wit": "quick and funny, and it gets a real laugh",
    "SelfAwareness": "naming what's actually going on, and it's right",
}


def estimate_left(past, interest):
    """Turns left, estimated from the meter: the distance to 25 (or to 0) over the recent
    pace. The game ends at either, and nobody knows which in advance."""
    deltas = [t["interest_delta"] for t in past[-3:]]
    pace = sum(deltas) / len(deltas) if deltas else 2.0
    if pace > 0.3:
        left = (25 - interest) / pace
    elif pace < -0.3:
        left = interest / -pace
    else:
        left = 6
    return max(1, min(12, round(left)))


def dice_meaning(t):
    r, stat = t["roll"] or {}, t["chosen_stat"]
    hit = STAT_HIT.get(stat, "the way they meant it")
    margin = (r.get("total") or 0) - (r.get("dc") or 0)
    if r.get("nat20"):
        return (f"A natural 20. This is the best message in the chat: {hit}. It cashes something in, a setup or "
                "something the other one told them, used in a way that shows they understood, and the other one's "
                "guard visibly drops.")
    if r.get("nat1"):
        return ("A natural 1. The worst thing they could have sent, and it lands exactly where it hurts the other "
                "one most: on their fear, or on something they told them.")
    if r.get("success"):
        if margin >= 6:
            return f"A big hit: {hit}, better than they meant it."
        if margin <= 1:
            return f"It only just lands: {hit}, barely, and it nearly went wrong."
        return f"A clean hit: {hit}."
    return "It came out wrong, in exactly the way the miss describes, and that shows in the message itself."


def turn_target(knobs, key, seed, turn, spent, left, past, state, nat20):
    """This turn's share of the word budget. The budget is an average for the whole chat,
    not a cap: big moments draw more, routine turns less, and the seed varies it so long and
    short turns sit next to each other. Once it's spent, a chat that's dying gets terse and
    one that's working keeps its pace."""
    budget = knobs.get("word_budget", 1250)
    rng = random.Random(f"{seed}-{turn}-budget")
    remaining, ending = budget - spent, key.startswith("ending")
    if remaining <= 0:
        last = past[-1]["interest_delta"] if past else 0
        if last < 0 and not ending:
            return rng.randint(4, 14)
        if state in STRONG or ending:
            return max(30, round(spent / max(1, len(past))))
        return rng.randint(15, 35)
    weight = 1.6 if ending else 1.5 if key in BIG_BEATS else 0.7 if key in ("opener", "setup") else 1.0
    if nat20:
        weight *= 1.4
    target = remaining / (max(1, left) + (0 if ending else 2)) * weight * rng.uniform(0.5, 1.5)
    if not ending:
        target = min(target, max(10, remaining - budget * 0.12))  # the ending's share stays reserved
    return int(max(4, min(130, target)))


def draw_shape(knobs, key, seed, turn, target):
    spec = knobs["shapes"].get(key) or knobs["shapes"]["default"]
    rng = random.Random(f"{seed}-{turn}-shape")
    counts = {side: rng.randint(*spec[side]) for side in ("player", "datee")}
    want = min(6, max(1, round(target / 20)))
    while counts["player"] + counts["datee"] < want:
        side = "datee" if counts["datee"] and counts["datee"] <= counts["player"] + 1 else "player"
        counts[side] += 1
    weights = {side: [rng.choice((0.3, 0.6, 1, 1, 1.5, 2.5)) for _ in range(counts[side])] for side in counts}
    total = sum(sum(w) for w in weights.values()) or 1
    shape = {side: [max(1, min(45, round(target * w / total))) for w in weights[side]] for side in counts}
    shape["long"], shape["long_words"] = spec.get("long"), spec.get("long_words", LONG_WORDS)
    shape["target"] = target
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
    parts.append(f"About {shape['target']} words in all this turn.")
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
    """Take the writer's notes for the game out of the chat."""
    kept = []
    notes = {"told": [], "hooks": [], "planted": [], "callback": [], "paid": [], "collided": False}
    for ln in text.splitlines():
        if FLAG.match(ln):
            notes["collided"] = True
            continue
        m = SETUP.match(ln)
        if m:
            notes[m.group(1).lower()].append((m.group(2) or "").strip().strip("*_ ").strip())
            continue
        m = NOTE.match(ln)
        if m:
            who = next((names[x] for x in ("player", "datee") if names[x].lower() in m.group(2).lower()),
                       m.group(2).strip())
            notes["told" if m.group(1).upper() == "TOLD" else "hooks"].append((who, m.group(3).strip().strip("*_ ").strip()))
            continue
        kept.append(ln)
    return "\n".join(kept).strip(), notes


def piece_of(what):
    m = re.match(r"\s*(?:piece\s*)?([123])\s*[:.)\-]\s*(.*)$", what, re.I)
    return (int(m.group(1)), m.group(2).strip() or what) if m else (None, what)


def setup_no(x):
    m = re.search(r"[123]", x or "")
    return int(m.group()) if m else None


def story_step(i, key, is_last, left, names, pieces, setups, collided, nat20):
    """Where the story stands and what this turn has to move: the game's story clock, built
    only from what has actually happened so far. Returns the text and the notes it requires.
    key is None before the roll, when nothing is required yet."""
    key = key or ""
    turn, lines, need = i + 1, [], []
    lines.append("This is the last turn." if is_last else
                 f"Going by the meter, the chat probably has about {left} turn{'s' if left != 1 else ''} left.")
    for side in ("player", "datee"):
        name = names[side]
        got = sorted(pieces[name])
        nxt = next((k for k in (1, 2, 3) if k not in got), None)
        known = (f"The reader knows piece{'s' if len(got) > 1 else ''} {', '.join(map(str, got))} of {name}'s origin"
                 if got else f"The reader knows nothing of {name}'s origin yet")
        if nxt is None:
            lines.append(f"- {known}: all of it.")
        elif nxt == 3:
            lines.append(f"- {known}; piece 3, the part they'd never say, is saved for the end.")
        else:
            due = bool(key) and key != "opener" and not key.startswith("ending") and left <= (3 - nxt) + 1
            lines.append(f"- {known}; next is piece {nxt}" + (", and it has to come out this turn." if due else "."))
            if due:
                need.append(f"TOLD:{name}")
    if key in EXCHANGE_BEATS and not need and any(len(pieces[names[s]] & {1, 2}) < 2 for s in ("player", "datee")):
        need.append("TOLD:any")
    if collided:
        lines.append(f"- Their habits collided on turn {collided}.")
    elif key and key not in ("opener", "setup") and (
            (i >= 3 and all(pieces[names[s]] for s in ("player", "datee"))) or left <= 3 or key.startswith("ending")):
        lines.append("- Their habits haven't collided yet, and it's time: one's habit lands on the other one's fear, "
                     "the way the plan's Collision says.")
        need.append("COLLIDED")
    else:
        lines.append("- Their habits haven't collided yet.")
    setup_need = None
    if key.startswith("ending") and any(s.get("planted") and not s.get("paid") for s in setups.values()):
        setup_need = "PAID"
    elif nat20:
        setup_need = "PAID"
    for n in (1, 2, 3):
        st = setups.get(n, {})
        if st.get("paid"):
            lines.append(f"- Setup {n}: paid off on turn {st['paid']}.")
        elif st.get("called"):
            lines.append(f"- Setup {n}: planted on turn {st['planted']}, came back on turn {st['called']}, "
                         "waiting for its payoff" + (", which is now." if key.startswith("ending") else "."))
        elif st.get("planted"):
            due = (turn - st["planted"] >= 3 and setup_need is None and not key.startswith("ending")
                   and (key in EXCHANGE_BEATS or key == "callback"))
            lines.append(f"- Setup {n}: planted on turn {st['planted']}, not back yet"
                         + ("; it comes back this turn, meaning more by now." if due else "."))
            if due:
                setup_need = "CALLBACK"
        else:
            due = (i >= 2 and setup_need is None and key and key not in BIG_BEATS and not key.startswith("ending"))
            lines.append(f"- Setup {n}: not planted yet" + ("; plant it this turn, as a throwaway." if due else "."))
            if due:
                setup_need = "PLANTED"
    if key == "callback" and setup_need is None:
        setup_need = "CALLBACK" if any(s.get("planted") and not s.get("called") for s in setups.values()) else "PLANTED"
    if setup_need:
        need.append(setup_need)
    if nat20:
        lines.append("- A natural 20 this turn: something gets cashed in.")
    if key == "ending_ghost":
        need = [x for x in need if x == "PAID"]
    return "\n".join(lines), need


def notes_problems(notes, need, names, is_end):
    problems, told_by = [], [w for w, _ in notes["told"]]
    for n in need:
        if n == "TOLD:any" and not notes["told"]:
            problems.append("nothing new from either origin came out (no TOLD note)")
        elif n.startswith("TOLD:") and n != "TOLD:any" and n[5:] not in told_by:
            problems.append(f"the next piece of {n[5:]}'s origin didn't come out (no TOLD note for them)")
        elif n == "COLLIDED" and not notes["collided"]:
            problems.append("their habits didn't collide (no COLLIDED note)")
        elif n in ("PLANTED", "CALLBACK", "PAID") and not notes[n.lower()]:
            problems.append({"PLANTED": "the setup due to be planted wasn't (no PLANTED note)",
                             "CALLBACK": "the setup due to come back didn't (no CALLBACK note)",
                             "PAID": "nothing was cashed in (no PAID note)"}[n])
    if not is_end:
        for name in set(names.values()):
            ks = {piece_of(w)[0] for who, w in notes["told"] if who == name}
            if len(ks - {None}) > 1:
                problems.append(f"{name} gave away more than one piece of their origin in one turn")
    return problems


def exchange_text(told, hooks):
    if told:
        out = "What they've told each other so far:\n" + "\n".join(
            f"- turn {n}, {who}" + (f" (piece {k})" if k else "") + f": {fact}" for n, who, k, fact in told)
    else:
        out = "Neither of them has told the other anything real yet."
    if hooks:
        out += "\nHooks dropped in the last two turns (check the chat for whether the other one took them up):\n" + "\n".join(
            f"- turn {n}, {who}: {h}" for n, who, h in hooks)
    return out


CAST_KEEP = {"shows", "fear", "habit", "habit marker", "lie", "voice"}
SHARED = {"collision", "setups", "if they click", "if it falls apart", "heat"}
FIELDS = CAST_KEEP | SHARED | {"origin", "accident", "snap"}


def cast_sheet(plan, names):
    """Who the two characters are, cut from the plan for the writers: what they show, their
    fear, habit, lie and voice. Origins, accidents, setups, the collision, the heat (which
    names what has to happen first) and both endings stay with the director, so the writers react to the chat
    instead of steering toward lines and props they've already read."""
    parts = {names["player"]: [], names["datee"]: []}
    heat, hidden, current, field = [], [], None, None
    for ln in plan.splitlines():
        bare = ln.strip().strip("#*_ ").strip()
        if not bare:
            continue
        who = next((names[s] for s in ("player", "datee")
                    if re.match(re.escape(names[s]) + r"(?![\w:])(?!\s*:)", bare) and len(bare) <= len(names[s]) + 24), None)
        if who:
            current, field = who, None
            continue
        # a heading may run on before its colon ("Origin, in three pieces, from the surface ...:")
        m = re.match(r"^([A-Za-z][A-Za-z ]{1,24}?)(?:\s*[,(][^:]{0,200})?[\s*_]*:[\s*_]*(.*)$", bare)
        if m and m.group(1).strip().lower() in FIELDS:
            field = m.group(1).strip().lower()
            if field in SHARED:
                current = None
            line, rest = f"{m.group(1).strip()}: {m.group(2).strip()}".rstrip(), m.group(2).strip()
        else:
            line = rest = bare
        if current and field in CAST_KEEP:
            parts[current].append(line)
        elif current is None and field == "heat":
            heat.append(line)
        elif current and field in ("origin", "accident") and rest:
            hidden.append(rest)
    for name, got in parts.items():
        low = [x.lower() for x in got]
        if not any(x.startswith("fear:") for x in low) or not any(x.startswith("habit:") for x in low):
            tl.die(f"the plan has no Fear and Habit for {name}, so the writers' cast sheet can't be cut from it:\n{plan[:2000]}")
    sheet = "\n\n".join(n + "\n" + "\n".join(g) for n, g in parts.items())
    low = sheet.lower()
    leaked = [h for h in hidden if len(h) > 12 and h.lower() in low]
    if leaked or re.search(r"(?im)^\s*(origin|accident)\b", sheet):
        tl.die("the writers' cast sheet would carry an origin or accident from the plan:\n" + "\n".join(leaked[:3])
               + "\n\n" + sheet[:2000])
    return sheet


DIRECTION = re.compile(r"(?ims)^[\s*_#>\-]*DIRECTION[\s*_]*:[\s*_]*(.+)")
NOTE_HEADS = ("Where they are", "What each knows", "Not asked yet", "Working", "Stale", "Last direction")


def direction_of(text):
    m = DIRECTION.search(text)
    return (m.group(1) if m else text).strip()


def notes_of(text):
    m = DIRECTION.search(text)
    head = text[:m.start()] if m else text
    return re.sub(r"(?is)^.*?NOTES[\s*_]*:", "", head, count=1).strip()


LABELS = re.compile(r"(?i)\b(?:setup|piece)\s*#?\d|\binterest\b|\(\s*[+-]\d|\b(?:TOLD|PLANTED|CALLBACK|PAID|COLLIDED)\b")


def direction_problems(direction):
    """A direction is plain sentences about meaning; the game's labels go only in its notes line."""
    body = re.split(r"(?i)For the notes\s*:", direction)[0]
    out, n = [], len(body.split())
    if n > 140:
        out.append(f"the direction runs {n} words before the notes line, more than 120")
    hits = sorted({m.group(0) for m in LABELS.finditer(body)}, key=str.lower)
    if hits:
        out.append("the direction uses the game's labels outside the notes line (" + ", ".join(hits) + ")")
    return out


def notes_missing(text):
    n = notes_of(text).lower()
    return [h for h in NOTE_HEADS if h.lower() not in n] + ([] if DIRECTION.search(text) else ["the direction"])


class Director:
    """One session for the whole chat: every message and every reply stays in it, so the
    director remembers what it noted and asked for, and sees whether it worked."""

    def __init__(self, caller, system, effort):
        self.caller, self.system, self.effort, self.messages = caller, system, effort, []

    def ask(self, user, label):
        out = self.caller(self.system, user, label, self.effort, {"history": list(self.messages)})
        self.messages += [{"role": "user", "content": user}, {"role": "assistant", "content": out}]
        return out


CALL_TITLES = {
    "direction before the roll": ("Director, before the roll", True),
    "direction before the roll retry": ("Director, before the roll, asked again", True),
    "options": ("Options writer", False),
    "direction": ("Director, after the roll", True),
    "direction retry": ("Director, after the roll, asked again", True),
    "delivery": ("Turn writer", False),
    "delivery retry": ("Turn writer, second attempt", False),
}


def turn_calls(calls, n_turns):
    """Every prompt sent in each turn, in order and exactly as sent, for the report."""
    out = [[] for _ in range(n_turns)]
    for c in calls:
        m = re.match(r"^turn (\d+) (.+)$", c.get("label", ""))
        if not m or m.group(2) not in CALL_TITLES or not 1 <= int(m.group(1)) <= n_turns:
            continue
        title, session = CALL_TITLES[m.group(2)]
        if session:
            title += f": message {(c.get('session_messages') or 0) // 2 + 1} sent into the director's session"
            note = ("The director's session keeps the plan, every earlier message and every reply it wrote; this is "
                    "what was added this time. Its system prompt is under Prompts sent once.")
        else:
            title += ": prompt as sent"
            note = "A fresh call: this is everything it got, besides its system prompt under Prompts sent once."
        out[int(m.group(1)) - 1].append({"title": title, "note": note, "user": c["user"], "output": c["output"]})
    return out


def must_text(need, names):
    """What the game requires this turn, in plain words; the director says which piece or setup."""
    say = {"TOLD:any": "something new from one of their origins comes out, the piece the director names (note: TOLD)",
           "COLLIDED": "one's habit lands on the other one's fear (note: COLLIDED)",
           "PLANTED": "a setup is planted as a throwaway, the one the director names (note: PLANTED and its number)",
           "CALLBACK": "a setup comes back, meaning more by now, the one the director names (note: CALLBACK and its number)",
           "PAID": "something pays off, the one the director names (note: PAID and its number)"}
    out = [say.get(n) or f"the next piece of {n[5:]}'s origin comes out, the one the director names (note: TOLD)"
           for n in need]
    return "\n".join(f"- {x}" for x in out) if out else "- nothing beyond what the director says"


def roll_line(t):
    r = t["roll"] or {}
    if r.get("success") or r.get("nat1"):
        return dice_meaning(t)
    return "It misses: what they meant comes out wrong, the way the director describes."


def mood_text(t, names, is_last, outcome):
    d, who = t["interest_delta"], names["datee"]
    feel = ("warms up a lot" if d >= 3 else "warms up" if d >= 1 else "holds steady" if d == 0
            else "cools a little" if d >= -2 else "cools sharply")
    end = {"DateSecured": ", and agrees to meet", "Unmatched": ", and unmatches", "Ghosted": ", and never replies"}
    if is_last and outcome in end:
        return f"{who} {feel}{end[outcome]}."
    if t["interest_after"] >= 21:
        return f"{who} {feel}, but isn't ready to agree to meet yet."
    return f"{who} {feel}."


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
            marks = [x.strip().strip("\"'`*“”‘’").strip() for x in m.group(1).split("|")]
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


READER_SYSTEM = ("You have just read a whole chat from Pinder, a game that looks like a dating app, where everyone is a "
                 "sentient dick. Answer only from what is in the chat. If the chat doesn't show it, say \"can't tell\". "
                 "One or two short sentences per answer.")
GRADER_SYSTEM = "You check whether a finished piece of writing delivered what its plan intended. Be strict and brief."


def reader_check(caller, plan, output, names):
    """A reader who sees only the finished chat answers what the reader should know by the
    end; a second call grades the answers against the plan. The writer's own notes prove only
    that it says it delivered; this checks what reached the page."""
    qs = []
    for n in (names["player"], names["datee"]):
        qs += [f"What is {n} afraid of?", f"What does {n} keep doing so they don't have to feel it?",
               f"Where does that come from, for {n}?", f"At the end, did {n} drop that habit or hold on to it?"]
    qs += ["What happened when the two of them ran into each other's habits?",
           "Which line would you remember tomorrow, and why?", "Where, if anywhere, did you laugh?"]
    user = "The chat:\n\n" + output + "\nAnswer these, numbered:\n" + "\n".join(f"{k + 1}. {q}" for k, q in enumerate(qs))
    answers = caller(READER_SYSTEM, user, "reader check", "low", {})
    g_user = ("A writers' room planned a chat, and a reader who saw only the finished chat answered questions about it. "
              "For each of the first nine answers, say whether the reader got what the plan intended: yes, partly or "
              "no, with a few words why. Then one last line: SCORE: the number of yes answers plus half a point for "
              "each partly, out of 9.\n\n"
              f"The plan:\n\n{plan}\n\nThe questions and the reader's answers:\n\n{answers}\n")
    grade = caller(GRADER_SYSTEM, g_user, "reader grade", "low", {})
    m = re.search(r"SCORE:\s*\**\s*([\d.]+)", grade)
    return {"questions": qs, "answers": answers, "grade": grade, "score": float(m.group(1)) if m else None}


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
               for k in ("plan", "options", "deliver", "direct")}
    plan_user = tl.render(files["plan_input.md"], common, "plan_input.md").strip() + "\n"
    plan = caller(systems["plan"], plan_user, "plan", knobs.get("plan_reasoning"), {})
    cast = cast_sheet(plan, names)
    director = Director(caller, systems["direct"], knobs.get("turn_reasoning"))

    story, cons, turns, chat, out = Story(knobs), tl.load_consequences(), mech["turns"], [], []
    told, hooks, setups = [], [], {}
    pieces = {names["player"]: set(), names["datee"]: set()}
    collided, spent, seed = None, 0, mech.get("seed")
    habits = habit_markers(plan, names)
    for i, t in enumerate(turns):
        is_last = i == len(turns) - 1
        r = t["roll"] or {}
        tv = tl.turn_values(t, cons, names, datee_diag, is_last, mech.get("outcome"))
        if r.get("success"):  # the generic pass text says "sincere" for every move; the meaning comes separately
            tv["result"] = "SUCCESS" + (" (natural 20)" if r.get("nat20") else "") + f". The {t['chosen_stat']} roll landed."
        left = estimate_left(turns[:i], t["interest_before"])
        needs = story.needs(i, t["interest_before"])
        banned = habit_banned(chat, habits)
        h_note = habit_note(habits, banned)
        pre_step, _ = story_step(i, None, False, left, names, pieces, setups, collided, False)
        base = {**common, "plan": plan, "cast": cast, "history": history(chat), "n": t["turn"],
                "story_so_far": story.so_far(), "habit_note": h_note,
                "exchange": exchange_text(told, [h for h in hooks if h[0] >= t["turn"] - 2])}
        since = (f"The plan for this match:\n\n{plan}\n\nThe chat hasn't started yet." if not chat else
                 f"Turn {chat[-1][0]} as the writer delivered it:\n" + "\n".join(f"{w}: {m}" for w, m in chat[-1][1]))
        do_user = tl.render(files["direct_options_input.md"],
                            {**base, "since": since, "step": pre_step, "interest": t["interest_before"], "state": t["state_before"],
                             "options": ", ".join(t["options"]), "needs": needs}, "direct_options_input.md").strip() + "\n"
        o_raw = director.ask(do_user, f"turn {t['turn']} direction before the roll")
        o_problems = notes_missing(o_raw) + direction_problems(direction_of(o_raw))
        if o_problems:
            o_raw = director.ask(f"Your answer broke the format: {'; '.join(o_problems)}. Write the notes and the "
                                 "direction again, in the format asked for.", f"turn {t['turn']} direction before the roll retry")
            o_problems = notes_missing(o_raw) + direction_problems(direction_of(o_raw))
        o_direction, d_notes = direction_of(o_raw), notes_of(o_raw)
        o_user = tl.render(files["options_input.md"],
                           {**base, "options": ", ".join(t["options"]), "direction": o_direction},
                           "options_input.md").strip() + "\n"
        o_text = caller(systems["options"], o_user, f"turn {t['turn']} options", knobs.get("turn_reasoning"),
                        {"offered": t["options"]})
        intended = parse_options(o_text, t["options"]).get(t["chosen_stat"])
        if not intended:
            tl.die(f"turn {t['turn']}: the options output has no block for the picked move {t['chosen_stat']}:\n{o_text[:1500]}")
        key, beat = story.beat(i, t, is_last, mech.get("outcome"))
        ghost, nat20 = key == "ending_ghost", bool(r.get("nat20"))
        now_left = 1 if is_last else left
        step, need = story_step(i, key, is_last, now_left, names, pieces, setups, collided, nat20)
        target = turn_target(knobs, key, seed, t["turn"], spent, now_left, turns[:i], t["state_after"], nat20)
        shape = draw_shape(knobs, key, seed, t["turn"], target)
        meaning, must = dice_meaning(t), must_text(need, names)
        dt_user = tl.render(files["direct_turn_input.md"],
                            {**base, **tv, "step": step, "dice_meaning": meaning, "intended": intended, "beat": beat,
                             "beat_key": key, "must": must, "shape": shape_text(shape, names)},
                            "direct_turn_input.md").strip() + "\n"
        direction = direction_of(director.ask(dt_user, f"turn {t['turn']} direction"))
        t_problems = direction_problems(direction)
        if t_problems:
            direction = direction_of(director.ask(f"Your direction broke the format: {'; '.join(t_problems)}. Write the "
                                                  "direction again.", f"turn {t['turn']} direction retry"))
            t_problems = direction_problems(direction)
        d_user = tl.render(files["deliver_input.md"],
                           {**base, **tv, "intended": intended, "roll_line": roll_line(t),
                            "mood": mood_text(t, names, is_last, mech.get("outcome")), "direction": direction,
                            "must": must, "shape": shape_text(shape, names)}, "deliver_input.md").strip() + "\n"
        ctx = {"ghost": ghost, "shape": shape, "beat": key, "required": need}
        exempt = names[SNAPS[key]] if key in SNAPS else None

        def attempt(prompt, label):
            raw = caller(systems["deliver"], prompt, label, knobs.get("turn_reasoning"), ctx)
            text, notes = strip_notes(raw, names)
            lines = parse_lines(text, names)
            problems = (shape_problems(lines, shape, names) + habit_problems(lines, habits, banned, exempt)
                        + notes_problems(notes, need, names, key.startswith("ending")))
            return raw, notes, lines, problems

        d_text, notes, lines, problems = attempt(d_user, f"turn {t['turn']} delivery")
        retried = bool(problems)
        if problems:
            again = (d_user + "\nYour answer was:\n\n" + d_text + "\n\nIt broke this turn's rules: " +
                     "; ".join(problems) + f". Write turn {t['turn']} again so it keeps all of them.\n")
            d_text, notes, lines, problems = attempt(again, f"turn {t['turn']} delivery retry")
        for who_told, what in notes["told"]:
            k, fact = piece_of(what)
            if who_told in pieces:
                if k is None:
                    k = next((x for x in (1, 2, 3) if x not in pieces[who_told]), 3)
                pieces[who_told].add(k)
            told.append((t["turn"], who_told, k, fact))
        hooks.extend((t["turn"], w, h) for w, h in notes["hooks"])
        for kind, field in (("planted", "planted"), ("callback", "called"), ("paid", "paid")):
            for x in notes[kind]:
                n = setup_no(x)
                if n is None:
                    pick = {"planted": lambda s: not s.get("planted"),
                            "callback": lambda s: s.get("planted") and not s.get("called") and not s.get("paid"),
                            "paid": lambda s: s.get("planted") and not s.get("paid")}[kind]
                    n = next((k for k in (1, 2, 3) if pick(setups.get(k, {}))), None)
                if n is not None:
                    setups.setdefault(n, {}).setdefault(field, t["turn"])
                    setups[n].setdefault("planted", t["turn"])
        if notes["collided"] and collided is None:
            collided = t["turn"]
        who = {w for w, _ in lines}
        if names["player"] not in who or (names["datee"] not in who and shape["datee"]):
            tl.die(f"turn {t['turn']}: the delivery output is missing a speaker:\n{d_text[:1500]}")
        words = sum(len(m.split()) for _, m in lines)
        spent += words
        chat.append((t["turn"], lines))
        noted = ([f"Told, {w}: {f}" for w, f in notes["told"]] + [f"Hook dropped, {w}: {h}" for w, h in notes["hooks"]]
                 + [f"{k.capitalize()}: {x or '?'}" for k in ("planted", "callback", "paid") for x in notes[k]]
                 + (["Their habits collided."] if notes["collided"] else []))
        brief = (f"Picked {t['chosen_stat']}. {tv['result']}\n{tv['checks']}\n"
                 f"Interest {t['interest_before']} → {t['interest_after']} ({t['state_after']}).\n"
                 f"Beat the game chose: {key}\n"
                 f"Required notes: {', '.join(need) or 'none'}\n\nShape: {shape_text(shape, names)} "
                 f"Written: {words} words; {spent} of the {knobs.get('word_budget', 1250)} budget spent."
                 + (f"\n\nHabits: {h_note}" if h_note else "")
                 + (f"\nRules still broken after a retry: {'; '.join(problems)}" if problems else
                    "\n(kept the rules after a retry)" if retried else "")
                 + ("\n\n" + "\n".join(noted) if noted else ""))
        out.append({"turn": t["turn"], "beat": key, "needs": needs, "options_user": o_user, "options": o_text,
                    "direct_options_user": do_user, "direction_options": o_direction,
                    "direct_user": dt_user, "direction": direction, "director_notes": d_notes,
                    "direction_problems": o_problems + t_problems,
                    "deliver_user": d_user, "delivered": d_text, "brief": brief, "shape": shape,
                    "retried": retried, "shape_problems": problems, "notes": notes, "habit_note": h_note,
                    "step": step, "required": need, "target": target, "words": words})
    output = "\n\n".join(f"Turn {n}\n" + "\n".join(f"{w}: {m}" for w, m in lines) for n, lines in chat) + "\n"
    planted_turns = [s["planted"] for s in setups.values() if s.get("planted")]
    called_turns = [s["called"] for s in setups.values() if s.get("called")]
    return {"names": names, "common": common, "systems": systems, "plan_user": plan_user, "plan": plan, "cast": cast,
            "turns": out, "output": output, "planted_turn": min(planted_turns) if planted_turns else None,
            "callback_turn": min(called_turns) if called_turns else None, "told": told, "hooks_open": hooks,
            "habits": habits, "pieces": {k: sorted(v) for k, v in pieces.items()},
            "setups": {str(k): v for k, v in sorted(setups.items())}, "collided_turn": collided, "words": spent}


def dry_caller(names):
    def caller(system, user, label, effort, ctx):
        if label == "plan":
            return "\n\n".join(f"{n}\nShows: dry\nFear: dry\nHabit: dry\nHabit marker: none\nLie: dry\n"
                               "Origin, in three pieces, from the surface to the part they'd never say:\n"
                               "1. dry-origin-one\n2. dry-origin-two\n3. dry-origin-three\nAccident: dry-accident-line\n"
                               f"Voice: dry\n- dry" for n in (names["player"], names["datee"])) + \
                "\n\nCollision: dry\nIf they click: dry\nHeat: dry"
        if "direction before the roll" in label:
            return ("NOTES:\n" + "\n".join(f"{h}: dry" for h in NOTE_HEADS) + "\nDIRECTION: (dry run)")
        if "direction" in label:
            return "DIRECTION: (dry run)"
        if label.startswith("reader"):
            return "(dry run)\nSCORE: 0"
        if label.endswith("options"):
            return "\n\n".join(f"OPTION {s}\n{names['player']}: (dry run, {s})" for s in ctx["offered"])
        shape = ctx["shape"]
        lines = [f"{names['player']}: dry"] * len(shape["player"]) + [f"{names['datee']}: dry"] * len(shape["datee"])
        for n in ctx.get("required", []):
            if n.startswith("TOLD:"):
                lines.append(f"TOLD: {names['player'] if n == 'TOLD:any' else n[5:]}: dry")
            else:
                lines.append(n)
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
        hist = ctx.get("history") or []
        if args.dry_run:
            text = dry_caller(names)(system, user, label, effort, ctx)
            calls.append({"label": label, "system_sha256": tl.sha256(system), "user": user, "output": text,
                          "session_messages": len(hist)})
            save()
            return text
        if provider == "openrouter":
            k = {x: knobs[x] for x in ("temperature", "top_p", "max_tokens") if x in knobs}
            if effort:
                k["reasoning"] = {"effort": effort}
            data, seconds = tl.call_openrouter(system, user, model, key, k, history=hist)
            text = ((data.get("choices") or [{}])[0].get("message") or {}).get("content") or ""
            usage, served = data.get("usage") or {}, data.get("model")
        else:
            flat = "".join(f"[{m['role']}]\n{m['content']}\n\n" for m in hist) + ("[user]\n" if hist else "") + user
            data, seconds = tl.call_claude(system, flat, model)
            text = data.get("result") or ""
            usage = {"cost": data.get("total_cost_usd")}
            served = ",".join(sorted((data.get("modelUsage") or {}).keys()))
        models.append(served)
        calls.append({"label": label, "system_sha256": tl.sha256(system), "user": user, "output": text,
                      "model": served, "seconds": seconds, "usage": usage, "session_messages": len(hist)})
        save()
        if not text.strip():
            tl.die(f"{label}: the model returned nothing")
        return text.strip()

    result = play(variant_dir, knobs, mech, args.player, args.datee, caller)
    reader = reader_check(caller, result["plan"], result["output"], names)
    systems = result["systems"]
    with open(os.path.join(run_dir, "prompt.md"), "w", encoding="utf-8") as f:
        f.write("".join(f"<!-- system prompt: {k} -->\n{v}\n\n" for k, v in systems.items()) +
                "<!-- plan user prompt -->\n" + result["plan_user"] +
                "\n<!-- every per-turn user prompt is in live_calls.json -->\n")
    parts = {"system": systems["plan"], "user": result["plan_user"], "plan": result["plan"],
             "options_system": systems["options"], "deliver_system": systems["deliver"],
             "direct_system": systems["direct"], "cast": result["cast"],
             "turn_calls": turn_calls(calls, len(result["turns"])),
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
        "required_missed": [[t["turn"], t["shape_problems"]] for t in result["turns"] if t["shape_problems"]],
        "hooks_open": [list(x) for x in result["hooks_open"]],
        "habits": result["habits"],
        "words": result["words"], "word_budget": knobs.get("word_budget", 1250),
        "pieces": result["pieces"], "setups": result["setups"], "collided_turn": result["collided_turn"],
        "reader_score": reader["score"], "reader": reader,
        "format_checks": tl.format_checks(result["output"], names, mech["turns_played"]),
    }
    with open(os.path.join(run_dir, "output.md"), "w", encoding="utf-8") as f:
        f.write(result["output"])
    with open(os.path.join(run_dir, "run.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=2, ensure_ascii=False)
    tl.write_report(run_dir)
    add_reader_to_report(run_dir, reader)
    print(os.path.relpath(run_dir, tl.REPO))


def add_reader_to_report(run_dir, reader):
    import html
    path = os.path.join(run_dir, "report.html")
    if not os.path.exists(path):
        return
    with open(path, encoding="utf-8") as f:
        page = f.read()
    sec = ("<section><h2>Reader check</h2><p>A reader who saw only the chat answered these questions; a second call "
           f"graded the answers against the plan. Score: {reader['score']} of 9.</p>"
           f"<pre>{html.escape(reader['answers'])}</pre>"
           f"<details><summary>Grade against the plan</summary><pre>{html.escape(reader['grade'])}</pre></details></section>")
    page = page.replace("</body>", sec + "</body>", 1) if "</body>" in page else page + sec
    with open(path, "w", encoding="utf-8") as f:
        f.write(page)
