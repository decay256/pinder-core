"""Live mode for the tone lab: the chat written one turn at a time, the way the game will
play it, instead of all at once after the game is over.

Why: in the real game nobody knows in advance which of the three offered moves the player
will pick or how the dice will land, so a story cannot be written ahead of time, turn by
turn. This mode tests whether a story still holds under that constraint:

1. One planning call at match start writes the story plan for the pair: cravings, lies,
   quirks, secrets, where their wounds collide, setups and both endings.
2. Each turn, the engine's result for that turn (which move the player picked, how the
   dice landed) goes to the director with what the game knows, and one call writes what was
   actually sent and the match's reply. (Up to v11 a first call wrote the three options
   the player sees before the roll; see v12.)

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

v9: the writers draft and the director has the final say. In v8 the director's directions
were still a layer the writers could ignore or over-obey (in turn 2 all three options carried
the same prop because the direction said "in every option"), and the fiction drifted into
one building ("meet downstairs", "the doorway") while the director's own notes said they
were apart. Now each writer drafts from the game's prompt alone (who they are, where each is
tonight, the chat, the interest level, the template, the shape), and the director, in its
session, gets that draft, answers a fixed checklist about it (where are they, what did the
other just say, what does this cost, what does this interest level need, what is stale,
does it sound like them) and writes the version that goes out, the options the player sees
and the turn as sent. The game's checks (shape, habit, required notes) run on the
director's version, plus three new ones: a line that puts them in one place before the
match agrees to meet, a [bracketed] fact the writer left for the director, and a line that
is neither a message nor a note. Each interest level now names what the chat needs from
it, and the turns-left estimate no longer jumps with the meter (v8's went 4, 8, 3, 12 in
four turns, and the turn it said 3 demanded an origin piece and the collision at once).

v10: one session writes the chat. In v9 the director rewrote most of every draft (less than
half of a draft survived into what went out), so the two writers cost two calls a turn and
added little; they had been kept apart from the plan so they would not steer toward its
props, but the director picks the material anyway. Now the plan is written once and one
session writes every turn: before the roll the three options, after the roll the turn as
sent. What the game knows comes from the tone lab's situation table (situations.yaml):
for the roll, what it does to the meant message and how the match receives it; what the
interest level and the size of its change mean; any shadow, trap or thirsty slip; the
template for the beat; and how long each side's messages run. The random message shape is
gone: the length guidance is the table's, and the game checks only the extremes (no message
over the limit, one long one for an accident or reveal, a ceiling per turn, a terse match
after a big loss, a ghost who never replies).

v11: one source. Every sentence the tone lab sends to a model comes from the situation table:
the prompt templates, what each game situation means, the story clock's sentences, what a
required note asks for, the habit rule, the checks and what a retry says, and the reader's
questions. The code keeps the logic (which beat a turn carries, what is due, what a check
catches) and fills numbers and names into the table's sentences. situations.py renders the
whole file as docs/tone-lab/situations.html, and selftest.py checks that nothing in the file
is missing from the page.

v17: the review edits instead of describing. In v16 the review asked questions about the draft,
and the director answered them by describing the draft and passing it (the turn as sent differed
from the draft in 4 of 11 turns). The review is now a list of instructions, each with what to do
when a line breaks it, and the director names every fix as the old words and the new ones. One
instruction makes sure each line could only be its sender's and the two don't sound alike; another
makes sure nothing repeats a topic, theme, joke, prop, pet word or opener, and replaces a repeat
with something the plan has not spent yet, which the notes now list under Unused. The code counts
what has repeated (openers used three times or more, words that came up in three turns or more)
and tells the director every turn. The plan's Voice treats pet words and openers as seasoning, so
a voice trait does not harden into a tic. The run record keeps how much the review changed.

v16: length is guidance and the voice comes from the plan. No length is checked any more: the
per-message, per-turn and message-count caps and the match's word cap after a big loss are gone,
and so are their retries; the game checks only who writes. The director is told where the chat
stands (the words so far against the rough size of a whole chat, which it may run over) and,
counted by the code, how each of them has written over the last three turns, so a repeating shape
is visible. The plan gives each character a background (generation, schooling, money, vices) that
shapes their words, a voice that never changes, how each feeling changes the way they type (one
rants, the other goes down to one-word questions) and a break kept for the biggest moment; the
shared table of how feelings show becomes the default. Each turn the director drafts, reviews the
draft against dialogue questions adapted from the novel engine (who sent it without the names, the
plan's register, what each line does to the other, subtext, stale material, shape, the last word)
and sends the revised version. After the chat a blind reader sorts the shuffled messages into two
senders, which measures how distinct the voices are.

v14: a self-consistency pass over v13. Each fix removes two lines of one turn message that
told the director opposite things. Length is one line for each of them: the player character's
from the beat when the beat is about them, otherwise from the move; the match's from the beat
when it is about the match, otherwise from the swing. The interest level's "who carries the chat"
and the beat's general length are gone, since they contradicted the swing and the roll. A trap
the turn sets off is told under this turn, as a consequence, not under where they stand, and it
is never given as the cause of the match's reaction on that turn; one set off again as it ran out
is said to be back. The interest levels say what the level means, not what the story should do
(which went stale: "the lie shows its first crack" after the secret was out). On a secret turn
the clock says piece 2 comes out now, which it used to contradict. A won ending doesn't demand
the collision. The first turn says there is nothing from the match yet, which the Charm rewriter
assumed there was. Under a beat about the match, the swing doesn't also describe the reply.

v13: the director's message follows a state model (docs/tone-lab/state-model.md). Every
value the game has is either interpreted in words or left out as technical, and the message
is ordered the way the director needs it: where they stand (the match's interest and what it
means, any trap and which of its three turns this is), where the story is (where the chat is,
how the director read each of them last turn, what is out and owed, the hooks still hanging),
this turn (the move and whether it is the character's strength, how the roll came out in
words, what else fired, how the match takes it, how their interest moved and why), and how
long. The full list of what each has told the other is no longer re-sent: the session holds
it. A new note, Feeling, names each one's feeling from a fixed list before the turn is
written; the match's has to agree with how their interest moved. The match gets one length
statement a turn with a message count, which is checked.

v12: no pretend roll. The engine has already played every turn, so the director no longer
writes three options before a roll whose result the code already has (two of the three were
thrown away every turn, and the step cost a call). Each turn is one message with everything
the game knows, the picked move and the roll included, and one answer: the notes, now with
what the player character meant to send, then the checklist, then the turn as sent. A retry
asks for all three again. The director still never sees a later turn. The option writing the
shipping game does before the player picks is no longer exercised here.

The engine still plays the whole session first (--mechanics-only, normally with
--agent random so the choices are as unpredictable as a person's), but the writer only
ever sees the turns up to the current one, so it knows no more than the live game would.
Which story beat a turn carries is decided here from the game so far, never from later
turns; selftest.py checks that.
"""

