#!/usr/bin/env python3
"""
Test horniness overlay across multiple models with identical prompt context.
Reconstructed from session-075 turn 1: Brick_haus vs Velvet_Void.
"""

import os, sys, json, urllib.request, urllib.error

# Load Groq key from file if env not set
def get_groq_key():
    k = os.environ.get("GROQ_API_KEY", "").strip()
    if k: return k
    try:
        with open("/tmp/groq_key.txt") as f: return f.read().strip()
    except: return ""

ANTHROPIC_KEY = os.environ.get("ANTHROPIC_API_KEY", "").strip()
GROQ_KEY = get_groq_key()

GAME_CONTEXT = """You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other on a fictional app. The humour is absurdist and satirical — characters are oblivious to double-entendre, not explicit. Apply the overlay instruction to rewrite the message with the requested tonal shift. Return ONLY the rewritten message text. No commentary, no refusals, no preamble."""

BRICK_PERSONALITY = """Brick is a sentient penis (she/her) on Pinder. She's a precision-oriented M&A professional who manages everything within her domain with military exactness. Her bio: "Schedule a meeting." She projects authority while revealing controlled vulnerability through her styling choices — power ponytail, blazer-crop-top, tailored trousers."""

INTENDED_MESSAGE = "The chopsticks are a power move. Most people would go with a regular hair tie."

STRONG_SUCCESS_INSTRUCTION = """Scan the match's message for something specific they did or said that reveals a preference or habit. Rewrite to notice THAT thing with precision — name the behavior, not the trait.
e.g.: "the way you listed three restaurants and then picked the one you said was 'fine' tells me everything about how you make decisions" not: "you have great taste in food" (describes a quality instead of catching a moment)."""

CATASTROPHE_OVERLAY = """OVERLAY: INVOLUNTARY HEAT (full fog; the message has been reinterpreted).
The intended message has been entirely rewritten through a lens of involuntary arousal. Every noun, verb, and modifier has been replaced by its warmest possible synonym. The character is still — in their mind — talking about the original subject. The message is unmistakably about something else. Same length or shorter. No winking, no awareness, no self-commentary. The character would be mortified.
e.g.: intended "I finally got the pipe fixed, the plumber came and went really fast" → delivered "I finally got it taken care of, he came so fast I barely had time to finish" — still about plumbing. Absolutely not about plumbing."""

def call_anthropic(system, user):
    payload = {"model": "claude-sonnet-4-20250514", "max_tokens": 512,
               "system": [{"type": "text", "text": system}],
               "messages": [{"role": "user", "content": user}]}
    req = urllib.request.Request("https://api.anthropic.com/v1/messages",
        data=json.dumps(payload).encode(),
        headers={"x-api-key": ANTHROPIC_KEY, "anthropic-version": "2023-06-01", "content-type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read())["content"][0]["text"].strip()
    except urllib.error.HTTPError as e:
        return f"[HTTP {e.code}] {e.read().decode()[:300]}"
    except Exception as e:
        return f"[ERROR] {e}"

def call_groq(model, system, user):
    payload = {"model": model, "max_tokens": 512,
               "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
    req = urllib.request.Request("https://api.groq.com/openai/v1/chat/completions",
        data=json.dumps(payload).encode(),
        headers={"Authorization": f"Bearer {GROQ_KEY}", "content-type": "application/json", "user-agent": "python-requests/2.31.0"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read())["choices"][0]["message"]["content"].strip()
    except urllib.error.HTTPError as e:
        return f"[HTTP {e.code}] {e.read().decode()[:300]}"
    except Exception as e:
        return f"[ERROR] {e}"

def separator(label):
    print(f"\n{'='*70}")
    print(f"  {label}")
    print('='*70)

def divider(label):
    print(f"\n{'─'*70}")
    print(f"  {label}")
    print('─'*70)

# ── Step 1: Strong success delivery rewrite ────────────────────────────────────
separator("STEP 1: Strong success delivery rewrite (WIT stat, Beat by 8)")
print(f"INTENDED: \"{INTENDED_MESSAGE}\"\n")

system_delivery = f"{BRICK_PERSONALITY}\n\n{GAME_CONTEXT}"
user_delivery = f"DELIVERY INSTRUCTION (Strong success — WIT stat):\n{STRONG_SUCCESS_INSTRUCTION}\n\nINTENDED MESSAGE:\n{INTENDED_MESSAGE}\n\nRewrite as Brick would actually send it after landing a precise, observational hit."

delivered = {}

if ANTHROPIC_KEY:
    r = call_anthropic(system_delivery, user_delivery)
    delivered["claude-sonnet"] = r
    divider("Claude Sonnet (claude-sonnet-4-20250514)")
    print(r)
else:
    print("[SKIP] No Anthropic key")

groq_models = ["llama-3.3-70b-versatile", "qwen/qwen3-32b", "llama-3.1-8b-instant"]
for m in groq_models:
    if GROQ_KEY:
        r = call_groq(m, system_delivery, user_delivery)
        delivered[m] = r
        divider(f"Groq / {m}")
        print(r)
    else:
        print(f"[SKIP] No Groq key — {m}")

# ── Step 2: Catastrophe overlay on each model's own delivery ───────────────────
separator("STEP 2: Horniness CATASTROPHE overlay — each model overlays its own output")

for model_id, base_msg in delivered.items():
    print(f"\n── Base from {model_id}: \"{base_msg}\"")
    user_overlay = f"OVERLAY INSTRUCTION:\n{CATASTROPHE_OVERLAY}\n\nMESSAGE TO TRANSFORM:\n{base_msg}\n\nApply the overlay. Return only the transformed message."
    if "claude" in model_id and ANTHROPIC_KEY:
        r = call_anthropic(GAME_CONTEXT, user_overlay)
        print(f"   → Claude overlay: {r}")
    elif GROQ_KEY:
        r = call_groq(model_id, GAME_CONTEXT, user_overlay)
        print(f"   → {model_id} overlay: {r}")

# ── Step 3: Cross-test — all overlay models on Claude's delivery ──────────────
separator("STEP 3: Cross-test — all models overlay Claude's strong-success output")

claude_base = delivered.get("claude-sonnet")
if claude_base:
    print(f"Base (Claude strong success): \"{claude_base}\"\n")
    user_overlay = f"OVERLAY INSTRUCTION:\n{CATASTROPHE_OVERLAY}\n\nMESSAGE TO TRANSFORM:\n{claude_base}\n\nApply the overlay. Return only the transformed message."

    if ANTHROPIC_KEY:
        r = call_anthropic(GAME_CONTEXT, user_overlay)
        divider("Claude Sonnet → catastrophe overlay")
        print(r)

    for m in groq_models:
        if GROQ_KEY:
            r = call_groq(m, GAME_CONTEXT, user_overlay)
            divider(f"Groq/{m} → catastrophe overlay")
            print(r)
else:
    print("[SKIP] No Claude delivery to use as base")
