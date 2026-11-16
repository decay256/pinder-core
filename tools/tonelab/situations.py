#!/usr/bin/env python3
"""The tone lab's situation table: what each game state means for the chat, looked up per
turn and handed to whoever writes it.

Why: the game records every turn's move, result tier, shadow, trap, thirsty slip and
interest change, and Pinder Core has guidance written for each of them, but the tone lab
passed on only one-line summaries ("the message reads awkward and off-putting"). This table
is the tone lab's own copy, seeded once from Pinder Core and then edited here: the game's
files are not read at run time and are not authoritative for the tone lab. Entries the
game has no equivalent for (story meaning, message length per situation) are new and
marked as such.

  python3 tools/tonelab/situations.py seed [--force]   write situations.yaml from Pinder Core
  python3 tools/tonelab/situations.py html             render docs/tone-lab/situations.html
"""

import argparse
import datetime
import html
import json
import os
import re
import sys

import yaml

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
TABLE = os.path.join(HERE, "variants", "live", "situations.yaml")
PAGE = os.path.join(REPO, "docs", "tone-lab", "situations.html")

MOVES = {  # tone lab name: (delivery key, reaction key, what the move is, from options.md)
    "Charm": ("charm", "charm", "Make it easy, warm, reassuring."),
    "Rizz": ("rizz", "rizz", "Say plainly that they want them."),
    "Honesty": ("honesty", "honesty", "Show something real, or ask for it."),
    "Chaos": ("chaos", "chaos", "Swerve somewhere unexpected that still makes sense."),
    "Wit": ("wit", "wit", "Be quick and funny about it."),
    "SelfAwareness": ("sa", "self-awareness", "Name what's actually going on underneath."),
}
TIERS = ["clean", "strong", "critical", "exceptional", "nat20", "fumble", "misfire", "trope_trap", "catastrophe", "nat1"]
STATES = [("Unmatched", 0, 0), ("Bored", 1, 4), ("Lukewarm", 5, 9), ("Interested", 10, 15),
          ("Very Into It", 16, 20), ("Almost There", 21, 24), ("Date Secured", 25, 25)]

# New: how long messages run in each situation. The game has nothing for this; these are
# first guesses to argue with.
ROLL_LENGTH = {
    "clean": "As long as they meant it.",
    "strong": "As long as they meant it, or a little shorter: sharper, not longer.",
    "critical": "Shorter than they meant it. It lands because it is exact.",
    "exceptional": "One line, shorter than meant.",
    "nat20": "One short line, the best in the chat.",
    "fumble": "As long as they meant it; the flinch is at the end.",
    "misfire": "As long as they meant it, or one clause too long.",
    "trope_trap": "Longer than meant: the cliché takes room.",
    "catastrophe": "One long message, then a short panicked one, or the long one alone.",
    "nat1": "Short, and it lands exactly on the sore spot.",
}
STATE_NEEDS = {  # the chat's needs at each level (from the v9 interest goals)
    "Unmatched": "It's over.",
    "Bored": "The match is one message from leaving. The act has failed; only something true could keep them, and it may not be enough. If it ends here, it ends unhinged, with the vulnerability showing.",
    "Lukewarm": "The match isn't convinced. The act is in the way: something real has to get through, or they drift.",
    "Interested": "Curious. They trade hooks and each learns one thing about the other; the lie shows its first crack.",
    "Very Into It": "They want each other. What they've told each other matters now, the filth gets personal, and every honest line costs something.",
    "Almost There": "Meeting is on the table and the last guard is up. Only the habit put down, or the part they'd never say, gets them over it.",
    "Date Secured": "They agree to meet: a connection the reader feels, each of them putting the habit down for the other.",
}
STATE_LENGTH = {
    "Unmatched": "Nothing, or one last line.",
    "Bored": "The match: one short message, a few words.",
    "Lukewarm": "The match: short, one or two messages; the player writes more than the match.",
    "Interested": "Both normal: a few short messages each.",
    "Very Into It": "The one opening up writes more and longer; several messages in a row.",
    "Almost There": "Short and charged; every message costs something.",
    "Date Secured": "Short and plain on both sides.",
}
SWINGS = [  # new: what a change in interest does to the match's reply
    {"name": "big loss", "from": -99, "to": -3, "means": "It hurt. The match pulls back hard and doesn't explain why.",
     "length": "The match: one message, five words at most, or a single word."},
    {"name": "loss", "from": -2, "to": -1, "means": "A step cooler. The match answers less than it was asked.",
     "length": "The match: shorter than last turn."},
    {"name": "flat", "from": 0, "to": 0, "means": "Nothing moved. The match keeps its distance and waits.",
     "length": "About as long as last turn."},
    {"name": "gain", "from": 1, "to": 2, "means": "It worked a little. The match gives a little more back than it was asked.",
     "length": "The match: a little longer than last turn."},
    {"name": "big gain", "from": 3, "to": 99, "means": "It landed. The match drops some guard: either suddenly talkative, or stopped short by it.",
     "length": "The match: either a burst of several messages, or one word."},
]
BEAT_LENGTH = {
    "opener": "Short: the act is a one-liner.", "setup": "Short.", "early": "Normal.", "warming": "Getting longer.",
    "close": "Short and charged.", "breakthrough": "The naked line is the shortest in the chat.",
    "forced_breakthrough": "The naked line is short.", "catastrophe": "One long message, then a short one.",
    "secret_miss": "One long message, then a short one.", "discovery": "The match short and pointed.",
    "match_reveal": "The match writes more than ever before.", "player_snap": "A burst of short messages.",
    "match_snap": "A burst of short messages from the match.", "ending_win": "Short and plain.",
    "ending_loss": "Unhinged and long, then one short line.", "ending_ghost": "The player's messages get shorter into the silence.",
    "ending_open": "Short.", "callback": "Normal.", "reveal_pending": "",
}


