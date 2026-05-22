You are a code-reviewer subagent for the Pinder dev swarm. Review **pinder-web PR #671** (fix for web#654 — re-estimate progress past 1.2×; "longer than usual" past 2×; rolling-median; smooth transitions; i18n; telemetry log). Implementer ran at Rung 2 sonnet; you run at Rung 1 deepseek per the offset rule.

## Context
- Companion core PR #971 already merged at `da62856` (3 new `turn_progress.*` i18n keys in `ui.yaml`).
- Web PR #671 carries the frontend logic + submodule bump.

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-web/LESSONS_LEARNED.md` for any review-relevant lessons.
3. Pull the PR + ticket:
   ```bash
   cd /root/projects/pinder-web
   gh pr view 671 --repo decay256/pinder-web --json title,body,headRefName,commits,additions,deletions,changedFiles
   gh issue view 654 --repo decay256/pinder-web --json title,body
   gh pr diff 671 --repo decay256/pinder-web
   ```
4. Check out the branch:
   ```bash
   cd /root/projects/pinder-web
   git worktree add /tmp/work-654-review fix/654-progress-reestimate
   cd /tmp/work-654-review
   git submodule update --init pinder-core
   pnpm -C frontend install
   ```

## Review focus (priority order)

1. **Correctness of the AC mapping.** Verify against the ticket's 4 acceptance criteria:
   - **AC1: Re-estimate at 1.2× elapsed.** `computeProgressDisplay` should transition `estimating → reestimating` when elapsed > 1.2× original. New target = `rollingMedian(history)` if ≥3 samples, else `1.5× fallback`.
   - **AC2: Graceful UI, no flicker.** TurnProgressIndicator uses 500ms ease-out tween. Check the CSS / animation — does it actually smooth the bar transition?
   - **AC3: Log actual-vs-estimated on stream complete.** `useGenerationTiming.stop()` emits `console.info('[gen-time]', { sessionId, estimateMs, actualMs, ratio })` only on success path (not cancel/error). Verify the success-only gating.
   - **AC4: "Longer than usual" message past 2× elapsed.** State machine transitions `reestimating → longer_than_usual` at >2× original (the impl says "anchored to the original estimate" — sanity-check that's the right anchor, not the current re-estimate).

2. **The estimateMs snapshot.** Impl notes `estimateMs` is snapshotted at `start()` time so the logged value reflects what the user actually saw. Confirm: if the user saw a re-estimated value at 1.5× elapsed and the stream completed at 2.5× the original, the log should record the ORIGINAL estimate, not the re-estimated one — that's the right "actual vs estimated" comparison for future tuning. Or should it record both? Decide whether the choice is defensible.

3. **The `1ms floor on re-estimate`.** Impl says "Re-estimate is floored at `elapsed+1ms` so the bar never snaps to 100%". Sanity-check: is this floor applied AFTER the rolling-median calculation? What happens when the rolling median IS less than current elapsed (i.e., this generation is already an outlier)? The floor prevents the bar from going backwards, but the UX should also signal "outlier" — does it?

4. **Rolling history persistence.** Where does the history live — module-level memory, in-hook state, or localStorage? The spawn template suggested localStorage for cross-tab consistency. Check:
   ```bash
   cd /tmp/work-654-review
   grep -rn "localStorage\|generationHistory\|gen-history" frontend/src 2>&1 | head -10
   ```
   If it's in-memory only: the rolling history dies on page refresh, which means a re-estimate after refresh falls back to the 1.5× fallback every time. Is that acceptable, or a blocker?

5. **i18n key plumbing.** Confirm:
   - The web side reads `t('turn_progress.reestimating')`, `t('turn_progress.longer_than_usual')`, etc. (or whatever names landed in core PR #971).
   - The submodule pointer is at `da62856` or newer.
   - No hardcoded English copy.

6. **Pre-existing tsc error.** Impl reports `useTurnSource.ts(382,59) TS2345` reproduces on main. Confirm:
   ```bash
   cd /tmp/work-654-review
   git stash
   git checkout origin/main
   pnpm -C frontend exec tsc --noEmit 2>&1 | grep -E "useTurnSource.ts.*382"
   git checkout fix/654-progress-reestimate
   git stash pop 2>/dev/null
   ```
   If the same error exists on main, the impl's claim stands (it's tracked separately as web#663 per cont3). If it's new in this PR, that's a blocker.

7. **16 new tests.** Skim:
   - Initial state `estimating` returns expected value.
   - Transition `estimating → reestimating` at 1.2×.
   - Transition `reestimating → longer_than_usual` at 2×.
   - Fallback at <3 history samples.
   - Rolling-median computed correctly from N≥3 samples.
   - 1ms floor when median < elapsed.
   - `stop()` logs on success path; doesn't log on cancel/error.
   - Smoothing transition (snap test or interpolation assertion).
   - estimateMs snapshot preserves the original at log time.

8. **No out-of-scope edits.** Diff scan: scope should be focused on the progress hook + indicator + new history hook + tests + submodule bump + i18n consumption. No `pnpm-lock.yaml`. No prettier sweeps. No unrelated imports.

9. **Build + test on the branch.**
   ```bash
   cd /tmp/work-654-review
   pnpm -C frontend exec tsc --noEmit 2>&1 | tail -10
   pnpm -C frontend test --run 2>&1 | tail -10
   ```
   Confirm: tsc has ONLY the pre-existing useTurnSource.ts:382 error (or is clean); 989 tests pass.

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

Report back with the verdict block + a posted GitHub review (`gh pr review 671 --approve` or `gh pr review 671 --request-changes --body "..."`).

**Note:** if `gh pr review --approve` is blocked with "Can not approve your own pull request" (decay256 owns both the PR and the gh token), fall back to `gh pr review 671 --comment --body "<verdict block>"` and put the structured verdict line in the comment body.

All upstream events follow USER.md response-style — short, lead with result, no tables.
