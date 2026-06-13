You are a no-context code reviewer subagent reviewing ONE pull request (#1146 for ticket #1133) against the project's Definition of Done. You did NOT write this code — your job is to be the structural critic.

## Role spec

Read and follow /root/projects/eigentakt/agents/code-reviewer.md (verdict format: APPROVE or CHANGES_REQUESTED with a numbered blocker list; distinguish blocking from non-blocking findings).

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /root/projects/pinder-web/pinder-core/LESSONS_LEARNED.md (or in your checkout). Key ones:
- WIRE-CONTRACT-REGRESSION-TESTS: persisted yaml keys are a wire/persisted shape. Verify the back-compat regression test covers: OLD-keys-only parses, NEW-keys-only parses, and BOTH prefers the NEW value. If a happy-path-only test could pass while silently dropping the old key, that is a BLOCKER. Back-compat read is a MANDATORY acceptance criterion for #1133 — its absence is a BLOCKER.
- IMPLEMENTER-OVERCLAIMS-DETERMINISTIC-FAILURE: re-run the test scope yourself; do not trust pasted counts.
- BUILD-PIPELINE-DISCIPLINE: confirm build output is real (0 errors).

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- CI = LOCAL ONLY. Re-run `dotnet build Pinder.Core.sln` + `dotnet test Pinder.Core.sln` yourself in a fresh checkout of the PR branch. Verify the claimed counts.
- Scope = pinder-core ONLY. CONFIRM no Unity edits and no pinder-web admin-UI edits. The pinder-web emitter update must be a LISTED cross-repo follow-up, NOT implemented here. If pinder-web/Unity files are touched, that is a BLOCKER.

## The review — PR #1146 closes #1133 (yaml key player_role_description → player_avatar_role_description)

Checkout and review:
```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/review-1146 origin/fix/1133-yaml-key-player-avatar-role-description 2>/dev/null || git worktree add /tmp/review-1146 fix/1133-yaml-key-player-avatar-role-description
cd /tmp/review-1146
git submodule update --init 2>/dev/null || true
```
(If the branch name differs, find it: `gh pr view 1146 --repo decay256/pinder-core --json headRefName`.)

Verify against #1133 acceptance:
1. New keys (`player_avatar_role_description`, and `player_avatar_probing` if renamed) read correctly; OLD keys still accepted (back-compat read path) with NEW PREFERRED when both present. This dual-key read is MANDATORY.
2. **Back-compat regression test** present and real: old-only parses, new-only parses, both-prefers-new. Implementer named it `LoadFrom_LegacyYamlKeys_BackwardCompatibility_Regression1133`. Confirm it genuinely exercises all three cases (not just the happy path).
3. Source-anchor strings in `SessionSystemPromptBuilder.cs` updated so prompt-tracer attribution points at the NEW key names.
4. `data/game-definition.yaml` keys renamed. Out-of-scope kept: transcript sender family (`PlayerName`, `playerSender*`) must NOT be renamed.
5. pinder-web emitter update LISTED as a cross-repo follow-up (not implemented).
6. Re-run build + full test suite. Implementer claims build 0 err, tests 4337 passed / 0 failed / 27 skipped. Verify. If any failure, run 3× and report deterministic-vs-flake and whether origin/main shares it.
7. Confirm scope: no Unity, no pinder-web files touched.

## Report back
Your verdict (APPROVE or CHANGES_REQUESTED) with a numbered blocker list (empty if none) and any non-blocking findings. Include YOUR build result and YOUR test counts (pass/fail/skip), and whether the back-compat regression test is genuine. Be concise. Do NOT merge — the orchestrator merges.
