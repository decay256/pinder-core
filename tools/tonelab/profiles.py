#!/usr/bin/env python3
"""Draft character profiles for the tone lab (v22): a person with a life, written from the game's
character file, instead of the file's own consolidated personality and backstory.

The game's character files describe a person as humiliations: brag-and-truth pairs, drawer items and
a backstory stitched from item and anatomy fragments, with no people, places, job, history or wants
in them, so a chat built on them has nothing for the other one to find out. The bundled files on
this branch also share whole passages with each other (the reconsolidation of 2026-09-07 spread one
character's fragments into all six). This tool writes each character a profile in a fixed shape
(what the match sees, their life now, the people in it, how they got here, what they love and are
good at, how they are with people, what they want, their brags and truths, and how they text in each
situation the chat can put them in), keeping the bio, what they own, the body, the drawer and the
clinician's notes, and marking the passages they share with other characters so the model doesn't
build on them. The game's own files and generator are not touched; the profiles live next to this
tool and a live variant reads them when its variant.json says "profiles".

    python3 -I tools/tonelab/profiles.py --key-stdin            # all six, in order
    python3 -I tools/tonelab/profiles.py --key-stdin --only brick
    python3 -I tools/tonelab/profiles.py --dry --only reuben     # print the prompt, call nothing
"""
import argparse
import datetime
import json
import os
import re
import sys

import yaml

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import tonelab as tl  # noqa: E402

DIR = os.path.join(HERE, "profiles")
GENERATOR = os.path.join(DIR, "generator.yaml")
RECORD = os.path.join(DIR, "generation.json")
ORDER = ("reuben", "brick", "gerald", "sable", "velvet", "zyx")
CLAIMS_HEAD = "What they claim, and what's true"
TEXTING_HEAD = "How they text, by situation"
PEOPLE_HEAD = "The people in their life"
LIFE_HEAD = "Their life now"


def table():
    with open(GENERATOR, encoding="utf-8") as f:
        return yaml.safe_load(f)


def character(slug):
    with open(os.path.join(tl.REPO, "data", "characters", f"{slug}.json"), encoding="utf-8") as f:
        return json.load(f)


def words(text):
    return re.findall(r"[a-z0-9]+", text.lower().replace("’", "'").replace("'", ""))


def runs(text, n):
    w = words(text)
    return {tuple(w[i:i + n]) for i in range(len(w) - n + 1)}


def file_texts(c):
    """The parts of a character file the older generator wrote: what may be shared between files."""
    out = [c.get("consolidated_personality") or "", c.get("consolidated_backstory") or ""]
    for pair in (c.get("backstory_categories") or {}).values():
        out += [pair.get("bio_lie") or "", pair.get("tragic_reality") or ""]
    out += list(c.get("stake_lines") or [])
    return [x for x in out if x.strip()]


def sentences(text):
    return [s.strip() for s in re.split(r"(?<=[.!?])\s+|\n+", text) if len(words(s)) >= 5]


def all_fragments(c):
    """Every fragment the game gives this character: from each item and each band of their body."""
    items = {i["id"]: i for i in tl._load_json(tl.ITEMS_FILE)}
    out = [(items.get(i) or {}).get(f"{k}_fragment") or "" for i in c.get("items") or []
           for k in ("personality", "backstory")]
    anatomy = c.get("anatomy") or {}
    for p in tl._load_json(tl.ANATOMY_FILE):
        if p["id"] in anatomy:
            b = tl._band(p, anatomy[p["id"]]) or {}
            out += [b.get(f"{k}_fragment") or "" for k in ("personality", "backstory")]
    return [x for x in out if x]


def shared_passages(slug, chars, n=5):
    """Sentences of this character's file that share a run of n words with another character's file,
    unless they come from one of this character's own fragments (then the others copied them)."""
    others = set()
    for other, c in chars.items():
        if other != slug:
            for t in file_texts(c):
                others |= runs(t, n)
    own = set()
    for frag in all_fragments(chars[slug]):
        own |= runs(frag, 3)
    seen, out = set(), []
    for t in file_texts(chars[slug]):
        for s in sentences(t):
            if s not in seen and runs(s, n) & others and len(runs(s, 3) & own) < 2:
                seen.add(s)
                out.append(s)
    return out


