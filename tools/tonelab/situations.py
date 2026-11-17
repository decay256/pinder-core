"""The tone lab's situation table, rendered as a static page.

tools/tonelab/variants/live/situations.yaml is the one source for everything the tone lab
tells the models: the prompt templates; what each move, roll result, interest level, swing,
beat, shadow, trap and thirsty slip means; the story clock's sentences; what a required note
asks for; the habit rule; the checks and what a retry says; the reader's questions. live.py
reads every sentence from it. This script renders the whole file as
docs/tone-lab/situations.html, laid out the way the prompts are built: first a map of each
call's messages and what fills each placeholder (the table's assembly section), then the
sections in the order the models see them, and selftest.py checks that every text in the file appears
on the page and that the committed page is current.

The table was seeded on 2026-11-16 from Pinder Core's data (the seeder is in git history,
up to commit 28b9f3e) and is edited here from then on; the game's files are not read at run
time.

  python3 tools/tonelab/situations.py html    render docs/tone-lab/situations.html
"""

import html
import os
import sys

import yaml

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
TABLE = os.path.join(HERE, "variants", "live", "situations.yaml")
PAGE = os.path.join(REPO, "docs", "tone-lab", "situations.html")

GROUPS = [  # page order: the prompts in the order the models see them; a section the file gains later goes last
    ("How the prompts are built", ["assembly"]),
    ("Prompt templates", ["prompts"]),
    ("The plan", ["session", "character", "stats"]),
    ("The director's system prompt", ["feelings", "checklist"]),
    ("Each turn's message, in the order it is told", ["turn", "interest", "traps", "clock", "needs", "beats", "moves",
                                                      "results", "shadows", "thirst", "swings", "notes", "habits",
                                                      "limits"]),
    ("Checks and retries", ["checks"]),
    ("After the chat", ["reader"]),
]
TITLES = {
    "assembly": "How the prompts are built", "prompts": "Prompt templates", "session": "Session: time and heat",
    "character": "Character card pieces", "stats": "Character stats", "feelings": "Feelings",
    "checklist": "Director's checklist", "turn": "The turn message's frame", "interest": "Interest levels",
    "traps": "Traps", "clock": "Story clock", "needs": "What each point in the chat needs", "beats": "Story beats",
    "moves": "Moves and how each roll lands", "results": "Roll results", "shadows": "Shadows taking over",
    "thirst": "Thirsty slips", "swings": "Interest swings in one turn", "notes": "Required notes",
    "habits": "Habit rule", "limits": "Limits the game checks", "checks": "Checks and retries",
    "reader": "Reader and grader",
}
SKIP = {"about", "sections"}

CSS = """
:root{--bg:#fbfaf7;--fg:#1e1d1a;--muted:#6b6860;--line:#e3e0d8;--card:#ffffff;--game:#2f6f4f;--new:#8a4b12;--code:#f3f1ec}
@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){--bg:#16161a;--fg:#e8e6e1;--muted:#a19d94;
--line:#33323a;--card:#1d1d22;--game:#7cc49c;--new:#e5a565;--code:#24242a}}
:root[data-theme="dark"]{--bg:#16161a;--fg:#e8e6e1;--muted:#a19d94;--line:#33323a;--card:#1d1d22;--game:#7cc49c;
--new:#e5a565;--code:#24242a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,sans-serif}
main{max-width:1180px;margin:0 auto;padding:24px 16px 80px}h1{font-size:26px;margin:0 0 6px}
h2{font-size:20px;margin:40px 0 6px;padding-top:12px;border-top:1px solid var(--line);scroll-margin-top:110px}h3{font-size:16px;margin:22px 0 6px}
h4{font-size:14px;margin:16px 0 4px;color:var(--muted)}p.lead,.muted{color:var(--muted)}
nav{position:sticky;top:0;background:var(--bg);padding:8px 0;border-bottom:1px solid var(--line);z-index:2;
display:flex;flex-wrap:wrap;gap:4px 14px}nav a{color:inherit;cursor:pointer;font-size:14px}nav b{font-size:12px;color:var(--muted);font-weight:600;margin-left:6px}.group{margin:48px 0 0;font-size:12px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}td.ph{font-family:ui-monospace,monospace;font-size:13px;width:150px}
input#q{width:100%;max-width:420px;padding:6px 10px;margin:10px 0;border:1px solid var(--line);border-radius:6px;
background:var(--card);color:var(--fg)}
.wrap{overflow-x:auto}table{border-collapse:collapse;width:100%;margin:6px 0 12px;background:var(--card)}
th,td{border:1px solid var(--line);padding:6px 8px;vertical-align:top;text-align:left;white-space:pre-wrap}
th{font-size:13px;color:var(--muted);font-weight:600}td.k{font-family:ui-monospace,monospace;font-size:13px;width:180px}
td.src{font-size:12px;color:var(--muted)}pre{white-space:pre-wrap;background:var(--code);border:1px solid var(--line);
border-radius:6px;padding:12px;font-size:13px}ol,ul{margin:4px 0 12px}
.tag{font-size:11px;padding:1px 6px;border-radius:9px;margin-left:8px;vertical-align:middle;border:1px solid}
.tag.game{color:var(--game)}.tag.new{color:var(--new)}code{font-family:ui-monospace,monospace;font-size:13px}
"""

