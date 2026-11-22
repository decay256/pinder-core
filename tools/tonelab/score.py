#!/usr/bin/env python3
"""The tone lab's scorecard (v23).

A language model scores the lab's three kinds of work, the character profiles, the plans and
the chats, on the dimensions in scorecard.yaml. Every dimension scores 1 to 6 with no
midpoint, so each score leans one way: a 3 is below the bar that dimension writes as its
line, a 4 is above it. Every score comes with the reason, the quotes it rests on, and the
change that would raise it.

Where a judge who knows the answer would be the wrong reader (does a reader who sees only the
chat learn what Reuben does for a living?), a separate probe call sees only what that reader
would see, and the judge compares the probe's answer with the truth.

  score.py targets                  list what can be scored, with the material each one gets
  score.py run [--targets ...]      score (resumable: a score is kept until its dimension,
                                    its material or the judge changes; --force rescores)
  score.py page                     docs/tone-lab/scores/index.html, the explorer, and focus.html, the
                                    experiment's own lighter view
  score.py hosted --out FILE        the focus view for the hosted dashboard (republished after each run)
  score.py doc                      docs/tone-lab/scorecard.md, the catalogue in prose

The judge is the subscription Claude CLI by default (--judge claude --model opus), a
different model family from the writer the chats were made with; --judge openrouter takes
--model and --key-stdin like the other tools.
"""
import argparse
import concurrent.futures as cf
import datetime as dt
import hashlib
import json
import re
import sys
import threading
from pathlib import Path

import yaml

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import tonelab as tl  # noqa: E402
import profiles as pf  # noqa: E402

ROOT = HERE.parents[1]
CARD = HERE / "scorecard.yaml"
FOCUS = HERE / "focus.yaml"  # which dimensions decide the current experiment (shown in the explorer)
RUNS = ROOT / "docs" / "tone-lab" / "runs"
OUT = ROOT / "docs" / "tone-lab" / "scores"
DOC = ROOT / "docs" / "tone-lab" / "scorecard.md"
# The last version before the profile generator: its plan prompt holds the old character cards.
OLD_CARDS_RUN = "20261118T163642Z-preview-reuben-brick-s23"
PROFILE_SLUGS = ("reuben", "brick")
CHAT_FULL = 3          # the newest full chats
SEED_TAG = "-s23"      # plans: every live and preview run on seed 23 (other seeds repeat versions)

JUDGE_SYSTEM = """You score work from a comedy-writing lab against a scorecard.

{context}

Score each dimension you are given from 1 to 6. There is no midpoint:
{scale}

Decide the side first: does the work meet the bar the dimension writes under "Line"? If it does, the score is 4, 5 or 6; if not, 1, 2 or 3. Then decide how far from the line it is.

Be strict and specific. Rest every score on the work itself, and quote it; never praise in general terms. A dimension the work was not designed for is still scored on what is there: something missing is a 1, not a pass.

For each dimension return:
- "score": an integer 1 to 6, or null only when the material cannot show the dimension at all (for example the ending of a chat that stops after two turns); say why in "why"
- "why": two to four sentences: what earned this score, and what kept it from the next one up (for a 4, also what keeps it above the line)
- "evidence": one to three short verbatim quotes from the material (under 25 words each) that the score rests on
- "raise": one sentence: the change that would raise the score by one

Return only JSON, one entry per dimension in the order given:
{{"scores": [{{"id": "...", "score": 4, "why": "...", "evidence": ["..."], "raise": "..."}}]}}"""

# The lab as the judge is told about it, unless the family's own `context` in scorecard.yaml replaces it.
LAB_CONTEXT = ("The lab makes Pinder's chats: funny, filthy, satirical dating-app conversations between two adult characters (the cast are anthropomorphic genitals with human lives, jobs and flats), written turn by turn while the game's dice decide how each turn goes. What we want: the reader gets to know two people indirectly; each one is deciding whether the other is safe and fun for a night, or the vibe for a date; and the night reaches a moment where the guard comes down, or a decision about going home together.")

PROBE_SYSTEM = ("You are a reader, not an editor. Answer only from what you are shown, and say so where it "
                "does not tell you; never guess beyond it. Plain prose, no JSON, no preamble.")

_calls = threading.Semaphore(6)
_print = threading.Lock()


def say(*a):
    with _print:
        print(*a, flush=True)


