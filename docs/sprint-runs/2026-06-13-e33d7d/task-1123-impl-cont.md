You are a backend engineer subagent FINISHING a partially-complete GitHub ticket in an EXISTING git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (use the EXISTING worktree — do NOT recreate it)

A previous implementer made substantial progress on this ticket but ran out of iterations before building/committing. ALL their work is uncommitted in /tmp/work-1123 on branch fix/1123-symmetric-two-session-gm. Your job is to FINISH it. Run EXACTLY this:

```bash
unset GITHUB_TOKEN
cd /tmp/work-1123
git status            # confirm the branch + uncommitted changes are present
git branch --show-current   # must print fix/1123-symmetric-two-session-gm
```

Do NOT run `git worktree add` (it already exists). Do NOT touch /root/projects/pinder-web/pinder-core directly. Do NOT reset/stash/discard the existing changes — build ON TOP of them. Use /usr/bin/dotnet (8.0.128) directly; do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH (that shim is BROKEN).

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). At the end of your PR body you MUST include `## DoD Evidence` and `## Research Log`.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1123/LESSONS_LEARNED.md. Key for this ticket:
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include `dotnet build` output, not just tests.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines; file a follow-up refactor ticket if one would cross 600.
- CACHE-PREFIX-STABILITY: keep static-prefix-first ordering (GM system prompt + character spec cacheable; transcript volatile suffix).
- SUBMODULE-SYNC-AFTER-REBASE: `git submodule update --init` after any rebase before building.

## AGENTS.md (project rules)

Honor the project AGENTS.md: CI = LOCAL ONLY (verify with `dotnet build` + `dotnet test`; never gate on GitHub Actions). Scope = pinder-core engine only; do NOT touch Unity client or pinder-web frontend.

## The ticket — #1123 Symmetric two-session GM (FINISH the in-progress implementation)

Issue #1123 makes the avatar GM session stateful + cached + bleed-isolated, structurally identical to the datee session (only the injected character spec differs; ONE labeled AVATAR:/DATEE: transcript; both sessions cacheable prefix). The previous implementer ALREADY did the core engine work (uncommitted in /tmp/work-1123):
- Added `AvatarHistory` to GameSessionState (field + Clone + AdoptStateFrom + RestoreFromSnapshot), `_avatarHistory` + public `AvatarHistory` on GameSession, ResimulateData.AvatarHistory, GameStateSnapshot.AvatarHistory, threaded through CreateSnapshot.
- Added `StatefulAvatarResult` type + a stateful `DeliverMessageAsync(context, history, ct)` overload on `IStatefulLlmAdapter`, implemented in PinderLlmAdapter/AnthropicLlmAdapter/OpenAiLlmAdapter/NullLlmAdapter. DeliveryStage threads `state.AvatarHistory` and accumulates returned entries.
- Extracted shared `SendStatefulAsync` in PinderLlmAdapter used by both datee + avatar paths (only injected character spec differs).
- Anthropic avatar path reuses `BuildPlayerAvatarOnlySystemBlocks` + `ConversationSession` replay; OpenAI reuses `BuildStatefulRequestJson` cache_control wrapping.
- Bleed removal: `DateeContext.PlayerAvatarPrompt` and `DeliveryContext.DateePrompt` were dead full-spec carries (injected but never read). Added new `PublicProfileCard` struct (name + public profile fields only) + `GameSessionHelpers.BuildPublicProfileCard`; replaced full-spec fields with `PlayerAvatarCard`/`DateeCard` on the contexts; re-pointed DateeResponseStage + DeliveryStage. New untracked files: src/Pinder.Core/Characters/PublicProfileCard.cs, src/Pinder.Core/Conversation/StatefulAvatarResult.cs.
- Wired session-runner snapshot (SessionSnapshot.AvatarHistory etc.) with a #1129 deploy-ordering note.

`src` builds clean. The TEST projects do NOT build yet. YOUR REMAINING WORK:

