#!/usr/bin/env python3
"""Pinder tone lab: write a whole simulated chat in one model call, on the game's real dice.

The split is deliberate:

* Rules are not in this file. The session runner plays the session with the real
  GameSession and no model (--mechanics-only) and writes every turn's rolls, checks,
  traps and interest as JSON. Changing a prompt can never change a die.
* Everything that shapes the writing lives in a variant directory
  (tools/tonelab/variants/<name>/): the prompt, the character card, the per-turn
  brief, the system prompt and a few numeric knobs. Copy a variant to experiment.
* Every run is saved with the exact prompt, the mechanics, the output and a record of
  what produced it, including the model the CLI reports it actually used.

Usage:
  tools/tonelab/tonelab.py run --player reuben --datee brick --seed 42
  tools/tonelab/tonelab.py run --variant my-variant --seed 7 --dry-run
"""

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import string
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
VARIANTS = os.path.join(HERE, "variants")
RUNS = os.path.join(REPO, "docs", "tone-lab", "runs")
RUNNER_PROJECT = os.path.join(REPO, "session-runner", "session-runner.csproj")
RUNNER_DLL = os.path.join(REPO, "session-runner", "bin", "Release", "net8.0", "session-runner.dll")
CONSEQUENCES = os.path.join(REPO, "data", "i18n", "en", "consequences.yaml")
ANATOMY_FILE = os.path.join(REPO, "data", "anatomy", "anatomy-parameters.json")
ITEMS_FILE = os.path.join(REPO, "data", "items", "starter-items.json")
VARIANT_FILES = ("system.md", "prompt.md", "character.md", "turn.md")
DEFAULT_CLOCK = "2026-11-14T23:30:00Z"  # late night: the game's highest horniness band


def die(msg):
    sys.exit(f"tonelab: {msg}")


