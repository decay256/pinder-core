You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-core PR #972** (fix for core#927 — add `FinalVerdict` + `FinalTier` to `RollCheckResult` as engine-side single source of truth for post-shadow-corruption verdict). Implementer ran at Rung 2 sonnet; you run at Rung 1 deepseek per the offset rule.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` for any review-relevant lessons (esp. #598 / #901 / #920).
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-core
   gh pr view 972 --repo decay256/pinder-core --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 927 --repo decay256/pinder-core --json title,body
   gh pr diff 972 --repo decay256/pinder-core
   ```
4. Check out the branch:
   ```bash
   cd /root/projects/pinder-core
   git worktree add /tmp/work-927-review chore/927-rollcheckresult-final-verdict-tier
   cd /tmp/work-927-review
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping.** Verify:
   - `RollVerdict` enum (`Success` | `Miss`) added.
   - `RollCheckResult.FinalVerdict` (RollVerdict) + `FinalTier` (FailureTier) added with snake_case `JsonPropertyName` + `JsonStringEnumConverter` (matches the #924 pattern).
   - Defaults to pre-shadow values when no override applied.
   - `internal ApplyFinalOverride(verdict, tier)` method exists.
   - `GameSession` shadow-corruption block calls `ApplyFinalOverride(Miss, shadowTier)` exactly when `shadow_check.IsMiss && shadow_check.OverlayApplied && roll.IsSuccess` (the demotion case from the ticket).
   - Existing `IsSuccess` / `Tier` on both `RollResult` and `RollCheckResult` are unchanged (back-compat).

2. **The `ApplyFinalOverride` mutation pattern.** This is the design call that needs scrutiny: instead of computing final values at construction time, the engine constructs `RollCheckResult` with defaults, then mutates via `ApplyFinalOverride`. Decide:
   - Is `ApplyFinalOverride` correctly `internal` (only callable from Pinder.Core + Pinder.Core.Tests via `InternalsVisibleTo`)? Grep for the assembly attribute:
     ```bash
     cd /tmp/work-927-review
     grep -rn "InternalsVisibleTo" src/Pinder.Core 2>&1
     ```
   - Is there a risk of double-override (calling `ApplyFinalOverride` twice)? Should the method be idempotent or throw on second-call? Check the implementation.
   - Is there any path in `GameSession` where the shadow demotion path is entered but `ApplyFinalOverride` is NOT called (e.g. some early-return)? Grep the corruption block.
   - Alternative design — passing final values to the constructor — would be cleaner but require constructor-signature changes everywhere `RollCheckResult` is built. The mutation approach trades that churn for a small mutability surface. Defensible IF mutability is scoped (internal) and well-tested.

3. **Simulator parallel resolution path.** The spawn template explicitly called out: "Mirror the same population in the simulator (`session-runner/Simulator/*.cs` if there's a parallel resolution path)". Did the impl do this? Grep:
   ```bash
   cd /tmp/work-927-review
   grep -rn "shadow.*corrupt\|ApplyShadow\|RollCheckResult" session-runner/Simulator 2>&1 | head -10
   ```
   If the simulator has a parallel path that doesn't call `ApplyFinalOverride`, the simulator's `FinalVerdict` / `FinalTier` will silently report pre-demotion values — that's a blocker (the simulator is one of the three derivation sites the ticket cited).

4. **8 new tests in `Issue927_FinalVerdictTierTests.cs`.** Skim:
   - Plain success → `FinalVerdict=Success`, `FinalTier=None`.
   - Plain miss → `FinalVerdict=Miss`, `FinalTier=<original-tier>`.
   - Nat 20 → Catastrophe demotion (shadow case) → `FinalVerdict=Miss`, `FinalTier=Catastrophe`, AND `IsSuccess=true` + `Tier=None` unchanged.
   - Shadow miss without overlay applied → `FinalVerdict` matches original.
   - Multiple shadow-check passes → only the final overlay flows in.
   - Are there at least these 5 + a JSON-shape test pinning snake_case + string-enum?
   - Is there a test that pins back-compat on `IsSuccess` / `Tier`?

5. **No out-of-scope edits.** Diff should be exactly 4 files (RollVerdict.cs + RollCheckResult.cs + GameSession.cs + the test file). Scan for unrelated changes.

6. **Build + test on the branch.**
   ```bash
   cd /tmp/work-927-review
   dotnet build pinder-core.sln 2>&1 | tail -10
   dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -10
   ```
   Confirm: 0 build errors; 2805 tests pass.

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

Report back with the verdict block + a posted GitHub review (`gh pr review 972 --approve` or `gh pr review 972 --request-changes --body "..."`).

**Note:** if `gh pr review --approve` is blocked with "Can not approve your own pull request" (decay256 owns both the PR and the gh token), fall back to `gh pr review 972 --comment --body "<verdict block>"` and put the structured verdict line in the comment body.

All upstream events follow USER.md response-style — short, lead with result, no tables.
