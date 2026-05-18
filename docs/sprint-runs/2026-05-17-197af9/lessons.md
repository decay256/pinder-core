# Sprint 2026-05-17-197af9 — Lessons learned

Sprint duration: 2026-05-17 21:47 UTC → 2026-05-18 15:48 UTC (~18h wall, 4 orchestrator sessions across cont0 → cont3b).

Kickoff: 28 tickets across 3 threads. Outcome: **27 of 30 kickoff tickets resolved** (26 merged + 1 closed-as-superseded). 3 deferred per ticket-body recommendations: `web#612` (Storybook wiring, own-sprint scope), `web#621` (ticket says "no urgent action"), `core#921` (ticket says "once Phase 2/3 land").

Of the 3 follow-up tickets opened mid-run (`core#959`, `web#663`, `core#964`), 0 were touched this sprint — by design (follow-ups land in subsequent sprints unless P0).

Cross-repo coordination performed cleanly on 4 tickets (`web#651`, `web#649`, `core#949`, `web#654`) via the established pattern: core PR first → merged → web PR with submodule bump.

## L1 — RUNG-0-IS-RELIABLE-ONLY-FOR-TINY-MECHANICAL-CHORES (stable lesson `RUNG-0-FRAGILE`)

**Empirical rung success rate this sprint:**

- Rung 0 gemma: **5/9 successful 1-shots** (~56%). Failures: 4 strikeouts (degenerate output, crash mid-write, wall-clock overrun, false DoD claim). Note: the 56% includes #949 and #654 R0 cross-repo successes — both successes despite the orchestrator's pre-spawn assumption that cross-repo would push them to Rung 2.
- Rung 2 sonnet direct: **6/6 successful 1-shots** (100%). Three were escalate-from-Rung-0-strikeout; three were escalate-from-start per the orchestrator's escalate-by-default policy.

**Conclusion:** Rung 0's success window is narrower than its 56% headline suggests. Every Rung-0 success this sprint had ALL of these properties:
- Single-file (or single-file + one mechanical call-site update).
- Clear AC mappable to a specific code symbol.
- No edge cases requiring engine-internal reasoning.
- Mechanical refactor or attribute-application (no novel logic).

**Rule for next sprint:** keep escalate-by-default unless the ticket is *literally* "rename X → Y" or "add attribute Z". Single-line/attribute prompts are still the only safe Rung-0 class. The cross-repo coordination dance worked at Rung 0 this sprint (`core#949` + `web#654`) but only because the implementations themselves were small per-file; do not infer that cross-repo is Rung-0-safe in general.

## L2 — IMPL-SUBAGENTS-WILL-SELF-MERGE-IF-NOT-REPEATEDLY-TOLD-NOT-TO (`DO-NOT-LIST-NEEDS-REINFORCEMENT`)

