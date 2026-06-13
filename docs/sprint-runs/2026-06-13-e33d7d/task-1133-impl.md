You are a backend engineer subagent implementing ONE GitHub ticket (#1133) end-to-end in an isolated git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (isolated worktree — non-negotiable)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1133 origin/main
cd /tmp/work-1133
git submodule update --init 2>/dev/null || true
git checkout -b fix/1133-yaml-key-player-avatar-role-description
```

All edits, builds, tests, commits happen inside /tmp/work-1133. Use /usr/bin/dotnet (8.0.128) directly. Do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH — that remoting shim is BROKEN.

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). Your PR body MUST include `## DoD Evidence` and `## Research Log` sections.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1133/LESSONS_LEARNED.md. Key ones:
- SUBMODULE-SYNC-AFTER-REBASE: after any rebase, `git submodule update --init` before building.
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include the `dotnet build` output, not just tests.
- WIRE-CONTRACT-REGRESSION-TESTS: persisted yaml keys are a wire/persisted shape — silently dropping the old key would pass a happy-path test. Pin the back-compat read path (old-only, new-only, both-prefers-new) in a regression test.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines.

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- CI = LOCAL ONLY. Verify with `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- Scope = pinder-core ONLY. DO NOT touch the Unity client or the pinder-web admin UI. The pinder-web emitter update is a CROSS-REPO follow-up, NOT implemented here.
- Keep changes scoped to exactly what #1133 specifies; do not drive-by refactor.

## Scope — #1133 align config key player_role_description → player_avatar_role_description

Follow-up from #1122 (the player→PLAYER AVATAR semantic split). #1122 renamed code identifiers but deliberately KEPT the persisted yaml config keys `player_role_description` / `player_probing` because renaming them is a config-migration needing back-compat.

1. `data/game-definition.yaml`: `player_role_description` → `player_avatar_role_description`. Optionally `player_probing` → `player_avatar_probing` (see below — the probing rename is OPTIONAL). (Confirmed present ~lines 39 and 144 — verify current line numbers before editing.)
2. `src/Pinder.LlmAdapters/GameDefinition.Parser.cs`: `GetRequired("player_role_description")` (~line 81) + `GetOptional("player_probing")` (~line 107).
3. `src/Pinder.LlmAdapters/GameDefinition.cs`: `PlayerRoleDescription` / `PlayerProbing` properties (keep the C# property names OR align them — your call, but the PERSISTED yaml key is what must change + stay back-compat; note your choice in the Research Log).
4. `src/Pinder.LlmAdapters/SessionSystemPromptBuilder.cs`: the `AppendLine(..., "game-definition.yaml", "player_role_description")` / `player_probing` source-anchor strings (~lines 62, 89, 122, 125) — update the anchor key strings so the prompt-tracer source attribution points at the NEW key names.
5. **Backward-compat (MANDATORY, hard acceptance criterion):** the parser MUST accept BOTH old and new keys for one release — read old, **prefer new** when both present — so staging's live game-definition.yaml and the staging-edits admin flow don't break mid-migration.

### Acceptable smaller slice
Per the refiner: the `player_probing` → `player_avatar_probing` rename is OPTIONAL (it's a GetOptional key). If the dual-optional-key handling isn't worth it, renaming ONLY `player_role_description` and leaving `player_probing` as-is is acceptable — note the decision in DoD.

### Out of scope
- The transcript display-name/sender family (`PlayerName`, `playerSender`/`playerSenderName`) — KEEP per #1122's audit (it's a sender label, not the avatar's voice).
- pinder-web admin UI / staging emitter (cross-repo follow-up — list it, do not edit).
- Unity.

## Acceptance (assert in tests — xUnit)
- New keys read; old keys still accepted (back-compat read path) with new PREFERRED when both present.
- Source-anchor strings in SessionSystemPromptBuilder updated so prompt-tracer attribution points at the new key names.
- **Regression test (WIRE-CONTRACT-REGRESSION-TESTS):** a parser test asserting a game-definition.yaml with ONLY the old keys still parses (read path), one with ONLY new keys parses, and one with BOTH prefers the new value. This is the exact "looks right but is wrong" trap — pin it.
- `dotnet build Pinder.Core.sln` succeeds (capture output).
- `dotnet test Pinder.Core.sln` green. Baseline on main = 4442+ passed / 0 failed / 27 skipped (counts have grown across the sprint; confirm 0 failures, your new tests ADD to the pass count). If ANY test fails, run that scope 3× and report deterministic-vs-flake, and whether the same failure exists on origin/main. Do NOT label a regression as a flake.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1133` on its own line, plus `## DoD Evidence` (build + test output) and `## Research Log` (which keys you renamed, the back-compat read strategy, whether you renamed player_probing or kept it, the pinder-web emitter follow-up you filed/listed).
- Do NOT merge. Do NOT push to main. Report the PR URL + commit SHA when done.
- Append to /tmp/work-1133/agent.log a `started` JSONL line at entry and a `completed` line with PR URL + SHA at exit.

Report back: PR URL, commit SHA, build result, full test result (pass/fail/skip counts, with rerun analysis if any failures), list of files changed with line counts, which keys renamed, the back-compat strategy, and the cross-repo follow-up listed.
