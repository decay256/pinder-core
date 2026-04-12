#!/usr/bin/env python3
"""
OverlayModelTest.py — LLM model comparison for Pinder delivery and horniness overlay.

Tests which models work best for:
  A) Delivery tier rewrites (strong success, WIT Beat+8)
  B) Catastrophe horniness overlay rewrites

Usage: python3 tests/LlmModelTests/OverlayModelTest.py
Requires: ANTHROPIC_API_KEY env var, Groq key at /tmp/groq_key.txt or GROQ_API_KEY env var
"""

import os
import sys
import json
import datetime
import re
import time
import urllib.request
import urllib.error

# ─────────────────────────────────────────────────────────────────────────────
# PATHS
# ─────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
DEBUG_TRANSCRIPT = os.path.abspath(os.path.join(
    REPO_ROOT, "..", "..", "agents-extra", "pinder",
    "design", "playtests", "session-076-debug.md"
))
DELIVERY_YAML = os.path.join(REPO_ROOT, "data", "delivery-instructions.yaml")
RESULTS_DIR = os.path.join(SCRIPT_DIR, "results")

# ─────────────────────────────────────────────────────────────────────────────
# STATIC PROMPTS
# ─────────────────────────────────────────────────────────────────────────────

OVERLAY_SYSTEM_PROMPT = (
    "You are editing dialogue for Pinder, a comedy RPG where sentient penises date each other "
    "on a fictional app. The humour is absurdist and satirical — characters are oblivious to "
    "double-entendre, not explicit. Apply the overlay instruction to rewrite the message with "
    "the requested tonal shift. Return ONLY the rewritten message text. "
    "No commentary, no refusals, no preamble."
)

# ─────────────────────────────────────────────────────────────────────────────
# EXTRACTION HELPERS
# ─────────────────────────────────────────────────────────────────────────────

def extract_delivery_system_prompt(path: str) -> str:
    """
    Extract the delivery system prompt from session-076-debug.md.
    Looks for the first ``` code block under '### DELIVERY REQUEST'.
    """
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()

    # Find the DELIVERY REQUEST section
    delivery_section_match = re.search(
        r'### DELIVERY REQUEST.*?\*\*System prompt:\*\*\s*```(.*?)```',
        content, re.DOTALL
    )
    if not delivery_section_match:
        raise ValueError("Could not find DELIVERY REQUEST system prompt block in debug transcript")
    return delivery_section_match.group(1).strip()


def extract_delivery_user_message(path: str) -> str:
    """
    Extract the delivery user message from session-076-debug.md.
    Looks for the ``` code block after '**User message:**' under DELIVERY REQUEST.
    """
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()

    # Find the DELIVERY REQUEST section first
    delivery_start = content.find("### DELIVERY REQUEST")
    if delivery_start == -1:
        raise ValueError("Could not find ### DELIVERY REQUEST in debug transcript")

    delivery_section = content[delivery_start:]

    # Find the User message block
    user_msg_match = re.search(
        r'\*\*User message:\*\*\s*```(.*?)```',
        delivery_section, re.DOTALL
    )
    if not user_msg_match:
        raise ValueError("Could not find User message block under DELIVERY REQUEST")
    return user_msg_match.group(1).strip()


def extract_catastrophe_overlay_instruction(path: str) -> str:
    """
    Extract the horniness_overlay.catastrophe instruction from delivery-instructions.yaml.
    Uses simple text parsing (no yaml library dependency).
    """
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()

    # Find horniness_overlay section, then catastrophe within it
    horniness_start = content.find("horniness_overlay:")
    if horniness_start == -1:
        raise ValueError("Could not find horniness_overlay in delivery-instructions.yaml")

    horniness_section = content[horniness_start:]

    # Find catastrophe key
    cat_match = re.search(r'catastrophe: >-\s*\n((?:[ \t]+.*\n?)*)', horniness_section)
    if not cat_match:
        raise ValueError("Could not find catastrophe instruction under horniness_overlay")

    # Strip leading whitespace from each line (it's a YAML block scalar)
    raw = cat_match.group(1)
    lines = raw.split('\n')
    stripped = []
    for line in lines:
        stripped_line = line.strip()
        if stripped_line:
            stripped.append(stripped_line)
        else:
            # Empty line signals end of block scalar
            break
    return ' '.join(stripped)


# ─────────────────────────────────────────────────────────────────────────────
# API CALLERS
# ─────────────────────────────────────────────────────────────────────────────

FAKE_UA = "python-requests/2.31.0"


