"""Build the eight-writers reading page: the template with every chat and profile embedded."""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import writers as w  # noqa: E402

O = os.path.join(w.REPO, "docs/tone-lab/scratch/writers")
REPO = w.REPO
items = {it["id"]: it for it in json.load(open(os.path.join(REPO, "data/items/starter-items.json"), encoding="utf-8"))}
chars = {s: json.load(open(os.path.join(REPO, "data/characters", f"{s}.json"), encoding="utf-8")) for s in ("reuben", "brick")}


def after_brief(slug):
    d = chars[slug]
    pron = d.get("gender_identity", "they/them")
    p = w.USER.format(brief="[the brief]", char=d["name"], pronouns=pron, obj=w.OBJ.get(pron, "them"),
                      tidbits=w.tidbits(d, items))
    return p.split("[the brief]", 1)[1].strip()


def read(p):
    return open(p, encoding="utf-8").read().strip()


runs = json.load(open(os.path.join(O, "chat-reuben-brick", "run.json"), encoding="utf-8"))["writers"]
data = {
    "player": chars["reuben"]["name"], "match": chars["brick"]["name"],
    "writers": [{"id": wid, "name": name,
                 "chat": read(os.path.join(O, "chat-reuben-brick", f"{wid}.md")),
                 "reuben": read(os.path.join(O, "reuben", f"{wid}.md")),
                 "brick": read(os.path.join(O, "brick", f"{wid}.md"))}
                for wid, name in w.WRITERS if wid in runs and "error" not in runs[wid]],
    "how": {"system": w.SYSTEM, "brief": w.brief(),
            "profile_reuben": after_brief("reuben"), "profile_brick": after_brief("brick"),
            "chat": w.CHAT.format(brief="[the brief]", a=chars["reuben"]["name"], b=chars["brick"]["name"],
                                  a_profile=f"[{chars['reuben']['name']}, as this writer wrote him]",
                                  b_profile=f"[{chars['brick']['name']}, as this writer wrote her]").split("[the brief]", 1)[1].strip()},
}
t = read(os.path.join(os.path.dirname(os.path.abspath(__file__)), "writers_page.html"))
js = json.dumps(data, ensure_ascii=False).replace("</", "<\\/")
out = t.replace("__DATA__", js)
open(os.path.join(O, "index.html"), "w", encoding="utf-8").write(out + "\n")
print(len(data["writers"]), "writers,", len(out.encode()) // 1024, "KB")
