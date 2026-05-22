You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-web PR #667** (fix for web#650 — global FoldableHintBanner for weakness-window signal). Implementer ran at Rung 2 sonnet; you run at Rung 3 opus per the offset rule.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` for any review-relevant lessons.
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-web
   gh pr view 667 --repo decay256/pinder-web --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 650 --repo decay256/pinder-web --json title,body
   gh pr diff 667 --repo decay256/pinder-web
   ```
4. Check out the branch to inspect locally:
   ```bash
   cd /root/projects/pinder-web
   git worktree add /tmp/work-650-review fix/650-weakness-window-banner
   cd /tmp/work-650-review
   git submodule update --init pinder-core
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping.** The ticket says: "When a turn's opponent response generates a WEAKNESS signal that affects the next turn's DC, the FoldableHintBanner renders globally (above options or above the conversation) the moment the opponent response lands." Verify the banner actually mounts in `resultPendingSection` (opponent response phase), not just in the option-picker phase. Verify the banner text names BOTH the stat AND the DC reduction amount.

2. **Banner gate logic — `weaknessBannerGate.ts`.** Two resolver helpers (`resolveWeaknessBannerFromResult` + `resolveWeaknessBannerFromTurnState`). Both need to:
   - Handle null / missing signal cleanly (no crash, no false-positive banner).
   - Correctly recover stat from `turnLog`'s last `detected_window` entry for the turn-state path (this is fragile — what if `turnLog` is empty? What if multiple windows?).
   - Not double-render the banner when both result and turn-state have signals.

3. **EventBox formula display (acceptance criterion 3).** The ticket also says: "When the weakness fires (player picks the matching stat), the next turn's expanded EventBox should show the reduction in its roll formula." Did the implementer surface the weakness DC reduction in `ModifierBagRollFormula`? Check the diff. If MISSING, that's a CHANGES_REQUESTED blocker.

4. **Pre-existing import-failure noise.** Implementer reports "27 pre-existing import failures for missing codegen artifact `generated/en` — unrelated." Spot-check one of those failures to confirm it's pre-existing (e.g. `git log --oneline -- frontend/src/generated/en` or check whether `main` also has these failures with `pnpm -C frontend test --run`).

5. **Regression test coverage.** New file `weaknessBannerGate.test.ts` (11 tests). Skim — are the negative cases covered (null `detected_window`, mismatched stat, zero reduction)? Are both resolver paths tested?

6. **Out-of-scope edits.** Per the inline-revert problem on #649: scan the diff for anything that ISN'T about the weakness-window banner (icon swaps, prettier sweeps, unrelated imports, unrelated copy changes). If found, flag CHANGES_REQUESTED with the specific lines.

7. **Tests run on the branch.** Re-run the targeted tests yourself:
   ```bash
   cd /tmp/work-650-review
   pnpm -C frontend exec tsc --noEmit
   pnpm -C frontend test weaknessBannerGate --run
   pnpm -C frontend test FoldableHintBanner --run
   pnpm -C frontend test OptionSelectionWidget --run
   ```

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
Concise. The Rung 3 opus review for #649 was tight (7000 output tokens) and caught the real issues. Match that bar.

Report back with the verdict block + a posted GitHub review (`gh pr review 667 --approve` or `gh pr review 667 --request-changes --body "..."`).
