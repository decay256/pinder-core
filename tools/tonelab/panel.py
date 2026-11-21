#!/usr/bin/env python3
"""The author panel: eight readers, each with one writer's sensibility, score a piece of work on the
scorecard, and a chair brings them to one score per dimension.

The single judge reads every dimension with one taste. The panel asks the same questions through eight
(panel.yaml): each reader scores a batch of dimensions on its own, with the same material and the same
reader probes as the single judge, then a chair gets the work, the dimensions and all eight answers and
returns the score the panel would sign, who decided it and who still dissents. Every reader is the same
model given a lens; none of these writers took part.

The result is written beside the single judge's as docs/tone-lab/scores/<target>-panel.json, and the
scorecard page shows it as its own column, with each reader's score and the dissent under the reasoning.

    python3 -I tools/tonelab/panel.py run --targets profile-reuben-v22,profile-reuben-v24
"""
import argparse
import concurrent.futures as cf
import datetime as dt
import json
import sys
import threading
from pathlib import Path

import yaml

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import score as sc  # noqa: E402
import tonelab as tl  # noqa: E402

PANEL = HERE / "panel.yaml"
MIN_READERS = 6  # a batch goes to the chair with at least this many readers' answers


def panel():
    text = PANEL.read_text(encoding="utf-8")
    return yaml.safe_load(text), sc.sha(text)


def problems_in(P):
    out = []
    rs = P.get("readers") or []
    if len(rs) < MIN_READERS:
        out.append(f"the panel has {len(rs)} readers, fewer than {MIN_READERS}")
    ids = [r.get("id") for r in rs]
    if len(set(ids)) != len(ids):
        out.append("two readers share an id")
    out += [f"reader {r.get('id')}: no name or lens" for r in rs if not (r.get("name") and str(r.get("lens") or "").strip())]
    for k in ("reader_note", "chair"):
        if not str(P.get(k) or "").strip():
            out.append(f"no {k}")
    return out


def panel_path(t):
    return sc.OUT / f"{t['id']}-panel.json"


def the_panel(answers, ds):
    """Every reader's answer, dimension by dimension, for the chair."""
    out = []
    for d in ds:
        lines = [f"[{d['id']}] {d['name']}"]
        for r, got in answers:
            s = got[d["id"]]
            ev = " | ".join(s["evidence"]) or "no quotes"
            lines.append(f"- {r['name']}: {s['score']}. {s['why']} Quotes: {ev}")
        out.append("\n".join(lines))
    return "\n\n".join(out)


def dissents(text):
    a, b = text.find("{"), text.rfind("}")
    try:
        return {s.get("id"): str(s.get("dissent") or "").strip() for s in json.loads(text[a:b + 1]).get("scores") or []}
    except (ValueError, json.JSONDecodeError):
        return {}


def ask(caller, system, user, label, ds, calls, lock):
    for attempt in (1, 2):
        text, meta = caller(system, user, label)
        with lock:
            calls.append(dict(meta, label=label + (" retry" if attempt == 2 else "")))
        try:
            return sc.parse_scores(text, ds), text
        except (ValueError, json.JSONDecodeError) as e:
            if attempt == 2:
                raise sc.JudgeError(f"{label}: {e}")
            user = user + f"\n\n=== YOUR LAST ANSWER COULD NOT BE USED ===\n{e}. Return only the JSON."


