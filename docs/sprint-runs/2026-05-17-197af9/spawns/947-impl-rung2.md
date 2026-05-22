You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **core#947** in pinder-core: get Anthropic prompt caching actually working on the OpenRouter Sonnet-4.6 path. Currently `cache_read=0` every turn; input tokens grow ~2k/turn; cost rises monotonically.

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-947 origin/main
cd /tmp/work-947
git checkout -b chore/947-anthropic-prompt-cache
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on #871 (prompt-yaml migration epic) and any LLM-adapter / OpenRouter lessons.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 947 --repo decay256/pinder-core --json number,title,body,comments`.
5. Inspect the staging-test review dump if it exists:
   ```bash
   ls /tmp/staging-test-review/ 2>&1
   cat /tmp/staging-test-review/findings.md 2>&1 | head -50
   ```

## Diagnosis (this is the bulk of the work — measure before changing)

### Phase A.1 — token-stability profile

Build a small profiler (one-shot script, NOT shipped in the PR) that simulates a 3-turn session and dumps, for each turn, the message-list segment-by-segment:
- Token counts per segment (use tiktoken or the Anthropic tokenizer if available).
- Which segments are byte-stable vs which mutate between turns.

Goal: identify the "stable prefix" — the contiguous prefix segment from message[0] up to the first turn-mutating block. Report its token count. If <1024, that's root cause #1.

### Phase A.2 — cache_control marker audit

```bash
cd /tmp/work-947
grep -rn "cache_control\|ephemeral\|persistent\|prompt_cache" src 2>&1
# If zero hits: AnthropicLlmAdapter / OpenAiStreamingTransport aren't setting cache markers AT ALL. That's root cause #2.
```

Check:
- `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` — does it set cache_control on any message?
- `src/Pinder.LlmAdapters/OpenAiStreamingTransport.cs` (the OpenRouter-as-OpenAI-shape path) — does it forward Anthropic-specific cache_control fields?

### Phase A.3 — OpenRouter pass-through verification

The ticket flags this as plausible: "OpenRouter passes Anthropic prompt-cache through, but routes for Sonnet-4.6 may not."

Check `/root/projects/eigentakt/model-routing.yaml` for the Sonnet-4.6 route definition. Look at OpenRouter docs (web fetch ok if you must) for prompt-cache support on `anthropic/claude-sonnet-4.6` via OpenRouter. If the route is on OpenRouter and OpenRouter doesn't forward cache_control for that model, the fix is config-level (route direct to Anthropic for cacheable workloads).

## Implementation (only after Phase A diagnosis is complete and documented)

The fix is some combination of:

### Fix 1 — restructure prompts so the stable prefix is contiguous and ≥1024 tokens

If diagnosis shows the prefix is too short or interleaved with dynamic content, reorder. Per ticket: "Restructure the prompt so the stable prefix is ≥1024 tokens and at the *beginning* of the message list."

Concretely:
- All static content (system prompt, archetype directive, character sheets, stake) at the start.
- All dynamic content (conversation history, current state, options-asked) at the end.
- If reordering breaks any test, that's a sign the test was over-specific about message ordering — fix the test.

### Fix 2 — set cache_control markers on the stable prefix

In `AnthropicLlmAdapter.cs`:
- On the last message of the stable prefix, set `cache_control = { type: "ephemeral" }`.
- For longer-lived prefixes (e.g. system prompt that lives across sessions), `{ type: "persistent" }` is the better choice if Anthropic supports it.

If the path is OpenRouter (OpenAI-shape transport), the cache_control field has to be forwarded via the extra_body / unknown-fields mechanism. Verify OpenAI SDK / OpenRouter accepts the pass-through.

### Fix 3 — config gate

Per ticket: "if [OpenRouter doesn't forward], surface a config option to either route direct-Anthropic or skip the cache-control markers."

If you find OpenRouter Sonnet-4.6 doesn't forward cache_control, add a config flag like `Llm.UseAnthropicCacheControl` (default off) that's on for direct-Anthropic routes and off for OpenRouter — until OpenRouter fixes their side. Plus a one-line ADR-style note in code or `docs/` explaining the choice.

### Regression test

Per ticket: "log-assert `cache_read_input_tokens > 0` for turn 2+ when running a 3-turn smoke session."

Add an integration test (likely in `tests/Pinder.Core.Tests` or a new `tests/Pinder.LlmAdapters.Tests` test class) that:
- Mocks the Anthropic transport to return a usage block with `cache_creation_input_tokens > 0` on turn 1 and `cache_read_input_tokens > 0` on turn 2.
- Asserts the adapter correctly extracts and reports those numbers in its usage record.
- Asserts that on the request side, the constructed payload has `cache_control` set on the stable-prefix message.

(A FULL e2e smoke test requires real API tokens and is out of scope for a sprint chore. The mock-based regression test is the right level.)

## Tests + build

```bash
cd /tmp/work-947
dotnet build pinder-core.sln 2>&1 | tail -10
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -20
dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj 2>&1 | tail -20
```

## DoD evidence + Research Log

PR body MUST include:

```markdown
Closes #947

## Diagnosis findings
- **Stable prefix token count (turn 2):** <N tokens>
- **Stable prefix was interleaved:** <yes/no — which segments mutated where>
- **cache_control markers present before this PR:** <yes/no — which adapter>
- **OpenRouter Sonnet-4.6 cache_control pass-through:** <confirmed-supports / confirmed-strips / unknown>

## DoD Evidence
- [ ] Stable prefix restructured to be contiguous + ≥1024 tokens (if needed)
- [ ] `cache_control: ephemeral` markers set on stable prefix in Anthropic adapter
- [ ] OpenRouter path: <forwards markers natively / config-gated off / TBD>
- [ ] New regression test (mock-based) asserts cache_read_input_tokens > 0 on turn 2+
- [ ] `dotnet build`: clean
- [ ] `dotnet test`: <N/N pass across affected test projects>

## Research Log
<3-4 paragraphs: what the staging-test data showed, what the diagnosis revealed, what changed, what's deferred (e.g. real-API-key smoke test), what to watch for in the next sprint's staging-test review>
```

## Open PR

```bash
gh pr create --repo decay256/pinder-core --base main --head chore/947-anthropic-prompt-cache \
  --title "chore(#947): wire Anthropic prompt cache on OpenRouter Sonnet-4.6 path" \
  --body "<Diagnosis findings + DoD evidence + Research Log per template>

Closes #947"
```

Report back with the PR URL + commit SHA. If diagnosis revealed the root cause is purely a config-level issue (OpenRouter doesn't pass through markers for the chosen model), the PR may be tiny (just the config gate + ADR-style note) — that's fine, the diagnosis is the value.

## DO-NOT list
- Do NOT touch `/root/projects/pinder-core` directly. Use the worktree.
- **Do NOT merge the PR yourself.** (#949 + #654 impls violated this — orchestrator logged both. Don't be the third.)
- Do NOT push to `main`.
- Do NOT include unrelated edits.
- Do NOT ship a real-API-key smoke test in the PR (mock-based regression test only).
- Do NOT change unrelated prompt content while restructuring (move blocks, don't edit them — restructure-vs-rewrite is a sharp boundary).

All upstream events follow USER.md response-style — short, lead with result, no tables.

When done, your final report goes back to the orchestrator. I will handle review/merge.
