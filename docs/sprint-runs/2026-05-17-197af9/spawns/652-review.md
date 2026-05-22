You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-web PR #668** (fix for web#652 — main-roll formula should fold INSIDE success/miss EventBox, not as a sibling after the intended-message text). Implementer ran at Rung 2 sonnet (with the orchestrator finishing the commit/push inline after the impl subagent crashed mid-run post-edit); you run at Rung 1 deepseek per the offset rule.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` for any review-relevant lessons.
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-web
   gh pr view 668 --repo decay256/pinder-web --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 652 --repo decay256/pinder-web --json title,body
   gh pr diff 668 --repo decay256/pinder-web
   ```
4. Check out the branch to inspect locally:
   ```bash
   cd /root/projects/pinder-web
   git worktree add /tmp/work-652-review fix/652-formula-folds-under-eventbox
   cd /tmp/work-652-review
   git submodule update --init pinder-core
   pnpm -C frontend install
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping.** The ticket says:
   - Main-roll formula is a foldable section INSIDE the success/miss EventBox, not a sibling after the intended-message text.
   - Expanded layout: consequence → formula → text-mod (per #649 F3).
   - Folded box shows the result; expanded shows the formula.
   - Visually: collapsed = one EventBox header; expanded = consequence + formula + text-mod.
   
   Verify the PR diff actually achieves this. Per the PR body, the ModifierBagRollFormula was ALREADY inside the expanded fold via #649 PR #666 — this PR's actual change is removing the now-redundant standalone outcome banner above the event stack and merging its content (label + emoji + outcome_summary) into the option_roll RollEventBox header. Confirm: (a) the standalone outcome banner JSX is gone; (b) the option_roll RollEventBox header now carries verdict-tone, verdict-emoji, verdict-label, and the outcome_summary line; (c) no second formula render appears anywhere.

2. **The new `summary` prop on `RollEventBoxProps`.** It's an optional pass-through to EventBox's existing row-2 subtitle slot. Verify:
   - The prop is correctly typed in `rollTypes.ts`.
   - `RollEventBox.tsx` forwards it to `<EventBox ... summary={summary} />`.
   - `EventBox`'s existing summary handling (row 2, no truncate) is the right place for the outcome_summary line.

3. **`outcomeStyle` tone return.** A new `tone: EventBoxTone` field is added to outcomeStyle's return. Confirm:
   - Each branch returns the right tone (`'positive'` for nat20 + success, `'negative'` for nat1 + Misfire/Fumble/Catastrophe + generic miss, `'trap'` for TropeTrap).
   - The `EventBoxTone` type is correctly imported from `./EventBox`.

4. **No out-of-scope edits.** Per the inline-revert problem on #649: scan the diff for anything that ISN'T about folding the formula / removing the outcome banner / wiring the tone+summary. If found (icon swaps, prettier sweeps, unrelated copy changes, unrelated imports), flag CHANGES_REQUESTED with the specific lines.

5. **Test coverage.** The PR did NOT add new tests, on the rationale that `summary` is exercised by existing EventBox row-2 tests + `RollEventBox.test.ts` already covers the expanded/collapsed contract. Decide: is that defensible? A snapshot or layout test asserting "outcome banner is gone from TurnResultDisplay" would catch a future regression where someone re-adds a duplicate banner. If you think that's a blocker, say so. If you think it's a non-blocking follow-up note, say so.

6. **No `pnpm-lock.yaml` committed.** The #647 implementer broke this rule and got force-reverted. Confirm the PR's `changedFiles` is exactly 3 (TurnResultDisplay.tsx + RollEventBox.tsx + rollTypes.ts) and no lockfile.

7. **Tests + tsc on the branch.** Re-run:
   ```bash
   cd /tmp/work-652-review
   pnpm -C frontend exec tsc --noEmit
   pnpm -C frontend test --run
   ```
   Confirm: tsc clean; 973+ tests pass; no new failures.

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
Concise. Match the Rung 3 opus review for #649's bar (tight, real issues only).

Report back with the verdict block + a posted GitHub review (`gh pr review 668 --approve` or `gh pr review 668 --request-changes --body "..."`).

All upstream events follow USER.md response-style — short, lead with result, no tables.