JS = """
document.querySelectorAll('nav a').forEach(function(a){a.addEventListener('click',function(e){e.preventDefault();
var t=document.getElementById(a.getAttribute('data-to'));if(t)t.scrollIntoView({behavior:'smooth',block:'start'});});});
document.getElementById('q').addEventListener('input',function(){var q=this.value.toLowerCase();
document.querySelectorAll('tbody tr').forEach(function(r){r.style.display=!q||r.textContent.toLowerCase().indexOf(q)>=0?'':'none';});});
"""


def load(path=TABLE):
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def texts(node):
    """Every text in the table, for the selftest's check that the page shows all of it."""
    if isinstance(node, str):
        yield node
    elif isinstance(node, dict):
        for x in node.values():
            yield from texts(x)
    elif isinstance(node, list):
        for x in node:
            yield from texts(x)


def e(x):
    if x is True:
        return "yes"
    if x is None or x is False:
        return ""
    return html.escape(str(x))


def scalar(x):
    return x is None or isinstance(x, (str, int, float, bool))


def is_rows(node):
    vals = list(node.values()) if isinstance(node, dict) else node
    return bool(vals) and all(isinstance(r, dict) and all(scalar(v) for v in r.values()) for r in vals)


def cell(col, v):
    if col == "source":
        return f"<td class='src'>{e(v)}</td>"
    return f"<td>{e(v)}</td>"


def rows_table(node, key_title):
    """A dict or list of flat records as one table, a column per field."""
    items = list(node.items()) if isinstance(node, dict) else [(None, r) for r in node]
    cols = []
    for _, r in items:
        cols += [c for c in r if c not in cols]
    if "source" in cols:
        cols = [c for c in cols if c != "source"] + ["source"]
    head = ([f"<th>{e(key_title)}</th>"] if isinstance(node, dict) else []) + [f"<th>{e(c)}</th>" for c in cols]
    body = []
    for k, r in items:
        tds = ([f"<td class='k'>{e(k)}</td>"] if isinstance(node, dict) else []) + [cell(c, r.get(c)) for c in cols]
        body.append("<tr>" + "".join(tds) + "</tr>")
    return f"<div class='wrap'><table><thead><tr>{''.join(head)}</tr></thead><tbody>{''.join(body)}</tbody></table></div>"


def render(node, depth, key_title="key"):
    """Any part of the table: flat records as a table, texts as key/text rows, lists as lists,
    long multi-line texts (the prompt templates) as blocks."""
    if isinstance(node, list):
        if is_rows(node):
            return rows_table(node, key_title)
        if all(scalar(x) for x in node):
            return "<ol>" + "".join(f"<li><code>{e(x)}</code></li>" if depth >= 9 else f"<li>{e(x)}</li>" for x in node) + "</ol>"
        return "".join(render(x, depth + 1) for x in node)
    if not isinstance(node, dict):
        return f"<p>{e(node)}</p>"
    if is_rows(node):
        return rows_table(node, key_title)
    rest = {k: v for k, v in node.items() if k != "source"}
    if "source" in node and scalar(node["source"]) and is_rows(rest):
        return rows_table(rest, key_title) + f"<p class='muted'>Source: {e(node['source'])}</p>"
    out, flat = [], []

    def flush():
        if flat:
            out.append("<div class='wrap'><table><tbody>" + "".join(
                f"<tr><td class='k'>{e(k)}</td>{cell(k, v)}</tr>" for k, v in flat) + "</tbody></table></div>")
            flat.clear()
    h = f"h{min(depth + 3, 4)}"
    for k, v in node.items():
        if isinstance(v, str) and "\n" in v and len(v) > 300:
            flush()
            out.append(f"<{h}>{e(k)}</{h}><pre>{e(v)}</pre>")
        elif scalar(v):
            flat.append((k, v))
        elif isinstance(v, list) and all(scalar(x) for x in v):
            flush()
            out.append(f"<{h}>{e(k)}</{h}>" + render(v, 9 if k == "apart_words" else depth + 1))
        else:
            flush()
            out.append(f"<{h}>{e(k)}</{h}>" + render(v, depth + 1, k))
    flush()
    return "".join(out)


