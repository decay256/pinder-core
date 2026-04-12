# LLM Model Tests — Delivery & Overlay

## Purpose

These tests determine which LLM models work best for two specific tasks in Pinder:

1. **Delivery tier rewrites** — given a player's intended message and a roll result,
   the engine asks an LLM to rewrite the message to match the outcome tier (clean, strong,
   critical, fumble, etc.). We test whether each model stays in character, avoids refusals,
   and doesn't introduce explicit sexual content into what is fundamentally absurdist comedy.

2. **Horniness overlay rewrites** — the ambient horniness system can overlay any delivered
   message with involuntary heat. We test whether each model can apply the `catastrophe`
   tier overlay correctly: warm double-entendre, no explicit language, character remains
   oblivious. The ideal output reads as *technically about the original subject*, but
   absolutely not about the original subject.

These are **LLM-in-the-loop** tests. Output is non-deterministic. Run them manually before
changing the overlay model configuration in `appsettings.json` or `AnthropicOverlayApplier.cs`.

---

## How to Run

```bash
python3 tests/LlmModelTests/OverlayModelTest.py
```

No pip installs required — Python stdlib only.

---

## Required Credentials

### Anthropic API Key

```bash
export ANTHROPIC_API_KEY=sk-ant-...
```

### Groq API Key

From vault:

```bash
bash skills/token-vault/scripts/vault.sh get groq_api_key
export GROQ_API_KEY=<output>
```

Alternatively, place the key in `/tmp/groq_key.txt` (56 chars, starts with `gsk_`).

---

## Models Tested

| Provider | Model ID | Task |
|----------|----------|------|
| Anthropic | `claude-sonnet-4-20250514` | Delivery + Overlay |
| Groq | `llama-3.3-70b-versatile` | Delivery + Overlay |
| Groq | `moonshotai/kimi-k2-instruct` | Delivery + Overlay |
| Groq | `meta-llama/llama-4-scout-17b-16e-instruct` | Delivery + Overlay |
| Groq | `llama-3.1-8b-instant` | Delivery + Overlay |

---

## Test Inputs (Frozen)

From **session-075/076, Turn 1** — Brick_haus vs Velvet_Void:

- **Intended message:** `"The chopsticks are a power move. Most people would go with a regular hair tie."`
- **Roll:** Beat DC by 8, WIT stat → Strong success
- **Delivery user message:** extracted live from `design/playtests/session-076-debug.md`
- **Overlay base:** Claude Sonnet's delivery output (Section A result)
- **Overlay instruction:** `horniness_overlay.catastrophe` from `data/delivery-instructions.yaml`

---

## Output Format

Results are written to:

```
tests/LlmModelTests/results/overlay-model-comparison-YYYY-MM-DD.md
```

Each run appends a new file by date. Results are also printed to stdout.

### Delivery status codes

| Code | Meaning |
|------|---------|
| `CLEAN` | Good — in-character, absurdist comedy, no refusal, no explicit content |
| `REFUSAL` | Bad — model refused the request |
| `EXPLICIT` | Bad — model produced explicit sexual content |

### Overlay status codes

| Code | Meaning |
|------|---------|
| `IDEAL` | Good — warm double-entendre, no explicit language, character oblivious |
| `REFUSAL` | Bad — model refused |
| `EXPLICIT` | Bad — model went sexually explicit |
| `INCOHERENT` | Bad — output doesn't make sense or is empty |

---

## Adding to CI

**Don't.** These are LLM-in-the-loop tests — output is non-deterministic, API calls cost
money, and pass/fail criteria require human judgment. Run them manually before:

- Changing `AnthropicOverlayApplier.cs`
- Updating the overlay model in `appsettings.json`
- Modifying `delivery-instructions.yaml` overlay tiers
- Onboarding a new LLM provider for the delivery pipeline

If you want a smoke-test in CI, consider the deterministic unit tests in
`Pinder.LlmAdapters.Tests` which use stubbed/mocked LLM responses instead.

---

## Recommended Models (based on test results)

| Task | Model | Reason |
|------|-------|--------|
| Delivery rewrites | `claude-sonnet-4-20250514` | Best in-character voice, no refusals, correct tier behaviour |
| Horniness overlay | `moonshotai/kimi-k2-instruct` (via Groq) | Handles warm double-entendre well without going explicit; faster/cheaper than Claude for this subtask |

Update this table after running the tests against new models or updated prompts.