import difflib
import json
import os
import random
import re

import yaml

import tonelab as tl

FILES = ("situations.yaml",)
BAD_TIERS = {"catastrophe", "legendary"}  # miss tiers bad enough to shake a secret out
ENDINGS = {"DateSecured": "ending_win", "Unmatched": "ending_loss", "Ghosted": "ending_ghost"}
DEFAULT_TABLE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "variants", "live", "situations.yaml")
_table = None


def use_table(table):
    """The situation table every sentence below is read from (play() sets it for its variant)."""
    global _table
    _table = table


def T():
    global _table
    if _table is None:
        with open(DEFAULT_TABLE, encoding="utf-8") as f:
            _table = yaml.safe_load(f)
    return _table


def say(path, **values):
    """A sentence from the situation table, by its path, with the game's values filled in."""
    node = T()
    for k in path.split("."):
        node = node[k]
    return tl.render(node, values, f"situations.yaml {path}")


def plural(n, word):
    return f"{n} {word}{'s' if n != 1 else ''}"


def beat_set(flag):
    return {k for k, b in T()["beats"].items() if isinstance(b, dict) and b.get(flag)}


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

    def __init__(self, knobs, beats):
        self.beats, self.needs_text = beats, T()["needs"]
        self.fired, self.secret_turn, self.log = set(), None, []

    def so_far(self):
        c, rows = T()["clock"], T()["beats"]
        done = "; ".join(say("clock.done_item", n=n, spent=rows[k]["spent"]) for n, k in self.log) or c["nothing_yet"]
        ahead = [c["ahead"][k] for k, gone in (("breakthrough", "breakthrough"), ("secret", "secret"),
                                               ("match_reveal", "match_reveal")) if gone not in self.fired]
        return say("clock.so_far", done=done, ahead=", ".join(ahead) or c["ahead"]["nothing"])

    def needs(self, i, interest):
        n = self.needs_text
        parts = [n["opener"] if i == 0 else n["early"] if i <= 2 else n["middle"]]
        if i > 0 and "breakthrough" not in self.fired:
            parts.append(n["breakthrough"])
        if i > 0 and "secret" not in self.fired:
            parts.append(n["secret"])
        if "secret" in self.fired and self.secret_turn is not None and i - self.secret_turn <= 3:
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
        real = bool(T()["moves"][t["chosen_stat"]].get("breakthrough"))
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
        elif ok and "breakthrough" not in f and i >= 2 and (real or delta >= 3):
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
        if key not in self.beats:
            tl.die(f"the situation table has no beat '{key}'")
        if T()["beats"][key].get("spent"):
            self.log.append((t["turn"], key))
        text = self.beats[key]
        if key.startswith("ending") and "secret" in f and "match_reveal" not in f:
            text += " " + self.beats["reveal_pending"]
        return key, text


SNAPS = {"player_snap": "player", "match_snap": "datee"}  # the snapper's habit is the weapon, so it isn't rationed
NOTE = re.compile(r"^[\s*_>\-]*(TOLD|HOOK)\s*[:\-]\s*([^:]+?)\s*:\s*(.+?)\s*$", re.I)
SETUP = re.compile(r"^[\s*_>\-]*(PLANTED|CALLBACK|PAID)\s*(?:[:\-]\s*(.*?))?[\s*_]*$", re.I)
FLAG = re.compile(r"^[\s*_>\-]*COLLIDED[\s*_.!]*$", re.I)
def estimate_left(past, interest):
    """Turns left, estimated from the meter: the distance to 25 (or to 0) over the pace. The
    game ends at either, and nobody knows which in advance. The meter swings by several
    points a turn, and v8's estimate from the last three turns jumped between three and
    twelve, which made the game demand an origin piece and the collision in one turn and
    then nothing for three. Now the pace is the whole chat's average, pulled toward a typical
    1.25 a turn, and since a chat usually runs ten to fifteen turns the estimate stays between
    eight and fourteen turns from the start while the meter is in the middle."""
    i, deltas = len(past), [t["interest_delta"] for t in past]
    pace = (sum(deltas) + 1.25 * 4) / (len(deltas) + 4)
    if pace > 0.3:
        left = (25 - interest) / pace
    elif pace < -0.3:
        left = interest / -pace
    else:
        left = 6
    lo = 8 - i if 5 <= interest <= 20 else 1
    return max(1, min(12, 14 - i, max(lo, round(left))))


def load_table(variant_dir):
    with open(os.path.join(variant_dir, "situations.yaml"), encoding="utf-8") as f:
        return yaml.safe_load(f)


def beats_of(table):
    return {k: v["story"] for k, v in table["beats"].items()}


MISS_KEY = {"fumble": "fumble", "misfire": "misfire", "tropetrap": "trope_trap", "catastrophe": "catastrophe",
            "legendary": "catastrophe"}


def roll_key(r):
    """The game's own result tiers (Pinder.Core RollOutcomeIntensity): a hit by its margin over the DC."""
    if r.get("nat20"):
        return "nat20"
    if r.get("nat1") or (r.get("tier") or "").lower() == "legendary":
        return "nat1"
    if r.get("success"):
        m = (r.get("total") or 0) - (r.get("dc") or 0)
        return "exceptional" if m >= 15 else "critical" if m >= 10 else "strong" if m >= 5 else "clean"
    return MISS_KEY.get((r.get("final_tier") or r.get("tier") or "").lower(), "misfire")


def interest_row(table, interest):
    return next(x for x in table["interest"] if x["from"] <= interest <= x["to"])


def swing_row(table, delta):
    return next(x for x in table["swings"] if x["from"] <= delta <= x["to"])


def fill(text, **values):
    """A table row that carries its own placeholders, filled in."""
    return tl.render(text, values, "situations.yaml")


def phase_of(i, is_last, left):
    return "opener" if i == 0 else "last" if is_last else "early" if i <= 2 else "late" if left <= 3 else "middle"


def strength_of(stats, move):
    S, v = T()["stats"], stats.get(move, 0)
    return "strong" if v >= S["strong_from"] else "weak" if v <= S["weak_to"] else "middling"


def trap_lines(table, t, names, starts):
    """The traps in force as the turn starts, with their acting and which of their three turns this
    is, and the traps this turn sets off. Every trap lasts three turns and the turn it fires counts
    as the first (Pinder.Core TrapState); picking the move that clears it disarms it before the
    message goes out. A trap set off again the turn after it ran out is said to be back. Only the
    traps in force as the turn starts are a cause of the match's reaction."""
    before, after = set(t.get("active_traps_before") or []), set(t.get("active_traps_after") or [])
    cleared = str(t.get("trap_cleared") or "").lower()
    stages = table["turn"]["relationship"]["trap_stage"]
    lines, active, fresh = [], [], []
    for trap in sorted(before | after):
        row = table["traps"].get(trap.lower())
        if not row or trap.lower() == cleared:
            continue
        acting = say("traps." + trap.lower() + ".acting", name=names["player"])
        if trap not in before:
            again = starts.get(trap) == t["turn"] - 3
            starts[trap] = t["turn"]
            fresh.append(say("turn.event." + ("trap_again" if again else "trap_new"), name=names["player"],
                             trap=row["name"], datee=names["datee"], acting=acting))
            continue
        k = max(1, min(3, t["turn"] - starts.get(trap, t["turn"]) + 1))
        active.append(row["name"])
        lines.append(say("turn.relationship.trap", name=names["player"], trap=row["name"],
                         stage=stages[("first", "second", "third")[k - 1]], acting=acting))
    return lines, active, fresh