def panel_target(t, cat, P, psha, caller, judge_id, force=False):
    base = sc.load_result(t)
    if not base.get("scores"):
        raise sc.JudgeError(f"{t['id']}: score it with score.py first; the panel reuses its reader probes")
    dims = [d for d in cat["dimensions"] if d["family"] == t["kind"] and d.get("scope") != "set"]
    msha = sc.sha(json.dumps(t["material"], sort_keys=True))
    path = panel_path(t)
    rec = {} if force or not path.exists() else json.loads(path.read_text(encoding="utf-8"))
    rec.update({k: t.get(k) for k in ("kind", "version", "order", "run", "source", "when")})
    rec.update({"id": f"{t['id']}-panel", "panel_of": t["id"], "label": f"{t['label']} · author panel",
                "note": "Scored by the author panel: eight readers, each the same model reading with one writer's "
                        "sensibility, score on their own, and a chair brings them to one score. None of these writers "
                        "took part.",
                "readers": [{"id": r["id"], "name": r["name"]} for r in P["readers"]],
                "materials": base.get("materials"), "probes": base.get("probes") or {}})
    rec.setdefault("scores", {})
    rec.setdefault("calls", [])
    lock = threading.Lock()
    fresh = lambda d: (s := rec["scores"].get(d["id"])) and s.get("def") == d["sha"] and s.get("material") == msha \
        and s.get("panel") == psha and s.get("judge") == judge_id  # noqa: E731
    todo = [d for d in dims if not fresh(d)]
    if not todo:
        sc.say(f"[{rec['id']}] up to date")
        return rec
    batches = {}
    for d in todo:
        batches.setdefault(d["batch"], []).append(d)
    seen = dict(t["material"], **{f"probe {k}": v["answer"] for k, v in rec["probes"].items()})

    def run_batch(item):
        b, ds = item
        user = sc.judge_prompt(cat, t, ds, rec["probes"])
        base_system = sc.judge_system(cat)

        def read(r):
            system = f"{base_system}\n\n{r['lens'].strip()}\n\n{P['reader_note'].strip()}"
            return r, ask(caller, system, user, f"batch {b} {r['id']}", ds, rec["calls"], lock)[0]

        answers, failed = [], []
        with cf.ThreadPoolExecutor(len(P["readers"])) as ex:
            for f in [ex.submit(read, r) for r in P["readers"]]:
                try:
                    answers.append(f.result())
                except sc.JudgeError as e:
                    failed.append(str(e))
        if len(answers) < MIN_READERS:
            raise sc.JudgeError(f"{t['id']} batch {b}: only {len(answers)} readers answered ({'; '.join(failed)})")
        order = {r["id"]: i for i, r in enumerate(P["readers"])}
        answers.sort(key=lambda x: order[x[0]["id"]])
        chair_system = P["chair"].format(scale="\n".join(f"{k}. {v}" for k, v in sorted(cat["scale"].items())))
        chair_user = user + "\n\n=== THE PANEL ===\n" + the_panel(answers, ds)
        got, raw = ask(caller, chair_system, chair_user, f"batch {b} chair", ds, rec["calls"], lock)
        dis = dissents(raw)
        with lock:
            for d in ds:
                s = got[d["id"]]
                s.update({"dissent": dis.get(d["id"], ""), "def": d["sha"], "material": msha, "panel": psha,
                          "judge": judge_id, "batch": b, "found": [sc.quote_found(q, seen) for q in s["evidence"]],
                          "readers": [{"id": r["id"], "name": r["name"], "score": a[d["id"]]["score"],
                                       "why": a[d["id"]]["why"], "evidence": a[d["id"]]["evidence"]} for r, a in answers]})
                rec["scores"][d["id"]] = s
            rec["judge"] = f"panel:{judge_id}"
            path.write_text(json.dumps(rec, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
        spread = [max(x["score"] for x in s["readers"] if x["score"] is not None) -
                  min(x["score"] for x in s["readers"] if x["score"] is not None) for s in (rec["scores"][d["id"]] for d in ds)]
        sc.say(f"[{rec['id']}] batch {b} ({len(ds)}): {len(answers)} readers, widest spread {max(spread)}"
               + (f", {len(failed)} reader(s) failed" if failed else ""))

    errors = []
    with cf.ThreadPoolExecutor(4) as ex:
        for f in [ex.submit(run_batch, it) for it in sorted(batches.items())]:
            try:
                f.result()
            except sc.JudgeError as e:
                errors.append(str(e))
    rec["judge"] = f"panel:{judge_id}"
    rec["scored_utc"] = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    rec["repo_commit"] = tl.git("rev-parse", "--short", "HEAD").strip()
    rec["cost"] = round(sum(c.get("cost") or 0 for c in rec["calls"]), 4)
    path.write_text(json.dumps(rec, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    if errors:
        raise sc.JudgeError("; ".join(errors))
    return rec


def cmd_run(args):
    cat = sc.catalogue()
    P, psha = panel()
    bad = sc.problems_in(cat) + problems_in(P)
    if bad:
        tl.die("panel: " + "; ".join(bad))
    want = args.targets.split(",")
    ts = [t for t in sc.targets() if t["id"] in want]
    if len(ts) != len(want):
        tl.die(f"panel: unknown targets {sorted(set(want) - {t['id'] for t in ts})}")
    model = args.model or "opus"
    caller = sc.make_caller("claude", model)
    sc.say(f"panel of {len(P['readers'])} on {len(ts)} targets with claude:{model}")
    failed = []
    with cf.ThreadPoolExecutor(args.workers) as ex:
        futs = {ex.submit(panel_target, t, cat, P, psha, caller, f"claude:{model}", args.force): t for t in ts}
        for f in cf.as_completed(futs):
            try:
                rec = f.result()
                sc.say(f"done {rec['id']}: {sc.summary(rec, cat)}")
            except sc.JudgeError as e:
                failed.append(futs[f]["id"])
                sc.say(f"FAILED {futs[f]['id']}: {e}")
    sc.write_page(cat)
    sc.write_doc(cat)
    return 1 if failed else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run")
    r.add_argument("--targets", required=True, help="comma list of target ids, already scored by score.py")
    r.add_argument("--model")
    r.add_argument("--force", action="store_true")
    r.add_argument("--workers", type=int, default=2)
    a = ap.parse_args()
    return cmd_run(a)


if __name__ == "__main__":
    sys.exit(main())
