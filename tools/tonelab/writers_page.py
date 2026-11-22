"""Build the eight-writers reading page with every run: each run's chats, profiles and prompts."""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import writers as w  # noqa: E402

BASE = os.path.join(w.REPO, "docs/tone-lab/scratch")
RUNS = [  # newest first
    ("v3", "Run 3: own texting style", "writers-v3",
     "Run 3. As run 2, but the game's texting-style fragments are removed from the list, and the profile prompt adds one sentence: \"Develop your own texting style for this character, so the character is distinct.\""),
    ("v2", "Run 2: the writer decides", "writers-v2",
     "Run 2. The system prompt makes the writer the author and the authority on the characters and the dialogue; the game's material is raw material they may throw away; form and length are theirs."),
    ("v1", "Run 1: first prompt", "writers",
     "Run 1. One line of persona (\"You are <writer>.\"); the game's material offered as inspiration, not requirements; the chat \"without any constraints\"."),
]
REPO = w.REPO
items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
chars = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in ("reuben", "brick")}


def read(p):
    return open(p, encoding="utf-8").read().strip()


def after_brief(template, slug, rid):
    d = chars[slug]
    pron = d.get("gender_identity", "they/them")
    p = template.format(brief="[the brief]", char=d["name"], pronouns=pron, obj=w.OBJ.get(pron, "them"),
                        tidbits=w.tidbits(d, items, texting=w.TEXTING[rid]))
    head, tail = p.split("[the brief]", 1)
    return (head.strip() + "\n\n[the brief]\n\n" + tail.strip()).strip()


runs = []
for rid, label, sub, about in RUNS:
    O = os.path.join(BASE, sub)
    P = w.PROMPTS[rid]
    rec = json.load(open(os.path.join(O, "chat-reuben-brick", "run.json"), encoding="utf-8"))["writers"]
    chat = P["chat"].format(brief="[the brief]", a=chars["reuben"]["name"], b=chars["brick"]["name"],
                            a_profile=f"[{chars['reuben']['name']}, as this writer wrote him]",
                            b_profile=f"[{chars['brick']['name']}, as this writer wrote her]")
    runs.append({"id": rid, "label": label, "about": about,
                 "writers": [{"id": wid, "name": name,
                              "chat": read(os.path.join(O, "chat-reuben-brick", f"{wid}.md")),
                              "reuben": read(os.path.join(O, "reuben", f"{wid}.md")),
                              "brick": read(os.path.join(O, "brick", f"{wid}.md"))}
                             for wid, name in w.WRITERS if wid in rec and "error" not in rec[wid]],
                 "how": {"system": P["system"], "brief": w.brief(),
                         "profile_reuben": after_brief(P["user"], "reuben", rid),
                         "profile_brick": after_brief(P["user"], "brick", rid), "chat": chat}})
data = {"player": chars["reuben"]["name"], "match": chars["brick"]["name"], "runs": runs}
t = read(os.path.join(os.path.dirname(os.path.abspath(__file__)), "writers_page.html"))
out = t.replace("__DATA__", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
open(os.path.join(BASE, "writers", "index.html"), "w", encoding="utf-8").write(out + "\n")
print(", ".join(f"{r['id']}: {len(r['writers'])} writers" for r in runs), "|", len(out.encode()) // 1024, "KB")