def own_fragments(c):
    """The game's own fragments for this character: from each item, and from the extremes of the body."""
    items = {i["id"]: i for i in tl._load_json(tl.ITEMS_FILE)}
    out = []
    for iid in c.get("items") or []:
        it = items.get(iid) or {}
        for kind in ("personality", "backstory"):
            frag = (it.get(f"{kind}_fragment") or "").strip()
            if frag:
                out.append(f"- {it.get('display_name', iid)} ({kind}): {frag}")
    anatomy = c.get("anatomy") or {}
    for p in tl._load_json(tl.ANATOMY_FILE):
        v = anatomy.get(p["id"])
        if v is None or p["id"].startswith("skin") or 0.2 <= v <= 0.8:
            continue
        b = tl._band(p, v) or {}
        name = tl.ANATOMY_GLOSS.get(p["id"], p["name"].lower())
        for kind in ("personality", "backstory"):
            frag = (b.get(f"{kind}_fragment") or "").strip()
            if frag:
                out.append(f"- {name} ({kind}): {frag}")
    return out


def item_names(c):
    items = {i["id"]: i for i in tl._load_json(tl.ITEMS_FILE)}
    return ", ".join((items.get(i) or {}).get("display_name", i) for i in c.get("items") or []) or "nothing listed"


def sections_of(md):
    """{heading: body} for the profile's '## ' sections, in order."""
    out, head = {}, None
    for line in md.splitlines():
        m = re.match(r"^##\s+(.+?)\s*$", line)
        if m:
            head = m.group(1)
            out[head] = []
        elif head is not None:
            out[head].append(line)
    return {k: "\n".join(v).strip() for k, v in out.items()}


def pairs_of(text):
    return [{"bio_lie": a.strip(), "tragic_reality": b.strip()}
            for a, b in re.findall(r"(?m)^\s*-\s*says:\s*(.+)\n\s+truth:\s*(.+)$", text)]


def barred_motifs(c, T):
    """The spread motifs this character's own fragments don't contain: another character's."""
    own = " ".join(all_fragments(c)).lower()
    return [m for m in T.get("motifs") or [] if not re.search(m["pattern"], own)]


def problems(md, c, T, shared):
    """What keeps a profile from being usable: the game's checks, in plain words for a retry."""
    out = []
    used = [m["name"] for m in barred_motifs(c, T) if re.search(m["pattern"], md, re.I)]
    if used:
        out.append("These motifs belong to another character and must not appear, reworded or not (the words that "
                   "give them away included): " + "; ".join(used))
    heads = re.findall(r"(?m)^##\s+(.+?)\s*$", md)
    if heads != T["headings"]:
        out.append("The headings must be exactly these, in this order, each after '## ': "
                   + "; ".join(T["headings"]) + ". Got: " + "; ".join(heads))
    if not re.match(r"^#\s+\S", md):
        out.append("The profile starts with '# ' and the character's name.")
    sec = sections_of(md)
    first = sec.get(T["headings"][0], "")
    if " ".join(words(c.get("bio") or "")) not in " ".join(words(first)):
        out.append("The bio is quoted word for word under the first heading, as the file has it.")
    if T.get("city") and T["city"].lower() not in sec.get(LIFE_HEAD, "").lower():
        out.append(f"Everyone on Pinder lives in {T['city']}: name {T['city']} and their neighbourhood under "
                   f"'{LIFE_HEAD}'.")
    texting = sec.get(TEXTING_HEAD, "")
    missing = [s for s in T["situations"] if f"**{s['label']}:**" not in texting]
    if missing:
        out.append("Under the texting heading, every situation has its own line, labelled exactly: "
                   + ", ".join(f"**{s['label']}:**" for s in missing) + " is missing.")
    n_pairs = len(pairs_of(sec.get(CLAIMS_HEAD, "")))
    if not 4 <= n_pairs <= 6:
        out.append(f"Under '{CLAIMS_HEAD}' there are four to six pairs, each '- says: ...' then '  truth: ...' "
                   f"on the next line (found {n_pairs}).")
    people = re.findall(r"(?m)^\s*-\s*\*\*", sec.get(PEOPLE_HEAD, ""))
    if not 4 <= len(people) <= 6:
        out.append(f"Under '{PEOPLE_HEAD}' there are four to six people, one line each starting '- **' "
                   f"(found {len(people)}).")
    n = len(words(md))
    lo, hi = T["words"]["check_from"], T["words"]["check_to"]
    if not lo <= n <= hi:
        change = f"cut about {n - T['words']['to']} words, from the longest sections" if n > hi else \
            f"add about {T['words']['from'] - n} words where the life is thinnest"
        out.append(f"The profile runs {T['words']['from']} to {T['words']['to']} words; this one has {n}: {change}.")
    mine = runs(md, 8)
    copied = [s for s in shared if runs(s, 8) & mine]
    if copied:
        out.append("These passages are shared with other characters and were copied; leave them out: "
                   + " | ".join(copied[:4]))
    return out