def load_yaml(path):
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def seed(force):
    if os.path.exists(TABLE) and not force:
        sys.exit(f"{TABLE} exists; it is the tone lab's own copy now. Pass --force to overwrite it from Pinder Core.")
    data = os.path.join(REPO, "data")
    di = load_yaml(os.path.join(data, "delivery-instructions.yaml"))["delivery_instructions"]
    er = load_yaml(os.path.join(data, "prompts", "emotional-reactions.yaml"))["prompts"]
    tp = load_yaml(os.path.join(data, "prompts", "templates.yaml"))["prompts"]
    sc = load_yaml(os.path.join(data, "delivery-instructions.yaml"))["shadow_corruption"]
    traps = json.load(open(os.path.join(data, "traps", "traps.json"), encoding="utf-8"))
    beats = json.load(open(os.path.join(HERE, "variants", "live", "variant.json"), encoding="utf-8"))["beats"]

    def er_text(key):
        return ((er.get(key) or {}).get("system_prompt") or "").strip()

    moves = {}
    for name, (dk, rk, means) in MOVES.items():
        m = re.search(r"SHADOW:\s*([A-Z]+)", di[dk]["fumble"])
        tiers = {}
        for tier in TIERS:
            tiers[tier] = {
                "message": di[dk][tier].strip(),
                "received": er_text(f"emotional-reaction-event-{rk}-{tier}"),
                "length": ROLL_LENGTH[tier],
                "source": f"message: delivery-instructions.yaml {dk}.{tier}; received: emotional-reactions.yaml "
                          f"emotional-reaction-event-{rk}-{tier}; length: new",
            }
        moves[name] = {"means": means, "shadow": m.group(1).capitalize() if m else None, "tiers": tiers}
    interest = []
    for state, lo, hi in STATES:
        slug = state.lower().replace(" ", "-")
        interest.append({"state": state, "from": lo, "to": hi,
                         "match_feels": er_text(f"emotional-reaction-interest-{slug}"),
                         "chat_needs": STATE_NEEDS[state], "length": STATE_LENGTH[state],
                         "source": f"match_feels: emotional-reactions.yaml emotional-reaction-interest-{slug}; "
                                   "chat_needs: tone lab v9; length: new"})
    shadows = {}
    for key, v in sc.items():
        shadows[key.capitalize()] = {"about": v.get("description", "").strip(),
                                     **{t: v[t].strip() for t in ("fumble", "misfire", "trope_trap", "catastrophe") if t in v},
                                     "source": f"delivery-instructions.yaml shadow_corruption.{key}"}
    trap_out = {t["id"]: {"name": t["display_name"], "stat": t["stat"], "summary": t["summary"].strip(),
                          "effect": t["llm_instruction"].strip(), "clears": t["clear_method"].strip(),
                          "source": f"traps.json {t['id']}"} for t in traps}
    thirst = {"tiers": {}, "source": "message: delivery-instructions.yaml horniness_overlay.<tier>; received: "
                                     "templates.yaml datee-horniness-tier-intensity-<tier>; below/above: "
                                     "datee-horniness-reaction-below-threshold, -high-interest"}
    for tier in ("fumble", "misfire", "trope_trap", "catastrophe"):
        thirst["tiers"][tier] = {"message": di["horniness_overlay"][tier].strip(),
                                 "received": ((tp.get("datee-horniness-tier-intensity-" + tier.replace("_", "-")) or {})
                                              .get("system_prompt") or "").strip()}
    thirst["received_at_low_interest"] = tp["datee-horniness-reaction-below-threshold"]["system_prompt"].strip()
    thirst["received_at_high_interest"] = tp["datee-horniness-reaction-high-interest"]["system_prompt"].strip()
    table = {
        "about": ("The tone lab's situation table. Seeded on " + datetime.date.today().isoformat() + " from Pinder Core's "
                  "data (each entry names its source) and edited here from then on; the game's files are not read at "
                  "run time. 'new' marks text the game has no equivalent for."),
        "moves": moves, "interest": interest, "swings": SWINGS, "shadows": shadows, "traps": trap_out,
        "thirst": thirst,
        "beats": {k: {"story": v, "length": BEAT_LENGTH.get(k, ""), "source": "variant.json beats; length: new"}
                  for k, v in beats.items()},
    }
    with open(TABLE, "w", encoding="utf-8") as f:
        yaml.safe_dump(table, f, allow_unicode=True, sort_keys=False, width=110)
    print(os.path.relpath(TABLE, REPO))


