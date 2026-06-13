You are a backend engineer subagent implementing ONE GitHub ticket end-to-end in an isolated git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (isolated worktree — non-negotiable)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1123 origin/main
cd /tmp/work-1123
git checkout -b fix/1123-symmetric-two-session-gm
```

All edits, builds, tests, commits happen inside /tmp/work-1123. Use /usr/bin/dotnet (8.0.128). Do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH — that remoting shim is BROKEN (missing scripts/remote-exec.sh). Use local dotnet directly.

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). At the end of your PR body you MUST include `## DoD Evidence` and `## Research Log` sections.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1123/LESSONS_LEARNED.md. Key ones for this ticket:
- SUBMODULE-SYNC-AFTER-REBASE: after any rebase, `git submodule update --init` before building.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines. This ticket ADDS behavior; if a file would cross 600 lines, split it or file a follow-up refactor ticket via `gh issue create` and note it in the Research Log.
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include the `dotnet build` output, not just tests.
- CACHE-PREFIX-STABILITY: the cacheable prefix must be (GM system prompt + character spec) with the running transcript as the volatile suffix — keep static-prefix-first ordering in SessionSystemPromptBuilder so prompt caching pays off.

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- CI = LOCAL ONLY. Verify with `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- Scope = pinder-core only (the engine). DO NOT touch the Unity client or pinder-web frontend (separate repo). If a frontend change is implied, note it for a follow-up; do not edit pinder-web here.
- This is a BEHAVIOR-CHANGING ticket (not a rename). Keep behavior changes scoped to exactly what #1123 specifies; do not drive-by refactor unrelated code.

## The ticket — #1123 Symmetric two-session GM: make the avatar session stateful + cached like the datee session

> Part of the Pinder two-session Game-Master refactor. BEHAVIOR-CHANGING. Depends on #1121 (OPPONENT→DATEE, MERGED) and #1122 (player→PlayerAvatar, MERGED) — both already landed on main; current code uses `Datee` and `PlayerAvatar` terminology.

### Model (the target design)
Each session = `GM system prompt + character spec + running transcript`. The **only** difference between the two sessions is the injected **character spec**.
- The GM is a puppeteer that acts out ONE character, NOT the character itself.
- The conversation is **ONE labeled transcript** (lines prefixed `AVATAR:` / `DATEE:`), compiled **identically** in both sessions.
- **No assistant/user role mirroring** — both sessions see the same labeled transcript as context.

### Scope (within pinder-core only)
1. **Make the avatar session stateful** like the datee session: accumulate the labeled transcript across turns; the engine (`GameSession` / `GameSessionState`) owns BOTH histories. Today only the datee call is a persistent context window (`GameSessionState.DateeHistory`, threaded through `DateeResponseStage` → `IStatefulLlmAdapter.GetDateeResponseAsync(ctx, history)` and the Anthropic stateful replay path). The avatar/player-avatar side is currently STATELESS one-shot — `AnthropicLlmAdapter.GetDialogueOptionsAsync` / `DeliverMessageAsync` rebuild `BuildPlayerAvatar(...)` fresh every turn with no history param and there is NO `AvatarHistory` field anywhere. Add a persistent avatar history mirroring `DateeHistory` exactly (clone, CopyFrom, reset, snapshot/restore/resimulate paths) and a stateful avatar adapter overload parallel to `GetDateeResponseAsync`.
2. **Both sessions compiled via the identical path**; only the character spec differs. Extract/confirm one shared compile path (e.g. a private `Compile(systemPrompt, characterSpec, history, userContent)`) used by BOTH datee and avatar sessions; the injected spec (`DateePrompt` vs `PlayerAvatarPrompt`) is the only delta.
3. **Prompt caching for both**: system prompt + character spec + transcript prefix must be cacheable on the avatar session just like the datee session. Reuse the existing ephemeral `cache_control` cache-block machinery (`CacheBlockBuilder.BuildPlayerAvatarOnlySystemBlocks` / `BuildDateeOnlySystemBlocks`) and the Anthropic stateful replay block. Keep static-prefix-first ordering. Cross-ref `AnthropicTransportCachingTests`.
4. **Strict bleed isolation:** each session's system prompt carries ONLY its own character's stake. The other character appears only as (a) a public dating-app card and (b) sent messages in the labeled transcript. REMOVE the current "full player prompt in datee context" bleed: `DateeContext.PlayerAvatarPrompt` is populated from `player.AssembledSystemPrompt` (the full player character system prompt) at `DateeResponseStage.cs` and consumed via the steering path (`OpenAiLlmAdapter` / `PinderLlmAdapter`). Replace that full-spec carry with a minimal public dating-app card struct (name + public profile card fields only). Apply the symmetric rule to the avatar context (it must not carry the full datee spec). Re-point any consumers (steering path) accordingly.

### Acceptance (assert in tests — xUnit)
- Avatar GM response at turn N includes turns 1..N-1 from its persistent history (NOT rebuilt from scratch). Add a test asserting the avatar adapter receives accumulated history.
- **Bleed isolation tests:** the datee session's system/context never contains the avatar's private stake (full PlayerAvatar system prompt); the avatar session's never contains the datee's private stake. Assert both directions.
- **Caching verified:** repeated turns reuse the cached prefix on BOTH sessions (extend `AnthropicTransportCachingTests` / `CacheBlockBuilder` specs to cover the avatar stateful path).
- `dotnet build` of Pinder.Core.sln succeeds (capture output).
- `dotnet test` green. Baseline before this ticket = 4427 passed / 0 failed / 27 skipped. If ANY test fails, run the suite 3× and report deterministic-vs-flake, and whether the same failure exists on origin/main. Do NOT label a regression as a flake.

### Key files (recon map — verify before editing)
- `src/Pinder.Core/Conversation/GameSession.cs` (~:24,49-50 `_dateeHistory`), `GameSessionState.cs` (~:20 `DateeHistory`; clone :59, CopyFrom :104, reset :175) — add avatar history symmetrically.
- `src/Pinder.Core/Conversation/DateeResponseStage.cs` (~:63-110 stateful datee build; :64 the bleed source).
- `src/Pinder.Core/Conversation/DateeContext.cs` (~:13,63-84 `PlayerAvatarPrompt` bleed field).
- `src/Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` (~:68-142 stateless avatar path; :154-194 datee stateful replay).
- `src/Pinder.LlmAdapters/SessionSystemPromptBuilder.cs` (~:30 Build, :72/79 BuildPlayerAvatar, BuildDatee; :42-64 static-prefix-first).
- `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` + `.Trace.cs`; `Anthropic/CacheBlockBuilder.cs` (:20/:48/:69).
- `TurnOrchestrator.Helpers.cs` (~:80 `BuildHistoryForLlmContext`) / `HistoryFormatter` — single labeled `AVATAR:`/`DATEE:` transcript.
- Tests: `tests/Pinder.Core.Tests/DateeResponseTests.cs`, `tests/Pinder.Core.Tests/Conversation/*`, `tests/Pinder.LlmAdapters.Tests/Anthropic/AnthropicTransportCachingTests.cs`, `SessionDocumentBuilderSpecTests*.cs`, `Anthropic/Issue241_LegendaryFailVoiceTests.cs`.

### Out of scope (do NOT do here)
- The shared GM puppeteer system prompt + parseable output contract (#1124 — depends on this ticket).
- Collapsing delivery into a commit step (#1125).
- Persistence schema rename / data reset (#1129) — but NOTE: #1121 already hard-renamed persisted keys with NO back-compat on the assumption #1129 does the wipe. If you ADD new persisted fields (e.g. AvatarHistory in a snapshot), follow the same no-back-compat / wipe-owned-by-#1129 convention and document it in the Research Log so #1129 carries it forward.
- Unity client / pinder-web frontend.
- Docs sweep (#1130) — but DO update any pinder-core/docs reference that becomes outright false (e.g. "avatar is stateless"); note what you changed.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1123` on its own line, plus `## DoD Evidence` (build + test output) and `## Research Log` (design decisions: the shared compile path, the bleed-removal approach + the public-card struct shape, the new persisted avatar-history field and its #1129 deploy-ordering note, any follow-ups filed).
- Do NOT merge. Do NOT push to main. Report the PR URL + commit SHA when done.
- Append to agent.log (/tmp/work-1123/agent.log): a `started` JSONL line at entry and a `completed` line with PR URL + SHA at exit. Use `bash /root/projects/eigentakt/scripts/log.sh` if it works, else append a JSONL line manually.

Report back: PR URL, commit SHA, build result, full test result (with rerun analysis if any failures), a list of files changed with line counts, how you implemented each of the 4 scope items, the bleed-removal approach, and any follow-up tickets you filed.
