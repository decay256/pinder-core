You are a no-context code reviewer reviewing ONE pull request with fresh eyes. You did not write this code. Be a structural critic.

## Role spec

Read and follow /root/projects/eigentakt/agents/code-reviewer.md (Review Checklist + Output Format). You MUST end with an explicit verdict line: `**Verdict: APPROVE**` or `**Verdict: CHANGES_REQUESTED**`. Use `gh pr review --comment` (NOT --approve; SELF-APPROVE-BLOCKED).

## Lessons (named)

- SUBMODULE-SYNC-AFTER-REBASE: if you rebase, `git submodule update --init` before building.
- BUILD-PIPELINE-DISCIPLINE: re-run the build yourself; tests-pass alone is insufficient.
- IMPLEMENTER-OVERCLAIMS-DETERMINISTIC-FAILURE: do not trust the implementer's "all green" — re-run build + a representative test slice yourself.
- FILE-SIZE-LIMIT-AND-DRY: reject files >600 lines unless a follow-up refactor issue is logged.

## AGENTS.md (project rules)

- CI = LOCAL ONLY. Verify by running `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- pinder-core scope only. Confirm the PR did NOT touch pinder-web frontend or Unity.

## The PR — #1132 (Closes #1122): rename in-game CHARACTER "player" → PLAYER AVATAR

Repo: decay256/pinder-core. Branch `fix/1122-player-to-player-avatar` → `main`. This is a SEMANTIC-SPLIT rename: the in-game character becomes PLAYER AVATAR; the HUMAN user/account/option-picker stays "player". #1121 (OPPONENT→DATEE) is already on main.

### Setup
```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/review-1132 origin/fix/1122-player-to-player-avatar
cd /tmp/review-1132
```
Do NOT edit the canonical clone. Work in /tmp/review-1132.

### Verification points (answer each explicitly)
1. **Audit table fidelity (THE core check).** The PR body contains an audit table of every "player" touchpoint with a RENAME or KEEP disposition. Cross-check the table against the actual diff (`git diff origin/main...HEAD`): (a) every RENAME row corresponds to a real rename in the diff; (b) every KEEP row is genuinely untouched; (c) the diff contains NO rename that's missing from the table. Flag any mismatch.
2. **Semantic correctness.** Are the RENAMED symbols genuinely the in-game CHARACTER's voice/profile/prompt (PlayerAvatar), and the KEPT symbols genuinely human/account/option-pick/display? Scrutinize borderline calls — especially `playerSenderName` (implementer KEPT it as transcript display label) and `player_role_description`/`player_probing` persisted yaml keys (KEPT, deferred to follow-up #1133). Do you agree with these dispositions? State agreement or dissent with reasoning.
3. **Pure-rename integrity.** Confirm NO behavioral change — only identifier/string renames. Voice-isolation behavior must be unchanged. Flag any logic edit.
4. **Build + tests yourself.** Run `dotnet build Pinder.Core.sln` (dotnet 8.0.128 local; NOTE the remoting shim at /root/.openclaw/agents-extra/pinder/bin is BROKEN — use /usr/bin/dotnet directly) and `dotnet test Pinder.Core.sln` + `bash scripts/check-prompt-content.sh`. Report the actual counts YOU observed.
5. **Scope.** No edits to pinder-web, Unity, or human-side "player" identifiers. No file over 600 lines.

### Output
Post your review to PR #1132 via `gh pr review 1132 --repo decay256/pinder-core --comment --body "..."` with the verdict line embedded. Then report back to the orchestrator in plain text: verdict, the build/test counts you actually ran, your audit-table cross-check result (any mismatches), your agreement/dissent on the borderline dispositions (point #2), and any blocking vs non-blocking findings.