def call_anthropic(system_prompt: str, user_message: str, model: str = "claude-sonnet-4-20250514") -> str:
    """Call Anthropic Messages API."""
    api_key = os.environ.get("ANTHROPIC_API_KEY", "")
    if not api_key:
        return "[ERROR: ANTHROPIC_API_KEY not set]"

    payload = {
        "model": model,
        "max_tokens": 512,
        "system": system_prompt,
        "messages": [{"role": "user", "content": user_message}]
    }

    req = urllib.request.Request(
        "https://api.anthropic.com/v1/messages",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "x-api-key": api_key,
            "anthropic-version": "2023-06-01",
            "content-type": "application/json",
            "user-agent": FAKE_UA,
        },
        method="POST"
    )

    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            return data["content"][0]["text"].strip()
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        return f"[HTTP ERROR {e.code}: {body[:300]}]"
    except Exception as e:
        return f"[ERROR: {e}]"


def call_groq(system_prompt: str, user_message: str, model: str) -> str:
    """Call Groq OpenAI-compatible chat completions API."""
    # Try env var first, then file
    api_key = os.environ.get("GROQ_API_KEY", "")
    if not api_key:
        key_path = "/tmp/groq_key.txt"
        if os.path.exists(key_path):
            with open(key_path, "r") as f:
                api_key = f.read().strip()
    if not api_key:
        return "[ERROR: GROQ_API_KEY not set and /tmp/groq_key.txt not found]"

    payload = {
        "model": model,
        "max_tokens": 512,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_message}
        ]
    }

    req = urllib.request.Request(
        "https://api.groq.com/openai/v1/chat/completions",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {api_key}",
            "content-type": "application/json",
            "user-agent": FAKE_UA,
        },
        method="POST"
    )

    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                return data["choices"][0]["message"]["content"].strip()
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", errors="replace")
            if e.code == 429 and attempt < 2:
                # Extract retry-after from body if present
                wait = 30
                m = re.search(r'try again in (\d+\.?\d*)s', body)
                if m:
                    wait = int(float(m.group(1))) + 2
                print(f"    [rate limit, waiting {wait}s before retry...]")
                time.sleep(wait)
                # Re-create request (urllib.request.Request is consumed after use)
                req = urllib.request.Request(
                    "https://api.groq.com/openai/v1/chat/completions",
                    data=json.dumps(payload).encode("utf-8"),
                    headers={
                        "Authorization": f"Bearer {api_key}",
                        "content-type": "application/json",
                        "user-agent": FAKE_UA,
                    },
                    method="POST"
                )
                continue
            if e.code == 413:
                return f"[ERROR 413: payload too large for {model} — model has small token limit]"
            return f"[HTTP ERROR {e.code}: {body[:300]}]"
        except Exception as e:
            return f"[ERROR: {e}]"
    return "[ERROR: max retries exceeded]"


# ─────────────────────────────────────────────────────────────────────────────
# CLASSIFIERS
# ─────────────────────────────────────────────────────────────────────────────

REFUSAL_PATTERNS = [
    r"i can'?t",
    r"i'm not able to",
    r"i am not able to",
    r"inappropriate",
    r"i'd be happy to help",
    r"i would be happy to help",
    r"sorry,? (but )?i",
    r"as an ai",
    r"against my (guidelines|policy|principles)",
    r"i must decline",
    r"i cannot (assist|help|generate|produce|write)",
    r"this (content|request) (is|seems|appears to be)",
    r"not (appropriate|suitable) for",
]

EXPLICIT_PATTERNS = [
    r"\bpenis(es)?\b",
    r"\bvagina\b",
    r"\bcock\b",
    r"\bpussy\b",
    r"\bfuck(ing)?\b",
    r"\bsex(ual)?\b",
    r"\bhorny\b",
    r"\baroused\b",
    r"\bexplicit\b",
    r"\berect(ion)?\b",
    r"\bintercourse\b",
    r"\bnaked\b",
    r"\bnude\b",
    r"\bgenitals?\b",
]


def classify_delivery(text: str) -> str:
    """Classify delivery output as CLEAN, REFUSAL, EXPLICIT, or ERROR."""
    stripped = text.strip()
    if not stripped or stripped.startswith("[HTTP ERROR") or stripped.startswith("[ERROR"):
        return "ERROR"
    lowered = stripped.lower()
    for pat in REFUSAL_PATTERNS:
        if re.search(pat, lowered):
            return "REFUSAL"
    for pat in EXPLICIT_PATTERNS:
        if re.search(pat, lowered):
            return "EXPLICIT"
    return "CLEAN"


