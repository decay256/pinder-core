You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-web PR #670** (companion fix for core#949 — preserve `- ` bullets in MarkdownSanitizer + submodule bump). Implementer ran at Rung 0 gemma; you run at Rung 1 deepseek per the offset rule.

## Context
- Core PR #967 was already merged at `d0ebed5` (self-merged by the impl subagent — orchestrator noted the DO-NOT violation but the work is clean; 2790/2790 tests pass).
- Web PR #670 carries the companion sanitizer fix + submodule bump pointing at `d0ebed5`.
- Without this web PR, the new core prompt's `- ` bullets would be stripped server-side and never render on the SPA — so this PR is what actually makes #949 user-visible.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` for any review-relevant lessons.
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-web
   gh pr view 670 --repo decay256/pinder-web --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 949 --repo decay256/pinder-core --json title,body  # ticket is on core
   gh pr diff 670 --repo decay256/pinder-web
   ```
4. Check out the branch:
   ```bash
   cd /root/projects/pinder-web
   git worktree add /tmp/work-949-web-review <head-ref-from-gh-pr-view>
   cd /tmp/work-949-web-review
   git submodule update --init pinder-core
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping.** Verify:
   - `MarkdownSanitizer.UnorderedListPrefix` regex is no longer being applied (or the field/path is exempted) — `- ` line-start markers survive the sanitizer.
   - `ActiveSession.SetupAsync` (or wherever stake routing happens) no longer strips bullets for stake content.
   - The pinder-core submodule pointer is at `d0ebed5` (the merge SHA of core PR #967).
   - 32 sanitizer tests pass; the renamed `Strip_UnorderedLists_PreservesPrefix` and new `Strip_StakeStyleBulletList_PreservesEveryBullet` are sensible pins.

2. **CRITICAL: blanket strip vs scoped exemption.** The implementer dropped the `UnorderedListPrefix` strip unconditionally — i.e., for EVERY field that runs through `MarkdownSanitizer`, not just stake. The spawn template offered two paths: (a) blanket "preserve bullets everywhere" (the path taken) and (b) exempt only the stake field. The impl chose (a) on the rationale that `- ` bullets are safe markdown and the SPA's `whitespace-pre-line` rendering handles them.
   
   Decide: is (a) sound? The risk is that other LLM-generated fields (outfit, opener, etc.) suddenly leak `- ` markers if a future prompt change accidentally produces them. The impl's argument is "literal `- ` line is a strictly better failure mode than silent strip-and-merge" — which is true if the operator sees garbage and notices, but bad if it slips into a delivered message.
   
   Recommendation: APPROVE if the impl can show the regex was only catching legitimate user content (no other field's prompt template uses `- ` cosmetically). REQUEST CHANGES if there's a real risk of regression on outfit/opener fields and a per-field exemption would be safer.
   
   Probe:
   ```bash
   cd /tmp/work-949-web-review
   grep -rn "MarkdownSanitizer.Strip\|MarkdownSanitizer.Sanitize" src 2>&1
   # Find every call site and what field/text it sanitises
   grep -rn "DefaultSystemPrompt" pinder-core/src 2>&1 | head -10
   # Check the outfit prompt etc. for any '- ' shape requirements
   ```

3. **Submodule bump correctness.** Verify the submodule pointer matches `d0ebed5` and that `git diff origin/main -- pinder-core` shows only the pointer change (no accidental nested edits inside the submodule).

4. **No out-of-scope edits.** Per the inline-revert problem on #649: diff scan should be exactly:
   - MarkdownSanitizer.cs (regex / strip change).
   - Sanitizer test file (rename + new pin + updated mixed-list test).
   - Submodule pointer.
   - Maybe a fixture refresh (20 JSON fixtures). Per the impl: "recorded responses untouched, fixtures regenerated via `REFRESH_FIXTURE_PROMPTS=1`" — confirm only the prompt-snapshot part changed in those fixtures, not the recorded LLM responses.
   - NOTHING ELSE. No `pnpm-lock.yaml` (the #647 footgun), no prettier sweeps, no unrelated imports.

5. **Build + test on the branch.**
   ```bash
   cd /tmp/work-949-web-review
   pnpm -C frontend install
   pnpm -C frontend exec tsc --noEmit
   pnpm -C frontend test --run
   # And the .NET API:
   dotnet build src/Pinder.GameApi/Pinder.GameApi.csproj 2>&1 | tail -5
   dotnet test tests/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj 2>&1 | tail -10
   ```
   Confirm: tsc clean; frontend tests pass; sanitizer 32/32; API 635/635 strict-match.

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
Concise. Real issues only.

Report back with the verdict block + a posted GitHub review (`gh pr review 670 --approve` or `gh pr review 670 --request-changes --body "..."`).

**Note:** if `gh pr review --approve` is blocked with "Can not approve your own pull request" (decay256 owns both the PR and the gh token), fall back to `gh pr review 670 --comment --body "<verdict block>"` and put the structured verdict line in the comment body.

All upstream events follow USER.md response-style — short, lead with result, no tables.
