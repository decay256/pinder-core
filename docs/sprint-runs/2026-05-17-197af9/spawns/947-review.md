You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-core PR #974** (fix for core#947 — wire Anthropic prompt cache on OpenRouter Sonnet-4.6 path). Implementer ran at Rung 2 sonnet; you run at Rung 1 deepseek per the offset rule.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` for any review-relevant lessons (esp. #206 and any LLM-adapter / OpenRouter / prompt-yaml lessons).
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-core
   gh pr view 974 --repo decay256/pinder-core --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 947 --repo decay256/pinder-core --json title,body
   gh pr diff 974 --repo decay256/pinder-core
   ```
4. Check out the branch:
   ```bash
   cd /root/projects/pinder-core
   git worktree add /tmp/work-947-review chore/947-anthropic-prompt-cache
   cd /tmp/work-947-review
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping.** The ticket has 5 ACs:
   - **AC1: Profile turn-prompt stability.** Impl reports system prompts are 16k+ chars and byte-identical across 3 turns — diagnosis done. Verify the methodology was sound (not just spot-check).
   - **AC2: Restructure for stable prefix ≥1024 tokens.** Per diagnosis, prefix already qualifies; no restructure. Sanity-check: confirm no prompt-template reordering happened (DO-NOT rule: "restructure ≠ rewrite").
   - **AC3: cache_control markers on stable prefix in Anthropic adapter.** Implementer wrapped the system message in content-block array `[{type:"text", text:..., cache_control:{type:"ephemeral"}}]` on BOTH `OpenAiLlmAdapter` (BuildRequestJson + BuildStatefulRequestJson) and `OpenAiTransport` / `OpenAiStreamingTransport`. Verify all four call sites are covered. Verify `AnthropicLlmAdapter` is unchanged (already had markers since #206).
   - **AC4: OpenRouter pass-through verification.** Impl claims OpenRouter docs confirm Sonnet-4.6 supports `cache_control: ephemeral` pass-through. Spot-verify by web search if you're unsure ("OpenRouter prompt caching anthropic claude-sonnet"). If OpenRouter actually doesn't pass through for Sonnet-4.6 the whole PR is moot.
   - **AC5: Regression test asserts `cache_read_input_tokens > 0` on turn 2+.** Impl shipped 6 mock-based tests in `Issue947_PromptCacheControlTests` that assert request-side shape (cache_control present + plain-string fallback). The mock-based regression substitutes for the live `cache_read > 0` smoke test that requires real API keys — that's defensible. Verify the tests actually pin BOTH the wrapped-content-block shape AND the plain-string fallback.

2. **The `UseAnthropicCacheControl` config toggle.** Per the ticket spawn: "if [OpenRouter doesn't forward], surface a config option to either route direct-Anthropic or skip the cache-control markers." Verify:
   - Default is `true` (markers enabled).
   - When toggled off, the request payload reverts to plain-string system messages (verified by a regression test).
   - Toggle name + location follow the existing `OpenAiOptions` pattern.

3. **`OpenAiCacheControl` helper.** Is it cleanly factored? Or is it inline duplicated logic? Look for:
   - Single helper that wraps a string into the content-block array shape.
   - Called from all four sites (LlmAdapter BuildRequestJson + BuildStatefulRequestJson, Transport, StreamingTransport).
   - Not over-engineered (no DI / factory pattern; it's a static helper).

4. **No out-of-scope edits.** Diff scan: 6 files for +349/-11. Should be:
   - `src/Pinder.LlmAdapters/OpenAiLlmAdapter.cs` (BuildRequestJson + BuildStatefulRequestJson)
   - `src/Pinder.LlmAdapters/OpenAiTransport.cs`
   - `src/Pinder.LlmAdapters/OpenAiStreamingTransport.cs`
   - New: `OpenAiCacheControl.cs` (helper)
   - Test file: `Issue947_PromptCacheControlTests.cs`
   - Maybe one place where the option is wired (e.g. `OpenAiOptions.cs`)
   
   No prompt-template edits. No `AnthropicLlmAdapter` changes. Confirm.

5. **46 pre-existing Rules failures.** Impl reports `Pinder.Rules.Tests` has 46 pre-existing failures verified against `origin/main`. Spot-verify:
   ```bash
   cd /tmp/work-947-review
   git checkout origin/main -- .
   dotnet test tests/Pinder.Rules.Tests/Pinder.Rules.Tests.csproj 2>&1 | tail -5
   git checkout HEAD -- .
   ```

6. **Build + test on the branch.**
   ```bash
   cd /tmp/work-947-review
   dotnet build pinder-core.sln 2>&1 | tail -10
   dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -10
   dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj 2>&1 | tail -10
   ```
   Confirm: 0 build errors; Core 2810 pass; LlmAdapters 1080 pass.

7. **Risk surface check: does the content-block array shape break OpenAI proper?** The change wraps system messages in an array of content blocks for ALL OpenAI-shape transports — which includes both OpenRouter (where cache_control is meaningful) AND any direct-OpenAI route (where cache_control is ignored). OpenAI's API accepts the content-block array shape too, but verify the impl considered this (a regression test for direct-OpenAI should still produce a valid request).

## Verdict format

End your review with exactly one of these structured verdicts:

```
VERDICT: APPROVE
<2-4 line summary of what's good and what residual concerns (if any) are out-of-scope follow-ups>
```

OR

```
VERDICT: CHANGES_REQUESTED
Blockers (must be fixed before merge):
- <specific file:line — what's wrong — what to do>
- <...>
Non-blocking notes:
- <...>
```

The orchestrator parses this verdict literally. Follow the format.

## Output style
Concise. Real issues only. This PR is the last impl of the sprint — don't ask for changes unless they're real blockers.

Report back with the verdict block + a posted GitHub review (`gh pr review 974 --approve` or `gh pr review 974 --request-changes --body "..."`).

**Note:** if `gh pr review --approve` is blocked with "Can not approve your own pull request" (decay256 owns both the PR and the gh token), fall back to `gh pr review 974 --comment --body "<verdict block>"` and put the structured verdict line in the comment body.

All upstream events follow USER.md response-style — short, lead with result, no tables.