def taken_by_others(slug, profiles):
    """Names of people and the life lines other characters' profiles already use."""
    lines = []
    for other, md in profiles.items():
        if other == slug or not md:
            continue
        sec = sections_of(md)
        people = re.findall(r"(?m)^\s*-\s*\*\*([^*]+?)\*\*", sec.get(PEOPLE_HEAD, ""))
        life = " ".join(sec.get(LIFE_HEAD, "").split())[:400]
        lines.append(f"- {other}: people: {'; '.join(p.strip(' .') for p in people)}. Life: {life}")
    return "\n".join(lines) or "- nobody yet"


def prompt_values(slug, chars, profiles):
    c = chars[slug]
    diag = c.get("psychiatric_diagnosis") or {}
    shared = shared_passages(slug, chars)
    pairs = "\n".join(f"- says: {p.get('bio_lie', '')}\n  truth: {p.get('tragic_reality', '')}"
                      for p in (c.get("backstory_categories") or {}).values())
    drawer = "\n".join("- " + ln.strip().lstrip("-•*").strip()
                       for ln in (c.get("psychological_stake") or "").splitlines() if ln.strip())
    return shared, {
        "name": c.get("name") or slug, "pronouns": c.get("gender_identity") or "", "level": c.get("level") or "",
        "bio": (c.get("bio") or "").strip(), "items": item_names(c),
        "body": "\n".join(tl.body_lines(c)) or "- nothing extreme",
        "fragments": "\n".join(own_fragments(c)) or "- none",
        "texting_style": "\n".join(tl.texting_style_lines(c)) or "- nothing distinctive",
        "personality": (c.get("consolidated_personality") or "").strip(),
        "backstory": (c.get("consolidated_backstory") or "").strip(),
        "pairs": pairs or "- none", "stake_lines": "\n".join(f"- {s}" for s in c.get("stake_lines") or []) or "- none",
        "shared": "\n".join(f"- {s}" for s in shared) or "- none",
        "barred": "\n".join(f"- {m['name']}" for m in barred_motifs(c, table())) or "- none",
        "drawer": drawer or "- empty",
        "wound": diag.get("derived_feeling", ""), "defence": diag.get("defense_reaction", ""),
        "safe_connection": diag.get("safe_connection", ""),
        "taken": taken_by_others(slug, profiles),
    }


def system_prompt(T):
    labels = "\n".join(f"- **{s['label']}:** {s['when']}" for s in T["situations"])
    return tl.render(T["system"], {"situations": labels, "city": T["city"], "words_from": T["words"]["from"],
                                   "words_to": T["words"]["to"]}, "generator.yaml system").strip()


def clean(text):
    text = text.strip()
    m = re.match(r"^```(?:markdown|md)?\s*\n(.*)\n```\s*$", text, re.S)
    return (m.group(1) if m else text).strip() + "\n"


