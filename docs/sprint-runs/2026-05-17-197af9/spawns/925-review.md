You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-core PR #966** (fix for core#925 — rename `TurnSnapshot.DefendingRollStat` → `DefendingStat`). Implementer ran at Rung 0 gemma; you run at Rung 1 deepseek per the offset rule.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` for any review-relevant lessons.
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-core
   gh pr view 966 --repo decay256/pinder-core --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 925 --repo decay256/pinder-core --json title,body
   gh pr diff 966 --repo decay256/pinder-core
   ```
4. Check out the branch to inspect locally:
   ```bash
   cd /root/projects/pinder-core
   git worktree add /tmp/work-925-review chore/925-turnsnapshot-defendingstat-rename
   cd /tmp/work-925-review
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping.** The ticket recommends option (1): "Rename `TurnSnapshot.DefendingRollStat` → `DefendingStat` to match the wire field name (`defending_stat`)." Verify the rename happened and all call sites updated.

2. **CRITICAL: Snapshot JSON shape.** The implementer's own findings note:
   > The property had no `[JsonPropertyName("defending_stat")]` attribute, so it serialized as PascalCase `"DefendingRollStat"`. The rename therefore does change the snapshot JSON key to `"DefendingStat"`, not to `"defending_stat"`. The other `defending_stat` wire fields (on `RollResult` and `OpponentDefenseSnapshot`/`TurnDefenseEntry`) are unchanged — those have explicit attributes.
   
   Decide:
   - Is the session-runner snapshot a debug-only artifact (in which case `"DefendingStat"` is fine, just slightly inconsistent), OR is it consumed by a tool / test fixture / replay path (in which case the JSON key change is a regression)?
   - If it's debug-only, is adding `[JsonPropertyName("defending_stat")]` an obvious nicety that should ship in this PR (it's a one-line change matching the wire field name), OR should it be a follow-up to keep the PR scope clean?
   
   Grep for consumers:
   ```bash
   cd /tmp/work-925-review
   grep -rn "DefendingRollStat\|\"DefendingRollStat\"" . 2>&1
   grep -rn "snapshot.json\|sessionSnapshot" tests session-runner docs 2>&1 | head
   ```
   
   Recommendation guidance: if no tool consumes the snapshot JSON key, the PR is fine as-is and add a one-line PR comment recommending a follow-up to add the JsonPropertyName attribute. If anything DOES consume it, that's a blocker — request changes to add the attribute in this PR.

3. **No collision with `TurnDefenseEntry.DefendingStat`.** Confirmed by grep + ticket. Sanity-check.

4. **No out-of-scope edits.** Diff should be exactly 2 files (`session-runner/Snapshot/SessionSnapshot.cs` + `session-runner/Program.cs`) plus xmldoc updates. Scan for anything unrelated (formatting sweeps, unrelated imports, prettier passes).

5. **Tests + build on the branch.**
   ```bash
   cd /tmp/work-925-review
   dotnet build pinder-core.sln 2>&1 | tail -10
   dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -10
   ```
   Confirm: build clean; 2790+ tests pass; no new failures.

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

Report back with the verdict block + a posted GitHub review (`gh pr review 966 --approve` or `gh pr review 966 --request-changes --body "..."`).

All upstream events follow USER.md response-style — short, lead with result, no tables.