def relationship_text(table, t, names, starts):
    """Where the two of them stand as the turn starts: the match's interest and what it means, and any trap."""
    ir = interest_row(table, t["interest_before"])
    line = say("turn.relationship.interest", datee=names["datee"], interest=t["interest_before"], state=ir["state"],
               means=fill(ir["means"], datee=names["datee"]))
    traps, active, fresh = trap_lines(table, t, names, starts)
    return "\n".join([line] + traps), active, fresh


def event_text(table, t, names, is_last, outcome, beat, stats, active, fresh=(), first=False, about_match=False):
    """What happened this turn, in words: the move, the roll, what else fired, how the match takes
    it, how their interest moved and why (when the cause shows in the message), and the beat."""
    E, datee, player = "turn.event.", names["datee"], names["player"]
    r, stat = t["roll"] or {}, t["chosen_stat"]
    mv = table["moves"][stat]
    rk = roll_key(r)
    row = mv["tiers"][rk]
    out = [say(E + "picked", name=player, move=stat, strength=table["turn"]["event"]["strength"][strength_of(stats, stat)],
               means=mv["means"])]
    result = table["results"][rk]["name"]
    out.append(say(E + "roll_hit", result=result) if r.get("success") else
               say(E + "roll_miss", result=result, name=player, shadow=mv["shadow"],
                   shadow_means=table["shadows"][mv["shadow"]]["means"]))
    out.append(say(E + "message", text=row["message"]))
    if first:
        out.append(say(E + "first", datee=datee))
    out += list(fresh)
    causes = []
    sh = t.get("shadow")
    if sh and sh.get("miss"):
        tier, entry = MISS_KEY.get((sh.get("tier") or "").lower(), "misfire"), table["shadows"].get(sh["shadow"].capitalize())
        if entry:
            out.append(say(E + "shadow", name=player, shadow=sh["shadow"], tier=tier.replace("_", " "), about=entry["about"],
                           text=entry.get(tier, "")))
            causes.append(say(E + "cause.shadow", shadow=sh["shadow"], datee=datee))
    h = t.get("horniness")
    if h and h.get("miss"):
        tier, th = MISS_KEY.get((h.get("tier") or "").lower(), "fumble"), table["thirst"]
        mood = (th["received_at_high_interest"] if t["interest_after"] >= th["high_interest_from"]
                else th["received_at_low_interest"])
        out.append(say(E + "thirst", tier=tier.replace("_", " "), message=th["tiers"][tier]["message"], datee=datee,
                       received=th["tiers"][tier]["received"], mood=mood))
        causes.append(say(E + "cause.thirst", datee=datee))
    st = t.get("steering")
    if st and st.get("succeeded"):
        out.append(table["turn"]["event"]["steering"])
    out.append(say(E + "received", datee=datee, text=row["received"]))
    sw = swing_row(table, t["interest_delta"])
    sv = dict(datee=datee, before=t["interest_before"], after=t["interest_after"], swing=sw["name"])
    # under a beat about the match, the beat says how they reply; the swing's meaning would say it twice, differently
    out.append(say(E + "swing_bare", **sv) if about_match else say(E + "swing", means=sw["means"], **sv))
    if t["interest_delta"] < 0:
        causes += [say(E + "cause.trap", trap=x, datee=datee) for x in active]
    if causes:
        out.append(say(E + "why", causes=" And ".join(causes)))
    before, after = interest_row(table, t["interest_before"]), interest_row(table, t["interest_after"])
    if after["state"] != before["state"]:
        out.append(say(E + "band", state=after["state"], means=fill(after["means"], datee=datee)))
    if is_last and outcome:
        ends = table["turn"]["event"]["endings"]
        out.append(say(E + "endings." + outcome, datee=datee) if outcome in ends else outcome)
    elif t["interest_after"] >= table["turn"]["event"]["not_yet_from"]:
        out.append(say(E + "not_yet", interest=t["interest_after"], datee=datee))
    out.append(say(E + "job", beat=beat))
    return "\n".join(out), rk, sw


def story_text(i, is_last, left, so_far, step, needs, last_feel, names, hooks):
    """Where the story is: where the chat is, what has happened and what is owed, how the director
    read each of them last turn, what the chat needs at this point, and the hooks still hanging."""
    out = [say("turn.story.phase", phase=T()["turn"]["story"]["phases"][phase_of(i, is_last, left)]), so_far, step]
    for side in ("player", "datee"):
        f = last_feel.get(names[side])
        if f:
            out.append(say("turn.story.feeling", name=names[side], feeling=f["text"]))
    if needs:
        out.append(say("turn.story.needs", needs=needs))
    if hooks:
        out.append(hooks_text(hooks))
    return "\n".join(x for x in out if x)


def feelings_of(head, names):
    """Each character's feeling from the director's Feeling note: the first listed feeling after their name."""
    F, heads = T()["feelings"], [h for h in note_heads() if h != "Feeling"]
    m = re.search(r"(?ims)\bFeeling[\s*_]*:(.*?)(?=^[\s*_\-#]*(?:" + "|".join(map(re.escape, heads)) + r")[\s*_]*:|\Z)",
                  head)
    seg, out = (m.group(1) if m else ""), {}
    every = F["negative"] + F["positive"]
    for side, other in (("player", "datee"), ("datee", "player")):
        name = names[side]
        k = seg.lower().find(name.lower())
        if k < 0:
            out[name] = {"feeling": None, "text": ""}
            continue
        rest = seg[k + len(name):]
        cut = rest.lower().find(names[other].lower())
        part = rest[:cut] if cut >= 0 else rest
        w = re.search(r"\b(" + "|".join(every) + r")\b", part, re.I)
        text = " ".join(part.split()).strip(" :;,-*_")
        out[name] = {"feeling": w.group(1).lower() if w else None, "text": text[:300]}
    return out


def feeling_problems(feel, names, delta, ghost):
    """Each of them has a feeling from the list, and the match's agrees with how their interest moved."""
    F, out = T()["feelings"], []
    for side in ("player", "datee"):
        if side == "datee" and ghost:
            continue
        if not (feel.get(names[side]) or {}).get("feeling"):
            out.append(say("checks.feeling.missing", name=names[side], list=", ".join(F["negative"] + F["positive"])))
    got = (feel.get(names["datee"]) or {}).get("feeling")
    if got and delta and not ghost:
        want = "negative" if delta < 0 else "positive"
        if got not in F[want]:
            out.append(say("checks.feeling.direction", name=names["datee"], feeling=got,
                           moved=T()["checks"]["feeling"]["moved"]["fell" if delta < 0 else "rose"],
                           kind="positive" if want == "negative" else "negative"))
    return out


