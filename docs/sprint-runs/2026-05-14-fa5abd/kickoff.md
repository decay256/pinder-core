# Sprint 2026-05-14-fa5abd — full-throttle backlog drain

**Orchestrator session:** ca4089c4-56d8-4a0f-aa9c-d3be08bcaf9b
**Started (UTC):** 2026-05-14T08:17:49Z
**Authorization:** Daniel — "Drain on all open issues, full throttle!" (2026-05-14)
**Repos in scope:** decay256/pinder-core, decay256/pinder-web
**Implementable count (pre-triage):** 13

## Scoped tickets (pre-refiner)

### pinder-core (12)
- #859 [security] Pinder.RemoteAssets: enforce https scheme on Configuration.BaseUrl
- #860 [security] Pinder.RemoteAssets: cap HttpClient.MaxResponseContentBufferSize
- #862 [bug] Player option intended_text contains meta-prefixes that survive pipeline
- #863 [bug] Delivery LLM splits one-paragraph intended message into two paragraphs
- #864 [bug] Horniness overlay at Catastrophe tier produces word-soup substitutions
- #865 [design-q] Shadow corruption produces 1000+ char walls of text — intended?
- #866 [design-q] Opponent response length unconstrained relative to player message
- #867 [perf/cost] Delivery prompt is 10.6k tokens on turn 1 — audit and right-size
- #868 [bug] Ship the 15-stem stake prompt as locked in #826 comment
- #869 [bug] Texting-style enforcement parity for opponent
- #870 [bug] Voice-isolation guard in OpponentResponseInstruction
- #871 [arch] Finish #843 — migrate Phases 2-5 prompt content to yaml (URGENT)

### pinder-web (1)
- #583 [bug,arch-concern] GameApi bundles stale copy of pinder-core/data/delivery-instructions.yaml

## Model routing

Loaded from <eigentakt>/model-routing.yaml. Rungs:
- Rung 0 — openrouter/google/gemma-4-31b-it
- Rung 1 — openrouter/deepseek/deepseek-v4-pro
- Rung 2 — anthropic/claude-sonnet-4-6 (pinned 4-6)
- Rung 3 — anthropic/claude-opus-4-7 (pinned 4-7)

Per the 2026-05-14 anti-stall rules: every implementer starts at Rung 0. Escalation via 4.escalate triggers only.

