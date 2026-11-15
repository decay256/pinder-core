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

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
VARIANTS = os.path.join(HERE, "variants")
RUNS = os.path.join(REPO, "docs", "tone-lab", "runs")
RUNNER_PROJECT = os.path.join(REPO, "session-runner", "session-runner.csproj")
RUNNER_DLL = os.path.join(REPO, "session-runner", "bin", "Release", "net8.0", "session-runner.dll")
CONSEQUENCES = os.path.join(REPO, "data", "i18n", "en", "consequences.yaml")
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
    for key in ("ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"):
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
    return c, diag, {
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
    }


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
    briefs = [render(files["turn.md"],
                     turn_values(t, cons, names, datee_diag, i == len(turns) - 1, mech.get("outcome")),
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
    return files, render(files["system.md"], values, "system.md").strip(), \
        render(files["prompt.md"], values, "prompt.md").strip() + "\n"


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
    model = args.model or knobs.get("model", "sonnet")
    turns = args.turns or int(knobs.get("turns", 30))

    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    run_id = f"{stamp}-{args.variant}-{args.player}-{args.datee}-s{args.seed}"
    run_dir = os.path.join(RUNS, run_id)
    os.makedirs(run_dir)

    mech = play_mechanics(args.player, args.datee, args.seed, turns, args.clock,
                          os.path.join(run_dir, "mechanics.json"))
    files, system_prompt, prompt = build_prompt(variant_dir, knobs, mech, args.player, args.datee)
    with open(os.path.join(run_dir, "prompt.md"), "w", encoding="utf-8") as f:
        f.write("<!-- system prompt -->\n" + system_prompt + "\n\n<!-- user prompt -->\n" + prompt)

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
        "model_requested": None if args.dry_run else model,
    }
    if not args.dry_run:
        data, seconds = call_claude(system_prompt, prompt, model)
        output = (data.get("result") or "").strip() + "\n"
        with open(os.path.join(run_dir, "output.md"), "w", encoding="utf-8") as f:
            f.write(output)
        record.update({
            # The model(s) the CLI says served the call: the label comes from here, not from a default.
            "models_reported": sorted((data.get("modelUsage") or {}).keys()),
            "cli_session_id": data.get("session_id"),
            "cli_version": subprocess.run(["claude", "--version"], capture_output=True, text=True).stdout.strip(),
            "seconds": seconds,
            "usage": data.get("usage"),
            "format_checks": format_checks(output, {"player": mech["player"]["display_name"],
                                                    "datee": mech["datee"]["display_name"]},
                                           mech["turns_played"]),
        })
    with open(os.path.join(run_dir, "run.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=2, ensure_ascii=False)
    print(os.path.relpath(run_dir, REPO))


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
    r.add_argument("--model", default=None, help="CLI model alias or id (default: variant.json, else sonnet)")
    r.add_argument("--dry-run", action="store_true", help="save mechanics and prompt, call no model")
    args = p.parse_args()
    if args.cmd == "run":
        cmd_run(args)


if __name__ == "__main__":
    main()
