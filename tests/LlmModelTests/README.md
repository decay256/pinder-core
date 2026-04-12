# LLM Model Tests — Overlay & Delivery

Tests which models work best for delivery rewrites and horniness overlay calls.

## How to run

```bash
# Requires keys:
export ANTHROPIC_API_KEY=...
# Groq key from vault:
bash /root/.openclaw/agents-extra/swarm-infra/skills/token-vault/scripts/vault.sh get groq_api_key > /tmp/groq_key.txt

python3 tests/LlmModelTests/OverlayModelTest.py
```

## What it tests

**Section A — Delivery (large-context models only)**
Full 33k-char engine system prompt + delivery user message.
Only models with >8k context: Claude Sonnet, llama-3.3-70b, kimi-k2.

**Section B — Overlay (all models)**
Short ~700-char system prompt with opponent context (bio + items).
Uses updated A+C latent-heat instruction from delivery-instructions.yaml.
All Groq models + Claude.

Base message: always the raw intended message ("The chopsticks are a power move..."),
not the tier-modified version.

## Current recommendations (2026-04-12)

| Call type | Recommended model | Reason |
|---|---|---|
| Delivery (tier modifier) | Claude Sonnet | Best character voice, handles full system prompt |
| Overlay (horniness) | Claude Sonnet | Best on literal intended message; minimal word changes, stays in domain |

## Overlay prompt design (A+C)

- **A**: Opponent context (bio + items) injected into overlay system prompt
- **C**: Latent-heat instruction: surface charge with minimum word changes, stay in physical domain. No fire/heat metaphors. Synonyms must remain plausible for the original object.

## CI note

LLM-in-the-loop tests — non-deterministic. Run manually before changing overlay model config. Do not add to automated CI.
