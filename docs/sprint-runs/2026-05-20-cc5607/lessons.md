# Sprint 2026-05-20-cc5607 — lessons

## L1 — ZERO-TOKENS-STREAM-CUTS-CHECK-DISK-FIRST (stable)

Observed 5 of 8 OpenRouter DeepSeek V4 Pro spawns in this sprint completed with the `zero_token_stats` flake signature (runtime 4-13min, tokens 0). In every case the runtime accounting was wrong — work had often completed on disk, just the closing Stats line was lost.

**Rule:** when a subagent returns `tokens 0 (in 0 / out 0)` but with non-zero runtime, the orchestrator's first action is to inspect `/tmp/work-<ticket>` git status + log + PR list BEFORE invoking same-rung retry. If commits landed AND tests pass locally, treat as success with synthetic spawn-recover (`stats-reparse` outcome `zero_token_stats_but_pr_opened` or similar). Only retry when worktree is empty or work is genuinely incomplete.

This is a refinement of FLAKE-RETRY-BEFORE-ESCALATE (still correct), not a replacement.

## L2 — IMPLEMENTER-COMMIT-CHECKPOINTS-IN-PROMPT (stable)

Sprint #976 attempt-1 ran 11 min and committed zero. Attempt-2 with explicit "commit at checkpoints 1/2/3/4" instruction landed a substantial commit (442215c — interface + adapter + 3 engine wirings + GameSessionConfig) before its own stream-cut, and the finisher then completed against that baseline in 8 min.

**Rule:** every implementer prompt for non-trivial multi-file tickets should include explicit commit checkpoints with wall-clock budgets (e.g. "≤ 5 min in: commit the first artifact"). The `templates/implementer-prompt.md` "Atomic commits per ticket" line is too weak for DeepSeek-class implementers under stream-cut risk.

Candidate text to add to canonical template:

> **Commit early, commit often.** Stream-cuts on long runs are real. After every logical chunk (interface, adapter, first engine wired, tests pass), run `git add -A && git commit -m "wip: <one-line>"`. Never work for more than 5 minutes without committing. This is a survival contract, not a code-quality nit.

## L3 — REGRESSION-FROM-LARGE-FILE-REWRITE (stable, calibration-class)

PR #982 first reviewer pass would have caught a 6-test regression: the implementer's #976 edits to `GameSession.cs` (82-line diff) accidentally reverted #957's transactional changes in Wait()/CheckInterestEndConditions/CheckGhostTrigger. The model appears to have regenerated those methods from a pre-#957 mental model when editing the broader file.

**Rule:** when an implementer touches a file with recent (same-sprint or last-2-sprint) substantive changes, the prompt must explicitly call out those recent changes and instruct: "Read the current HEAD of $FILE before editing; DO NOT regenerate methods that you weren't asked to change." Pinder-specific instance: any GameSession.cs edit should preface with "recent invariants: #942/#955 (StartTurnAsync transactional), #957 (Wait() transactional), #956 (ShadowGrowthEffects typed list)."

Reviewer focus: any diff that changes methods unrelated to the ticket's stated scope is a CHANGES_REQUESTED blocker even if all new tests pass — because the reverted invariants are guarded by EXISTING tests that the reviewer should run.

## L4 — FIX-PASS-FASTER-THAN-FORMAL-REVIEW-WHEN-BUG-IS-IDENTIFIED (provisional)

For #976's #957 regression, the orchestrator spotted the bug via local test-run and dispatched a fix-pass directly instead of routing through code-reviewer first. Fix-pass landed in 8m20s. Routing through a formal CHANGES_REQUESTED review would have added a ~5min reviewer round-trip with no new information.

**Provisional rule:** when the orchestrator has already pinpointed the failing assertion AND the diff, skip the formal review for the fix-pass cycle. Run the second-pass review AFTER the fix lands. Logged as orchestrator-default; revisit if review accuracy suffers.
