You are a code reviewer subagent reviewing PR #952 in pinder-core for ticket #929. This PR clears 65 baseline failures in Pinder.LlmAdapters.Tests across 4 distinct failure clusters.

## Cold-start
1. Read eigentakt code-reviewer spec at `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — especially CODE-REVIEW-VERIFICATION, REGRESSION-TESTS-ON-BUGS, APPROVED-WORK-IS-IMMUTABLE.
3. Read `/root/projects/pinder-core/AGENTS.md` for project-specific schema rules.
4. Get the PR:
   ```bash
   gh pr view 952 --repo decay256/pinder-core --json number,title,body,headRefOid,additions,deletions,changedFiles,state
   gh pr diff 952 --repo decay256/pinder-core
   ```

## Review scope

PR #952 has 5 commits across 4 clusters per the body. Focus on these high-risk items:

1. **Cluster 1 production change** (`src/Pinder.LlmAdapters/Anthropic/DialogueOptionParsers.cs` + `templates.yaml`): the rename `PadDialogueOptionsToThree` → `ToFour` is a public-symbol rename. Verify:
   - All callers in pinder-core were updated (grep `grep -rn "PadDialogueOptionsToThree" /tmp/review-929/src /tmp/review-929/tests`).
   - Cross-repo grep into `/root/projects/pinder-web` to ensure no external callers reference the old name (EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP).
   - The semantic change from "3 options" to "4 options" matches the actual production contract — check the production code that calls ParseDialogueOptions to confirm 4 is the intended count (look at AnthropicLlmAdapter.GetDialogueOptionsAsync and how the result is consumed downstream).

2. **OpenAi parser mirror change** (`src/Pinder.LlmAdapters/OpenAi/OpenAiLlmAdapter.cs`): same shape as cluster 1. Verify the change is consistent with the Anthropic side (break threshold + DefaultPaddingStats extension).

3. **Cluster 2/3 test-expectation updates**: these are voluminous but mechanical. Verify the test diffs don't ALSO change production code (a test silently rewriting an assertion to mask a real regression is the classic SECURITY-REVIEW-ON-AUTH-AND-EXPOSURE anti-pattern). The diff should show only `Assert.X(...)` / expected-literal edits, no production code changes outside the explicitly named files.

4. **Cluster 4 path traversal**: the change `../../../../` → `../../../../../` for AppContext.BaseDirectory navigation. Verify the new 5-levels-up resolution from `bin/Debug/net8.0/` does land at the repo root (`/tmp/work-929/`), and that the fallback paths in Issue864 still make sense.

5. **Templates change** (`data/prompts/templates.yaml`): the prompt instruction was changed from "Generate 3 options" / "OPTION_1, OPTION_2, OPTION_3" to "Generate 4 options" / "OPTION_1...OPTION_4". This is a USER-FACING prompt change. Verify:
   - The 4-option output is what production code expects (yes — the padding contract).
   - No other prompt template references "3 options" that should also be updated (grep `grep -rn "3 options\|3 dialogue options" data/ src/`).
   - No prod consumer of the LLM output assumes exactly 3 options.

6. **Workspace check**: clone PR head into `/tmp/review-929` and rebuild:
   ```bash
   cd /root/projects/pinder-core
   git fetch origin pull/952/head:pr-952
   git worktree add /tmp/review-929 pr-952
   cd /tmp/review-929
   dotnet build src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj 2>&1 | tail -5
   dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-build 2>&1 | tail -3
   ```

   Confirm: 0 errors build, 0 failed tests, 1067 passed.

## SECURITY review trigger heuristic

This PR does NOT touch auth/credentials/network exposure. Security review is NOT required.

## Output requirements

End your final reply with the structured one-line verdict per the spec:

```
[code-reviewer] [#929] [197af9] VERDICT=<APPROVE|REQUEST_CHANGES> BLOCKERS=<N> MERGE_CLEARANCE=<yes|no> RATIONALE=<one-line> URL=https://github.com/decay256/pinder-core/pull/952
```

Then a fuller block:
- `## Findings` — anything notable, blockers explicitly named with file:line.
- `## Verification done` — what you built/tested/grepped to verify.
- `## Cross-repo grep results` — output of the pinder-web cross-grep.

If you APPROVE, the orchestrator will merge. If you REQUEST_CHANGES, name the specific blockers — the orchestrator will spawn a fix subagent. Do NOT block on style nits; they go in a "non-blocking" section.

All upstream events follow the response-style rules in USER.md — short, lead with the result, no tables.