CSS = """
:root{--bg:#fbfaf7;--fg:#1d1d1f;--muted:#6b6b70;--line:#e3e0d8;--card:#ffffff;--new:#2f6f4f;--newbg:#e6f2ea;--game:#5a4a8a;--gamebg:#ece8f6}
@media (prefers-color-scheme:dark){:root{--bg:#18181a;--fg:#ececee;--muted:#a0a0a8;--line:#323236;--card:#212124;--new:#8fd3ad;--newbg:#1f3329;--game:#c2b5f0;--gamebg:#2a2540}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 -apple-system,Segoe UI,Roboto,sans-serif}
main{max-width:1100px;margin:0 auto;padding:24px 16px 80px}h1{font-size:24px;margin:0 0 4px}h2{margin:36px 0 8px;font-size:19px}
h3{margin:20px 0 6px;font-size:16px}p.lead,.muted{color:var(--muted)}nav a{margin-right:14px;color:inherit}
input{width:100%;padding:8px 10px;border:1px solid var(--line);border-radius:6px;background:var(--card);color:var(--fg);margin:12px 0}
table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);margin:6px 0 14px}
th,td{text-align:left;vertical-align:top;padding:8px 10px;border-top:1px solid var(--line)}th{font-size:12px;color:var(--muted);font-weight:600;text-transform:uppercase;letter-spacing:.03em}
td.k{white-space:nowrap;font-weight:600;width:1%}.tag{font-size:11px;padding:1px 6px;border-radius:9px;margin-left:6px;white-space:nowrap}
.new{color:var(--new);background:var(--newbg)}.game{color:var(--game);background:var(--gamebg)}.src{font-size:12px;color:var(--muted)}
"""


