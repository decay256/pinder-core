#!/usr/bin/env python3
"""
Test horniness overlay across multiple models with identical prompt context.
Reconstructed from session-075 turn 1: Brick_haus vs Velvet_Void.
"""

import json, os, re, urllib.error, urllib.request
from pathlib import Path

TOKEN_RE = re.compile(r"{([A-Za-z_][A-Za-z0-9_]*)}")
CATALOG_PATH = Path(__file__).resolve().parents[1] / "data" / "prompts" / "overlay-model-comparison.yaml"


def load_catalog(path=CATALOG_PATH):
    try:
        import yaml
    except ImportError as exc:
        raise RuntimeError(
            "PyYAML is required to load data/prompts/overlay-model-comparison.yaml"
        ) from exc

    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}

    if data.get("schema_version") != 1:
        raise ValueError(f"{path} must declare schema_version: 1")

    prompts = data.get("prompts")
    if not isinstance(prompts, dict):
        raise ValueError(f"{path} must define a prompts mapping")

    scenario = data.get("comparison_scenario")
    if not isinstance(scenario, dict):
        raise ValueError(f"{path} must define a comparison_scenario mapping")

    return data


def prompt_text(catalog, key, field):
    try:
        value = catalog["prompts"][key][field]
    except KeyError as exc:
        raise KeyError(f"missing prompt catalog field: prompts.{key}.{field}") from exc
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"prompt catalog field must be non-empty: prompts.{key}.{field}")
    return value


def render_template(template, values):
    def replace(match):
        token = match.group(1)
        if token not in values:
            raise KeyError(f"prompt template references {{{token}}} but no value was supplied")
        return values[token]

    return TOKEN_RE.sub(replace, template)

# Load Groq key from file if env not set
def get_groq_key():
    k = os.environ.get("GROQ_API_KEY", "").strip()
    if k: return k
    try:
        with open("/tmp/groq_key.txt") as f: return f.read().strip()
    except: return ""

ANTHROPIC_KEY = os.environ.get("ANTHROPIC_API_KEY", "").strip()
GROQ_KEY = get_groq_key()

CATALOG = load_catalog()

GAME_CONTEXT = prompt_text(CATALOG, "overlay-model-comparison-game-context", "system_prompt")
BRICK_PERSONALITY = prompt_text(CATALOG, "overlay-model-comparison-brick-personality", "system_prompt")
STRONG_SUCCESS_INSTRUCTION = prompt_text(CATALOG, "overlay-model-comparison-strong-success-instruction", "system_prompt")
CATASTROPHE_OVERLAY = prompt_text(CATALOG, "overlay-model-comparison-catastrophe-overlay", "system_prompt")

INTENDED_MESSAGE = CATALOG["comparison_scenario"]["intended_message"]
GROQ_MODELS = CATALOG["comparison_scenario"]["groq_models"]

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

system_delivery = render_template(
    prompt_text(CATALOG, "overlay-model-comparison-delivery-system", "user_template"),
    {
        "brick_personality": BRICK_PERSONALITY,
        "game_context": GAME_CONTEXT,
    },
)
user_delivery = render_template(
    prompt_text(CATALOG, "overlay-model-comparison-delivery-user", "user_template"),
    {
        "strong_success_instruction": STRONG_SUCCESS_INSTRUCTION,
        "intended_message": INTENDED_MESSAGE,
    },
)

delivered = {}

if ANTHROPIC_KEY:
    r = call_anthropic(system_delivery, user_delivery)
    delivered["claude-sonnet"] = r
    divider("Claude Sonnet (claude-sonnet-4-20250514)")
    print(r)
else:
    print("[SKIP] No Anthropic key")

for m in GROQ_MODELS:
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
    user_overlay = render_template(
        prompt_text(CATALOG, "overlay-model-comparison-overlay-user", "user_template"),
        {
            "catastrophe_overlay": CATASTROPHE_OVERLAY,
            "base_message": base_msg,
        },
    )
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
    user_overlay = render_template(
        prompt_text(CATALOG, "overlay-model-comparison-overlay-user", "user_template"),
        {
            "catastrophe_overlay": CATASTROPHE_OVERLAY,
            "base_message": claude_base,
        },
    )

    if ANTHROPIC_KEY:
        r = call_anthropic(GAME_CONTEXT, user_overlay)
        divider("Claude Sonnet → catastrophe overlay")
        print(r)

    for m in GROQ_MODELS:
        if GROQ_KEY:
            r = call_groq(m, GAME_CONTEXT, user_overlay)
            divider(f"Groq/{m} → catastrophe overlay")
            print(r)
else:
    print("[SKIP] No Claude delivery to use as base")