def sha(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


def catalogue():
    cat = yaml.safe_load(CARD.read_text(encoding="utf-8"))
    for d in cat["dimensions"]:
        keys = {k: d.get(k) for k in ("name", "tells", "method", "low", "line", "high", "probe")}
        ctx = (cat["families"].get(d.get("family")) or {}).get("context")
        if ctx:  # a family with its own context: the judge reads the dimension through it
            keys["context"] = ctx
        d["sha"] = sha(json.dumps(keys, sort_keys=True))
    return cat


def problems_in(cat):
    """What is wrong with the catalogue itself (the selftest asserts there is nothing)."""
    out, seen = [], set()
    fams, mats = set(cat["families"]), set(cat["materials"])
    if sorted(cat["scale"]) != [1, 2, 3, 4, 5, 6]:
        out.append("scale must define 1 to 6")
    for d in cat["dimensions"]:
        i = d.get("id", "?")
        if i in seen:
            out.append(f"{i}: duplicate id")
        seen.add(i)
        if d.get("family") not in fams or not i.startswith(f"{d.get('family')}."):
            out.append(f"{i}: family")
        for k in ("name", "priority", "tells", "method", "low", "line", "high"):
            if not str(d.get(k) or "").strip():
                out.append(f"{i}: no {k}")
        if d.get("priority") not in ("core", "useful", "diagnostic"):
            out.append(f"{i}: priority")
        if d.get("scope") != "set" and not isinstance(d.get("batch"), int):
            out.append(f"{i}: batch")
        if "4" not in str(d.get("line")) or "3" not in str(d.get("line")):
            out.append(f"{i}: the line must say what separates a 3 from a 4")
        p = d.get("probe")
        if p and (not p.get("ask") or not set(p.get("sees") or []) <= mats or not p.get("sees")):
            out.append(f"{i}: probe")
        if not set(d.get("needs") or []) <= mats:
            out.append(f"{i}: needs")
        if d.get("scope") == "set" and d.get("probe"):
            out.append(f"{i}: a set dimension takes no probe")
    for f, s in cat["sees"].items():
        if not set(s) <= mats:
            out.append(f"sees.{f}")
    return out


# ── material ──────────────────────────────────────────────────────────────────

def git_subject(path):
    try:
        return tl.git("log", "-1", "--format=%s", "--", str(path)).strip()
    except Exception:
        return ""


def version_of(path):
    m = re.search(r"\bv(\d+)\b", git_subject(path))
    return f"v{m.group(1)}" if m else "v1"


def when(run_id):
    t = dt.datetime.strptime(run_id[:16], "%Y%m%dT%H%M%SZ")
    return t.strftime("%d %b %H:%M").lstrip("0")


def run_calls(d):
    if (d / "live_calls.json").exists():
        return json.loads((d / "live_calls.json").read_text(encoding="utf-8"))
    if (d / "preview.json").exists():
        return json.loads((d / "preview.json").read_text(encoding="utf-8")).get("calls") or []
    return []


def plan_call(d):
    for c in run_calls(d):
        if c.get("label") == "plan":
            return c
    return None


def plan_system(d, pc):
    """The planner's system prompt as that run sent it, checked against the hash the call recorded."""
    text = None
    if (d / "preview.json").exists():
        text = (json.loads((d / "preview.json").read_text(encoding="utf-8")).get("systems") or {}).get("plan")
    elif (d / "prompt_parts.json").exists():
        text = json.loads((d / "prompt_parts.json").read_text(encoding="utf-8")).get("system")
    want = (pc or {}).get("system_sha256")
    if text and want and hashlib.sha256(text.encode("utf-8")).hexdigest() != want:
        return None
    return text


def how_made(t):
    """What was sent to make a profile or a plan: the prompts in order, the model and the commit.

    A v22 profile comes from tools/tonelab/profiles.py with the template in profiles/generator.yaml;
    generation.json records every attempt, so this is the exact text, retries included. A plan is the
    planner's system prompt plus the brief it was given. A v21 card was not generated by the lab."""
    if t["kind"] == "profile":
        if t.get("variant"):
            record = Path(pf.DIR) / "variants" / t["variant"] / "generation.json"
            template = f"tools/tonelab/profiles/variants/{t['variant']}/generator.yaml"
        elif t["version"] == "v22":
            record, template = Path(pf.RECORD), "tools/tonelab/profiles/generator.yaml"
        else:
            return None
        if not record.exists():
            return None
        g = json.loads(record.read_text(encoding="utf-8"))["characters"].get(t["id"].split("-")[1])
        if not g:
            return None
        steps = [{"label": "The generator's system prompt", "text": g["system"]}]
        for i, a in enumerate(g.get("attempts") or []):
            steps.append({"label": "The request" if i == 0 else f"Sent back, attempt {i + 1}", "text": a["prompt"]})
            if a.get("problems"):
                steps[-1]["problems"] = a["problems"]
        n = len(g.get("attempts") or [])
        return {"model": g.get("model"), "made": (g.get("generated") or "")[:10], "commit": g.get("repo_commit"),
                "tool": f"tools/tonelab/profiles.py, template {template}",
                "note": f"{n} attempt{'s' if n != 1 else ''}: the tool checks each answer and sends the problems back.",
                "steps": steps}
    if t["kind"] == "plan":
        d = RUNS / t["run"]
        pc = plan_call(d)
        if not pc:
            return None
        sysp = plan_system(d, pc)
        steps = [{"label": "The planner's system prompt",
                  "text": sysp or "Not recorded for this run in a form that matches the hash of what was sent."},
                 {"label": "The brief: the cards and the dice for this match", "text": pc["user"]}]
        commit = None
        for f in ("preview.json", "run.json"):
            if (d / f).exists():
                commit = commit or json.loads((d / f).read_text(encoding="utf-8")).get("repo_commit")
        return {"model": pc.get("model"), "made": t.get("when"), "commit": commit, "tool": "tools/tonelab/tonelab.py",
                "note": None, "steps": steps}
    return None


def cards_from_brief(brief):
    """The two character cards in a plan prompt, split on their ### headings."""
    heads = list(re.finditer(r"(?m)^### (\S+), the (player|match)\b.*$", brief))
    out = {}
    for i, m in enumerate(heads):
        end = heads[i + 1].start() if i + 1 < len(heads) else len(brief)
        out[m.group(1).split("_")[0].lower()] = re.split(r"\n\s*Plan the match", brief[m.start():end])[0].strip()
    return out


def game_text(slug, chars, profs):
    """What the game's character file gives the character: the generator's input without its
    instructions about other characters."""
    _, v = pf.prompt_values(slug, chars, profs)
    return "\n".join([
        f"{v['name']} ({v['pronouns']}), level {v['level']}.",
        f"Bio, word for word: \"{v['bio']}\"",
        f"What they wear and own: {v['items']}",
        "Body (0 is the least anyone on the app has, 100 the most; extremes only):", v["body"],
        "Fragments from what they wear and their body:", v["fragments"],
        "How they text, from what they wear and their body:", v["texting_style"],
        "The drawer (private, filthy, embarrassing specifics):", v["drawer"],
        "What hurts them, as a clinician wrote it down:",
        f"- underneath: {v['wound']}", f"- how they defend against it: {v['defence']}",
        f"- what makes them feel safe: {v['safe_connection']}",
        "From the older generator, raw material (not binding):",
        f"Who they are: {v['personality']}", f"Backstory: {v['backstory']}", "Brags and truths:", v["pairs"],
    ])


def chat_parts(text):
    """[(turn header, [(speaker, message)])] from output.md."""
    turns, cur = [], None
    for ln in text.splitlines():
        if re.match(r"^Turn \d+\s*$", ln.strip()):
            cur = (ln.strip(), [])
            turns.append(cur)
        elif cur and (m := re.match(r"^([^:\n]{2,40}):\s?(.*)$", ln)):
            cur[1].append((m.group(1).strip(), m.group(2)))
        elif cur and ln.strip() and cur[1]:
            s, msg = cur[1][-1]
            cur[1][-1] = (s, msg + "\n" + ln)
    return turns


def masked(turns):
    out, k = [], 0
    for head, msgs in turns:
        out.append(head)
        for _, msg in msgs:
            k += 1
            out.append(f"[{k}] {msg}")
        out.append("")
    return "\n".join(out).strip()


def speakers(turns):
    return [s for _, msgs in turns for s, _ in msgs]


def dice_text(d, written=None):
    m = json.loads((d / "mechanics.json").read_text(encoding="utf-8"))
    lines = []
    for t in m["turns"]:
        r = t.get("roll") or {}
        flags = [x for x, k in (("natural 20", "nat20"), ("natural 1", "nat1")) if r.get(k)]
        h, s = t.get("horniness") or {}, t.get("shadow") or {}
        lines.append(
            f"Turn {t['turn']}: {t.get('chosen_stat')} rolled {r.get('total')} against {r.get('dc')}: "
            f"{'success' if r.get('success') else 'miss'} ({r.get('final_tier') or r.get('tier')}"
            f"{', ' + ', '.join(flags) if flags else ''}, risk {r.get('risk_tier')}); interest "
            f"{t.get('interest_before')} to {t.get('interest_after')} ({int(t.get('interest_delta') or 0):+d}), "
            f"{t.get('state_after')}; horniness check {h.get('tier')}; {s.get('shadow')} check {s.get('tier')}")
    lines.append(f"Outcome: {m.get('outcome')} after {m.get('turns_played')} turns.")
    if written:
        lines.append(f"Only turns 1 to {written} were written; the chat stops there.")
    return "\n".join(lines)


def variant_dirs():
    """Profile prompt variants: folders under profiles/variants/ with their own generator.yaml."""
    root = Path(pf.DIR) / "variants"
    return sorted(d for d in root.iterdir() if (d / "generator.yaml").exists()) if root.is_dir() else []


# The writers' runs on tone-lab-scratch: each writer wrote both characters, then a chat between them in
# one go, with no plan, no dice and no turns (tools/tonelab/writers.py). Folder, run number, what changed.
WRITERS_DIR = ROOT / "docs" / "tone-lab" / "scratch"
WRITER_RUNS = [
    ("writers", 1, "one line of persona, the game's material as inspiration"),
    ("writers-v2", 2, "the writer is the author and the authority"),
    ("writers-v3", 3, "the game's texting fragments removed; the writer develops the texting style"),
    ("writers-v4", 4, "run 3's prompts, given to six comedians"),
    ("writers-v5", 5, "the comedians' run 4 profiles; the chat held to 1,000 words"),
    ("writers-v6", 6, "the comedians' run 4 profiles; the chat held to 500 words"),
    ("writers-v7", 7, "three comedians with their own pitch; new profiles; the chat as a joke in 500 words"),
    ("writers-v8", 8, "run 7's profiles; the joke worked out first, the chat scored without it"),
    ("writers-v9", 9, "run 7's profiles; the brief cut to its what and why; one joke, one punchline"),
    ("writers-v10", 10, "new profiles and chat from the short brief; notes unsorted; the chat worked out as a bit"),
    ("writers-v11", 11, "each comedian's own brief; both characters in one short take; the chat set up and paid off"),
]
CHAT_PAIR = ("reuben", "brick")  # the player writes first


def char_name(slug):
    return json.loads((ROOT / "data" / "characters" / f"{slug}.json").read_text(encoding="utf-8"))["name"]


def writer_messages(text, names):
    """[(line index, speaker, message)] from a writer's chat: one "Name: message" per line, the name
    sometimes in bold, sometimes in another case ("reuben_404"). Headings, notes and stage directions
    are not messages."""
    pat = re.compile(r"^\s*(?:\*\*)?(" + "|".join(map(re.escape, names)) + r")(?:\*\*)?\s*:\s*(?:\*\*)?\s*(.*?)\s*$", re.I)
    canon = {n.lower(): n for n in names}
    out = []
    for i, ln in enumerate(text.splitlines()):
        m = pat.match(ln)
        if m and m.group(2).strip():
            out.append((i, canon[m.group(1).lower()], m.group(2).strip()))
    return out


def writer_targets():
    """A target for every writer's chat, and one set target per run that holds all of that run's chats."""
    names = [char_name(s) for s in CHAT_PAIR]
    out = []
    for folder, n, about in WRITER_RUNS:
        base = WRITERS_DIR / folder
        rec = base / "chat-reuben-brick" / "run.json"
        if not rec.exists():
            continue
        writers = json.loads(rec.read_text(encoding="utf-8")).get("writers") or {}
        done = []
        for wid, w in writers.items():
            if w.get("error"):
                continue
            # run 11 on: both characters were written together, in one text
            paired = (base / "characters" / f"{wid}.md").exists()
            paths = [base / "chat-reuben-brick" / f"{wid}.md"] + (
                [base / "characters" / f"{wid}.md"] if paired else [base / s / f"{wid}.md" for s in CHAT_PAIR])
            if not all(p.exists() for p in paths):
                continue
            text, *profiles = (p.read_text(encoding="utf-8").strip() for p in paths)
            msgs = writer_messages(text, names)
            if len(msgs) < 6:
                continue
            lines = text.splitlines()
            cut = len(msgs) - max(4, len(msgs) // 5)
            masked_text = "\n".join(f"[{k}] {m}" for k, (_, _, m) in enumerate(msgs, 1))
            brief = (f"=== {names[0]} and {names[1]}, as {w['writer']} wrote them ===\n{profiles[0]}" if paired else
                     "\n\n".join(f"=== {nm}, as {w['writer']} wrote them ===\n{p}" for nm, p in zip(names, profiles)))
            out.append({"id": f"chat-{folder}-{wid}", "kind": "chat", "label": f"Run {n} · {w['writer']}",
                        "version": f"run {n}", "order": 100 + n, "writer": wid, "writers_run": n,
                        "source": f"docs/tone-lab/scratch/{folder}/chat-reuben-brick/{wid}.md",
                        "note": (f"Written in one go by {w['writer']} from {'what they wrote about the two characters' if paired else 'the two profiles they wrote'} (run {n}: {about}). "
                                 "There is no plan, no dice and no turns: where a dimension speaks of turns, read exchanges."),
                        "speakers": [s for _, s, _ in msgs],
                        "material": {"chat": text, "brief": brief, "chat_masked": masked_text,
                                     "chat_but_last": "\n".join(lines[:msgs[cut][0]]).strip()}})
            done.append((w["writer"], text))
        if len(done) >= 2:
            out.append({"id": f"chatset-{folder}", "kind": "chat", "set": True, "label": f"Run {n} · all {len(done)} chats",
                        "version": f"run {n}", "order": 100 + n, "writers_run": n,
                        "source": f"docs/tone-lab/scratch/{folder}/chat-reuben-brick/",
                        "note": (f"Every chat of run {n} ({about}): the same pair, the same brief and the same prompt, "
                                 "each written by a different writer."),
                        "material": {"chats": "\n\n".join(f"=== The chat by {nm} ===\n{tx}" for nm, tx in done)}})
    return out


def targets():
    """Everything that can be scored, newest first within a kind, with its material."""
    profs = pf.read_profiles()
    chars = {s: pf.character(s) for s in pf.ORDER}
    out = []
    old_brief = (plan_call(RUNS / OLD_CARDS_RUN) or {}).get("user", "")
    old_cards = cards_from_brief(old_brief)
    for slug in PROFILE_SLUGS:
        game = game_text(slug, chars, profs)
        cast_now = "\n\n".join(f"## {s}\n{profs[s]}" for s in pf.ORDER if s != slug and s in profs)
        out.append({"id": f"profile-{slug}-v22", "kind": "profile", "label": f"{slug.title()} · v22 profile",
                    "version": "v22", "order": 22, "source": f"tools/tonelab/profiles/{slug}.md",
                    "material": {"profile": profs[slug], "game": game, "cast": cast_now}})
        for vd in variant_dirs():
            p = vd / f"{slug}.md"
            if not p.exists():
                continue
            meta = yaml.safe_load((vd / "generator.yaml").read_text(encoding="utf-8")) or {}
            v = str(meta.get("version") or vd.name)
            out.append({"id": f"profile-{slug}-{v}", "kind": "profile", "variant": vd.name,
                        "label": f"{slug.title()} · {v} {meta.get('label') or vd.name} profile", "version": v,
                        "order": int(v[1:]) if v[1:].isdigit() else 22,
                        "source": f"tools/tonelab/profiles/variants/{vd.name}/{slug}.md",
                        "note": f"Written by the {meta.get('label') or vd.name} variant of the profile prompt, from the "
                                "same character file and checked the same way; its cast is the v22 profiles.",
                        "material": {"profile": p.read_text(encoding="utf-8"), "game": game, "cast": cast_now}})
        if slug in old_cards:
            others = "\n\n".join(c for s, c in old_cards.items() if s != slug)
            out.append({"id": f"profile-{slug}-v21", "kind": "profile", "label": f"{slug.title()} · v21 card",
                        "version": "v21", "order": 21, "source": f"docs/tone-lab/runs/{OLD_CARDS_RUN}/preview.html",
                        "note": "The character card as the v21 planner saw it, before the profile generator. "
                                "Its cast is only the other card of the same match.",
                        "material": {"profile": old_cards[slug], "game": game, "cast": others}})
    runs = sorted((d for d in RUNS.iterdir() if d.is_dir() and SEED_TAG in d.name
                   and ("-live-" in d.name or "-preview-" in d.name)), key=lambda d: d.name, reverse=True)
    full, first = [], []
    for d in runs:
        pc = plan_call(d)
        if not pc:
            continue
        v = version_of(d)
        kind = "preview" if "-preview-" in d.name else ("first look" if "-first" in d.name else "full run")
        src = f"docs/tone-lab/runs/{d.name}/" + ("preview.html" if kind == "preview" else "report.html")
        base = {"version": v, "order": int(v[1:]), "run": d.name, "source": src, "when": when(d.name)}
        out.append(dict(base, id=f"plan-{d.name}", kind="plan", label=f"{v} · {when(d.name)} · {kind}",
                        material={"brief": pc["user"], "plan": pc["output"]}))
        if kind == "preview" or not (d / "output.md").exists():
            continue
        rec = json.loads((d / "run.json").read_text(encoding="utf-8"))
        (first if rec.get("stop_after") else full).append((d, rec, base, kind))
    for d, rec, base, kind in full[:CHAT_FULL] + first[:1]:
        text = (d / "output.md").read_text(encoding="utf-8").strip()
        turns = chat_parts(text)
        last = text.rfind("\nTurn ")
        written = rec.get("turns_written") if rec.get("stop_after") else None
        note = (f"This chat stops after turn {written} of a {rec.get('turns_played')}-turn game: score what the "
                f"written turns can show." if written else None)
        out.append(dict(base, id=f"chat-{d.name}", kind="chat", label=f"{base['version']} · {base['when']} · {kind}",
                        note=note, speakers=speakers(turns),
                        material={"chat": text, "plan": plan_call(d)["output"], "brief": plan_call(d)["user"],
                                  "dice": dice_text(d, written), "chat_but_last": text[:last].strip() if last > 0 else text,
                                  "chat_masked": masked(turns)}))
    return out + writer_targets()


# ── the judge ─────────────────────────────────────────────────────────────────

class JudgeError(Exception):
    pass


def make_caller(judge, model, key=None):
    def caller(system, user, label):
        with _calls:
            try:
                if judge == "claude":
                    data, secs = tl.call_claude(system, user, model)
                    return data.get("result") or "", {"seconds": secs, "cost": data.get("total_cost_usd") or 0}
                data, secs = tl.call_openrouter(system, user, model, key, {"reasoning": {"effort": "high"}})
                return (data["choices"][0]["message"].get("content") or ""), \
                    {"seconds": secs, "cost": (data.get("usage") or {}).get("cost") or 0}
            except SystemExit as e:
                raise JudgeError(f"{label}: the judge call failed ({e})")
    return caller


def sees_of(cat, t):
    """What the judge reads for this target: a set target reads its chats; the rest read their family's
    materials, without the ones this target has none of (a writer's chat has no plan and no dice)."""
    if t.get("set"):
        return ["chats"]
    return [m for m in cat["sees"][t["kind"]] if m in t["material"]]


def dims_for(cat, t):
    """The dimensions this target is scored on: set dimensions for a set target, the rest otherwise,
    without those whose `needs` the target lacks (skipped, not scored 1)."""
    return [d for d in cat["dimensions"] if d["family"] == t["kind"]
            and (d.get("scope") == "set") == bool(t.get("set"))
            and set(d.get("needs") or []) <= set(t["material"])]


def block(cat, t, names, kind_label=None):
    parts = []
    for n in names:
        title = cat["materials"][n]
        mark = " (the work being scored)" if n in (t["kind"], "chats") else ""
        parts.append(f"=== {n.upper().replace('_', ' ')}{mark}: {title} ===\n{t['material'][n].strip()}")
    return "\n\n".join(parts)


def dim_text(d):
    return (f"[{d['id']}] {d['name']} ({d['priority']})\nWhat it tells us: {d['tells']}\nHow to measure: {d['method']}\n"
            f"Low (1-2): {d['low']}\nLine (3 or 4): {d['line']}\nHigh (5-6): {d['high']}")


def judge_prompt(cat, t, dims, probes):
    parts = [block(cat, t, sees_of(cat, t))]
    if t.get("note"):
        parts.append("=== NOTE ===\n" + t["note"])
    pr = [f"For {d['id']}, a fresh reader who saw only {', '.join(d['probe']['sees'])} was asked:\n"
          f"{d['probe']['ask']}\nThey answered:\n{probes[d['id']]['answer'].strip()}"
          for d in dims if d.get("probe") and d["id"] in probes]
    if pr:
        parts.append("=== READER PROBES ===\n" + "\n\n".join(pr))
    parts.append(f"=== DIMENSIONS: score {cat['families'][t['kind']]['artefact']} ===\n\n"
                 + "\n\n".join(dim_text(d) for d in dims))
    return "\n\n".join(parts)


def judge_system(cat, kind=None):
    ctx = (cat["families"].get(kind) or {}).get("context") or LAB_CONTEXT
    return JUDGE_SYSTEM.format(context=ctx.strip(),
                               scale="\n".join(f"{k}. {v}" for k, v in sorted(cat["scale"].items())))


def parse_scores(text, dims):
    a, b = text.find("{"), text.rfind("}")
    if a < 0 or b < a:
        raise ValueError("no JSON object in the answer")
    data = json.loads(text[a:b + 1])
    got = {s.get("id"): s for s in data.get("scores") or []}
    out = {}
    for d in dims:
        s = got.get(d["id"])
        if not s:
            raise ValueError(f"no score for {d['id']}")
        sc = s.get("score")
        if sc is not None and (not isinstance(sc, int) or not 1 <= sc <= 6):
            raise ValueError(f"{d['id']}: score {sc!r} is not an integer 1 to 6")
        if not str(s.get("why") or "").strip():
            raise ValueError(f"{d['id']}: no reason given")
        ev = [str(e) for e in (s.get("evidence") or []) if str(e).strip()][:3]
        out[d["id"]] = {"score": sc, "why": s["why"].strip(), "evidence": ev, "raise": str(s.get("raise") or "").strip()}
    return out


def norm(s):
    return re.sub(r"[^a-z0-9]+", " ", s.lower().replace("’", "'")).strip()


def quote_found(q, material):
    """A quote counts as found when its words appear in order in some material (ellipses split it)."""
    hay = norm(" ".join(material.values()))
    pieces = [norm(p) for p in re.split(r"\.\.\.|…", q) if len(norm(p).split()) >= 2]
    return bool(pieces) and all(p in hay for p in pieces)


# ── scoring ───────────────────────────────────────────────────────────────────

def result_path(t):
    return OUT / f"{t['id']}.json"


def load_result(t):
    p = result_path(t)
    return json.loads(p.read_text(encoding="utf-8")) if p.exists() else {}


def save_result(t, rec):
    OUT.mkdir(parents=True, exist_ok=True)
    result_path(t).write_text(json.dumps(rec, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")


def score_target(t, cat, caller, judge_id, force=False):
    dims = dims_for(cat, t)
    msha = sha(json.dumps(t["material"], sort_keys=True))
    rec = {} if force else load_result(t)
    rec.update({k: t.get(k) for k in ("id", "kind", "label", "version", "order", "run", "source", "note", "when",
                                      "writer", "writers_run", "set")})
    rec["materials"] = {k: {"chars": len(v), "words": len(v.split())} for k, v in t["material"].items()}
    rec.setdefault("scores", {})
    rec.setdefault("probes", {})
    rec.setdefault("calls", [])
    lock = threading.Lock()

    def fresh(d):
        s = rec["scores"].get(d["id"])
        return bool(s) and s.get("def") == d["sha"] and s.get("material") == msha and s.get("judge") == judge_id

    todo = [d for d in dims if not fresh(d)]
    kept = dict(t["material"], **{f"probe {k}": v["answer"] for k, v in rec["probes"].items()})
    for s in rec["scores"].values():  # the quote check is cheap, so it is always redone
        s["found"] = [quote_found(q, kept) for q in s.get("evidence") or []]
    if not todo:
        save_result(t, rec)
        say(f"[{t['id']}] up to date")
        return rec

    def probe(d):
        p = d["probe"]
        user = block(cat, t, p["sees"]) + "\n\n=== QUESTION ===\n" + p["ask"]
        text, meta = caller(PROBE_SYSTEM, user, f"{t['id']} probe {d['id']}")
        with lock:
            rec["probes"][d["id"]] = {"sees": p["sees"], "ask": p["ask"], "answer": text.strip()}
            rec["calls"].append(dict(meta, label=f"probe {d['id']}"))
        say(f"[{t['id']}] probe {d['id']} {meta['seconds']}s")

    with cf.ThreadPoolExecutor(4) as ex:
        list(ex.map(probe, [d for d in todo if d.get("probe")]))

    batches = {}
    for d in todo:
        batches.setdefault(d.get("batch", 0), []).append(d)

    def run_batch(item):
        b, ds = item
        user = judge_prompt(cat, t, ds, rec["probes"])
        system = judge_system(cat, t["kind"])
        for attempt in (1, 2):
            text, meta = caller(system, user, f"{t['id']} batch {b}")
            with lock:
                rec["calls"].append(dict(meta, label=f"batch {b}" + (" retry" if attempt == 2 else "")))
            try:
                got = parse_scores(text, ds)
                break
            except (ValueError, json.JSONDecodeError) as e:
                if attempt == 2:
                    raise JudgeError(f"{t['id']} batch {b}: {e}")
                user = user + f"\n\n=== YOUR LAST ANSWER COULD NOT BE USED ===\n{e}. Return only the JSON."
        with lock:
            seen = dict(t["material"], **{f"probe {k}": v["answer"] for k, v in rec["probes"].items()})
            for d in ds:
                s = got[d["id"]]
                s.update({"def": d["sha"], "material": msha, "judge": judge_id, "batch": b,
                          "found": [quote_found(q, seen) for q in s["evidence"]]})
                rec["scores"][d["id"]] = s
            save_result(t, rec)
        say(f"[{t['id']}] batch {b} ({len(ds)}) {meta['seconds']}s")

    errors = []
    with cf.ThreadPoolExecutor(4) as ex:
        for f in [ex.submit(run_batch, it) for it in sorted(batches.items())]:
            try:
                f.result()
            except JudgeError as e:
                errors.append(str(e))
    rec["judge"] = judge_id
    rec["scored_utc"] = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    rec["repo_commit"] = tl.git("rev-parse", "--short", "HEAD").strip()
    rec["cost"] = round(sum(c.get("cost") or 0 for c in rec["calls"]), 4)
    save_result(t, rec)
    if errors:
        raise JudgeError("; ".join(errors))
    return rec


def summary(rec, cat):
    pri = {d["id"]: d["priority"] for d in cat["dimensions"]}
    vals = [(pri.get(k), v["score"]) for k, v in rec.get("scores", {}).items() if v.get("score") is not None]
    core = [s for p, s in vals if p == "core"]
    mean = lambda xs: round(sum(xs) / len(xs), 2) if xs else None  # noqa: E731
    return {"core": mean(core), "all": mean([s for _, s in vals]), "below": sum(1 for _, s in vals if s <= 3),
            "scored": len(vals)}


def cmd_run(args):
    cat = catalogue()
    bad = problems_in(cat)
    if bad:
        tl.die("the scorecard has problems: " + "; ".join(bad))
    ts = targets()
    if args.targets:
        want = args.targets.split(",")
        ts = [t for t in ts if t["kind"] in want or t["id"] in want]
    if not ts:
        tl.die("nothing to score")
    ts.sort(key=lambda t: ("profile", "chat", "plan").index(t["kind"]))
    key = None
    if args.judge == "openrouter":
        key = sys.stdin.readline().strip().lstrip("﻿") if args.key_stdin else None
        if not key:
            tl.die("openrouter needs --key-stdin")
    model = args.model or ("opus" if args.judge == "claude" else "google/gemini-4-argon")
    judge_id = f"{args.judge}:{model}"
    caller = make_caller(args.judge, model, key)
    say(f"scoring {len(ts)} targets with {judge_id}")
    failed = []
    with cf.ThreadPoolExecutor(args.workers) as ex:
        futs = {ex.submit(score_target, t, cat, caller, judge_id, args.force): t for t in ts}
        for f in cf.as_completed(futs):
            t = futs[f]
            try:
                rec = f.result()
                say(f"done {t['id']}: {summary(rec, cat)}")
            except JudgeError as e:
                failed.append(t["id"])
                say(f"FAILED {t['id']}: {e}")
    write_page(cat)
    write_doc(cat)
    say(f"page {OUT / 'index.html'}")
    if failed:
        say(f"{len(failed)} targets incomplete (rerun to fill them): {', '.join(failed)}")
        return 1
    return 0


def cmd_targets(args):
    cat = catalogue()
    for t in targets():
        mats = ", ".join(f"{k} {len(v.split())}w" for k, v in t["material"].items())
        say(f"{t['id']} | {t['label']} | {mats}")
    bad = problems_in(cat)
    say("scorecard ok" if not bad else "scorecard problems: " + "; ".join(bad))


# ── the catalogue in prose, and the explorer ──────────────────────────────────

def write_doc(cat):
    L = ["# The tone lab's scorecard", "",
         "Candidate metrics for the three kinds of work the lab produces: the character profile, the plan and the chat. "
         "Generated from `tools/tonelab/scorecard.yaml` by `score.py doc`; edit the YAML, not this file.", "",
         "Every dimension scores 1 to 6. There is no midpoint, so a score has to lean: 3 is below the bar and 4 is above "
         "it. Each dimension writes its own bar as **Line**.", ""]
    L += [f"- **{k}**: {v}" for k, v in sorted(cat["scale"].items())]
    L += ["", "**Core** dimensions are the ones we need to know whether the product works, **useful** ones steer prompt "
          "work, **diagnostic** ones explain a failure once we know there is one. A dimension with a **probe** is "
          "measured through a separate reader who sees only part of the material, so the judge is not the one who "
          "already knows the answer.", ""]
    for fam, f in cat["families"].items():
        L += [f"## {f['title']}", "", f"Scored: {f['artefact']}. The judge sees: "
              + ", ".join(cat["materials"][m] for m in cat["sees"][fam]) + ".", ""]
        for d in [d for d in cat["dimensions"] if d["family"] == fam]:
            tag = d["priority"] + (", scored on a set of chats or runs at once, not per artefact" if d.get("scope") == "set" else "")
            if d.get("needs"):
                tag += ", only where there is a " + " and a ".join(d["needs"])
            L += [f"### {d['name']} ({tag})", "", d["tells"], ""]
            if d.get("source"):
                L.append(f"- **From:** {d['source']}")
            L.append(f"- **How:** {d['method']}")
            if d.get("probe"):
                L.append(f"- **Probe** (sees {', '.join(d['probe']['sees'])}): {d['probe']['ask']}")
            L += [f"- **Low:** {d['low']}", f"- **Line:** {d['line']}", f"- **High:** {d['high']}", ""]
    DOC.write_text("\n".join(L).rstrip() + "\n", encoding="utf-8")


def page_data(cat):
    results = []
    for p in sorted(OUT.glob("*.json")):
        r = json.loads(p.read_text(encoding="utf-8"))
        r["summary"] = summary(r, cat)
        results.append(r)
    # The work itself, so the page can show a profile or plan beside its scores. Briefs, game files and
    # chats stay out: the brief alone is larger than every plan together, and the chat tab is off.
    ts = [t for t in targets() if t["kind"] in ("profile", "plan")]
    texts = {t["id"]: t["material"][t["kind"]] for t in ts}
    prompts = {t["id"]: p for t in ts if (p := how_made(t))}
    for r in results:
        if r.get("panel_of") in texts:
            texts[r["id"]] = texts[r["panel_of"]]
        if r.get("panel_of") in prompts:
            prompts[r["id"]] = prompts[r["panel_of"]]
    focus = yaml.safe_load(FOCUS.read_text(encoding="utf-8")) if FOCUS.exists() else None
    data = {"scale": cat["scale"], "families": cat["families"], "materials": cat["materials"],
            "dimensions": [{k: d.get(k) for k in ("id", "family", "name", "priority", "tells", "method", "low", "line",
                                                  "high", "probe", "scope", "batch", "proposed", "source", "needs")}
                           for d in cat["dimensions"]],
            "results": results, "texts": texts, "prompts": prompts, "focus": focus, "lean": False,
            "built": dt.datetime.now(dt.timezone.utc).strftime("%d %b %Y %H:%M UTC")}
    return data, focus


def render_page(d):
    blob = json.dumps(d, ensure_ascii=False).replace("</", "<\\/")
    return (HERE / "score_page.html").read_text(encoding="utf-8").replace("/*DATA*/null", blob)


def write_page(cat):
    OUT.mkdir(parents=True, exist_ok=True)
    data, focus = page_data(cat)
    (OUT / "index.html").write_text(render_page(data), encoding="utf-8")
    if focus:
        (OUT / "focus.html").write_text(render_page(lean_data(data, focus)), encoding="utf-8")


def write_hosted(cat, out):
    """The focus page as a hosted page (a private claude.ai artifact) that stays open and is republished
    after each scoring run. The host supplies the document shell, so this writes the title, styles, markup
    and script only, and the page drops its links into the repository, which the host cannot reach."""
    data, focus = page_data(cat)
    if not focus:
        raise SystemExit("no focus.yaml: the hosted page is the focus page")
    html = render_page(dict(lean_data(data, focus), hosted=True))
    head = html.split("<head>", 1)[1].split("</head>", 1)[0]
    body = html.split("<body>", 1)[1].rsplit("</body>", 1)[0]
    head = re.sub(r'<meta (charset|name="viewport")[^>]*>\s*', "", head)
    Path(out).write_text(head.strip() + "\n" + body.strip() + "\n", encoding="utf-8")


def lean_data(data, focus):
    """The focus page: full reasoning and text only for the work in the experiment, numbers for the rest.

    The full explorer carries every score's reasoning and every text, about a megabyte, which is more than
    the app's file viewer opens. The experiment needs the detail only for its own work and its references;
    older versions keep their scores so the grid and the trend still show them."""
    keep = set(focus.get("reference") or []) | {p["id"] for p in focus.get("planned") or []}
    keep |= {r["id"] for r in data["results"] if r["kind"] in (focus.get("detail") or [])}
    off = set(focus.get("disabled") or [])
    slim_keys = ("id", "kind", "label", "version", "order", "run", "source", "when", "note", "summary", "judge")
    results = []
    for r in data["results"]:
        if r["kind"] in off:
            continue
        if r["id"] in keep:
            results.append(r)
        else:
            results.append(dict({k: r[k] for k in slim_keys if k in r}, slim=True,
                                scores={i: {"score": s.get("score")} for i, s in (r.get("scores") or {}).items()}))
    return dict(data, results=results, texts={i: t for i, t in data["texts"].items() if i in keep},
                prompts={i: p for i, p in data["prompts"].items() if i in keep}, lean=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("--targets", help="comma list of kinds (profile, plan, chat) or target ids")
    r.add_argument("--judge", choices=["claude", "openrouter"], default="claude")
    r.add_argument("--model")
    r.add_argument("--key-stdin", action="store_true")
    r.add_argument("--force", action="store_true")
    r.add_argument("--workers", type=int, default=4)
    sub.add_parser("targets")
    sub.add_parser("page")
    sub.add_parser("doc")
    h = sub.add_parser("hosted", help="write the focus page for hosting outside the repository")
    h.add_argument("--out", required=True)
    a = ap.parse_args()
    if a.cmd == "run":
        return cmd_run(a)
    if a.cmd == "targets":
        return cmd_targets(a)
    if a.cmd == "hosted":
        write_hosted(catalogue(), a.out)
        return 0
    cat = catalogue()
    write_page(cat) if a.cmd == "page" else write_doc(cat)
    return 0


if __name__ == "__main__":
    sys.exit(main())
