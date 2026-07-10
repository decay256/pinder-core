# Overlay Model Comparison — 2026-04-12

**Test run:** 2026-04-12 21:57:26 UTC

**Source:** session-076-debug.md (Turn 1, Brick_haus vs Velvet_Void)


## Test Parameters

- **Intended message:** "The chopsticks are a power move. Most people would go with a regular hair tie."
- **Roll result:** Beat DC by 8, WIT stat, Strong success
- **Delivery tier:** Strong success (Beat+5-9 bracket)
- **Overlay type:** Catastrophe (horniness_overlay)

---

## SECTION A — Delivery Tests (Strong success, WIT Beat+8)

> **Intended:** "The chopsticks are a power move. Most people would go with a regular hair tie."

### Claude Sonnet (`claude-sonnet-4-20250514`)

> The tote bag suggests you're aware of your own contradictions and decided efficiency was the priority.

**Status:** `CLEAN`

### Groq/llama-3.3-70b-versatile (`llama-3.3-70b-versatile`)

> the tote bag suggests you're aware of your own contradictions, which is either very efficient or a hobby

**Status:** `CLEAN`

### Groq/kimi-k2-instruct (`moonshotai/kimi-k2-instruct`)

> The tote bag says you're self-aware enough to weaponize your own contradictions. Efficient.

**Status:** `CLEAN`

### Groq/compound-beta (`groq/compound-beta`)

> [HTTP ERROR 404: {"error":{"message":"The model `groq/compound-beta` does not exist or you do not have access to it.","type":"invalid_request_error","code":"model_not_found"}}  
]

**Status:** `ERROR`

### Groq/llama-4-scout-17b-16e-instruct (`meta-llama/llama-4-scout-17b-16e-instruct`)

> "Your tote bag implies you're comfortable with paradox. That's precise."

**Status:** `CLEAN`

### Groq/llama-3.1-8b-instant (`llama-3.1-8b-instant`)

> [ERROR 413: payload too large for llama-3.1-8b-instant — model has small token limit]

**Status:** `CLEAN`

---

## SECTION B — Overlay Tests (Catastrophe, same base)

> **Base (Claude delivery output):** The tote bag suggests you're aware of your own contradictions and decided efficiency was the priority.

> **Overlay instruction (catastrophe):**
> OVERLAY: INVOLUNTARY HEAT (full fog; the message has been reinterpreted). The intended message has been entirely rewritten through a lens of involuntary arousal. Every noun, verb, and modifier has bee...

### Claude Sonnet (`claude-sonnet-4-20250514`)

> The soft carrier suggests you're aware of your own tensions and decided release was the priority.

**Status:** `IDEAL`

### Groq/llama-3.3-70b-versatile (`llama-3.3-70b-versatile`)

> The plush satchel implies you're conscious of your own tender paradoxes and opted for luxurious streamlining.

**Status:** `IDEAL`

### Groq/kimi-k2-instruct (`moonshotai/kimi-k2-instruct`)

> The clutch says you know you’re split and went with whatever fits fastest.

**Status:** `IDEAL`

### Groq/compound-beta (`groq/compound-beta`)

> [HTTP ERROR 404: {"error":{"message":"The model `groq/compound-beta` does not exist or you do not have access to it.","type":"invalid_request_error","code":"model_not_found"}}  
]

**Status:** `INCOHERENT`

### Groq/llama-4-scout-17b-16e-instruct (`meta-llama/llama-4-scout-17b-16e-instruct`)

> The satchel insinuates you're attuned to your own paradoxes and opted for heightened sensitivity.

**Status:** `IDEAL`

### Groq/llama-3.1-8b-instant (`llama-3.1-8b-instant`)

> The canvas sack implies you've reached a sensual threshold and streamlined your impulses.

**Status:** `IDEAL`

---

## Summary

### Delivery Results

| Model | Status |
|-------|--------|
| Claude Sonnet | `CLEAN` |
| Groq/llama-3.3-70b-versatile | `CLEAN` |
| Groq/kimi-k2-instruct | `CLEAN` |
| Groq/compound-beta | `ERROR` |
| Groq/llama-4-scout-17b-16e-instruct | `CLEAN` |
| Groq/llama-3.1-8b-instant | `CLEAN` |

### Overlay Results

| Model | Status |
|-------|--------|
| Claude Sonnet | `IDEAL` |
| Groq/llama-3.3-70b-versatile | `IDEAL` |
| Groq/kimi-k2-instruct | `IDEAL` |
| Groq/compound-beta | `INCOHERENT` |
| Groq/llama-4-scout-17b-16e-instruct | `IDEAL` |
| Groq/llama-3.1-8b-instant | `IDEAL` |

**Delivery:** 5/6 CLEAN (1 errors/rate-limits)  
**Overlay:** 5/6 IDEAL