def source_tag(node):
    srcs = list(texts({k: v for k, v in node.items() if k == "source"} if isinstance(node, dict) else {}))
    if not srcs and isinstance(node, (dict, list)):
        vals = node.values() if isinstance(node, dict) else node
        srcs = [r.get("source") for r in vals if isinstance(r, dict) and r.get("source")]
    if not srcs:
        return ""
    new = all(str(x).startswith("new") for x in srcs)
    some = any(str(x).startswith("new") for x in srcs)
    label = "new" if new else "from the game, edited" if some else "from the game"
    return f"<span class='tag {'new' if new else 'game'}'>{label}</span>"


def anchor(path):
    return path.split(".")[0].split("[")[0]


def assembly(a):
    """The map of how each prompt is built: per call, each message's template and what fills each placeholder."""
    out = [f"<p class='muted'>{e(a['intro'])}</p>"]
    for call in a["calls"]:
        out.append(f"<h3>{e(call['name'])}</h3><p class='muted'>{e(call['when'])}</p>")
        for m in call["messages"]:
            tpl = " + ".join(f"<a href='#{anchor(p)}'><code>{e(p)}</code></a>" for p in m["template"])
            rows = "".join(
                f"<tr><td class='ph'>${{{e(ph)}}}</td><td>{e(x['what'])}</td><td>"
                + ", ".join(f"<a href='#{anchor(p)}'><code>{e(p)}</code></a>" for p in x.get("from") or [])
                + "</td></tr>" for ph, x in m["fills"].items())
            out.append(f"<h4>{e(m['message'])}</h4><p>Template: {tpl}</p><div class='wrap'><table><thead><tr>"
                       "<th>Placeholder</th><th>What fills it</th><th>From the table</th></tr></thead>"
                       f"<tbody>{rows}</tbody></table></div>")
    return "".join(out)


def page(t):
    groups = [(g, [k for k in keys if k in t]) for g, keys in GROUPS]
    placed = {k for _, keys in groups for k in keys}
    rest = [k for k in t if k not in placed and k not in SKIP]
    if rest:
        groups.append(("Other sections", rest))
    order = [k for _, keys in groups for k in keys]
    first = {keys[0]: g for g, keys in groups if keys}
    intro = t.get("sections") or {}
    nav = "".join((f"<b>{e(first[k])}</b>" if k in first and len(dict(groups)[first[k]]) > 1 else "")
                  + f"<a data-to='{k}'>{e(TITLES.get(k, k))}</a>" for k in order)
    body = []
    for k in order:
        node = t[k]
        if k in first and len(dict(groups)[first[k]]) > 1:
            body.append(f"<p class='group'>{e(first[k])}</p>")
        body.append(f"<h2 id='{k}'>{e(TITLES.get(k, k))}{source_tag(node) if k != 'assembly' else ''}</h2>")
        if intro.get(k):
            body.append(f"<p class='muted'>{e(intro[k])}</p>")
        if k == "assembly":
            body.append(assembly(node))
        elif k == "moves":
            for name, mv in node.items():
                body.append(f"<h3>{e(name)}</h3>" + render(mv, 1, "result"))
        else:
            body.append(render(node, 0, k[:-1] if k.endswith("s") else k))
    left = {k: v for k, v in intro.items() if k not in order}
    if left:
        body.append("<h2 id='other'>Other notes</h2>" + render(left, 0))
    return ("<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' "
            "content='width=device-width,initial-scale=1'><title>Situation table</title>"
            f"<style>{CSS}</style></head><body><main><h1>Situation table</h1><p class='lead'>{e(t.get('about', ''))}</p>"
            f"<nav>{nav}</nav><input id='q' placeholder='Filter table rows'>" + "".join(body)
            + f"</main><script>{JS}</script></body></html>\n")


def main():
    if sys.argv[1:] != ["html"]:
        sys.exit(__doc__)
    with open(PAGE, "w", encoding="utf-8") as f:
        f.write(page(load()))
    print(f"wrote {os.path.relpath(PAGE, REPO)}")


if __name__ == "__main__":
    main()