def limits_for(table, key, swing, ghost):
    """What the game checks about a turn's messages: only who writes. No length is checked (v16)."""
    return {"ghost": ghost}


def shape_text(chat, names, last=3):
    """How each of them has written over the last few turns, counted by the code: told as information,
    so the director can see a shape repeating, never as a target."""
    out = []
    for side in ("player", "datee"):
        turns = []
        for _, lines in chat[-last:]:
            mine = [m for w, m in lines if w == names[side]]
            turns.append(say("turn.length.shape_turn", count=plural(len(mine), "message"),
                             words=plural(sum(len(m.split()) for m in mine), "word"))
                         if mine else T()["turn"]["length"]["shape_none"])
        if turns:
            out.append(say("turn.length.shape_one", name=names[side], turns=", then ".join(turns)))
    return "; ".join(out)


WORD = re.compile(r"[a-z0-9][a-z0-9']*")


def repeat_counts(chat, names):
    """What has already come back in the chat: each one's message openers used at least opener_min
    times, and the words (not common ones, not their names) that came up in at least word_turns_min
    turns, most turns first."""
    R = T()["turn"]["repeats"]
    norm = lambda x: WORD.findall(x.lower().replace("’", "'"))
    names_skip = {p for n in names.values() for p in norm(n)}
    skip, o_skip = set(R["skip"]) | names_skip, set(R["opener_skip"]) | names_skip
    openers = []
    for side in ("player", "datee"):
        firsts = {}
        for _, lines in chat:
            for w, m in lines:
                tok = m.split()
                if w != names[side] or not tok:
                    continue
                key = (norm(tok[0]) or [tok[0]])[0]
                if key not in o_skip:
                    firsts.setdefault(key, []).append(tok[0])
        openers += [(names[side], max(sorted(set(raw)), key=raw.count), len(raw))
                    for raw in firsts.values() if len(raw) >= R["opener_min"]]
    turns_of = {}
    for n, lines in chat:
        for _, m in lines:
            for w in set(norm(m)):
                w = w.strip("'")
                if len(w) >= R["word_letters"] and w not in skip:
                    turns_of.setdefault(w, set()).add(n)
    words = sorted(((w, len(ts)) for w, ts in turns_of.items() if len(ts) >= R["word_turns_min"]),
                   key=lambda x: (-x[1], x[0]))[:R["most"]]
    return {"openers": openers, "words": words}


def repeats_text(chat, names):
    """What has repeated so far, told to the director every turn, with what may bring a repeat back."""
    R, c = T()["turn"]["repeats"], repeat_counts(chat, names)
    out = [say("turn.repeats.opener", name=n, count=plural(k, "message"), word=w) for n, w, k in c["openers"]]
    out += [say("turn.repeats.word", word=w, turns=plural(k, "turn")) for w, k in c["words"]]
    return "\n".join(out + [R["rule"]]) if out else R["none"]


def length_text(table, t, rk, swing, key, names, spent, played, budget, chat):
    """One length statement for each of them, never two that can disagree: the player's from the
    beat when the beat is about them, otherwise from the move and the roll; the match's from the
    beat when it is about the match, otherwise from the swing. Then where the chat stands in words,
    and how each has written lately. None of it is checked."""
    L, b = "turn.length.", table["beats"].get(key) or {}
    match = fill(b["length"] if b.get("about_match") else swing["length"], datee=names["datee"])
    mine = (b["length"] if b.get("length") and not b.get("about_match")
            else table["moves"][t["chosen_stat"]]["tiers"][rk]["length"])
    out = [say(L + "player", name=names["player"], text=mine), say(L + "match", text=match)]
    out.append(say(L + "so_far", spent=spent, played=plural(played, "turn"), budget=budget) if played
               else say(L + "so_far_first", budget=budget))
    shapes = shape_text(chat, names)
    if shapes:
        out.append(say(L + "shape", shapes=shapes))
    return "\n".join(out)


def length_problems(lines, lim, names):
    """Who writes is checked: each of them sends something, and a match who ghosts sends nothing.
    How much anyone writes is not (v16)."""
    L = "checks.length."
    out, sides = [], {w for w, _ in lines}
    if names["player"] not in sides:
        out.append(say(L + "silent", name=names["player"]))
    if lim["ghost"] and names["datee"] in sides:
        out.append(say(L + "ghost_replied", name=names["datee"]))
    elif not lim["ghost"] and names["datee"] not in sides:
        out.append(say(L + "silent", name=names["datee"]))
    return out


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


SECRET_BEATS = ("catastrophe", "secret_miss", "discovery")  # the beats that spend the secret


def story_step(i, key, is_last, left, names, pieces, setups, collided, nat20):
    """Where the story stands and what this turn has to move: the game's story clock, built
    only from what has actually happened so far. Returns the text and the notes it requires.
    key is None before the roll, when nothing is required yet."""
    key, S = key or "", "clock.step."
    exchange, big = beat_set("exchange"), beat_set("big")
    turn, lines, need = i + 1, [], []
    for side in ("player", "datee"):
        name = names[side]
        got = sorted(pieces[name])
        nxt = next((k for k in (1, 2, 3) if k not in got), None)
        known = (say(S + "known", name=name, pieces=("pieces " if len(got) > 1 else "piece ") + ", ".join(map(str, got)))
                 if got else say(S + "known_nothing", name=name))
        if side == "player" and key in SECRET_BEATS and 2 not in got:
            # the accident (or the discovery) is piece 2, whether or not the hook came first
            lines.append(say(S + "secret_now", known=known))
            need.append(f"TOLD:{name}")
        elif nxt is None:
            lines.append(say(S + "all_known", known=known))
        elif nxt == 3:
            lines.append(say(S + "last_piece_saved", known=known))
        else:
            due = bool(key) and key != "opener" and not key.startswith("ending") and left <= (3 - nxt) + 1
            lines.append(say(S + ("next_piece_due" if due else "next_piece"), known=known, next=nxt))
            if due:
                need.append(f"TOLD:{name}")
    if key in exchange and not need and any(len(pieces[names[s]] & {1, 2}) < 2 for s in ("player", "datee")):
        need.append("TOLD:any")
    if collided:
        lines.append(say(S + "collided", turn=collided))
    elif key == "ending_win":
        lines.append(T()["clock"]["step"]["not_collided_win"])
    elif key and key not in ("opener", "setup") and (
            (i >= 3 and all(pieces[names[s]] for s in ("player", "datee"))) or left <= 3 or key.startswith("ending")):
        lines.append(T()["clock"]["step"]["collision_due"])
        need.append("COLLIDED")
    else:
        lines.append(T()["clock"]["step"]["not_collided"])
    setup_need = None
    if key.startswith("ending") and any(s.get("planted") and not s.get("paid") for s in setups.values()):
        setup_need = "PAID"
    elif nat20:
        setup_need = "PAID"
    for n in (1, 2, 3):
        st = setups.get(n, {})
        if st.get("paid"):
            lines.append(say(S + "setup_paid", n=n, paid=st["paid"]))
        elif st.get("called"):
            lines.append(say(S + ("setup_called_now" if key.startswith("ending") else "setup_called"), n=n,
                             planted=st["planted"], called=st["called"]))
        elif st.get("planted"):
            due = (turn - st["planted"] >= 3 and setup_need is None and not key.startswith("ending")
                   and (key in exchange or key == "callback"))
            lines.append(say(S + ("setup_planted_due" if due else "setup_planted"), n=n, planted=st["planted"]))
            if due:
                setup_need = "CALLBACK"
        else:
            due = (i >= 2 and setup_need is None and key and key not in big and not key.startswith("ending"))
            lines.append(say(S + ("setup_unplanted_due" if due else "setup_unplanted"), n=n))
            if due:
                setup_need = "PLANTED"
    if key == "callback" and setup_need is None:
        setup_need = "CALLBACK" if any(s.get("planted") and not s.get("called") for s in setups.values()) else "PLANTED"
    if setup_need:
        need.append(setup_need)
    if nat20:
        lines.append(T()["clock"]["step"]["nat20"])
    if key == "ending_ghost":
        need = [x for x in need if x == "PAID"]
    return "\n".join(lines), need


