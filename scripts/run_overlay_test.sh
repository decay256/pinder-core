#!/bin/bash
GROQ_API_KEY=$(bash /root/.openclaw/agents-extra/swarm-infra/skills/token-vault/scripts/vault.sh get groq_api_key 2>/dev/null)
export GROQ_API_KEY
export ANTHROPIC_API_KEY
python3 /root/.openclaw/workspace/pinder-core/scripts/test-overlay-models.py
