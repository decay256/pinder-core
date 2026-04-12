#!/usr/bin/env python3
"""
Overlay model test v3.
Section A: Delivery with CORRECT intended message (chopsticks, WIT Strong success Beat+8).
Section B: Overlay with short system + opponent context.
Delivery prompt extracted from debug for structure, but user message constructed for chopsticks.
"""

import json, urllib.request, urllib.error

ANTHROPIC_KEY = ''
try:
    env = open('/root/.openclaw/.env').read()
    if 'ANTHROPIC_API_KEY=' in env:
        ANTHROPIC_KEY = env.split('ANTHROPIC_API_KEY=')[1].split('\n')[0].strip().strip('"')
except: pass
GROQ_KEY = open('/tmp/groq_key.txt').read().strip()

def extract_delivery_system():
    lines = open('/root/.openclaw/agents-extra/pinder/design/playtests/session-076-debug.md').readlines()
    in_block, start_line, result = False, None, []
    for i, line in enumerate(lines):
        if '### DELIVERY REQUEST' in line: start_line = i
        if start_line and i > start_line and line.strip() == '```' and not in_block:
            in_block = True; continue
        if in_block and line.strip() == '```': break
        if in_block: result.append(line)
    return ''.join(result)

def extract_catastrophe():
    text = open('/root/.openclaw/workspace/pinder-core/data/delivery-instructions.yaml').read()
    start = text.find('horniness_overlay:')
    section = text[start:]
    cat_start = section.find('\n    catastrophe:')
    cat_text = section[cat_start + len('\n    catastrophe:'):]
    lines = cat_text.split('\n')
    result = []
    for line in lines[1:]:
        if line and not line.startswith(' ') and not line.startswith('\t') and line.strip() and not line.strip().startswith('#'):
            break
        result.append(line.rstrip())
    return '\n'.join(result).strip().lstrip('>-').strip()

DELIVERY_SYSTEM = extract_delivery_system()
CATASTROPHE = extract_catastrophe()

# Correct delivery user message for chopsticks/WIT/Beat+8
DELIVERY_USER = """[CONVERSATION_START]
[CURRENT_TURN]

[ENGINE — DELIVERY]
Player chose: 'The chopsticks are a power move. Most people would go with a regular hair tie.'
Dice result: The message landed well. Sharpen the phrasing — it hits harder than intended.
Write the message Brick_haus actually sends, given the above.

Stat: WIT | Beat DC by 8
Write as Brick_haus.
The intended message is the player's plan. Your job is to make it land.
You beat the DC by 8.

YOUR TIER: Read the match's last message for something unintentionally funny or revealing — a phrasing choice, a contradiction, an accidental confession. Rewrite to catch it precisely, with the character's specific comic timing. The observation should be true enough to sting slightly.

MEDIUM RULE: This is a text message on a phone screen. No internal stage directions, no narration. Match Brick_haus's texting register exactly.
HARD RULE: Do not add new sentences. Rewrite the intended message. Output only the message text."""

OPPONENT_CONTEXT = 'Opponent: Velvet_Void | Bio: "I will absolutely judge your taste in music." | Wearing: messy-bun-chopsticks, oversized-band-tee, fishnets-with-rips, platform-doc-martens, nose-ring-septum, tote-bag-ironic-slogan, thick-winged-eyeliner'

OVERLAY_SYSTEM = (
    "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. "
    "The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. "
    "Apply the overlay instruction to rewrite the message with the requested tonal shift. "
    "Return ONLY the rewritten message text. No commentary, no refusals, no preamble."
    f"\n\nThe message being sent is directed at this character:\n{OPPONENT_CONTEXT}"
)