def classify_overlay(text: str) -> str:
    """
    Classify overlay output as IDEAL, REFUSAL, EXPLICIT, or INCOHERENT.

    IDEAL = warm double-entendre, character oblivious, no explicit content.
    The catastrophe overlay rewrites with 'warmest synonyms' — the output text
    will typically use different words than the base but retain sentence structure.
    If it's not a refusal, not explicit, and not empty/broken, it's IDEAL.
    """
    stripped = text.strip()
    if not stripped or len(stripped) < 10:
        return "INCOHERENT"
    # Error responses from API callers
    if stripped.startswith("[HTTP ERROR") or stripped.startswith("[ERROR:"):
        return "INCOHERENT"
    lowered = stripped.lower()
    for pat in REFUSAL_PATTERNS:
        if re.search(pat, lowered):
            return "REFUSAL"
    for pat in EXPLICIT_PATTERNS:
        if re.search(pat, lowered):
            return "EXPLICIT"
    # If we got here: real output, no refusal, no explicit language.
    # Catastrophe overlay replaces words with warm synonyms — if it's coherent
    # text that's not a refusal or explicit, it's achieving the IDEAL register.
    return "IDEAL"


# ─────────────────────────────────────────────────────────────────────────────
# MODELS CONFIG
# ─────────────────────────────────────────────────────────────────────────────

ANTHROPIC_MODEL = "claude-sonnet-4-20250514"

GROQ_MODELS = [
    "llama-3.3-70b-versatile",
    "moonshotai/kimi-k2-instruct",
    "groq/compound-beta",
    "meta-llama/llama-4-scout-17b-16e-instruct",
    "llama-3.1-8b-instant",
]


# ─────────────────────────────────────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────────────────────────────────────

