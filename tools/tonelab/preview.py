#!/usr/bin/env python3
"""A preview of a live variant: the prompts sent once, the plan and the first turn, and nothing more.
Plays the real mechanics, calls the model for the plan and turn 1 (the director, then the writer, with
their retries), and stops before turn 2. Writes preview.html and preview.json into a new run folder.

    python3 -I tools/tonelab/preview.py --seed 23 --key-stdin
"""
import argparse
import datetime
import html
import json
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tonelab as tl  # noqa: E402
import live  # noqa: E402


class Stop(Exception):
    pass


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--variant", default="live")
    ap.add_argument("--player", default="reuben")
    ap.add_argument("--datee", default="brick")
    ap.add_argument("--seed", type=int, default=23)
    ap.add_argument("--key-stdin", action="store_true")
    ap.add_argument("--old-cards", action="store_true", help="build the cards from the character files, as before v22")
    args = ap.parse_args()
    if not args.key_stdin:
        tl.die("preview: the key comes on stdin (--key-stdin)")
    key = sys.stdin.readline().strip().lstrip("﻿")  # a PowerShell pipe puts a byte-order mark first
    variant = os.path.join(tl.VARIANTS, args.variant)
    knobs = json.load(open(os.path.join(variant, "variant.json"), encoding="utf-8"))
    if args.old_cards:
        knobs.pop("profiles", None)
    path = os.path.join(tempfile.mkdtemp(prefix="tonelab-preview-"), "mechanics.json")
    mech = tl.play_mechanics(args.player, args.datee, args.seed, int(knobs.get("turns", 30)), tl.DEFAULT_CLOCK, path,
                             knobs.get("agent"))
    calls, systems = [], {}

    def caller(system, user, label, effort, ctx):
        role = "plan" if label == "plan" else "tracker" if label.endswith("tracker") else label.split()[2]
        systems.setdefault(role, system)
        if label.startswith("turn 2"):
            raise Stop()
        k = {x: knobs[x] for x in ("temperature", "top_p", "max_tokens") if x in knobs}
        if effort:
            k["reasoning"] = {"effort": effort}
        data, seconds = tl.call_openrouter(system, user, knobs["model"], key, k, history=ctx.get("history") or [])
        text = (((data.get("choices") or [{}])[0].get("message") or {}).get("content") or "").strip()
        calls.append({"label": label, "user": user, "output": text, "usage": data.get("usage") or {},
                      "seconds": seconds})
        if not text:
            tl.die(f"{label}: the model returned nothing")
        return text

    try:
        live.play(variant, knobs, mech, args.player, args.datee, caller)
    except Stop:
        pass
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    run_dir = os.path.join(tl.REPO, "docs/tone-lab/runs", f"{stamp}-preview{'-oldcards' if args.old_cards else ''}-{args.player}-{args.datee}-s{args.seed}")
    os.makedirs(run_dir)
    cost = round(sum((c["usage"] or {}).get("cost") or 0 for c in calls), 4)
    record = {"run_id": os.path.basename(run_dir), "variant": args.variant, "seed": args.seed,
              "model": knobs["model"], "repo_commit": tl.git("rev-parse", "--short", "HEAD"), "profiles": bool(knobs.get("profiles")),
              "turn_1": {k: mech["turns"][0][k] for k in ("chosen_stat", "interest_before", "interest_after")},
              "cost": cost, "systems": systems, "calls": calls}
    with open(os.path.join(run_dir, "preview.json"), "w", encoding="utf-8") as fh:
        json.dump(record, fh, indent=2, ensure_ascii=False)
    pre = lambda x: f"<pre>{html.escape(x)}</pre>"
    once = "".join(f"<details><summary>System prompt: {html.escape(k)}</summary>{pre(v)}</details>"
                   for k, v in systems.items())
    turns = "".join(f"<h3>{html.escape(c['label'])}</h3><details><summary>Sent</summary>{pre(c['user'])}</details>"
                    f"<p>Answer:</p>{pre(c['output'])}" for c in calls)
    page = ("<!doctype html><meta charset=utf-8><title>Tone lab preview</title><style>body{font:15px/1.5 system-ui,"
            "sans-serif;max-width:60rem;margin:2rem auto;padding:0 1rem;color:#222;background:#fff}pre{white-space:"
            "pre-wrap;background:#f4f4f4;padding:.75rem;border-radius:6px}summary{cursor:pointer;font-weight:600}"
            "@media(prefers-color-scheme:dark){body{color:#ddd;background:#181818}pre{background:#262626}}</style>"
            f"<h1>Preview: {html.escape(args.player)} and {html.escape(args.datee)}, seed {args.seed}</h1>"
            f"<p>{html.escape(knobs['model'])}, the plan and turn 1 only, {len(calls)} calls, ${cost}.</p>"
            f"<h2>Prompts sent once</h2>{once}<h2>The calls</h2>{turns}")
    with open(os.path.join(run_dir, "preview.html"), "w", encoding="utf-8") as fh:
        fh.write(page)
    print(os.path.relpath(run_dir, tl.REPO))


if __name__ == "__main__":
    main()