def page():
    t = load_yaml(TABLE)
    e = html.escape
    out = []

    def tag(src_new):
        return '<span class="tag new">new</span>' if src_new else '<span class="tag game">from the game</span>'

    def table(head, rows):
        out.append("<table><tr>" + "".join(f"<th>{h}</th>" for h in head) + "</tr>")
        for r in rows:
            out.append('<tr class="row">' + "".join(f'<td class="k">{c}</td>' if i == 0 else f"<td>{c}</td>"
                                                    for i, c in enumerate(r)) + "</tr>")
        out.append("</table>")

    out.append("<h2 id='moves'>Moves and how each roll lands</h2><p class='muted'>Per move, per result: what happens "
               "to the message the player meant, how the match receives it, and how long it runs. The game's text is "
               "written as orders to its own one-message rewriter, which is why it says \"You must\" and fixes lengths.</p>")
    for name, m in t["moves"].items():
        out.append(f"<h3>{e(name)}: {e(m['means'])} <span class='muted'>Fails through: {e(str(m['shadow']))}</span></h3>")
        table(["Result", "What happens to the message " + tag(False), "How the match receives it " + tag(False),
               "Length " + tag(True)],
              [[e(k), e(v["message"]), e(v["received"]), e(v["length"])] for k, v in m["tiers"].items()])
    out.append("<h2 id='interest'>Interest levels</h2>")
    table(["Level", "What the match feels " + tag(False), "What the chat needs " + tag(True), "Length " + tag(True)],
          [[f"{e(s['state'])}<div class='src'>{s['from']}–{s['to']}</div>", e(s["match_feels"]), e(s["chat_needs"]),
            e(s["length"])] for s in t["interest"]])
    out.append("<h2 id='swings'>Interest swings in one turn " + tag(True) + "</h2>")
    table(["Swing", "What it means", "Length"],
          [[f"{e(s['name'])}<div class='src'>{s['from'] if s['from'] > -99 else '…'} to {s['to'] if s['to'] < 99 else '…'}</div>",
            e(s["means"]), e(s["length"])] for s in t["swings"]])
    out.append("<h2 id='beats'>Story beats</h2><p class='muted'>Chosen by the tone lab's story clock from the game so far.</p>")
    table(["Beat", "What the turn does (tone lab)", "Length " + tag(True)],
          [[e(k), e(v["story"]), e(v["length"])] for k, v in t["beats"].items()])
    out.append("<h2 id='shadows'>Shadows taking over " + tag(False) + "</h2>")
    for name, s in t["shadows"].items():
        out.append(f"<h3>{e(name)}</h3><p>{e(s['about'])}</p>")
        table(["Severity", "What happens to the message"],
              [[e(k), e(v)] for k, v in s.items() if k not in ("about", "source")])
    out.append("<h2 id='traps'>Traps " + tag(False) + "</h2>")
    table(["Trap", "What it is", "What it does to messages", "How it clears"],
          [[f"{e(v['name'])}<div class='src'>{e(v['stat'])}</div>", e(v["summary"]), e(v["effect"]), e(v["clears"])]
           for v in t["traps"].values()])
    out.append("<h2 id='thirst'>Thirsty slips " + tag(False) + "</h2>")
    table(["Severity", "What slips out", "How the match takes it"],
          [[e(k), e(v["message"]), e(v["received"])] for k, v in t["thirst"]["tiers"].items()])
    table(["Interest", "How the match takes it"],
          [["below the threshold", e(t["thirst"]["received_at_low_interest"])],
           ["high", e(t["thirst"]["received_at_high_interest"])]])
    body = "\n".join(out)
    nav = "".join(f"<a href='#{a}'>{b}</a>" for a, b in (("moves", "Moves"), ("interest", "Interest"), ("swings", "Swings"),
                                                         ("beats", "Beats"), ("shadows", "Shadows"), ("traps", "Traps"),
                                                         ("thirst", "Thirst")))
    script = ("<script>document.getElementById('q').addEventListener('input',function(){var q=this.value.toLowerCase();"
              "document.querySelectorAll('tr.row').forEach(function(r){r.style.display=!q||r.textContent.toLowerCase()"
              ".indexOf(q)>=0?'':'none'})})</script>")
    doc = (f"<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,"
           f"initial-scale=1'><title>Situation table</title><style>{CSS}</style></head><body><main>"
           f"<h1>Situation table</h1><p class='lead'>{e(t['about'])}</p><nav>{nav}</nav>"
           f"<input id='q' placeholder='Filter rows'>{body}</main>{script}</body></html>")
    os.makedirs(os.path.dirname(PAGE), exist_ok=True)
    with open(PAGE, "w", encoding="utf-8") as f:
        f.write(doc)
    print(os.path.relpath(PAGE, REPO))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sp = ap.add_subparsers(dest="cmd", required=True)
    sp.add_parser("seed").add_argument("--force", action="store_true")
    sp.add_parser("html")
    a = ap.parse_args()
    seed(a.force) if a.cmd == "seed" else page()


if __name__ == "__main__":
    main()