1. **Add the new avatar `DeliverMessageAsync(context, history, ct)` overload to the 4 remaining fake/stub `IStatefulLlmAdapter` implementations** that don't have it yet: `Issue365_ShadowOnFailedRollTests`, `Issue399_HorninessShadowOrderingTests`, `Issue840_NoLengthClampTests`, `Issue927_FinalVerdictTierTests`. Use the SAME delegating pattern already applied to the fixed fakes (Issue536, Issue788, Issue1095, Issue364) — copy that pattern exactly. (Build errors will name any other CS0535 fakes you missed — fix all of them.)

2. **Update every test construction site** that passes the now-removed params `playerAvatarPrompt:` / `dateePrompt:` to `new DateeContext(...)` / `new DeliveryContext(...)`, and any test reading `.PlayerAvatarPrompt` / `.DateePrompt` on those two contexts. The compiler errors (CS1739 "does not have a parameter named 'dateePrompt'/'playerAvatarPrompt'", CS1503 arg-shift) enumerate them — fix until `dotnet build` of the FULL solution (src + all test projects) is 0 errors. Known affected files include: ShadowTaintTests.cs, ArchetypeInjectionTests.cs, Issue534_DebugFlagTests.cs, Issue241_LegendaryFailVoiceTests.cs, EngineInjectionBlockTests.Helpers.cs, SessionDocumentBuilderTests.Helpers.cs, SessionDocumentBuilderSpecTests.cs, AnthropicLlmAdapterIssue208Tests.cs, Issue372_ArchetypeDirectiveDeliveryTests.cs — plus any others the build surfaces. For each site, drop the removed arg (the field was dead/never-read, so removing it preserves behavior; if a test ASSERTED on .PlayerAvatarPrompt/.DateePrompt, replace the assertion with the equivalent on the new PlayerAvatarCard/DateeCard, or delete the now-meaningless assertion and note it in the Research Log).

3. **Add the required ACCEPTANCE TESTS (xUnit)** — these are mandatory acceptance criteria, not optional:
   - Avatar GM response at turn N includes turns 1..N-1 from its persistent history (assert the avatar adapter receives accumulated history, mirroring the existing datee-history test).
   - Bidirectional bleed isolation: assert the datee session's system/context never contains the avatar's private stake (full PlayerAvatar system prompt), AND the avatar session's never contains the datee's private stake. Both directions.
   - Caching: extend `AnthropicTransportCachingTests` / CacheBlockBuilder specs to cover the avatar stateful path reusing the cached prefix on repeated turns.

4. **Verify green:** `dotnet build Pinder.Core.sln` 0 errors (capture output). `dotnet test` — baseline before this ticket = 4427 passed / 0 failed / 27 skipped; your new tests ADD to the passing count. If ANY test fails, run that scope 3× and report deterministic-vs-flake and whether it also fails on origin/main. Do NOT mislabel a regression as a flake.

5. **Commit, push, open PR** against decay256/pinder-core main. PR body MUST contain `Closes #1123` on its own line, plus `## DoD Evidence` (build + full test summary) and `## Research Log` (the shared compile path, the bleed-removal approach + PublicProfileCard shape, the new persisted AvatarHistory field + its #1129 deploy-ordering note, any test assertions you had to drop/rewrite, any follow-ups filed). Do NOT merge. Do NOT push to main.

6. **agent.log:** append to /tmp/work-1123/agent.log a `started` JSONL line at entry and a `completed` line with PR URL + SHA at exit (`bash /root/projects/eigentakt/scripts/log.sh` if it works, else manual JSONL).

### Out of scope
- #1124 (shared GM puppeteer prompt + parseable output), #1125 (delivery→commit), #1126, #1129 (data reset/schema rename), #1130 (docs sweep). Unity / pinder-web frontend. Do NOT expand scope; just finish #1123 and make it green.

Report back: PR URL, commit SHA, build result, FULL dotnet test summary (passed/failed/skipped), list of files changed with line counts, the acceptance tests you added, any assertions rewritten/dropped, and any follow-up tickets filed.