def notes_problems(notes, need, names, is_end):
    m = T()["notes"]["missing"]
    problems, told_by = [], [w for w, _ in notes["told"]]
    for n in need:
        if n == "TOLD:any" and not notes["told"]:
            problems.append(m["TOLD:any"])
        elif n.startswith("TOLD:") and n != "TOLD:any" and n[5:] not in told_by:
            problems.append(say("notes.missing.TOLD", name=n[5:]))
        elif n == "COLLIDED" and not notes["collided"]:
            problems.append(m["COLLIDED"])
        elif n in ("PLANTED", "CALLBACK", "PAID") and not notes[n.lower()]:
            problems.append(m[n])
    if not is_end:
        for name in set(names.values()):
            ks = {piece_of(w)[0] for who, w in notes["told"] if who == name}
            if len(ks - {None}) > 1:
                problems.append(say("notes.missing.too_many_pieces", name=name))
    return problems


def hooks_text(hooks):
    """The hooks dropped in the last two turns, still open until the other one picks them up."""
    c, E = T()["clock"]["exchange"], "clock.exchange."
    return c["hooks_head"] + "\n" + "\n".join(say(E + "hook_item", n=n, who=who, hook=h) for n, who, h in hooks)


CHECK = re.compile(r"(?im)^[\s*_#>\-]*CHECK[\s*_]*:")
FINAL = re.compile(r"(?im)^[\s*_#>\-]*FINAL[\s*_]*:[\s*_]*")
DRAFT = re.compile(r"(?im)^[\s*_#>\-]*DRAFT[\s*_]*:[\s*_]*")
REVIEW = re.compile(r"(?im)^[\s*_#>\-]*REVIEW[\s*_]*:[\s*_]*")
LABEL_IN_CHAT = re.compile(r"\b(?:TOLD|HOOK|PLANTED|CALLBACK|PAID|COLLIDED)\b|(?i:\b(?:setup|piece)\s*#?\d)")


def note_heads():
    return T()["checks"]["note_heads"]


def apart_pattern():
    """A line that puts them in one place, or one on the way to the other, before the match agrees to meet."""
    return re.compile(r"(?i)\b(?:" + "|".join(T()["checks"]["apart_words"]) + r")\b")


def quoted(msgs):
    return " / ".join(f'"{m}"' for m in msgs[:3])


def sections(text):
    """A director answer split into its notes, its checklist answers, the version that goes out, its
    draft and its review of the draft."""
    c = CHECK.search(text)
    body = text[c.end():] if c else text
    m = FINAL.search(body)
    head = text[:c.start()] if c else (body[:m.start()] if m else text)
    notes = re.sub(r"(?is)^.*?NOTES[\s*_]*:", "", head, count=1).strip()
    middle = (body[:m.start()] if m else body) if c else ""
    d, r = DRAFT.search(middle), REVIEW.search(middle)
    check = middle[:d.start() if d else (r.start() if r else len(middle))].strip()
    if r and d and r.start() < d.end():
        r = None
    draft = middle[d.end():r.start() if r else len(middle)].strip() if d else ""
    review = middle[r.end():].strip() if r else ""
    final = FINAL.sub("", body[m.start():], count=1).strip() if m else ""
    return notes, check, final, draft, review


def chat_problems(msgs, allow_meet):
    """What no message may do, whoever wrote it."""
    out, apart = [], apart_pattern()
    near = [m for m in msgs if apart.search(m)]
    if near and not allow_meet:
        out.append(say("checks.chat.together", lines=quoted(near)))
    held = [m for m in msgs if "[" in m or "]" in m]
    if held:
        out.append(say("checks.chat.placeholder", lines=quoted(held)))
    tagged = [m for m in msgs if LABEL_IN_CHAT.search(m)]
    if tagged:
        out.append(say("checks.chat.labels", lines=quoted(tagged)))
    return out


def split_stray(lines):
    """Messages, and the lines that are neither a message nor a note (parse_chat glues those
    onto the message before them)."""
    kept, stray = [], []
    for w, m in lines:
        first, _, extra = m.partition("\n")
        stray += [x for x in extra.splitlines() if x.strip()]
        if w == "?":
            stray.append(first)
        else:
            kept.append((w, first.strip()))
    return kept, stray


class Director:
    """One session for the whole chat: every message and every reply stays in it, so the
    director remembers what it noted and wrote, and sees whether it worked."""

    def __init__(self, caller, system, effort):
        self.caller, self.system, self.effort, self.messages = caller, system, effort, []

    def ask(self, user, label, ctx=None):
        out = self.caller(self.system, user, label, self.effort, {**(ctx or {}), "history": list(self.messages)})
        self.messages += [{"role": "user", "content": user}, {"role": "assistant", "content": out}]
        return out


CALL_TITLES = {
    "": ("The turn: notes, checklist, draft, review and the turn as sent", True),
    "retry": ("The turn, asked again", True),
}