def generate(slug, chars, profiles, T, knobs, key):
    shared, values = prompt_values(slug, chars, profiles)
    system = system_prompt(T)
    user = tl.render(T["user"], values, "generator.yaml user").strip()
    k = {"reasoning": {"effort": knobs.get("profile_reasoning", "high")}}
    history, attempts, prompt = [], [], user
    for attempt in range(1 + int(T.get("retries", 2))):
        data, seconds = tl.call_openrouter(system, prompt, knobs["model"], key, k, history=history)
        text = clean((((data.get("choices") or [{}])[0].get("message") or {}).get("content") or ""))
        bad = problems(text, chars[slug], T, shared)
        usage = data.get("usage") or {}
        attempts.append({"prompt": prompt, "output": text, "problems": bad, "seconds": seconds,
                         "cost": usage.get("cost"), "usage": usage})
        print(f"{slug}: attempt {attempt + 1}, {len(words(text))} words, ${usage.get('cost')}, "
              f"{len(bad)} problem(s)", flush=True)
        if not bad:
            return text, {"system": system, "shared": shared, "attempts": attempts}
        history += [{"role": "user", "content": prompt}, {"role": "assistant", "content": text}]
        prompt = tl.render(T["retry"], {"problems": "\n".join(f"- {p}" for p in bad)}, "generator.yaml retry").strip()
    tl.die(f"{slug}: the profile still fails after {len(attempts)} attempts: {attempts[-1]['problems']}")


def read_profiles():
    out = {}
    for slug in ORDER:
        path = os.path.join(DIR, f"{slug}.md")
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                out[slug] = f.read()
    return out


def card_values(slug):
    """What a live card takes from a profile: the profile without its title and its brags (those
    are drawn one per match, as the focus), the texting section for the writer, and the brags."""
    path = os.path.join(DIR, f"{slug}.md")
    if not os.path.exists(path):
        tl.die(f"no profile for {slug} (tools/tonelab/profiles/{slug}.md); write one with profiles.py")
    with open(path, encoding="utf-8") as f:
        md = f.read()
    sec = sections_of(md)
    body = "\n\n".join(f"#### {h}\n{b}" for h, b in sec.items() if h != CLAIMS_HEAD)
    return {"profile": body, "texting_by_situation": sec.get(TEXTING_HEAD, ""), "pairs": pairs_of(sec.get(CLAIMS_HEAD, ""))}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="", help="comma-separated slugs (default: all six, in order)")
    ap.add_argument("--key-stdin", action="store_true")
    ap.add_argument("--dry", action="store_true", help="print the first prompt and call nothing")
    args = ap.parse_args()
    slugs = [s for s in args.only.split(",") if s] or list(ORDER)
    T = table()
    chars = {s: character(s) for s in ORDER}
    profiles = read_profiles()
    with open(os.path.join(tl.VARIANTS, "live", "variant.json"), encoding="utf-8") as f:
        knobs = json.load(f)
    if args.dry:
        shared, values = prompt_values(slugs[0], chars, profiles)
        print(system_prompt(T) + "\n\n=====\n\n" + tl.render(T["user"], values, "generator.yaml user"))
        return
    if not args.key_stdin:
        tl.die("profiles: the key comes on stdin (--key-stdin)")
    key = sys.stdin.readline().strip().lstrip("﻿")
    record = {}
    if os.path.exists(RECORD):
        with open(RECORD, encoding="utf-8") as f:
            record = json.load(f)
    record.setdefault("characters", {})
    for slug in slugs:
        md, how = generate(slug, chars, profiles, T, knobs, key)
        profiles[slug] = md
        with open(os.path.join(DIR, f"{slug}.md"), "w", encoding="utf-8") as f:
            f.write(md)
        record["characters"][slug] = {
            "generated": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "model": knobs["model"], "repo_commit": tl.git("rev-parse", "--short", "HEAD"),
            "words": len(words(md)), "cost": round(sum(a["cost"] or 0 for a in how["attempts"]), 4), **how}
        with open(RECORD, "w", encoding="utf-8") as f:
            json.dump(record, f, indent=2, ensure_ascii=False)
    total = sum(v.get("cost") or 0 for k, v in record["characters"].items() if k in slugs)
    print(f"done: {', '.join(slugs)}, ${round(total, 4)}")


if __name__ == "__main__":
    main()
