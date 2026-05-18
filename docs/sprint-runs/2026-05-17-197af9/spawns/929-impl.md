You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#929** as one PR in pinder-core.

## Workspace isolation (CRITICAL)
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-929 origin/main
cd /tmp/work-929
git checkout -b fix/929-llmadapters-tests-baseline
```

**Do NOT touch `/root/projects/pinder-core/` directly. All work in `/tmp/work-929/`.**

## Cold-start
1. Read eigentakt backend-engineer spec at `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — especially the named lessons WORKSPACE-ISOLATION, SUBMODULE-SYNC-AFTER-REBASE, SELF-APPROVE-BLOCKED, APPROVED-WORK-IS-IMMUTABLE, REGRESSION-TESTS-ON-BUGS, BUILD-PIPELINE-DISCIPLINE.
3. Read `/root/projects/pinder-core/AGENTS.md` — project schema rules.
4. `gh issue view 929 --repo decay256/pinder-core --json number,title,body,comments`. Note: #880 was just closed as duplicate; this is the canonical ticket. The cluster breakdown in the OWNER comment is the source of truth.

## Pathspec discipline (EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS)
- Never run `git add .`, `git add -A`, or `git add -u`.
- Stage with explicit file paths only.
- Do NOT commit `agent.log` (tracked, orchestrator-managed).
- Do NOT commit `.eigentakt-bin/`.

Tracked-file caution (EIGENTAKT-CHECK-TRACKED-BEFORE-GITIGNORE):
- Before .gitignoring or deleting any file, verify it is not tracked
  in origin/main: `git ls-tree origin/main <path>`.

Co-located test mirrors (EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE):
- Updates to a co-located `.test.ts` / `.test.tsx` are part of the
  source change when the test assertions hardcode literals the source
  edit reconciles. Document under `## Mirror test updates` in the
  Research Log.

Cross-repo grep for dead-code deletions (EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP):
- Before declaring any pinder-core symbol unused, grep BOTH
  `/root/projects/pinder-core/src,tests/` AND
  `/root/projects/pinder-web/src,tests/`. Document the grep in the
  Research Log.

Build evidence requirement (BUILD-PIPELINE-DISCIPLINE):
- Run the exact deploy build (`dotnet publish ...` for pinder-core game-api). NOT just the test runner. Capture tail in PR body under ## DoD Evidence.

## Big-picture approach (think before coding)

This ticket is a **test-infra baseline cleanup** with three distinct failure clusters that need different treatments:

1. **Dialogue-options padding (~20-30 tests, "Expected 4 / Actual 3")** — most suspicious cluster. Likely a real production regression where `ParseDialogueOptions` default-padding logic produces 3 instead of 4. Triage this FIRST. If it's a production bug, fix the code (don't just patch the tests). The fix likely affects:
   - `AnthropicLlmAdapterSpecTests.GetDialogueOptionsAsync_CompletelyUnparseableResponse_FourDefaults`
   - `ParseDialogueOptionsTests.Empty_string_returns_4_defaults`
   - `AnthropicLlmAdapterIssue208Tests.AC4_ParseDialogueOptions_Empty_Returns4Defaults`
   - many in `AnthropicLlmAdapterIssue208Tests`, `Issue240_DialogueOptionsFormatTests`

2. **AnthropicOptions surface widening** — `SpecAnthropicTests.AnthropicOptions_Has_Exactly10_PublicProperties` Expected 10 Actual 13. The production code is correct (properties were added intentionally); update the test to match the current surface.

3. **Instruction-template content drift** — `Issue489_*`, `Issue491_*`, `Issue544_*`, `Issue864_*`, `Issue865_*`, `SessionDocumentBuilder*` — the test asserts a literal substring that the template no longer contains. For each, run the test, read what the template actually emits now, decide:
   - If the new template is correct: update the test's expected literal.
   - If the test's intent was a stable behaviour: fix the template OR the test, whichever is the "right" behaviour.

4. **`Issue311_*`, `Issue372_*`, `OpenAi.*`, `EngineInjection*`** — older spec tests / sibling adapter. Same pattern: read, decide, fix or update.

## Implementation steps

1. Run `dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-build 2>&1 | tee /tmp/llmadapters-before.txt` (after a one-time `dotnet build`). Capture the FULL failure list. Categorize.
2. **For the dialogue-options padding cluster:** open the production code (`Pinder.LlmAdapters/Anthropic/ParseDialogueOptions*.cs` or similar — `grep -rn "ParseDialogueOptions" src/`). Find where the "pad to 4 defaults" should happen. Determine if it's a bug. If yes, fix it. If no, the tests should be updated.
3. **For the options-surface drift:** update `SpecAnthropicTests.AnthropicOptions_Has_Exactly10_PublicProperties` to match the current public-property count, or convert to a list-the-properties assertion that surfaces additions/removals (more useful).
4. **For instruction-template content drift:** for each Issue### test, decide if the template change was intentional. The OWNER comment on #929 suggests most are "template drift" (expected) — so update the test expectations. If you find a real regression, file a follow-up ticket.
5. **`[Fact(Skip = "...")]` is a last resort.** Per the AC: "fix, update assertion, or skip with explicit reason". Skip only with a tracking comment that names a follow-up ticket.

## Acceptance criteria (from ticket)
- [ ] `dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj` returns 0 failures.
- [ ] Root cause documented for each failure cluster in PR body or Research Log.
- [ ] Decide per cluster: fix production code (if regression) or update test expectations.
- [ ] If anything is a real production bug, file as separate non-test-infra ticket (e.g. dialogue-options padding) with `gh issue create` and reference in PR body.

## Workflow rules (mandatory)
- Atomic commits where possible (e.g. one commit per cluster).
- Run tests, capture to `/tmp/llmadapters-after.txt`, read tail/grep only. Do NOT pipe raw test output back into reasoning.
- Run the project's deploy build (`dotnet publish src/Pinder.GameApi/Pinder.GameApi.csproj -c Release`). Paste tail in DoD.
- Open PR via `gh pr create --repo decay256/pinder-core --base main --head fix/929-llmadapters-tests-baseline --fill`. Include `Closes #929` on its own line.

## DO NOT
- Do not merge.
- Do not push to main.
- Do not modify unrelated files.
- Do not work in `/root/projects/pinder-core/` — work in `/tmp/work-929/`.

## Logging to agent.log (AGENT-LOG-EVERYTHING)

At task entry, after reading cold-start materials:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#929" "Pinder.LlmAdapters.Tests" "started" "Triaging 65 baseline failures across 4 clusters"
```

At task exit, after PR is opened:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#929" "Pinder.LlmAdapters.Tests" "completed" "PR #N opened" "<commit-sha>"
```

## Output requirements

End your final reply with:
- Mandatory `## DoD Evidence` block: PR URL; test tail showing 0 failures; deploy build tail; `git log -1 --oneline`; push confirmation; `gh pr view N` output; agent.log entries.
- Mandatory `## Research Log` block — what you read, what you discovered per cluster, what you fixed vs what you updated tests for.
- Mandatory `## Filed follow-ups` block listing any new tickets (with URLs) if you discovered real production bugs.
- Flag any deviations from the spec with rationale.