def sha256(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def git(*args):
    res = subprocess.run(["git", "-C", REPO, *args], capture_output=True, text=True)
    return res.stdout.strip()


def model_free_env():
    """Environment for child processes: never pass API credentials on."""
    env = dict(os.environ)
    for key in ("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "OPENROUTER_API_KEY"):
        env.pop(key, None)
    return env


# ── mechanics: from the engine, never computed here ───────────────────────────

def play_mechanics(player, datee, seed, turns, clock, out_path):
    build = subprocess.run(
        ["dotnet", "build", RUNNER_PROJECT, "-c", "Release", "-nologo", "-v", "q"],
        capture_output=True, text=True, env=model_free_env())
    if build.returncode != 0:
        die("session-runner build failed:\n" + build.stdout[-3000:] + build.stderr[-2000:])
    with tempfile.TemporaryDirectory(prefix="tonelab-playtests-") as playtests:
        env = model_free_env()
        env["PINDER_PLAYTESTS_PATH"] = playtests  # keep the runner's own transcript out of the repo
        res = subprocess.run(
            ["dotnet", RUNNER_DLL, "--mechanics-only", "--player", player, "--datee", datee,
             "--seed", str(seed), "--turns", str(turns), "--clock", clock,
             "--mechanics-out", out_path],
            capture_output=True, text=True, env=env, timeout=600)
    if res.returncode != 0 or not os.path.exists(out_path):
        die(f"session-runner --mechanics-only failed (exit {res.returncode}):\n{res.stderr[-3000:]}")
    with open(out_path, encoding="utf-8") as f:
        return json.load(f)


# ── game text the per-turn brief reuses ───────────────────────────────────────

def load_consequences():
    """Flat 'consequence.*' strings from the game's i18n catalog (simple key: "value" lines)."""
    out = {}
    with open(CONSEQUENCES, encoding="utf-8") as f:
        for line in f:
            m = re.match(r'\s*(consequence\.[\w.]+):\s*"(.*)"\s*$', line)
            if m:
                out[m.group(1)] = m.group(2)
    return out


# ── rendering ─────────────────────────────────────────────────────────────────

def render(template, values, where):
    try:
        return string.Template(template).substitute(values)
    except KeyError as e:
        die(f"{where}: unknown placeholder ${{{e.args[0]}}}; available: {', '.join(sorted(values))}")
    except ValueError as e:
        die(f"{where}: {e}")


def _load_json(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _band(param, value):
    for b in param.get("bands") or []:
        if b["lower"] <= value <= b["upper"]:
            return b
    return None


# Plain words for the game's three latinate anatomy slider names (a gloss, not game data).
ANATOMY_GLOSS = {"prepucius": "foreskin", "arrugatis": "wrinkling", "gravitatis": "hang"}


def body_lines(c):
    """The extremes of a character's anatomy, from the game's own slider bands: only
    sliders in the bottom or top fifth of their range, skin colour left out."""
    anatomy = c.get("anatomy") or {}
    out = []
    for p in _load_json(ANATOMY_FILE):
        pid = p["id"]
        if pid not in anatomy or pid.startswith("skin") or pid == "isCircumcised":
            continue
        v = anatomy[pid]
        b = _band(p, v)
        if b is None or 0.2 <= v <= 0.8:
            continue
        name = ANATOMY_GLOSS.get(pid, p["name"].lower())
        out.append(f"- {name}: {round(b['lower'] * 100)}-{round(b['upper'] * 100)}")
    if "isCircumcised" in anatomy:
        out.append("- circumcised" if anatomy["isCircumcised"] else "- uncircumcised")
    return out


def texting_style_lines(c):
    """Texting-style lines from the character's items and anatomy bands, deduplicated.
    The game resolves conflicts between them in TextingStyleAggregator; this does not."""
    items = {i["id"]: i for i in _load_json(ITEMS_FILE)}
    frags = [(items.get(i) or {}).get("texting_style_fragment") or "" for i in c.get("items") or []]
    anatomy = c.get("anatomy") or {}
    for p in _load_json(ANATOMY_FILE):
        if p["id"] in anatomy:
            b = _band(p, anatomy[p["id"]])
            frags.append((b or {}).get("texting_style_fragment") or "")
    seen, out = set(), []
    for frag in frags:
        for line in frag.splitlines():
            line = line.strip()
            if line.startswith("- ") and line not in seen:
                seen.add(line)
                out.append(line)
    return out


def character_values(slug, engine_side, knobs):
    with open(os.path.join(REPO, "data", "characters", f"{slug}.json"), encoding="utf-8") as f:
        c = json.load(f)
    cats = c.get("backstory_categories") or {}
    wanted = knobs.get("secret_categories") or list(cats)[: int(knobs.get("secrets", 4))]
    secrets = []
    for key in wanted:
        pair = cats.get(key)
        if not pair:
            die(f"{slug}: no backstory category '{key}'")
        secrets.append(f"- says: {pair['bio_lie']}\n  truth: {pair['tragic_reality']}")
    stake_lines = (c.get("stake_lines") or [])[: int(knobs.get("stake_lines", 2))]
    diag = c.get("psychiatric_diagnosis") or {}
    stats = engine_side["effective_stats"]
    values = {
        "name": engine_side["display_name"],
        "pronouns": c.get("gender_identity", ""),
        "bio": c.get("bio", ""),
        "level": c.get("level", ""),
        "stats": ", ".join(f"{k} {v}" for k, v in stats.items()),
        "personality": c.get("consolidated_personality", ""),
        "backstory": c.get("consolidated_backstory", ""),
        "secrets": "\n".join(secrets),
        "stake_lines": "\n".join(f"- {s}" for s in stake_lines),
        "wound": diag.get("derived_feeling", ""),
        "defence": diag.get("defense_reaction", ""),
        "safe_connection": diag.get("safe_connection", ""),
        "reactions": "\n".join(f"- {stat}: {diag[key + '_reaction']}" for stat, key in STAT_KEY.items()
                               if diag.get(key + "_reaction")),
        "body": "\n".join(body_lines(c)),
        "texting_style": "\n".join(texting_style_lines(c)) or "- nothing distinctive",
        "drawer": (c.get("psychological_stake") or "").strip(),
    }
    # A variant may add or override per-character text (variant.json "characters": {slug: {...}}).
    values.update((knobs.get("characters") or {}).get(slug) or {})
    return c, diag, values


STAT_KEY = {"Charm": "charm", "Rizz": "rizz", "Honesty": "honesty", "Chaos": "chaos",
            "Wit": "wit", "SelfAwareness": "self_awareness"}


def turn_values(t, cons, names, datee_diag, is_last, outcome):
    r = t["roll"] or {}
    stat = t["chosen_stat"]
    tier = (r.get("final_tier") or r.get("tier") or "").lower()
    if r.get("success"):
        result = "SUCCESS" + (" (natural 20)" if r.get("nat20") else "") + ". " + \
            cons.get("consequence.roll.pass", "").replace("{stat}", stat)
    else:
        text = cons.get(f"consequence.roll.miss.{tier}", f"the roll missed ({tier})")
        result = f"MISS, failure tier {r.get('tier')}" + (" (natural 1)" if r.get("nat1") else "") + \
            ". " + text.replace("{stat}", stat)

    checks = []
    h = t.get("horniness")
    if h and h.get("miss"):
        checks.append(cons.get(f"consequence.horniness.miss.{h['tier'].lower()}",
                               "Horniness miss: a thirsty follow-up slips out.") +
                      f" (horniness roll {h['total']} vs DC {h['dc']})")
    s = t.get("shadow")
    if s and s.get("miss"):
        checks.append(cons.get(f"consequence.shadow.miss.{s['shadow'].lower()}",
                               f"The {s['shadow']} shadow took over.") +
                      f" (shadow roll {s['roll']} vs DC {s['dc']})")
    st = t.get("steering")
    if st and st["succeeded"]:  # a miss appends nothing, so it gives the writer nothing to show
        checks.append("Steering succeeded: the delivered message ends with a question that steers toward meeting up.")
    before, after = set(t.get("active_traps_before") or []), set(t.get("active_traps_after") or [])
    for trap in sorted(after - before):
        checks.append(f"Trap armed: {trap}. The player is now stuck in this pattern until it clears.")
    if t.get("trap_cleared"):
        checks.append(f"Trap cleared: {t['trap_cleared']}.")
    if t.get("combo"):
        checks.append(f"Combo: {t['combo']}.")
    if is_last and outcome:
        checks.append({"DateSecured": f"Interest reaches 25: {names['datee']} agrees to meet.",
                       "Unmatched": f"Interest hits 0: {names['datee']} unmatches.",
                       "Ghosted": f"{names['datee']} ghosts: no reply comes after this message."}.get(outcome, outcome))
    elif t["interest_after"] >= 21:
        checks.append(f"Interest is {t['interest_after']}, still below 25: {names['datee']} will not agree to meet yet.")

    delta = t["interest_delta"]
    return {
        "n": t["turn"],
        "stat": stat,
        "options": ", ".join(t["options"]),
        "die": r.get("used_die", ""),
        "modifier": (r.get("stat_modifier", 0) or 0) + (r.get("level_bonus", 0) or 0) + (r.get("external_bonus", 0) or 0),
        "total": r.get("total", ""),
        "dc": r.get("dc", ""),
        "result": result,
        "checks": "\n".join(f"- {c}" for c in checks) if checks else "- nothing else fires",
        "interest_before": t["interest_before"],
        "interest_after": t["interest_after"],
        "delta": f"{delta:+d}",
        "state_after": t["state_after"],
        "datee_stat_reaction": datee_diag.get(f"{STAT_KEY.get(stat, stat.lower())}_reaction", ""),
        "player_name": names["player"],
        "datee_name": names["datee"],
    }


def outcome_line(mech, names):
    n, interest, outcome = mech["turns_played"], mech["final_interest"], mech.get("outcome")
    return {
        "DateSecured": f"{names['datee']} agrees to meet on turn {n}.",
        "Unmatched": f"{names['datee']} unmatches on turn {n}.",
        "Ghosted": f"{names['datee']} ghosts after turn {n}.",
    }.get(outcome, f"The chat stops after turn {n} with interest at {interest}: no date yet, nothing resolved.")


def arc_beats(mech, texts):
    """Where each turn falls in the story, read off the engine's interest path: the
    biggest rise is the breakthrough, the biggest drop the crisis, the turn after a
    crisis that recovers the repair; other turns by interest level. The wording comes
    from the variant (variant.json "beats"); without it every beat is empty."""
    turns = mech["turns"]
    if not texts or not turns:
        return [""] * len(turns)
    deltas = [t["interest_after"] - t["interest_before"] for t in turns]
    keys = []
    for t in turns:
        lvl = t["interest_before"]
        keys.append("early" if lvl < 12 else "warming" if lvl < 18 else "close")
    rise = max(range(len(turns)), key=lambda i: deltas[i])
    drop = min(range(len(turns)), key=lambda i: deltas[i])
    if deltas[rise] >= 3:
        keys[rise] = "breakthrough"
    if deltas[drop] <= -2:
        keys[drop] = "crisis"
        if drop + 1 < len(turns) and deltas[drop + 1] > 0:
            keys[drop + 1] = "repair"
    keys[0] = "opener"
    keys[-1] = "ending"
    return [texts.get(k, "") for k in keys]


def build_prompt(variant_dir, knobs, mech, player, datee):
    files = {}
    for name in VARIANT_FILES:
        path = os.path.join(variant_dir, name)
        if not os.path.exists(path):
            die(f"variant {variant_dir} is missing {name}")
        with open(path, encoding="utf-8") as f:
            files[name] = f.read()

    names = {"player": mech["player"]["display_name"], "datee": mech["datee"]["display_name"]}
    _, _, pv = character_values(player, mech["player"], knobs)
    _, datee_diag, dv = character_values(datee, mech["datee"], knobs)
    player_card = render(files["character.md"], {**pv, "role": "the player"}, "character.md")
    datee_card = render(files["character.md"], {**dv, "role": "the match"}, "character.md")

    cons = load_consequences()
    turns = mech["turns"]
    beats = arc_beats(mech, knobs.get("beats"))
    briefs = [render(files["turn.md"],
                     {**turn_values(t, cons, names, datee_diag, i == len(turns) - 1, mech.get("outcome")),
                      "beat": beats[i]},
                     "turn.md") for i, t in enumerate(turns)]
    path = [mech["starting_interest"]] + [t["interest_after"] for t in turns]
    values = {
        "player_name": names["player"],
        "datee_name": names["datee"],
        "player_card": player_card.strip(),
        "datee_card": datee_card.strip(),
        "turns": "\n\n".join(b.strip() for b in briefs),
        "turns_played": mech["turns_played"],
        "interest_path": " → ".join(str(x) for x in path),
        "low_point": min(path),
        "high_point": max(path),
        "final_interest": mech["final_interest"],
        "outcome_line": outcome_line(mech, names),
        "time_of_day": mech.get("time_of_day", ""),
    }
    system_prompt = render(files["system.md"], values, "system.md").strip()
    prompt = render(files["prompt.md"], values, "prompt.md").strip() + "\n"
    # The pieces the report shows, so each turn can be read next to the brief it came from.
    parts = {"system": system_prompt, "user": prompt, "player_card": values["player_card"],
             "datee_card": values["datee_card"], "turn_briefs": [b.strip() for b in briefs]}
    return files, system_prompt, prompt, parts


# ── model call ────────────────────────────────────────────────────────────────

def call_claude(system_prompt, prompt, model):
    """One print-mode call through the subscription CLI: no tools, no settings files, a
    neutral working directory, so nothing but this prompt reaches the model."""
    with tempfile.TemporaryDirectory(prefix="tonelab-cwd-") as cwd:
        cmd = ["claude", "-p", "--model", model, "--output-format", "json",
               "--tools", "", "--setting-sources", "", "--system-prompt", system_prompt]
        started = time.time()
        res = subprocess.run(cmd, input=prompt, capture_output=True, text=True, cwd=cwd,
                             env=model_free_env(), timeout=1800)
    if res.returncode != 0:
        die(f"claude exited {res.returncode}: {res.stderr[-2000:]}{res.stdout[-2000:]}")
    try:
        data = json.loads(res.stdout)
    except json.JSONDecodeError:
        die("claude did not return JSON:\n" + res.stdout[-2000:])
    if data.get("is_error"):
        die("claude reported an error: " + str(data.get("result"))[:2000])
    return data, round(time.time() - started, 1)


OPENROUTER_URL = "https://openrouter.ai/api/v1/chat/completions"


def call_openrouter(system_prompt, prompt, model, key, knobs):
    """One chat-completions call through OpenRouter. Optional knobs passed through
    unchanged: temperature, top_p, max_tokens, reasoning."""
    body = {"model": model,
            "messages": [{"role": "system", "content": system_prompt},
                         {"role": "user", "content": prompt}]}
    for k in ("temperature", "top_p", "max_tokens", "reasoning"):
        if k in knobs:
            body[k] = knobs[k]
    req = urllib.request.Request(OPENROUTER_URL, data=json.dumps(body).encode("utf-8"),
                                 headers={"Authorization": f"Bearer {key}",
                                          "Content-Type": "application/json"})
    started = time.time()
    try:
        with urllib.request.urlopen(req, timeout=900) as resp:
            data = json.load(resp)
    except urllib.error.HTTPError as e:
        die(f"openrouter HTTP {e.code}: {e.read().decode('utf-8', 'replace')[:2000]}")
    if data.get("error"):
        die(f"openrouter error: {json.dumps(data['error'])[:2000]}")
    return data, round(time.time() - started, 1)


def format_checks(output, names, turns_played):
    markers = re.findall(r"(?m)^\s*\**Turn (\d+)\**\s*$", output)
    return {
        "turn_markers": len(markers),
        "turn_markers_match": [int(m) for m in markers] == list(range(1, turns_played + 1)),
        "has_table": bool(re.search(r"(?m)^\s*\|.*\|\s*$", output)),
        "has_heading": bool(re.search(r"(?m)^\s*#", output)),
        "player_lines": len(re.findall(rf"(?m)^\s*\**{re.escape(names['player'])}\**:", output)),
        "datee_lines": len(re.findall(rf"(?m)^\s*\**{re.escape(names['datee'])}\**:", output)),
        "words": len(output.split()),
    }


# ── command ───────────────────────────────────────────────────────────────────

def cmd_run(args):
    variant_dir = os.path.join(VARIANTS, args.variant)
    if not os.path.isdir(variant_dir):
        die(f"no variant '{args.variant}' in {VARIANTS}")
    knobs = {}
    knobs_path = os.path.join(variant_dir, "variant.json")
    if os.path.exists(knobs_path):
        with open(knobs_path, encoding="utf-8") as f:
            knobs = json.load(f)
    provider = args.provider or knobs.get("provider", "claude")
    model = args.model or knobs.get("model", "sonnet")
    turns = args.turns or int(knobs.get("turns", 30))
    key = None
    if provider == "openrouter" and not args.dry_run:
        # The key arrives on stdin or in the environment, is held in memory only, and
        # is stripped from every child process.
        key = sys.stdin.readline() if args.key_stdin else os.environ.get("OPENROUTER_API_KEY", "")
        key = key.lstrip("﻿").strip()  # Windows pipes may prepend a byte-order mark
        if not key:
            die("openrouter needs a key: OPENROUTER_API_KEY, or --key-stdin")

    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    run_id = f"{stamp}-{args.variant}-{args.player}-{args.datee}-s{args.seed}"
    run_dir = os.path.join(RUNS, run_id)
    os.makedirs(run_dir)

    mech = play_mechanics(args.player, args.datee, args.seed, turns, args.clock,
                          os.path.join(run_dir, "mechanics.json"))
    files, system_prompt, prompt, parts = build_prompt(variant_dir, knobs, mech, args.player, args.datee)
    with open(os.path.join(run_dir, "prompt.md"), "w", encoding="utf-8") as f:
        f.write("<!-- system prompt -->\n" + system_prompt + "\n\n<!-- user prompt -->\n" + prompt)
    with open(os.path.join(run_dir, "prompt_parts.json"), "w", encoding="utf-8") as f:
        json.dump(parts, f, indent=2, ensure_ascii=False)

    record = {
        "run_id": run_id,
        "created_utc": stamp,
        "variant": args.variant,
        "variant_sha256": {name: sha256(text) for name, text in files.items()},
        "knobs": knobs,
        "prompt_sha256": sha256(system_prompt + "\n" + prompt),
        "prompt_chars": len(system_prompt) + len(prompt),
        "repo_commit": git("rev-parse", "--short", "HEAD"),
        "repo_dirty": bool(git("status", "--porcelain", "--", "data", "session-runner", "src", "tools/tonelab")),
        "player": args.player, "datee": args.datee, "seed": args.seed, "clock": args.clock,
        "turn_cap": turns, "turns_played": mech["turns_played"],
        "final_interest": mech["final_interest"], "outcome": mech.get("outcome") or "NoTerminalOutcome",
        "provider": None if args.dry_run else provider,
        "model_requested": None if args.dry_run else model,
    }
    if not args.dry_run:
        if provider == "openrouter":
            data, seconds = call_openrouter(system_prompt, prompt, model, key, knobs)
            output = ((data.get("choices") or [{}])[0].get("message") or {}).get("content") or ""
            record.update({
                # What OpenRouter says served the call: the label comes from here, not from a default.
                "models_reported": [data.get("model")],
                "upstream_provider": data.get("provider"),
                "generation_id": data.get("id"),
                "finish_reason": (data.get("choices") or [{}])[0].get("finish_reason"),
            })
        else:
            data, seconds = call_claude(system_prompt, prompt, model)
            output = data.get("result") or ""
            record.update({
                # The model(s) the CLI says served the call: the label comes from here, not from a default.
                "models_reported": sorted((data.get("modelUsage") or {}).keys()),
                "cli_session_id": data.get("session_id"),
                "cli_version": subprocess.run(["claude", "--version"], capture_output=True, text=True).stdout.strip(),
            })
        output = output.strip() + "\n"
        with open(os.path.join(run_dir, "output.md"), "w", encoding="utf-8") as f:
            f.write(output)
        record.update({
            "seconds": seconds,
            "usage": data.get("usage"),
            "format_checks": format_checks(output, {"player": mech["player"]["display_name"],
                                                    "datee": mech["datee"]["display_name"]},
                                           mech["turns_played"]),
        })
    with open(os.path.join(run_dir, "run.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=2, ensure_ascii=False)
    write_report(run_dir)
    print(os.path.relpath(run_dir, REPO))


# ── report: the chat next to the prompt that produced it ──────────────────────

REPORT_CSS = """
:root{--bg:#f6f5f2;--panel:#fff;--ink:#1d1d1f;--muted:#6b6b70;--line:#e2e0da;--me:#2f6fed;--me-ink:#fff;
--them:#ecebe6;--them-ink:#1d1d1f;--ok:#1f7a44;--bad:#b4400b;--code:#f1f0ec}
@media (prefers-color-scheme:dark){:root{--bg:#141416;--panel:#1c1c1f;--ink:#ececf0;--muted:#9a9aa3;--line:#2e2e34;
--me:#3b7bff;--me-ink:#fff;--them:#2a2a2f;--them-ink:#ececf0;--ok:#4ade80;--bad:#fb923c;--code:#232328}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:760px;margin:0 auto;padding:28px 16px 72px}
h1{font-size:22px;margin:0 0 6px}
.meta{color:var(--muted);font-size:13px;margin:0 0 4px}
.meta code{font-size:12px}
.tools{margin:14px 0 4px;display:flex;gap:8px;flex-wrap:wrap}
.tools button{font:inherit;font-size:13px;padding:4px 10px;border:1px solid var(--line);border-radius:8px;
background:var(--panel);color:var(--ink);cursor:pointer}
details{background:var(--panel);border:1px solid var(--line);border-radius:10px;margin:8px 0}
summary{cursor:pointer;padding:9px 14px;font-weight:600;font-size:13.5px}
details pre{margin:0;padding:12px 14px;border-top:1px solid var(--line);background:var(--code);
white-space:pre-wrap;overflow-wrap:anywhere;font:12.5px/1.55 ui-monospace,Consolas,monospace;border-radius:0 0 10px 10px}
details details{margin:8px 12px}
.turn{margin:30px 0 0}
.head{display:flex;flex-wrap:wrap;gap:6px;align-items:center;font-size:13px;color:var(--muted)}
.head b{color:var(--ink);font-size:14px;margin-right:2px}
.chip{border:1px solid var(--line);border-radius:999px;padding:0 8px;background:var(--panel)}
.chip.ok{color:var(--ok);border-color:currentColor}.chip.bad{color:var(--bad);border-color:currentColor}
.meter{display:inline-block;height:5px;width:90px;background:var(--line);border-radius:3px;overflow:hidden;vertical-align:middle}
.meter i{display:block;height:100%;background:var(--me)}
.who{font-size:11px;color:var(--muted);margin:10px 4px 0}.who.me{text-align:right}
.bubble{width:fit-content;max-width:80%;padding:8px 12px;border-radius:16px;margin:3px 0;white-space:pre-wrap}
.bubble.me{margin-left:auto;background:var(--me);color:var(--me-ink);border-bottom-right-radius:4px}
.bubble.them{background:var(--them);color:var(--them-ink);border-bottom-left-radius:4px}
.end{margin:36px 0 0;padding:12px 14px;border:1px solid var(--line);border-radius:10px;background:var(--panel)}
.warn{color:var(--bad)}
@media (max-width:480px){.bubble{max-width:92%}}
"""


def parse_chat(output, names):
    """Turn N blocks of 'Name: message' lines, as the variants ask the model to write them."""
    turns = {}
    blocks = re.split(r"(?m)^\s*\**Turn (\d+)\**\s*$", output)
    for i in range(1, len(blocks), 2):
        lines = []
        for line in blocks[i + 1].strip().splitlines():
            line = line.strip()
            if not line:
                continue
            name, sep, text = line.partition(":")
            name = name.strip().strip("*").strip()
            if sep and name in names.values():
                lines.append([name, text.strip()])
            elif lines:
                lines[-1][1] += "\n" + line
            else:
                lines.append(["?", line])
        turns[int(blocks[i])] = lines
    return turns


def write_report(run_dir):
    from html import escape as e
    with open(os.path.join(run_dir, "run.json"), encoding="utf-8") as f:
        rec = json.load(f)
    with open(os.path.join(run_dir, "mechanics.json"), encoding="utf-8") as f:
        mech = json.load(f)
    parts_path = os.path.join(run_dir, "prompt_parts.json")
    if os.path.exists(parts_path):
        with open(parts_path, encoding="utf-8") as f:
            parts = json.load(f)
    else:
        # Older run: rebuild the pieces, and use them only if they reproduce the prompt that was sent.
        variant_dir = os.path.join(VARIANTS, rec["variant"])
        _, system_prompt, prompt, parts = build_prompt(variant_dir, rec.get("knobs") or {}, mech,
                                                       rec["player"], rec["datee"])
        if sha256(system_prompt + "\n" + prompt) != rec["prompt_sha256"]:
            die(f"{rec['run_id']}: variant '{rec['variant']}' changed since this run; per-turn briefs cannot be shown exactly")
        with open(parts_path, "w", encoding="utf-8") as f:
            json.dump(parts, f, indent=2, ensure_ascii=False)
    out_path = os.path.join(run_dir, "output.md")
    output = open(out_path, encoding="utf-8").read() if os.path.exists(out_path) else ""

    names = {"player": mech["player"]["display_name"], "datee": mech["datee"]["display_name"]}
    chat = parse_chat(output, names)
    model = (rec.get("models_reported") or [None])[0] or "no model call (dry run)"
    reported_by = {"openrouter": "OpenRouter", "claude": "the Claude CLI"}.get(rec.get("provider"), "")
    h = []
    h.append(f"<h1>{e(names['player'])} × {e(names['datee'])}</h1>")
    h.append(f"<p class=meta><b>{e(model)}</b>{' as reported by ' + reported_by if reported_by else ''} · variant "
             f"<code>{e(rec['variant'])}</code> · seed {rec['seed']} · {e(mech.get('time_of_day', ''))} · "
             f"<b>{e(rec['outcome'])}</b> after {rec['turns_played']} turns</p>")
    h.append(f"<p class=meta>Run <code>{e(rec['run_id'])}</code> · commit <code>{e(rec['repo_commit'])}</code>"
             f"{' (uncommitted changes)' if rec.get('repo_dirty') else ''} · prompt {rec['prompt_chars']:,} characters, "
             f"sha256 <code>{e(rec['prompt_sha256'][:12])}</code>"
             f"{' · ' + str(rec['seconds']) + ' s' if rec.get('seconds') else ''}"
             f"{' · $' + format(rec['usage']['cost'], '.3f') if isinstance(rec.get('usage'), dict) and 'cost' in rec['usage'] else ''}</p>")
    h.append("<p class=meta>The dice are the game engine's own; the model only wrote the messages. "
             "Each turn's fold shows the brief the model was given for it.</p>")
    h.append("<div class=tools><button onclick=\"document.querySelectorAll('.turn details').forEach(d=>d.open=true)\">"
             "Open all turn prompts</button><button onclick=\"document.querySelectorAll('details').forEach(d=>d.open=false)\">"
             "Close all</button></div>")
    h.append("<details><summary>The prompt, section by section</summary>"
             f"<details><summary>System prompt</summary><pre>{e(parts['system'])}</pre></details>"
             f"<details><summary>{e(names['player'])}'s card</summary><pre>{e(parts['player_card'])}</pre></details>"
             f"<details><summary>{e(names['datee'])}'s card</summary><pre>{e(parts['datee_card'])}</pre></details>"
             f"<details><summary>Full user prompt as sent</summary><pre>{e(parts['user'])}</pre></details>"
             "</details>")
    briefs = parts["turn_briefs"]
    for i, t in enumerate(mech["turns"]):
        r = t["roll"] or {}
        ok = r.get("success")
        res = "success" if ok else (r.get("tier") or "miss").lower()
        if r.get("nat20"):
            res += ", nat 20"
        if r.get("nat1"):
            res += ", nat 1"
        chips = [f"<span class=chip>{e(t['chosen_stat'])} {r.get('total')} vs {r.get('dc')}</span>",
                 f"<span class='chip {'ok' if ok else 'bad'}'>{e(res)}</span>"]
        hc, sc, st = t.get("horniness"), t.get("shadow"), t.get("steering")
        if hc and hc.get("miss"):
            chips.append("<span class='chip bad'>horniness slip</span>")
        if sc and sc.get("miss"):
            chips.append(f"<span class='chip bad'>{e(sc['shadow'])} shadow</span>")
        if st and st.get("succeeded"):
            chips.append("<span class='chip ok'>steering</span>")
        for trap in sorted(set(t.get("active_traps_after") or []) - set(t.get("active_traps_before") or [])):
            chips.append(f"<span class='chip bad'>trap: {e(trap)}</span>")
        if t.get("combo"):
            chips.append(f"<span class=chip>combo: {e(t['combo'])}</span>")
        pct = max(0, min(100, round(t["interest_after"] * 4)))
        chips.append(f"<span>interest {t['interest_before']} → {t['interest_after']} ({t['interest_delta']:+d})</span>"
                     f"<span class=meter><i style='width:{pct}%'></i></span>")
        h.append(f"<section class=turn><div class=head><b>Turn {t['turn']}</b>{''.join(chips)}</div>")
        brief = briefs[i] if i < len(briefs) else ""
        h.append(f"<details><summary>Prompt for turn {t['turn']}</summary><pre>{e(brief)}</pre></details>")
        lines = chat.get(t["turn"])
        if not lines:
            h.append("<p class='meta warn'>No message for this turn in the model output.</p>")
        for name, text in lines or []:
            side = "me" if name == names["player"] else "them"
            h.append(f"<div class='who {side}'>{e(name)}</div><div class='bubble {side}'>{e(text)}</div>")
        h.append("</section>")
    extra = sorted(set(chat) - {t["turn"] for t in mech["turns"]})
    if extra:
        h.append(f"<p class='meta warn'>The output also contains turns the game did not play: {extra}.</p>")
    h.append(f"<div class=end>{e(outcome_line(mech, names))}</div>")
    doc = ("<!doctype html><html lang=en><head><meta charset=utf-8>"
           "<meta name=viewport content='width=device-width,initial-scale=1'>"
           f"<title>{e(names['player'])} × {e(names['datee'])} · {e(rec['variant'])}</title>"
           f"<style>{REPORT_CSS}</style></head><body><main>{''.join(h)}</main></body></html>\n")
    path = os.path.join(run_dir, "report.html")
    with open(path, "w", encoding="utf-8") as f:
        f.write(doc)
    return path


def cmd_report(args):
    for run in args.runs:
        run_dir = run if os.path.isabs(run) else os.path.join(REPO, run)
        if not os.path.isdir(run_dir):
            run_dir = os.path.join(RUNS, run)
        if not os.path.exists(os.path.join(run_dir, "run.json")):
            die(f"not a run directory: {run}")
        print(os.path.relpath(write_report(run_dir), REPO))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run", help="play the mechanics, build the prompt, call the model, save the run")
    r.add_argument("--player", default="reuben")
    r.add_argument("--datee", default="brick")
    r.add_argument("--seed", type=int, default=42)
    r.add_argument("--turns", type=int, default=None, help="turn cap (default: variant.json, else the game's 30)")
    r.add_argument("--clock", default=DEFAULT_CLOCK, help="session clock, ISO-8601 (sets time-of-day horniness)")
    r.add_argument("--variant", default="baseline")
    r.add_argument("--provider", choices=["claude", "openrouter"], default=None,
                   help="claude: subscription CLI; openrouter: chat completions (default: variant.json, else claude)")
    r.add_argument("--model", default=None,
                   help="model alias or id, e.g. sonnet, google/gemini-4-argon (default: variant.json, else sonnet)")
    r.add_argument("--key-stdin", action="store_true", help="read the OpenRouter key from the first line of stdin")
    r.add_argument("--dry-run", action="store_true", help="save mechanics and prompt, call no model")
    rp = sub.add_parser("report", help="(re)write report.html for saved runs: the chat next to each turn's prompt")
    rp.add_argument("runs", nargs="+", help="run directory or run id")
    args = p.parse_args()
    if args.cmd == "run":
        cmd_run(args)
    elif args.cmd == "report":
        cmd_report(args)


if __name__ == "__main__":
    main()
