You are a **no-context code reviewer** for pinder-core PR **#910** covering ticket **#902** (MetaPrefixStripper — strip LLM meta-prefix labels after every overlay).

Fresh-eye objectivity. You see only the artifact.

## ⚠️ IMPORTANT — known pre-existing breakage on main

Before you do anything else, read this:

`Pinder.LlmAdapters.Tests` has **72 pre-existing failures on `main`** today (tracked as **#909**). They are NOT caused by #902. The orchestrator cross-validated by running the suite against `origin/main` directly: identical 72-fail set, `comm -13` between baseline-fails and PR-fails returns empty.

⇒ Do **not** request changes for the LlmAdapters 72-fail count. That's #909's job. But DO verify the claim independently — run both `main` and the PR branch and confirm the failure sets match.

`Pinder.Core.Tests` is 2683/2683 green on both main and the PR branch.

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/code-reviewer.md`. Follow Review Checklist + Output Format strictly.
2. Read the ticket: `gh issue view 902 --repo decay256/pinder-core`.
3. Read PR: `gh pr view 910 --repo decay256/pinder-core` + `gh pr diff 910 --repo decay256/pinder-core`
4. Read the pre-existing-breakage tracking issue: `gh issue view 909 --repo decay256/pinder-core` (so you understand what's NOT in scope).
5. **Fresh worktree:**
   ```bash
   cd /root/projects/pinder-core
   git fetch origin
   git worktree add /tmp/review-902 origin/fix/902-meta-prefix-stripper-r1
   cd /tmp/review-902
   ```

## Critically verify

### A. Acceptance criteria (from #902 DoD)

- [ ] `Pinder.Core.Text.MetaPrefixStripper` exists; regex widened to include hyphens.
- [ ] `GameSession.ResolveTurnAsync` applies the strip after every overlay-producing LLM call (delivery, failure overlay, trap, horniness, shadow) — **5 call sites total**.
- [ ] Each successful strip emits a `"Meta-Prefix Strip"` `TextDiff` layer with proper before/after spans (matches `CallbackStripper` pattern).
- [ ] `DialogueOptionParsers` routes through the shared `MetaPrefixStripper` (also `OpenAiLlmAdapter`).
- [ ] Tests added per ticket §Tests — verify `WOULD-YOU-RATHER`, `CONTEXT`, `RECOGNITION`, `OPENER`, `GENUINE QUESTION` are covered.
- [ ] `LESSONS_LEARNED.md` updated with the "sanitization-invariants-must-run-after-each-stage" lesson.

### B. Correctness hazards specific to this PR

1. **Regex over-strip.** `^(?:[A-Z][A-Z\s\-]+):\s*` will match `OPPONENT PROFILE: text` too — is that desired? In the ticket #902 spec yes, but if any pipeline path actually wants to KEEP a label like `OPPONENT PROFILE:` (e.g. an internal prompt construction string somewhere routes through this same stripper), that's a regression. Spot-check: is the strip applied ONLY to LLM **output** boundaries (option text, delivered message), or could it accidentally hit a prompt-construction surface? Grep `MetaPrefixStripper.Strip` and inspect every callsite.
2. **TextDiff span correctness.** `CallbackStripper` emits diffs with `before`/`after` spans that are byte-accurate. Verify the new code computes spans correctly (start at 0, end at the strip length, before-text is the stripped prefix, after-text is empty). If the diff produces wrong spans, replay tooling breaks subtly.
3. **Empty-result-after-strip handling.** What does `DialogueOptionParsers` do if `MetaPrefixStripper.Strip(text)` returns an empty string (i.e. the entire option was a label)? Look at the `if (string.IsNullOrEmpty(text)) continue;` pattern. Is "skip option entirely" the right behavior, or should the fallback padding kick in? This affects "options returned" count when an LLM returns a degenerate response.
4. **Idempotence.** Calling `Strip(Strip(x))` should equal `Strip(x)` — verify by inspection or test.

### C. Cross-cutting

- **Snapshot schema discipline (AGENTS.md).** The new `"Meta-Prefix Strip"` TextDiff is added to the existing `text_diffs` list. Verify `text_diffs` is already a captured field in `session-runner/Snapshot/SessionSnapshot.cs` (it should be — `CallbackStripper` diffs already flow there). If not captured, this is a schema gap.
- **No drive-by changes.** PR description says 6 commits, well-scoped. Run `git log --oneline origin/main..HEAD` from the worktree and confirm each commit is on-scope.

### D. Tests soundness

```bash
cd /tmp/review-902
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/test-902-r-core.txt 2>&1
echo "core_exit=$?"
tail -3 /tmp/test-902-r-core.txt
# Expect: 0 failed
```

Then verify the LlmAdapters pre-existing-breakage claim:
```bash
# Run on PR branch
dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-restore > /tmp/test-902-r-llm-pr.txt 2>&1
grep -E "Failed!|Total tests" /tmp/test-902-r-llm-pr.txt | tail -2

# Run on main baseline (separate worktree)
git worktree add /tmp/review-902-baseline origin/main
cd /tmp/review-902-baseline
dotnet restore > /tmp/restore-baseline.log 2>&1
dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-restore > /tmp/test-902-r-llm-main.txt 2>&1
grep -E "Failed!|Total tests" /tmp/test-902-r-llm-main.txt | tail -2

# Compare failure sets (strip timing brackets to normalize)
grep "^  Failed " /tmp/test-902-r-llm-pr.txt | sed -E 's/ \[[0-9< ]+ ?ms\]$//' | sort -u > /tmp/r-fails-pr.txt
grep "^  Failed " /tmp/test-902-r-llm-main.txt | sed -E 's/ \[[0-9< ]+ ?ms\]$//' | sort -u > /tmp/r-fails-main.txt
echo "--- in PR but not main (i.e. caused by #902): ---"
comm -23 /tmp/r-fails-pr.txt /tmp/r-fails-main.txt
echo "--- in main but not PR (i.e. fixed by #902): ---"
comm -13 /tmp/r-fails-pr.txt /tmp/r-fails-main.txt
```

If the diff shows ZERO failures introduced by #902: confirm the claim and proceed to verdict. If anything DOES show up: report it as a blocker.

## Output

Post review with:
- `gh pr review 910 --repo decay256/pinder-core --approve|--request-changes|--comment ...`

**Self-approve is BLOCKED if gh-cli identity matches PR author** (SELF-APPROVE-BLOCKED). The PR was opened by the orchestrator (gh identity probably `decay256`), so post `--comment` with explicit `**Verdict: APPROVE|CHANGES_REQUESTED|NEEDS_DISCUSSION**` so the orchestrator parses it.

## Log to agent.log

```bash
cd /tmp/review-902 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#902" "core/text" "review-started" "Starting review for PR #910"
```

At exit:
```bash
cd /tmp/review-902 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#902" "core/text" "review-done" "Verdict=<APPROVE|REQUEST_CHANGES|COMMENT>, Findings=N"
```

## Reply in this session with

- One-paragraph verdict.
- Specific findings if any (code locations).
- Test result tails (Core, LlmAdapters-PR, LlmAdapters-main, the comm diff).
- gh review URL.
- agent.log lines appended.

DO NOT push commits. DO NOT merge. DO NOT bump submodule pointer.
