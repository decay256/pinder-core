You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-core PR #969** (fix for core#920 — synthesise `RollCheckResult` in `CreateForcedFailResult`; tighten `RollResult.Check` to non-null). Implementer ran at Rung 2 sonnet; you run at Rung 1 deepseek per the offset rule.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` for any review-relevant lessons (esp. #901, #903, #918).
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-core
   gh pr view 969 --repo decay256/pinder-core --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 920 --repo decay256/pinder-core --json title,body
   gh pr diff 969 --repo decay256/pinder-core
   ```
4. Check out the branch:
   ```bash
   cd /root/projects/pinder-core
   git worktree add /tmp/work-920-review chore/920-rollresult-check-non-null
   cd /tmp/work-920-review
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping (option 2 — synthesise).** The ticket chose option (2). Verify:
   - `RollCheckResult.Synthesise(...)` factory exists, takes the bespoke fields a forced-fail RollResult carries, and returns a non-null `RollCheckResult` whose Verdict / Tier / Stat / die / total / DC fields are consistent with the inputs.
   - `GameSession.CreateForcedFailResult` calls Synthesise and passes the result to the primary ctor.
   - `RollResult` constructor signature: the primary now requires `RollCheckResult` (no `?`, no default). `check!` is gone.
   - The convenience-ctor overload (bespoke fields only, no `check` arg) chains to the primary with `Synthesise(...)`. Existing ~47 test sites use this overload implicitly.

2. **Tier-source consistency on shadow-corruption.** The implementer's report mentions "documented bespoke vs `Check.Tier` divergence on shadow-tier overrides". Check:
   - Is the divergence load-bearing for Phase 2 (wire DTO will read `Check.*`)? If the wire DTO reads `Check.Tier` and the bespoke field has been shadow-overridden but Synthesise rebuilds `Check.Tier` from the raw miss margin, the wire payload will report the pre-shadow tier and break Phase 2.
   - Verify with grep:
     ```bash
     cd /tmp/work-920-review
     grep -rn "shadow.*Tier\|ShadowTier\|Tier =" src/Pinder.Core/Conversation/GameSession.cs 2>&1 | head -20
     grep -rn "RollResult.*Tier\|result.Tier" src 2>&1 | head -20
     ```
   - If divergence is real and the test `CreateForcedFailResult`-mirror parity test documents it, decide whether that's a blocker (Phase 2 will need re-work to read bespoke `Tier` instead of `Check.Tier`, or Synthesise needs an extra `tierOverride` param). Recommendation: non-blocking note IF the divergence is only on the shadow-corruption path (which is its own out-of-band override per #598). Blocker IF the bespoke-vs-Check.Tier mismatch happens on regular forced fails.

3. **Synthesise field parity tests.** The new `Issue920_RollResultCheckNonNullTests.cs` has 7 tests. Skim:
   - Does it cover the empty-modifier-bag case (forced fails where no stat modifier applies)?
   - Does it cover the externalBonus-zero case?
   - Does the nat-20 test actually fire? (Forced fails shouldn't be Nat 20, but if Synthesise is called with an externally-rolled 20 it should reflect it.)
   - The null-rejection test on primary ctor: does it use a non-default-args call to disambiguate from the convenience overload?

4. **Pre-existing 46-failure noise.** The implementer reports `Pinder.Rules.Tests` has 46 pre-existing YAML-loader failures. Confirm:
   ```bash
   cd /tmp/work-920-review
   git checkout origin/main -- .
   dotnet test tests/Pinder.Rules.Tests/Pinder.Rules.Tests.csproj 2>&1 | tail -5
   git checkout HEAD -- .
   ```
   If the same 46 fail on main, it's noise. If they don't, it's a regression.

5. **No out-of-scope edits.** Diff scan: no wire-JSON shape changes (Phase 2 untouched), no bespoke-field deletion (Phase 3 untouched), no unrelated refactors. The #649 reviewer caught 2 out-of-scope edits and forced a revert — match that bar.

6. **Build + test on the branch.**
   ```bash
   cd /tmp/work-920-review
   dotnet build pinder-core.sln 2>&1 | tail -10
   dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -10
   ```
   Confirm: 0 build errors; 2797 tests pass.

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

Report back with the verdict block + a posted GitHub review (`gh pr review 969 --approve` or `gh pr review 969 --request-changes --body "..."`).

**Note:** if `gh pr review --approve` is blocked with "Can not approve your own pull request" (decay256 owns both the PR and the gh token), fall back to `gh pr review 969 --comment --body "<verdict block>"` and put the structured verdict line in the comment body. The orchestrator parses the verdict from the body.

All upstream events follow USER.md response-style — short, lead with result, no tables.