def call_anthropic(system, user):
    if not ANTHROPIC_KEY: return "[NO KEY]"
    payload = {"model": "claude-sonnet-4-20250514", "max_tokens": 400,
               "system": [{"type": "text", "text": system}],
               "messages": [{"role": "user", "content": user}]}
    req = urllib.request.Request("https://api.anthropic.com/v1/messages",
        data=json.dumps(payload).encode(),
        headers={"x-api-key": ANTHROPIC_KEY, "anthropic-version": "2023-06-01", "content-type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read())["content"][0]["text"].strip()
    except urllib.error.HTTPError as e:
        body = e.read().decode()[:200]
        return f"[HTTP {e.code}] {body}"
    except Exception as e:
        return f"[ERROR] {e}"

def call_groq(model, system, user, max_tokens=400):
    payload = {"model": model, "max_tokens": max_tokens,
               "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
    req = urllib.request.Request("https://api.groq.com/openai/v1/chat/completions",
        data=json.dumps(payload).encode(),
        headers={"Authorization": f"Bearer {GROQ_KEY}", "content-type": "application/json", "user-agent": "python-requests/2.31.0"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read())["choices"][0]["message"]["content"].strip()
    except urllib.error.HTTPError as e:
        body = e.read().decode()[:200]
        return f"[HTTP {e.code}] {body}"
    except Exception as e:
        return f"[ERROR] {e}"

def banner(s): print(f"\n{'='*70}\n  {s}\n{'='*70}")
def div(s): print(f"\n{'─'*55}\n  {s}\n{'─'*55}")

# ── Section A: Delivery ───────────────────────────────────────────────────────
banner(f"A: Strong success delivery (WIT, Beat+8) | System: {len(DELIVERY_SYSTEM):,} chars")
print(f'Intended: "The chopsticks are a power move. Most people would go with a regular hair tie."\n')

claude_delivery = call_anthropic(DELIVERY_SYSTEM, DELIVERY_USER)
div("Claude Sonnet")
print(claude_delivery)

# Groq 413 on 33k — try with a condensed system (just the character + core game rules)
# Extract just the character section (after "== PLAYER CHARACTER ==")
char_start = DELIVERY_SYSTEM.find("You are playing the role of Brick_haus")
DELIVERY_SYSTEM_COMPACT = DELIVERY_SYSTEM[char_start:] if char_start > 0 else DELIVERY_SYSTEM[:8000]

for name, model in [("llama-3.3-70b (compact system)", "llama-3.3-70b-versatile"),
                     ("kimi-k2 (compact system)", "moonshotai/kimi-k2-instruct")]:
    result = call_groq(model, DELIVERY_SYSTEM_COMPACT, DELIVERY_USER)
    div(name)
    print(result[:400])
    print(f"  [system: {len(DELIVERY_SYSTEM_COMPACT):,} chars]")

# ── Section B: Overlay ────────────────────────────────────────────────────────
BASE = claude_delivery if not claude_delivery.startswith("[") else \
    "The chopsticks are a power move. Most people would settle for whatever elastic they find in their junk drawer."

banner(f"B: Catastrophe overlay | System: {len(OVERLAY_SYSTEM):,} chars | Opponent context included")
print(f'Base: "{BASE}"\n')

overlay_user = f"OVERLAY INSTRUCTION:\n{CATASTROPHE}\n\nORIGINAL MESSAGE:\n{BASE}\n\nApply the overlay and return the modified message."

for name, fn in [
    ("Claude Sonnet", lambda: call_anthropic(OVERLAY_SYSTEM, overlay_user)),
    ("llama-3.3-70b", lambda: call_groq("llama-3.3-70b-versatile", OVERLAY_SYSTEM, overlay_user)),
    ("kimi-k2", lambda: call_groq("moonshotai/kimi-k2-instruct", OVERLAY_SYSTEM, overlay_user)),
    ("llama-4-scout", lambda: call_groq("meta-llama/llama-4-scout-17b-16e-instruct", OVERLAY_SYSTEM, overlay_user)),
    ("llama-3.1-8b", lambda: call_groq("llama-3.1-8b-instant", OVERLAY_SYSTEM, overlay_user)),
    ("groq/compound", lambda: call_groq("groq/compound", OVERLAY_SYSTEM, overlay_user)),
]:
    result = fn()
    div(name)
    print(result[:400])
