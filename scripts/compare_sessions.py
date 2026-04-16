#!/usr/bin/env python3
"""Pairwise comparison — exactly 2 rows per turn: Gerald's sent message + Sable's reply."""
import re

FILES = {
    "Gemma 26B": "/root/.openclaw/agents-extra/pinder/design/playtests/session-083-gerald_42-vs-sable_xo.md",
    "Sonnet 4":  "/root/.openclaw/agents-extra/pinder/design/playtests/session-084-gerald_42-vs-sable_xo.md",
    "Qwen3 30B": "/root/.openclaw/agents-extra/pinder/design/playtests/session-085-gerald_42-vs-sable_xo.md",
}

def parse_turns(path):
    text = open(path).read()
    split = re.compile(r'## ═══ TURN (\d+) ═══')
    parts = split.split(text)
    turns = []

    for i in range(1, len(parts), 2):
        num = int(parts[i])
        body = parts[i+1] if i+1 < len(parts) else ""

        # ── Find the final sent message ──────────────────────────────────────
        # The session log shows transforms in order as Diff (LayerName) blocks.
        # The LAST diff in the sequence is the final sent message.
        # If no diffs, use Intended (clean success). If no Intended, use plain quote.
        
        sent = ""
        
        # All Diff blocks in order — last one is the final message
        # Format: **Diff (LayerName):** "text" or **Diff (LayerName):** "~~old~~ ***new***"
        # Match Diff (LayerName) where LayerName may contain nested parens e.g. Shadow (Madness)
        all_diffs = re.findall(r'\*\*Diff \((?:[^()]*|\([^)]*\))+\):\*\*[\s\*]*"(.+?)"', body, re.DOTALL)
        if all_diffs:
            # Clean diff markup: ~~removed~~ and ***added*** — keep only added text
            last = all_diffs[-1].strip()
            # Remove strikethrough: ~~text~~
            last = re.sub(r'~~.+?~~\s*', '', last)
            # Remove bold-italic markers: ***text*** → text
            last = re.sub(r'\*\*\*(.+?)\*\*\*', r'\1', last)
            sent = last.strip()
        
        # No diffs — try Delivered
        if not sent:
            matches = re.findall(r'\*\*Delivered[^:]*:\*\* "(.+?)"', body, re.DOTALL)
            if matches: sent = matches[-1].strip()
        
        # Clean success — Intended
        if not sent:
            m = re.search(r'\*\*Intended:\*\* "(.+?)"', body, re.DOTALL)
            if m: sent = m.group(1).strip()
        
        # Plain quoted line (very clean success)
        if not sent:
            sends_block = re.search(r'\*\*📨 .+? sends:\*\*\s*\n((?:> .+\n?)+)', body)
            if sends_block:
                block = sends_block.group(1)
                quoted = re.findall(r'> "?(.+?)"?\s*$', block, re.MULTILINE)
                if quoted:
                    real = [q for q in quoted if not q.startswith('**') and len(q) > 10]
                    if real: sent = real[-1].strip()
        
        # Add steering question if it was appended
        steer = re.search(r'\*Gerald_42 adds:\* "(.+?)"', body)
        if steer and sent:
            sent = sent.rstrip('.!?') + " " + steer.group(1).strip()
        
        # ── Find Sable's reply ───────────────────────────────────────────────
        opp = re.search(r'📩 .+ replies:\*\*\s*\n((?:> .+\n?)+)', body)
        reply = ""
        if opp:
            raw = opp.group(1)
            # Remove the "> " prefix from each line and join
            lines = re.findall(r'> (.+)', raw)
            reply = " ".join(l.strip() for l in lines).strip()
        
        # Clean up [OPPONENT] artifacts
        sent = re.sub(r'\[OPPONENT\]\s*', '', sent or '').strip() or '*(no message extracted)*'
        reply = re.sub(r'\[OPPONENT\]\s*', '', reply or '').strip() or '*(no reply)*'
        
        # ── Interest delta ──────────────────────────────────────────────────
        int_m = re.search(r'Interest: [█░]+ +(\d+)/25 +\(([+-]?\d+)\)', body)
        interest = int_m.group(1) if int_m else '?'
        delta = int_m.group(2) if int_m else '?'
        
        turns.append({"num": num, "sent": sent, "reply": reply, "interest": interest, "delta": delta})

    return turns

sessions = {name: parse_turns(path) for name, path in FILES.items()}

PAIRS = [
    ("Gemma 26B", "Sonnet 4"),
    ("Gemma 26B", "Qwen3 30B"),
    ("Sonnet 4",  "Qwen3 30B"),
]

PAIR_FILES = {
    ("Gemma 26B", "Sonnet 4"):  "comparison-gemma-sonnet.md",
    ("Gemma 26B", "Qwen3 30B"): "comparison-gemma-qwen.md",
    ("Sonnet 4",  "Qwen3 30B"): "comparison-sonnet-qwen.md",
}

BASE = "/root/.openclaw/agents-extra/pinder/design/playtests/"

for (a, b) in PAIRS:
    fname = BASE + PAIR_FILES[(a, b)]
    turns_a = sessions[a]
    turns_b = sessions[b]
    max_t = max(len(turns_a), len(turns_b))

    lines = []
    lines.append(f"# {a} vs {b}")
    lines.append("")
    lines.append("Gerald vs Sable · Seed 42")
    lines.append("")
    lines.append("Each turn: two rows. **Gerald** = what he sent. **Sable** = her reply.")
    lines.append("")

    for i in range(max_t):
        ta = turns_a[i] if i < len(turns_a) else {"sent": "—", "reply": "—"}
        tb = turns_b[i] if i < len(turns_b) else {"sent": "—", "reply": "—"}

        lines.append(f"## Turn {i+1}")
        lines.append("")
        lines.append(f"| | **{a}** | **{b}** |")
        lines.append("|---|---|---|")
        lines.append(f"| **Gerald** | {ta['sent']} | {tb['sent']} |")
        lines.append(f"| **Sable** | {ta['reply']} | {tb['reply']} |")
        da = ta.get('delta','?'); db = tb.get('delta','?')
        ia = ta.get('interest','?'); ib = tb.get('interest','?')
        lines.append(f"| **Interest** | {ia}/25 ({da}) | {ib}/25 ({db}) |")
        lines.append("")

    open(fname, 'w').write('\n'.join(lines))
    print(f"Written: {fname}")

# Verify first few turns have no empty cells
print("\nSpot check — Turn 1 sent messages:")
for name, turns in sessions.items():
    if turns:
        print(f"  {name}: {turns[0]['sent'][:80]}")