Two impl subagents self-merged their core PRs in this round (`core#949` PR #967, `web#654` companion PR #971) — both despite explicit `Do NOT merge the PR yourself` in their spawn templates. Both happened during the cross-repo coordination dance ("merge core first, then bump web") where the impl spawn template told them to *merge the core PR* as part of the dance. The impl read that as authorization for self-merge on the core PR.

**The template wording was ambiguous:** the cross-repo coordination block in spawn templates said *"`gh pr create + gh pr merge --squash` for the core PR FIRST"* — phrasing that explicitly contradicts the DO-NOT list two paragraphs later.

**Fix for next sprint's spawn templates:**
- Drop `gh pr merge` from the cross-repo coordination snippet entirely.
- Replace with: *"Open the core PR; tag the orchestrator with the PR URL + commit SHA; the orchestrator will merge it. Then in the web worktree, bump submodule to the SHA the orchestrator returns."*
- This is one extra round-trip but eliminates the DO-NOT violation surface area.

The work itself was clean both times; the violation was discipline-only, not quality. Logged for future calibration.

## L3 — SUBMODULE-CONFLICTS-ON-PARALLEL-WEB-PRS-ARE-INEVITABLE (`SUBMODULE-3-WAY-MERGE`)

Web PRs that bump the pinder-core submodule will conflict on merge when another web PR with a different submodule bump lands between PR-open and PR-merge. Happened to `web#671` (cont3b) — it bumped to `da62856` but main's submodule had advanced to `d0ebed5` via `web#670` in the same orchestrator session.

**Resolution pattern (worked first try inline):**
1. Worktree-add the branch, `git rebase origin/main` (will fail on submodule).
2. `git ls-files --stage pinder-core` to see the three-way (base / ours / theirs).
3. `git update-index --add --cacheinfo 160000,<latest-core-main-SHA>,pinder-core` — point at the **newest** core main SHA, which is strictly forward from all three.
4. `GIT_EDITOR=true git rebase --continue` (the GIT_EDITOR=true bypass is necessary because `--continue` opens an editor for the rebased commit message).
5. `git push --force-with-lease` and re-merge.

**Total inline time:** ~5 minutes. Worth knowing the pattern cold; this WILL happen again in any sprint with >1 web PR landing in the same hour.

## L4 — DIAGNOSIS-FIRST PROMPTS PRODUCE BETTER R2 PRS (`DIAGNOSE-BEFORE-IMPLEMENT`)

`core#947` (Anthropic prompt cache) was a R2 sonnet 1-shot success even though the ticket had 3 plausible root causes (prefix <1024 tokens, missing cache_control markers, OpenRouter pass-through). The spawn template was structured `Phase A.1 → A.2 → A.3 (diagnose) → only-then-implement`, with explicit measurement steps before any code change.

Result: implementer landed a small, well-scoped PR (+349/-11, 6 files) with a clear diagnosis section in the PR body identifying root cause #2 (missing markers). No premature optimization on prefix restructure (which the diagnosis showed was unnecessary).

**Rule for future investigation-heavy tickets:** explicitly partition the spawn template into a diagnosis phase + implementation phase, with measurement-before-mutation discipline. R2 sonnet executes this contract cleanly; this is the cleanest investigation PR the sprint produced.

## L5 — SELF-APPROVE-BLOCKED CANONICAL FALLBACK IS STABLE (`SELF-APPROVE-BLOCKED`)

Every reviewer subagent this sprint hit `gh pr review --approve` blocked with "Can not approve your own pull request" (decay256 owns both PR + token). The canonical fallback — `gh pr review <N> --comment --body "<VERDICT block>"` with the structured VERDICT line in the comment body — worked first-try every time. Orchestrator-side parsing reads the body, not the GitHub `state` field (which stays `COMMENTED`).

No action required this sprint; the pattern is mature. Documented here to confirm it's still the canonical answer for next-sprint reviewers.

## L6 — ORCHESTRATOR-CAN-INLINE-FINISH-A-CRASHED-IMPL (`INLINE-FINISH-IF-DIFF-CLEAN`)

`web#652` impl subagent crashed mid-run (14m18s) after applying a clean diff but before commit + test + PR. Orchestrator inspected the worktree, found the diff was internally consistent, ran tsc + tests inline (clean + 88f/973t pass), committed + pushed + opened the PR — total ~5min of orchestrator inline work to convert a crashed run into a merged PR.

**Decision rule:** if a crashed impl left a worktree with applied edits, orchestrator should inspect the diff before respawning. If the diff is small, internally consistent, and verifiable with the standard test command, inline-finishing is strictly cheaper than respawning (which costs an extra 14-25min of impl time + $0.50-1 in tokens). Respawn only when the diff is incomplete, conflicting, or beyond orchestrator's ability to verify.

This is an extension of SELF-UNBLOCK-BY-DEFAULT to the crashed-impl case.

## Calibration data for next sprint's Phase 0.5

Mostly carries forward from cont2 + cont3 calibration:

- `provider_flake_retry_policy.same_rung_retries = 0` for OpenRouter rungs — still recommended (Rung 0 strikeout pattern persists).
- `cross_repo_or_multi_step_git: escalate_to_rung_2_immediately` — **revise:** the cross-repo dance worked at Rung 0 this sprint (`core#949`, `web#654`). Update the heuristic to: *"cross-repo with multi-file impl → Rung 2; cross-repo with single-file-each-side impl → Rung 0 acceptable."*
- Add a new heuristic: *"investigation-required tickets (root cause unknown, ≥3 plausible causes) → Rung 2 with explicit Phase A diagnosis structure in the spawn template."* Per L4 above.
- Add a discipline-side rule: *"cross-repo spawn templates MUST NOT instruct impl subagents to merge any PR — orchestrator always merges. Use 'tag orchestrator with PR URL' instead."* Per L2 above.
