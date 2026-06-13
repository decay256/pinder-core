You are a backend engineer subagent FINISHING a partially-complete GitHub ticket in an EXISTING git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (continue in the EXISTING worktree — do NOT recreate)

The worktree already exists with substantial work committed. Run EXACTLY this; do NOT touch /root/projects/pinder-web/pinder-core directly, and do NOT delete or re-add the worktree:

The worktree was originally created with `git worktree add /tmp/work-1125 origin/main` — it ALREADY EXISTS. Do NOT run `git worktree add` again, do NOT recreate or delete it, do NOT reset/stash. Just enter it:

```bash
unset GITHUB_TOKEN
cd /tmp/work-1125
git status
git log --oneline origin/main..HEAD
git branch --show-current   # expect: fix/1125-collapse-delivery
```

All edits, builds, tests, commits happen inside /tmp/work-1125 on the existing branch `fix/1125-collapse-delivery`. Use /usr/bin/dotnet (8.0.128) directly. Do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH — that remoting shim is BROKEN and a runaway recursive instance of it was already killed once in this worktree; if you see an `.eigentakt-bin/bash` process at high CPU, kill it. gh is authenticated as decay256 (HTTPS); after `unset GITHUB_TOKEN`, push with the gh credential helper.

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). Your PR body MUST include `## DoD Evidence` and `## Research Log`.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1125/LESSONS_LEARNED.md. Key ones: SUBMODULE-SYNC-AFTER-REBASE; FILE-SIZE-LIMIT-AND-DRY (≤400 soft/600 hard lines); BUILD-PIPELINE-DISCIPLINE (DoD includes build output); CACHE-PREFIX-STABILITY (GM system prompt + character spec are the cacheable prefix; transcript is volatile — keep static-prefix-first ordering).

## AGENTS.md (project rules)

CI = LOCAL ONLY (verify with local `dotnet build` + `dotnet test`, never GitHub Actions). Scope = pinder-core ONLY (no Unity client, no pinder-web frontend; note any such change as a follow-up). Keep changes scoped to #1125; no unrelated drive-by refactors.

## Current state of THIS worktree (what the prior implementer already did)

The prior Rung-0 implementer hit its iteration limit. State as of commit `659eee1` (off origin/main @ 08f3a36):
- **Build is GREEN (0 errors).** Production seam is done:
  - New `DeliveryOverlay.cs`: deterministic, NON-LLM mapping of (picked full line, FailureTier, miss-margin) → committed line. Success = verbatim; failure tiers degrade (hesitate/stumble/trim-tail/blurt), severity scales by tier + margin.
  - `DeliveryStage.cs` rewritten as a commit/overlay stage (no delivery LLM call): picked full line → steering append → `DeliveryOverlay.Apply` → commit. Trap/shadow/horniness overlays retained.
  - `DialogueOption.IntendedText` repurposed (doc-only) to carry the FULL sendable line (avoided a 104-site rename).
  - `DeliverMessageAsync` removed from `ILlmAdapter`, `IStatefulLlmAdapter`, and all 4 adapters (Pinder/Anthropic/OpenAi/Null) plus `SendStatefulAvatarAsync`.
- LlmAdapters/Rules/RemoteAssets test suites already GREEN (~1200+243+54 passing).
- One uncommitted modified file: `tests/Pinder.Core.Tests/Conversation/Issue1123_SymmetricTwoSessionGmTests.cs` (mid-edit).

## YOUR remaining work — drive #1125 to a fully GREEN suite, then open the PR