def turn_calls(calls, n_turns):
    """Every prompt sent in each turn, in order and exactly as sent, for the report."""
    out = [[] for _ in range(n_turns)]
    for c in calls:
        m = re.match(r"^turn (\d+)(?: (.+))?$", c.get("label", ""))
        if not m or (m.group(2) or "") not in CALL_TITLES or not 1 <= int(m.group(1)) <= n_turns:
            continue
        title, session = CALL_TITLES[m.group(2) or ""]
        if session:
            title += f": message {(c.get('session_messages') or 0) // 2 + 1} sent into the session"
            note = ("The session keeps the plan, every earlier message and every reply it wrote; this is what was "
                    "added this time. Its system prompt is under Prompts sent once.")
        else:
            title += ": prompt as sent"
            note = "A fresh call: this is everything it got, besides its system prompt under Prompts sent once."
        out[int(m.group(1)) - 1].append({"title": title, "note": note, "user": c["user"], "output": c["output"]})
    return out


def must_text(need, names):
    """What the game requires this turn, in plain words, with the note it needs."""
    a = T()["notes"]["asks"]
    out = [a[n] if n in a else say("notes.asks.TOLD", name=n[5:]) for n in need]
    return "\n".join(f"- {x}" for x in out) if out else a["nothing"]


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
        out.append(say("habits." + ("banned" if name in banned else "allowed"), name=name, shown=shown))
    return "\n".join(out)


def habit_problems(lines, habits, banned, exempt):
    problems = []
    for name, marks in habits.items():
        if name == exempt:
            continue
        n = sum(1 for w, m in lines if w == name and habit_used(m, marks))
        if name in banned and n:
            problems.append(say("habits.broke_ban", name=name))
        elif n > 1:
            problems.append(say("habits.too_often", name=name, n=n))
    return problems


def reader_check(caller, plan, output, names):
    """A reader who sees only the finished chat answers what the reader should know by the
    end; a second call grades the answers against the plan. The writer's own notes prove only
    that it says it delivered; this checks what reached the page."""
    R = T()["reader"]
    qs = []
    for n in (names["player"], names["datee"]):
        qs += [tl.render(q, {"name": n}, "situations.yaml reader.per_character") for q in R["per_character"]]
    qs += R["shared"]
    user = say("reader.prompt", chat=output, questions="\n".join(f"{k + 1}. {q}" for k, q in enumerate(qs)))
    answers = caller(R["system"], user, "reader check", "low", {})
    grade = caller(R["grader_system"], say("reader.grader_prompt", plan=plan, answers=answers), "reader grade", "low", {})
    m = re.search(r"SCORE:\s*\**\s*([\d.]+)", grade)
    return {"questions": qs, "answers": answers, "grade": grade, "score": float(m.group(1)) if m else None}


def attribution_check(caller, chat, names, seed):
    """A blind test of distinct voices: every message of the chat, shuffled and without names, sorted
    into two senders by a reader who knows nothing else. The score is the share sorted right under the
    better of the two ways to match the letters to the names, so 0.5 is chance; an unanswered message
    counts as wrong."""
    R = T()["reader"]
    msgs = [(w, m) for _, lines in chat for w, m in lines]
    order = list(range(len(msgs)))
    random.Random(f"attribution-{seed}").shuffle(order)
    listing = "\n".join(say("reader.attribution_line", k=k + 1, message=msgs[j][1]) for k, j in enumerate(order))
    answer = caller(R["attribution_system"], say("reader.attribution_prompt", n=len(msgs), messages=listing),
                    "attribution check", "low", {})
    got = {}
    for line in answer.splitlines():
        m = re.match(r"^[\s*_]*(\d+)[\s*_]*[:.)\-]\s*\**\s*([AB])\b", line, re.I)
        if m and 1 <= int(m.group(1)) <= len(msgs):
            got[int(m.group(1))] = m.group(2).upper()
    truth = {k + 1: msgs[j][0] for k, j in enumerate(order)}
    right = sum(1 for k, a in got.items() if (a == "A") == (truth[k] == names["player"]))
    score = max(right, len(got) - right) / len(msgs) if msgs else None
    return {"score": round(score, 3) if score is not None else None, "answered": len(got), "messages": len(msgs),
            "answer": answer}