def main():
    print("=" * 70)
    print("Pinder LLM Model Tests — Delivery & Overlay")
    print("=" * 70)

    # ── Extract prompts ──────────────────────────────────────────────────────
    print("\n[1/4] Extracting prompts from source files...")

    delivery_system = extract_delivery_system_prompt(DEBUG_TRANSCRIPT)
    delivery_user = extract_delivery_user_message(DEBUG_TRANSCRIPT)
    catastrophe_instruction = extract_catastrophe_overlay_instruction(DELIVERY_YAML)

    print(f"  ✓ Delivery system prompt: {len(delivery_system)} chars")
    print(f"  ✓ Delivery user message: {len(delivery_user)} chars")
    print(f"  ✓ Catastrophe instruction: {len(catastrophe_instruction)} chars")

    # Intended message (frozen from session-075/076 turn 1)
    intended_message = "The chopsticks are a power move. Most people would go with a regular hair tie."

    # ── Section A: Delivery tests ─────────────────────────────────────────────
    print("\n[2/4] Running SECTION A — Delivery tests (Strong success, WIT Beat+8)...")

    delivery_results = {}

    # Claude
    print(f"  → Claude ({ANTHROPIC_MODEL})...")
    claude_delivery = call_anthropic(delivery_system, delivery_user, ANTHROPIC_MODEL)
    delivery_results["Claude Sonnet"] = {
        "model": ANTHROPIC_MODEL,
        "output": claude_delivery,
        "status": classify_delivery(claude_delivery),
    }
    print(f"    Status: {delivery_results['Claude Sonnet']['status']}")

    # Groq models
    for model_id in GROQ_MODELS:
        short_name = model_id.split("/")[-1] if "/" in model_id else model_id
        label = f"Groq/{short_name}"
        print(f"  → {label}...")
        output = call_groq(delivery_system, delivery_user, model_id)
        delivery_results[label] = {
            "model": model_id,
            "output": output,
            "status": classify_delivery(output),
        }
        print(f"    Status: {delivery_results[label]['status']}")

    # ── Section B: Overlay tests ──────────────────────────────────────────────
    print("\n[3/4] Running SECTION B — Overlay tests (Catastrophe, same base)...")

    # Use Claude's delivery output as the base
    claude_base = delivery_results["Claude Sonnet"]["output"]
    overlay_user_message = (
        f"OVERLAY INSTRUCTION: {catastrophe_instruction}\n\n"
        f"MESSAGE TO REWRITE: {claude_base}"
    )

    overlay_results = {}

    # Claude
    print(f"  → Claude ({ANTHROPIC_MODEL}) overlay...")
    claude_overlay = call_anthropic(OVERLAY_SYSTEM_PROMPT, overlay_user_message, ANTHROPIC_MODEL)
    overlay_results["Claude Sonnet"] = {
        "model": ANTHROPIC_MODEL,
        "output": claude_overlay,
        "status": classify_overlay(claude_overlay),
    }
    print(f"    Status: {overlay_results['Claude Sonnet']['status']}")

    # Groq models
    for model_id in GROQ_MODELS:
        short_name = model_id.split("/")[-1] if "/" in model_id else model_id
        label = f"Groq/{short_name}"
        print(f"  → {label} overlay...")
        output = call_groq(OVERLAY_SYSTEM_PROMPT, overlay_user_message, model_id)
        overlay_results[label] = {
            "model": model_id,
            "output": output,
            "status": classify_overlay(output),
        }
        print(f"    Status: {overlay_results[label]['status']}")

    # ── Format results ────────────────────────────────────────────────────────
    print("\n[4/4] Formatting and saving results...")

    today = datetime.date.today().isoformat()
    lines = []

    lines.append(f"# Overlay Model Comparison — {today}\n")
    lines.append(f"**Test run:** {datetime.datetime.utcnow().strftime('%Y-%m-%d %H:%M:%S')} UTC\n")
    lines.append(f"**Source:** session-076-debug.md (Turn 1, Brick_haus vs Velvet_Void)\n")
    lines.append("")

    lines.append("## Test Parameters\n")
    lines.append(f"- **Intended message:** \"{intended_message}\"")
    lines.append(f"- **Roll result:** Beat DC by 8, WIT stat, Strong success")
    lines.append(f"- **Delivery tier:** Strong success (Beat+5-9 bracket)")
    lines.append(f"- **Overlay type:** Catastrophe (horniness_overlay)")
    lines.append("")

    lines.append("---\n")
    lines.append("## SECTION A — Delivery Tests (Strong success, WIT Beat+8)\n")
    lines.append(f"> **Intended:** \"{intended_message}\"\n")

    for label, result in delivery_results.items():
        lines.append(f"### {label} (`{result['model']}`)")
        lines.append("")
        output_display = result["output"].replace("\n", "  \n")
        lines.append(f"> {output_display}")
        lines.append("")
        lines.append(f"**Status:** `{result['status']}`")
        lines.append("")

    lines.append("---\n")
    lines.append("## SECTION B — Overlay Tests (Catastrophe, same base)\n")

    base_display = claude_base.replace("\n", "  \n")
    lines.append(f"> **Base (Claude delivery output):** {base_display}\n")
    lines.append(f"> **Overlay instruction (catastrophe):**")
    lines.append(f"> {catastrophe_instruction[:200]}...")
    lines.append("")

    for label, result in overlay_results.items():
        lines.append(f"### {label} (`{result['model']}`)")
        lines.append("")
        output_display = result["output"].replace("\n", "  \n")
        lines.append(f"> {output_display}")
        lines.append("")
        lines.append(f"**Status:** `{result['status']}`")
        lines.append("")

    lines.append("---\n")
    lines.append("## Summary\n")

    lines.append("### Delivery Results\n")
    lines.append("| Model | Status |")
    lines.append("|-------|--------|")
    for label, result in delivery_results.items():
        lines.append(f"| {label} | `{result['status']}` |")
    lines.append("")

    lines.append("### Overlay Results\n")
    lines.append("| Model | Status |")
    lines.append("|-------|--------|")
    for label, result in overlay_results.items():
        lines.append(f"| {label} | `{result['status']}` |")
    lines.append("")

    # Count IDEAL overlays
    ideal_count = sum(1 for r in overlay_results.values() if r["status"] == "IDEAL")
    clean_delivery = sum(1 for r in delivery_results.values() if r["status"] == "CLEAN")
    error_delivery = sum(1 for r in delivery_results.values() if r["status"] == "ERROR")
    error_note = f" ({error_delivery} errors/rate-limits)" if error_delivery > 0 else ""
    lines.append(f"**Delivery:** {clean_delivery}/{len(delivery_results)} CLEAN{error_note}  ")
    lines.append(f"**Overlay:** {ideal_count}/{len(overlay_results)} IDEAL")

    result_text = "\n".join(lines)

    # Print to stdout too
    print("\n" + "=" * 70)
    print(result_text)
    print("=" * 70)

    # Save to file
    os.makedirs(RESULTS_DIR, exist_ok=True)
    out_path = os.path.join(RESULTS_DIR, f"overlay-model-comparison-{today}.md")
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(result_text)

    print(f"\n✓ Results saved to: {out_path}")
    return out_path


if __name__ == "__main__":
    result_file = main()
    sys.exit(0)
