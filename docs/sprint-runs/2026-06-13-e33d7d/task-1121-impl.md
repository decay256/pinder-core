You are a backend engineer subagent implementing ONE GitHub ticket end-to-end in an isolated git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (isolated worktree — non-negotiable)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1121 origin/main
cd /tmp/work-1121
git checkout -b fix/1121-opponent-to-datee
```

All edits, builds, tests, commits happen inside /tmp/work-1121.

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). At the end of your PR body you MUST include `## DoD Evidence` and `## Research Log` sections.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1121/LESSONS_LEARNED.md. Key ones for this ticket:
- SUBMODULE-SYNC-AFTER-REBASE: after any rebase, `git submodule update --init` before building.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines; this is a pure rename so should not grow files.
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include the build command output (`dotnet build`), not just tests.

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- CI = LOCAL ONLY. Verify with `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- Scope = pinder-core only for THIS ticket (the engine). DO NOT touch the Unity client. The ticket mentions pinder-web frontend changes — those are OUT OF SCOPE for this pinder-core PR; note them for a follow-up but do not edit pinder-web here (it is a separate repo / submodule parent).

## The ticket — #1121 Rename OPPONENT → DATEE

> Part of the Pinder two-session Game-Master refactor. PURE RENAME, no behavioral change.

"Opponent" is a combat metaphor; replace with **DATEE** (coined like EMPLOYEE). Casing: `DATEE` in prompts, `Datee` in C#, `datee` in trace keys.

### Scope (within pinder-core only)
- Identifiers: `OpponentContext`→`DateeContext`, `GetOpponentResponseAsync`→`GetDateeResponseAsync`, `OpponentResponse`→`DateeResponse`, `BuildOpponent`→`BuildDatee`, `StatefulOpponentResult`→`StatefulDateeResult`, and EVERY member/local/param/field carrying "opponent". Rename files too (e.g. `OpponentVisibleProfile.cs`→`DateeVisibleProfile.cs`, `OpponentContext.cs`→`DateeContext.cs`, `OpponentResponse.cs`→`DateeResponse.cs`, `OpponentResponseStage.cs`, `OpponentDefenseSnapshot.cs`, `OpponentTimingCalculator.cs`, `OpponentResponseParsers.cs`). Use `git mv` for file renames.
- Prompts/templates: `OpponentResponseInstruction`, the "YOU ARE TALKING TO" prompt text, visible-profile DTO `OpponentVisibleProfile`→`DateeVisibleProfile`.
- Trace keys (string literals): `"opponent"`→`"datee"`, `"opponent-system"`→`"datee-system"`.
- Docs inside pinder-core/docs: `prompt-graph.md`, `ARCHITECTURE.md`, module docs, specs — rewrite the live references (NOT historical sprint-run logs under docs/sprint-runs/ and NOT CHANGELOG history entries).

### CRITICAL — preserve persisted-data back-compat
Some trace keys / JSON property names may be persisted (DB, audit NDJSON, serialized snapshots). Renaming a serialized JSON property name silently breaks deserialization of existing saved sessions. For ANY `[JsonPropertyName("opponent...")]` or serialization attribute or dictionary key that is persisted: rename the C# identifier but check whether the wire/JSON name must stay stable or needs a migration. The ticket title says "and persisted keys" so the new key IS desired — but if old data exists, add a back-compat read alias (accept both old+new on read, write new) rather than a hard break, UNLESS #1129 (the data-reset ticket) is explicitly handling the migration. Note your decision in `## Research Log`. When in doubt, prefer the reversible path (read-alias) and document it.

### Acceptance
- `git grep -ri opponent` inside the worktree returns 0 hits in LIVE code/prompts/docs (excluding `docs/sprint-runs/`, `agent.log`, `CHANGELOG.md` historical entries, `contracts/` historical sprint contracts, `LESSONS_LEARNED.md`). Report the exact exclude set you used and the residual grep output.
- `dotnet build` of Pinder.Core.sln succeeds (capture output).
- `dotnet test` green (capture summary; if any failures, run the suite 3× and report whether deterministic vs flake, and whether the same failure exists on origin/main).
- Trace output uses DATEE / DATEE-SYSTEM.

### Out of scope
- Behavioral changes (pure rename).
- pinder-web frontend changes (separate repo).
- The PLAYER→PLAYER AVATAR rename (#1122) — leave "player" alone.

## Build/test offload
A remoting bin exists at /root/.openclaw/agents-extra/pinder/bin/ (dotnet/npm/npx shims that offload heavy builds to a remote Docker container over Tailscale). If a local `dotnet build` is too heavy, prepend that dir to PATH. Otherwise local dotnet 8.0.128 is available.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1121` on its own line, plus `## DoD Evidence` (build + test output) and `## Research Log` (decisions, esp. the persisted-key back-compat call, and the grep exclude set).
- Do NOT merge. Do NOT push to main. Report the PR URL + commit SHA when done.
- Append to agent.log (/tmp/work-1121/agent.log): a `started` JSONL line at entry and a `completed` line with PR URL + SHA at exit. Use `bash /root/projects/eigentakt/scripts/log.sh` if it works, else append a JSONL line manually.

Report back: PR URL, commit SHA, build result, test result (with rerun analysis if any failures), the residual `git grep -ri opponent` output with your exclude set, file count changed, and any follow-ups you filed.