def shape_stats(chat, names):
    """How varied each one's messages are: words per message (smallest, median, largest), the spread
    (standard deviation over the mean) and messages per turn."""
    out = {}
    for side in ("player", "datee"):
        n = names[side]
        words = [len(m.split()) for _, lines in chat for w, m in lines if w == n]
        if not words:
            continue
        mean = sum(words) / len(words)
        sd = (sum((x - mean) ** 2 for x in words) / len(words)) ** 0.5
        out[n] = {"messages": len(words), "words": sum(words),
                  "per_message": {"min": min(words), "median": sorted(words)[len(words) // 2], "max": max(words)},
                  "spread": round(sd / mean, 2) if mean else None,
                  "messages_per_turn": [sum(1 for w, _ in lines if w == n) for _, lines in chat]}
    return out


def review_edits(turns, chat, names):
    """What the review did: per turn, how many words differ between the draft and the turn as sent,
    and how many replacements the review named."""
    per = []
    for t, (_, sent) in zip(turns, chat):
        a = " ".join(m for _, m in parse_lines(t.get("director_draft") or "", names)).split()
        b = " ".join(m for _, m in sent).split()
        ops = difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes()
        changed = sum(max(i2 - i1, j2 - j1) for tag, i1, i2, j1, j2 in ops if tag != "equal")
        named = sum(1 for line in (t.get("director_review") or "").splitlines() if "→" in line or "->" in line)
        per.append({"turn": t["turn"], "words_changed": changed, "replacements_named": named})
    return {"turns": len(per), "turns_changed": sum(1 for x in per if x["words_changed"]),
            "words_changed": sum(x["words_changed"] for x in per),
            "replacements_named": sum(x["replacements_named"] for x in per), "per_turn": per}


def beat_keys(knobs, table, turns, outcome):
    use_table(table)
    story = Story(knobs, beats_of(table))
    return [story.beat(i, t, i == len(turns) - 1, outcome)[0] for i, t in enumerate(turns)]


def parse_lines(text, names):
    chat = tl.parse_chat("Turn 0\n" + text, names)
    return [line for k in sorted(chat) for line in chat[k]]


def stats_text(stats):
    """What a character is strong and weak at, from the game's stats for them."""
    S, mv = T()["stats"], T()["moves"]
    strong = [say("stats.strong", move=k, value=v, text=mv[k]["strong"]) for k, v in stats.items() if v >= S["strong_from"]]
    weak = [say("stats.weak", move=k, value=v, text=mv[k]["weak"]) for k, v in stats.items() if v <= S["weak_to"]]
    mid = [k for k, v in stats.items() if S["weak_to"] < v < S["strong_from"]]
    return "\n".join([S["head"]] + strong + weak + ([say("stats.middling", moves=", ".join(mid))] if mid else []))


def shadows_text(c):
    """How a character's moves go wrong for them: their shadows from the character file, strongest first."""
    sh = (c.get("allocation") or {}).get("shadows") or {}
    move_of = {v["shadow"]: k for k, v in T()["moves"].items()}
    rows = sorted(((k.capitalize(), v) for k, v in sh.items() if v and k.capitalize() in T()["shadows"]),
                  key=lambda x: -x[1])
    if not rows:
        return T()["character"]["no_shadows"]
    return "\n".join([T()["character"]["shadows_head"]] + [
        say("character.shadow_line", shadow=k, value=v, move=move_of.get(k, "?"), means=T()["shadows"][k]["means"])
        for k, v in rows])


def tonight(c, values, rng, n_drawer):
    """Each match draws its own material, so one character does not tell the same story in
    every match: one backstory pair to build the secret around, and a few drawer lines."""
    cats = list((c.get("backstory_categories") or {}).values())
    pair = rng.choice(cats) if cats else None
    lines = [ln.strip().lstrip("-•*").strip() for ln in (c.get("psychological_stake") or "").splitlines() if ln.strip()]
    if n_drawer and len(lines) > n_drawer:
        lines = rng.sample(lines, n_drawer)
    return {**values,
            "focus": say("character.focus", lie=pair["bio_lie"], truth=pair["tragic_reality"]) if pair
            else T()["character"]["no_focus"],
            "drawer": "\n".join(say("character.drawer_line", line=ln) for ln in lines)}


def play(variant_dir, knobs, mech, player, datee, caller):
    """Write the whole chat turn by turn. caller(system, user, label, effort, ctx) -> text."""
    files = read_files(variant_dir)
    table = yaml.safe_load(files["situations.yaml"])
    use_table(table)
    P = table["prompts"]
    names = {"player": mech["player"]["display_name"], "datee": mech["datee"]["display_name"]}
    rng = random.Random(f"{mech.get('seed')}-{player}-{datee}")
    pc, _, pv = tl.character_values(player, mech["player"], knobs)
    dc, _, dv = tl.character_values(datee, mech["datee"], knobs)
    n_drawer = int(knobs.get("drawer_sample", 0))
    pv, dv = tonight(pc, pv, rng, n_drawer), tonight(dc, dv, rng, n_drawer)
    pv["stats"], dv["stats"] = stats_text(mech["player"]["effective_stats"]), stats_text(mech["datee"]["effective_stats"])
    pv["shadows"], dv["shadows"] = shadows_text(pc), shadows_text(dc)
    heat = next((x["text"] for x in table["session"]["heat"]
                 if x["from"] <= int(mech.get("session_horniness") or 0) <= x["to"]), "")
    common = {
        "player_name": names["player"], "datee_name": names["datee"],
        "time_of_day": table["session"]["times"].get(mech.get("time_of_day", ""), mech.get("time_of_day", "")),
        "heat": heat,
        "feelings": "\n".join(say("feelings.list_line", feeling=k, shows=v) for k, v in table["feelings"]["shows"].items()),
        "player_card": tl.render(P["character"], {**pv, "role": "the player"}, "prompts.character").strip(),
        "datee_card": tl.render(P["character"], {**dv, "role": "the match"}, "prompts.character").strip(),
        "checklist": "\n".join(f"{k + 1}. {q}" for k, q in enumerate(table["checklist"])),
        "review": "\n".join(f"{k + 1}. {q}" for k, q in enumerate(table["review"])),
    }
    bible = tl.render(P["bible"], common, "prompts.bible").strip()
    systems = {k: bible + "\n\n" + tl.render(P[k], common, "prompts." + k).strip() for k in ("plan", "direct")}
    plan_user = tl.render(P["plan_input"], common, "prompts.plan_input").strip() + "\n"
    plan = caller(systems["plan"], plan_user, "plan", knobs.get("plan_reasoning"), {})
    director = Director(caller, systems["direct"], knobs.get("turn_reasoning"))

    story, turns, chat, out = Story(knobs, beats_of(table)), mech["turns"], [], []
    told, hooks, setups = [], [], {}
    pieces = {names["player"]: set(), names["datee"]: set()}
    collided, spent, budget = None, 0, table["limits"]["chat_words"]
    habits = habit_markers(plan, names)
    starts, last_feel, stats = {}, {}, mech["player"]["effective_stats"]
    for i, t in enumerate(turns):
        is_last = i == len(turns) - 1
        left = estimate_left(turns[:i], t["interest_before"])
        needs = story.needs(i, t["interest_before"])
        banned = habit_banned(chat, habits)
        h_note = habit_note(habits, banned)
        so_far = story.so_far()
        since = say("turn.state.since_start", plan=plan) if not chat else say("turn.state.since_turn", n=chat[-1][0])
        key, beat = story.beat(i, t, is_last, mech.get("outcome"))
        ghost, nat20 = key == "ending_ghost", bool((t["roll"] or {}).get("nat20"))
        now_left = 1 if is_last else left
        step, need = story_step(i, key, is_last, now_left, names, pieces, setups, collided, nat20)
        relationship, active, fresh = relationship_text(table, t, names, starts)
        event, rk, swing = event_text(table, t, names, is_last, mech.get("outcome"), beat, stats, active, fresh,
                                      first=i == 0, about_match=bool(table["beats"][key].get("about_match")))
        irow = interest_row(table, t["interest_after"])
        # on a big beat the beat is the template: the general one for this point would contradict it
        story_now = story_text(i, is_last, now_left, so_far, step,
                               "" if key in beat_set("big") or key.startswith("ending") else needs, last_feel, names,
                               [h for h in hooks if h[0] >= t["turn"] - 2])
        lim = limits_for(table, key, swing, ghost)
        length = length_text(table, t, rk, swing, key, names, spent, i, budget, chat)
        allow_meet = is_last and mech.get("outcome") == "DateSecured"
        user = tl.render(P["turn"],
                         {**common, "since": since, "relationship": relationship, "story": story_now,
                          "habit_note": h_note, "event": event, "must": must_text(need, names), "length": length,
                          "repeats": repeats_text(chat, names)},
                         "prompts.turn").strip() + "\n"
        ctx = {"ghost": ghost, "required": need, "delta": t["interest_delta"]}
        exempt = names[SNAPS[key]] if key in SNAPS else None

        def judge(raw):
            head, check, final, draft, review = sections(raw)
            text, notes = strip_notes(final, names)
            lines, stray = split_stray(parse_lines(text, names))
            fmt = T()["checks"]["format"]
            problems = [say("checks.format.no_note_head", head=h) for h in note_heads() if h.lower() not in head.lower()]
            problems += ([] if check else [fmt["no_check"]])
            problems += ([] if draft and review else [fmt["no_review"]])
            problems += ([] if final else [fmt["no_final"]])
            problems += (length_problems(lines, lim, names) + habit_problems(lines, habits, banned, exempt)
                         + notes_problems(notes, need, names, key.startswith("ending"))
                         + chat_problems([m for _, m in lines], allow_meet))
            problems += feeling_problems(feelings_of(head, names), names, t["interest_delta"], ghost)
            if stray:
                problems.append(say("checks.format.stray", lines=quoted(stray)))
            return problems, notes, lines, check, head, draft, review

        t_label = f"turn {t['turn']}"
        d_text = director.ask(user, t_label, ctx)
        problems, notes, lines, d_check, d_notes, d_draft, d_review = judge(d_text)
        retried = bool(problems)
        if problems:
            d_text = director.ask(say("checks.retry.turn", problems="; ".join(problems), n=t["turn"]), t_label + " retry",
                                  ctx)
            problems, notes, lines, d_check, d_notes, d_draft, d_review = judge(d_text)
        feel = feelings_of(d_notes, names)
        last_feel = {k: v for k, v in feel.items() if v.get("feeling")}
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
        if names["player"] not in who or (names["datee"] not in who and not ghost):
            tl.die(f"turn {t['turn']}: the turn is missing a speaker:\n{d_text[:1500]}")
        words = sum(len(m.split()) for _, m in lines)
        spent += words
        chat.append((t["turn"], lines))
        noted = ([f"Told, {w}: {f}" for w, f in notes["told"]] + [f"Hook dropped, {w}: {h}" for w, h in notes["hooks"]]
                 + [f"{k.capitalize()}: {x or '?'}" for k in ("planted", "callback", "paid") for x in notes[k]]
                 + (["Their habits collided."] if notes["collided"] else []))
        brief = (f"Picked {t['chosen_stat']}: {T()['results'][rk]['name']}. Interest {t['interest_before']} → {t['interest_after']} "
                 f"({swing['name']}, {irow['state']}).\nBeat the game chose: {key}\n"
                 f"Required notes: {', '.join(need) or 'none'}\n"
                 f"Written: {words} words; {spent} so far, against a chat of about {budget}."
                 + (f"\n\nHabits: {h_note}" if h_note else "")
                 + (f"\nRules still broken after a retry: {'; '.join(problems)}" if problems else
                    "\n(kept the rules after a retry)" if retried else "")
                 + ("\n\n" + "\n".join(noted) if noted else ""))
        out.append({"turn": t["turn"], "beat": key, "roll_key": rk, "needs": needs, "user": user,
                    "director_notes": d_notes, "delivered": d_text, "director_check": d_check,
                    "director_draft": d_draft, "director_review": d_review,
                    "brief": brief, "limits": lim, "retried": retried, "shape_problems": problems, "notes": notes,
                    "habit_note": h_note, "step": step, "required": need, "words": words, "feelings": feel,
                    "relationship": relationship, "story": story_now, "event": event})
    output = "\n\n".join(f"Turn {n}\n" + "\n".join(f"{w}: {m}" for w, m in lines) for n, lines in chat) + "\n"
    planted_turns = [x["planted"] for x in setups.values() if x.get("planted")]
    called_turns = [x["called"] for x in setups.values() if x.get("called")]
    return {"names": names, "common": common, "systems": systems, "plan_user": plan_user, "plan": plan,
            "turns": out, "output": output, "planted_turn": min(planted_turns) if planted_turns else None,
            "callback_turn": min(called_turns) if called_turns else None, "told": told, "hooks_open": hooks,
            "habits": habits, "pieces": {k: sorted(v) for k, v in pieces.items()},
            "setups": {str(k): v for k, v in sorted(setups.items())}, "collided_turn": collided, "words": spent,
            "chat": chat}


def dry_caller(names):
    def caller(system, user, label, effort, ctx):
        if label == "plan":
            return "\n\n".join(f"{n}\nShows: dry\nTonight: dry-tonight\nFear: dry\nHabit: dry\nHabit marker: none\nLie: dry\n"
                               "Origin, in three pieces, from the surface to the part they'd never say:\n"
                               "1. dry-origin-one\n2. dry-origin-two\n3. dry-origin-three\nAccident: dry-accident-line\n"
                               f"Background: dry\nVoice: dry\n- dry\nBreak: dry" for n in (names["player"], names["datee"])) + \
                "\n\nCollision: dry\nIf they click: dry\nHeat: dry"
        if label.startswith("reader"):
            return "(dry run)\nSCORE: 0"
        if label.startswith("attribution"):
            return "1: A"
        lines = [f"{names['player']}: dry"] + ([] if ctx.get("ghost") else [f"{names['datee']}: dry"])
        draft = "\n".join(lines)
        for n in ctx.get("required", []):
            if n.startswith("TOLD:"):
                lines.append(f"TOLD: {names['player'] if n == 'TOLD:any' else n[5:]}: dry")
            else:
                lines.append(n)
        feel = f"{names['player']}: fear, mild, dry. {names['datee']}: {'hurt' if (ctx.get('delta') or 0) < 0 else 'joy'}, mild, dry"
        return ("NOTES:\n" + "\n".join(f"{h}: {feel if h == 'Feeling' else 'dry'}" for h in note_heads())
                + "\nCHECK:\n1. dry\nDRAFT:\n" + draft + "\nREVIEW:\n1. holds\nFINAL:\n" + "\n".join(lines))
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
    attribution = attribution_check(caller, result["chat"], names, args.seed)
    systems = result["systems"]
    with open(os.path.join(run_dir, "prompt.md"), "w", encoding="utf-8") as f:
        f.write("".join(f"<!-- system prompt: {k} -->\n{v}\n\n" for k, v in systems.items()) +
                "<!-- plan user prompt -->\n" + result["plan_user"] +
                "\n<!-- every per-turn user prompt is in live_calls.json -->\n")
    parts = {"system": systems["plan"], "user": result["plan_user"], "plan": result["plan"],
             "direct_system": systems["direct"],
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
        "words": result["words"], "word_budget": T()["limits"]["chat_words"],
        "pieces": result["pieces"], "setups": result["setups"], "collided_turn": result["collided_turn"],
        "reader_score": reader["score"], "reader": reader,
        "attribution_score": attribution["score"], "attribution": attribution,
        "shape": shape_stats(result["chat"], names),
        "review_edits": review_edits(result["turns"], result["chat"], names),
        "repeats": repeat_counts(result["chat"], names),
        "format_checks": tl.format_checks(result["output"], names, mech["turns_played"]),
    }
    with open(os.path.join(run_dir, "output.md"), "w", encoding="utf-8") as f:
        f.write(result["output"])
    with open(os.path.join(run_dir, "run.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=2, ensure_ascii=False)
    tl.write_report(run_dir)
    add_reader_to_report(run_dir, reader, attribution)
    print(os.path.relpath(run_dir, tl.REPO))


def add_reader_to_report(run_dir, reader, attribution=None):
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
    if attribution:
        sec += ("<section><h2>Blind attribution</h2><p>A reader saw every message shuffled and without names and sorted "
                f"them into two senders: {attribution['score']} sorted right ({attribution['answered']} of "
                f"{attribution['messages']} answered; 0.5 is chance).</p>"
                f"<details><summary>The answer</summary><pre>{html.escape(attribution['answer'])}</pre></details></section>")
    page = page.replace("</body>", sec + "</body>", 1) if "</body>" in page else page + sec
    with open(path, "w", encoding="utf-8") as f:
        f.write(page)
