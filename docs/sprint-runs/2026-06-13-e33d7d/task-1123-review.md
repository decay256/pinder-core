You are a no-context code reviewer reviewing ONE pull request with fresh eyes. You did not write this code. Be a structural critic.

## Workspace setup (isolated review worktree — non-negotiable)

Run EXACTLY this, do NOT edit the canonical clone /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/review-1123 origin/fix/1123-symmetric-two-session-gm
cd /tmp/review-1123
```

All builds + tests happen inside /tmp/review-1123. Use /usr/bin/dotnet (8.0.128) directly — the remoting shim at /root/.openclaw/agents-extra/pinder/bin is BROKEN (missing scripts/remote-exec.sh); do NOT prepend it to PATH.

## Role spec

Read and follow /root/projects/eigentakt/agents/code-reviewer.md (Review Checklist + Output Format). You MUST end with an explicit verdict line: `**Verdict: APPROVE**` or `**Verdict: CHANGES_REQUESTED**`. Use `gh pr review --comment` (NOT --approve; SELF-APPROVE-BLOCKED).

## Lessons (named) — LESSONS_LEARNED.md

- SUBMODULE-SYNC-AFTER-REBASE: if you rebase, `git submodule update --init` before building.
- BUILD-PIPELINE-DISCIPLINE: re-run the build yourself; tests-pass alone is insufficient.
- IMPLEMENTER-OVERCLAIMS-DETERMINISTIC-FAILURE: do not trust the implementer's "all green" — re-run build + a representative test slice yourself.
- FILE-SIZE-LIMIT-AND-DRY: reject files >600 lines unless a follow-up refactor issue is logged.

## AGENTS.md (project rules)

- CI = LOCAL ONLY. Verify by running `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- pinder-core scope only. Confirm the PR did NOT touch pinder-web frontend or Unity.

## The PR — #1134 (Closes #1123): Symmetric two-session GM — stateful + cached + bleed-isolated avatar session

Repo: decay256/pinder-core. Branch `fix/1123-symmetric-two-session-gm` → `main`. This is a BEHAVIOR-CHANGING ticket (not a rename). #1121 (OPPONENT→DATEE) and #1122 (player→PlayerAvatar) are already on main; current code uses `Datee` and `PlayerAvatar` terminology.

### The intended design
Each session = `GM system prompt + character spec + running transcript`. The avatar session must become stateful + cached + bleed-isolated, structurally identical to the existing datee session; the only delta between the two sessions is the injected character spec. ONE labeled `AVATAR:`/`DATEE:` transcript, compiled identically in both sessions, no assistant/user role mirroring.

### Verification points (answer each explicitly)
1. **Avatar history is genuinely stateful (the core behavior change).** Confirm `GameSessionState.AvatarHistory` (+ Clone / AdoptStateFrom / RestoreFromSnapshot), `GameSession._avatarHistory` + public `AvatarHistory`, `ResimulateData.AvatarHistory`, `GameStateSnapshot.AvatarHistory`, and the session-runner snapshot all mirror the existing `DateeHistory` paths EXACTLY (no asymmetry, no missing clone/reset path that would leak or drop history). The new stateful `DeliverMessageAsync(context, history, ct)` overload on `IStatefulLlmAdapter` must be implemented in ALL adapters (Pinder/Anthropic/OpenAi/Null) and every fake/stub in tests.
2. **Bidirectional bleed isolation (security-relevant).** The datee session's system/context must NOT contain the avatar's private stake (full PlayerAvatar system prompt) and vice versa. Verify the old full-spec carries (`DateeContext.PlayerAvatarPrompt`, `DeliveryContext.DateePrompt`) were genuinely dead (injected but never read) before removal, and the new `PublicProfileCard` carries ONLY public dating-app card fields (name + public profile), no private stake. Check both directions.
3. **Caching correctness.** System prompt + character spec + transcript prefix must be cacheable on the avatar session like the datee session (static-prefix-first ordering preserved; ephemeral cache_control reused). Confirm the cached prefix is reused across repeated turns and the transcript is the volatile suffix.
4. **Acceptance tests present + meaningful.** Confirm the 3 mandatory acceptance tests exist and actually assert what they claim: (a) avatar GM response at turn N includes turns 1..N-1 from persistent history; (b) bidirectional bleed isolation both directions; (c) avatar caching reuses the cached prefix across turns. Reject token/trivial assertions.
5. **Build + tests yourself.** Run `dotnet build Pinder.Core.sln` and `dotnet test Pinder.Core.sln` and (if present) `bash scripts/check-prompt-content.sh`. Report the ACTUAL counts you observed. Implementer claims build 0 err and 4431 passed / 0 failed / 27 skipped (baseline was 4427). Verify, don't trust.
6. **#1129 deploy-ordering note.** `AvatarHistory` is a NEW persisted field. Confirm the Research Log documents that its schema/data-reset coordination belongs to #1129 (no back-compat alias; restored pre-change snapshots start the avatar session cold — which must be safe). Flag if missing.
7. **Scope + size.** No edits to pinder-web frontend or Unity. No drive-by refactors beyond #1123's spec. No file over 600 lines without a logged follow-up.

### Output
Post your review to PR #1134 via `gh pr review 1134 --repo decay256/pinder-core --comment --body "..."` with the verdict line embedded. Then report back to the orchestrator in plain text: verdict, the build/test counts you actually ran, your findings on each of the 7 points (especially #1 symmetry and #2 bleed isolation), and any blocking vs non-blocking issues. If CHANGES_REQUESTED, enumerate each required change precisely.