### 1. Dead-code removal still owed by ticket scope (left present by the prior agent)
Remove the now-dead delivery-LLM surface (confirm no live callers first, then delete):
- `SessionDocumentBuilder.BuildDeliveryPrompt` and `BuildDeliveryPromptEx` (+ `SessionDocumentBuilder.Trace.cs` `{chosen_option}`/`{intended_message}` substitution paths that only fed the delivery call).
- `RollContextBuilder` delivery feed, `ToolSchemas.Delivery`, `EngineDeliveryBlock`, `BuildSuccessDeliveryInstruction`/`BuildFailureDeliveryInstruction` creative templates, and `LlmPhase.Delivery` — but ONLY if fully dead. If any is still referenced by retained behavior, keep it and note why in the Research Log. If removing `LlmPhase.Delivery` ripples widely, instead document it as a tiny follow-up rather than ballooning scope.

### 2. Migrate the ~32-33 failing Pinder.Core.Tests to the new commit-step behavior (these are EXPECTED test debt, NOT regressions)
The trace no longer contains a `delivery` LLM phase — it is now `avatar option-gen (ephemeral) → overlay (ephemeral) → commit → datee_response`. Update each failing test to assert the NEW ordering / behavior. Known failing groups:
- `Phase0` transport tests (`I3`/`I4`/`SmokeTest`/`F_FailureModes`/`I6_Cancellation`): assert the new commit-step trace ordering, not a `delivery` transport phase.
- `Issue308_ShadowThresholdWiringSpecTests` / `Issue308_DeliveryDateeShadowThresholdsTests`: they captured `DeliveryContext` via the removed `DeliverMessageAsync` hook — repoint to the overlay/commit path or assert the equivalent property.
- `Issue1123_SymmetricTwoSessionGmTests` (the uncommitted file): avatar history no longer accumulates the way it did — reconcile to intended behavior; if a persisted-behavior change is implied, it is a #1129-owned concern — document in Research Log, do NOT add back-compat.
- `Issue364_SteeringBeforeDelivery`, `Issue695_StatFailureCorruption`, `GameSessionTrapTaint`, `TrapDuration1Regression`, `Issue333`, `Issue840`, `Phase5` clone/transport tests.
- Corruption/failure-tier parity: assert the committed line is degraded for a failure outcome via the deterministic overlay (the corruption that previously came from the delivery LLM now comes from `DeliveryOverlay`).
Retire tests that asserted the existence of the delivery LLM call itself (that surface is gone by design); replace with equivalent assertions on the new commit/overlay path. Do NOT weaken a test merely to make it pass — preserve the property it guarded, repointed to the new mechanism.

### 3. Acceptance (must all hold before you open the PR)
- `dotnet build Pinder.Core.sln` succeeds (capture output).
- `dotnet test Pinder.Core.sln` GREEN. Baseline before #1125 = 4442 passed / 0 failed / 27 skipped. Final must be ≥ 4442 passing (your migrated/new tests may net a few different from baseline — report exact counts). If ANY test fails after your work, run that scope 3× and report deterministic-vs-flake AND whether the same failure exists on origin/main. Do NOT label a regression as a flake; do NOT delete a test just to get green.
- No LLM call labeled `delivery` in the trace; transcript contains only committed lines (option/steering/overlay text never persists); failure tiers degrade the committed line via overlay; options carry full text and downstream consumers are updated.

## PR
- Commit remaining work (clear messages), push the branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1125` on its own line, plus `## DoD Evidence` (build + FULL test output with counts) and `## Research Log` (delivery-LLM removal + its former callers/interface members; how options now carry full text; the `DeliveryOverlay` deterministic design; what `DeliveryStage` became; clean-history enforcement; the avatar-history behavior change noted for #1129 with NO back-compat; any follow-up tickets filed for residual dead code).
- Do NOT merge. Do NOT push to main. Report PR URL + commit SHA when done.
- Append to /tmp/work-1125/agent.log a `started` line at entry and a `completed` line with PR URL + SHA at exit.

Report back: PR URL, commit SHA, build result, FULL test result (exact passed/failed/skipped counts, with rerun analysis if any failures), list of files changed with line counts, what dead code you removed vs deferred (and any follow-up issue numbers filed), and confirmation that the clean-history + overlay-parity acceptance assertions exist as tests.
