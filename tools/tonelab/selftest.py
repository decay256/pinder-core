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
    """A live variant: every prompt renders on every turn of a few real games, and the story
    beats never look ahead (a game cut short gives the same beats up to the cut)."""
    import live
    for seed in (7, 23, 42):
        path = os.path.join(tmp, f"{name}-{seed}.json")
        m = tonelab.play_mechanics("reuben", "brick", seed, 30, tonelab.DEFAULT_CLOCK, path, knobs.get("agent"))
        names = {"player": m["player"]["display_name"], "datee": m["datee"]["display_name"]}
        prompts, writers, director = [], [], []

        def caller(system, user, label, effort, ctx):
            prompts.append(system + user)
            if "direction" in label:
                director.append((len(ctx.get("history") or []), user))
            if label.endswith(" options") or " delivery" in label:
                writers.append(user)
            return live.dry_caller(names)(system, user, label, effort, ctx)
        res = live.play(variant, knobs, m, "reuben", "brick", caller)
        check(len(res["turns"]) == len(m["turns"]) and len(prompts) == 1 + 4 * len(m["turns"]),
              f"{name} seed {seed}: a plan call and four calls per played turn, two of them the director's "
              f"({len(m['turns'])} turns)")
        check(all("If they click" not in u and "Collision:" not in u and "Origin:" not in u for u in writers),
              f"{name} seed {seed}: the writers never see the plan's origins, collision or endings")
        check([n for n, _ in director] == list(range(0, 2 * len(director), 2)),
              f"{name} seed {seed}: the director is one session, each call carrying every earlier message")
        check("If they click" in director[0][1] and not any("If they click" in u for _, u in director[1:]),
              f"{name} seed {seed}: the plan goes into the director's session once, at the start")
        check(all("${" not in p for p in prompts), f"{name} seed {seed}: every prompt renders with no unresolved placeholder")
        full = live.beat_keys(knobs, m["turns"], m.get("outcome"))
        same = all(live.beat_keys(knobs, m["turns"][:k], None)[:k - 1] == full[:k - 1] for k in range(2, len(m["turns"]) + 1))
        check(same, f"{name} seed {seed}: beats never depend on later turns ({' '.join(full)})")


def main():
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
