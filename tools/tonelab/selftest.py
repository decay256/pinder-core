#!/usr/bin/env python3
"""Self-check for the tone lab. Calls no model.

1. A --mechanics-only session is reproducible: the same seed and clock give
   byte-identical mechanics.
2. The mechanics are a consistent game: interest chains from turn to turn and
   stays within 0..25.
3. Without --mechanics-only and without an API key the runner refuses to start,
   so the mechanics path is the only one that runs model-free.
4. Every variant renders against real mechanics with no unknown placeholder.
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
            _, system_prompt, prompt, parts = tonelab.build_prompt(variant, knobs, ma, "reuben", "brick")
            check(len(parts["turn_briefs"]) == len(ma["turns"]), f"{name}: one prompt brief per played turn")
            check(bool(system_prompt) and "Turn 1." in prompt and "${" not in system_prompt + prompt,
                  f"{name}: renders with no unresolved placeholder")
    print("selftest passed")


if __name__ == "__main__":
    main()
