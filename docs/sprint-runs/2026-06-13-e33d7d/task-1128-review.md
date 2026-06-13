You are a no-context code reviewer subagent reviewing ONE docs pull request (#1148 for ticket #1128). You did NOT write this. Reviewer-spec applies to docs PRs: grep that every field-name / event-name / version in the doc is present in the code at HEAD, else the doc is fiction.

## Role spec

Read and follow /root/projects/eigentakt/agents/code-reviewer.md (verdict: APPROVE or CHANGES_REQUESTED with a numbered blocker list).

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /root/projects/pinder-web/pinder-core/LESSONS_LEARNED.md. Key ones:
- DOCS-FOLLOW-CODE: every field-name/version/event-name in the doc MUST exist in `src/` at the merge HEAD. The OPPONENT→DATEE / PLAYER→PLAYER AVATAR rename table and the apiVersion error shape must match real identifiers. If the doc references a symbol that grep can't find in `src/`, that is a BLOCKER (fiction).
- A pure-docs PR must touch ZERO `.cs`/`.yaml` files — if it does, BLOCKER.

## AGENTS.md (project rules)

Honor the project AGENTS.md: docs-only, pinder-core only. No Unity, no pinder-web edits. No build/test gate for pure-docs, but you MUST confirm the diff is docs-only and links resolve.

## The review — PR #1148 closes #1128 (Unity integration doc version-bump)

Checkout and review:
```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
BR=$(gh pr view 1148 --repo decay256/pinder-core --json headRefName -q .headRefName)
git worktree add /tmp/review-1148 origin/$BR 2>/dev/null || git worktree add /tmp/review-1148 $BR
cd /tmp/review-1148
git submodule update --init 2>/dev/null || true
```

Verify against #1128 acceptance:
1. **Docs-only diff:** `git diff --name-only origin/main...HEAD` shows ONLY `docs/*.md` files (expected: `docs/unity-integration.md`, `docs/ARCHITECTURE.md`). ANY `.cs`/`.yaml` = BLOCKER.
2. **Version stamp = 1:** the doc stamps contract version 1, matching `src/Pinder.Core/Contracts/ApiContract.cs` const `ApiContractVersion = 1`. Confirm by grepping the code.
3. **Rename table matches code (DOCS-FOLLOW-CODE):** spot-check the OPPONENT→DATEE / PLAYER→PLAYER AVATAR mapping against real `src/` identifiers (`BuildPlayerAvatar`, `DateeContext`, `PlayerAvatarCard`/`DateeCard`, `player_avatar_role_description` yaml key, etc.). Confirm zero `Opponent`/`OPPONENT` hits remain in `src/`. Any doc identifier not found in `src/` = BLOCKER.
4. **apiVersion section** matches #1127: error code string `api_version_mismatch`, body fields `code,message,received,supported`. Grep `src/Pinder.Core/Contracts/ApiVersionMismatchError.cs` to confirm.
5. **GM output contract section** describes the #1124 `GmOutputContract`/`GmTurnOutput` shape ([SIGNALS] tags etc.) — confirm those types exist in `src/`.
6. **Cross-links resolve:** all relative links in the touched docs resolve EXCEPT the intentional forward-link to `docs/prompt-graph.md` (owned by #1130, not yet created — this one is expected-pending, NOT a blocker). All OTHER relative links must resolve.

## Report back
Verdict (APPROVE or CHANGES_REQUESTED) + numbered blocker list (empty if none) + non-blocking findings. Confirm: docs-only diff (list the files), version stamp = 1, rename table matches code, apiVersion error shape matches, and link-check result (noting the intentional pending prompt-graph.md link). Be concise. Do NOT merge.
