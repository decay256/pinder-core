# Manual LLM Overlay Model Evaluation

These scripts run live model comparisons for delivery rewrites and horniness
overlay calls. They are manual evaluation tools, not automated tests: they call
external LLM APIs, print candidate outputs, and require a human reviewer to
judge quality.

## How to run

```bash
# Requires an Anthropic key in the environment:
export ANTHROPIC_API_KEY=...

# Recommended server wrapper; reads the Groq key from the vault:
bash scripts/run-manual-overlay-model-comparison.sh

# Or run the maintained comparison directly after setting GROQ_API_KEY:
python3 scripts/manual-overlay-model-comparison.py
```

## What it evaluates

**Section A - Delivery (large-context models only)**
Full engine system prompt plus delivery user message. This compares whether
candidate models preserve character voice while applying a strong-success
rewrite instruction.

**Section B - Overlay**
Short overlay prompt with datee context. This compares whether candidate models
apply the horniness overlay while keeping the result in domain and near the
original message length.

Base message: always the raw intended message ("The chopsticks are a power
move..."), not the tier-modified version.

## Current recommendations (2026-04-12)

| Call type | Recommended model | Reason |
|---|---|---|
| Delivery (tier modifier) | Claude Sonnet | Best character voice, handles full system prompt |
| Overlay (horniness) | Claude Sonnet | Best on literal intended message; minimal word changes, stays in domain |

## Overlay prompt design (A+C)

- **A**: Datee context (bio + items) injected into overlay system prompt.
- **C**: Latent-heat instruction: surface charge with minimum word changes,
  stay in physical domain, avoid fire/heat metaphors, and keep synonyms
  plausible for the original object.

## Historical debug harness

`scripts/manual-overlay-model-debug-v3.py` is a historical debug harness that
uses server-local debug files under `/root/.openclaw`. Prefer the maintained
comparison script above unless reproducing that specific session.

## CI note

Do not wire these manual evals into CI. They are non-deterministic,
credentialed, and have no pass/fail oracle.
