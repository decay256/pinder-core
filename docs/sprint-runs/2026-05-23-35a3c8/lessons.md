# Sprint Lessons Learned: 2026-05-23-35a3c8

### RESOURCE-EXHAUSTED-PROVIDERS-ESCALATION
**Title:** Google direct API 429 quota/resource exhaustion escalation.
**Symptom:** Subagent run fails with `RESOURCE_EXHAUSTED` (Google direct API 429 quota exceeded error).
**Root cause:** Shared-key direct Google API billing/quota was temporarily exhausted, blocking subsequent Rung 0 and Rung 1 calls.
**Rule:** When direct Google API 429 quota/resource exhaustion errors are encountered, immediately escalate the default rung of the role in `model-routing.yaml` to Rung 2 (`anthropic/claude-sonnet-4-6`) to route requests to the direct Anthropic API, bypassing the exhausted Google provider. Restoring the original model-routing settings at the end of the wave ensures subsequent sprints remain properly calibrated.
