You are a backend engineer subagent implementing ONE GitHub ticket end-to-end in an isolated git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (isolated worktree — non-negotiable)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1125 origin/main
cd /tmp/work-1125
git checkout -b fix/1125-collapse-delivery-into-commit
```

All edits, builds, tests, commits happen inside /tmp/work-1125. Use /usr/bin/dotnet (8.0.128). Do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH — that remoting shim is BROKEN. Use local dotnet directly. gh is authenticated as decay256 (HTTPS); push with the gh credential helper after `unset GITHUB_TOKEN`.

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). Your PR body MUST include `## DoD Evidence` and `## Research Log` sections.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1125/LESSONS_LEARNED.md. Key ones:
- SUBMODULE-SYNC-AFTER-REBASE: after any rebase, `git submodule update --init` before building.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines. If a file would cross 600, split it or file a follow-up refactor via `gh issue create` and note it in the Research Log.
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include the `dotnet build` output, not just tests.
- CACHE-PREFIX-STABILITY: the cacheable prefix is (GM system prompt + character spec); the running transcript is the volatile suffix. Don't disturb static-prefix-first ordering in SessionSystemPromptBuilder.

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- CI = LOCAL ONLY. Verify with `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- Scope = pinder-core only (the engine). DO NOT touch the Unity client or pinder-web frontend. If a frontend/Unity change is implied, note it for a follow-up; do not edit pinder-web here.
- This is a BEHAVIOR-AFFECTING ticket (per-turn flow change + removal of an LLM call). Keep changes scoped to exactly what #1125 specifies; do not drive-by refactor unrelated code.

## Prerequisite context (#1123 + #1124 are MERGED)

Build ON TOP of merged main (HEAD 08f3a36). Relevant already-landed structure:
- #1123: symmetric two-session GM — avatar session is stateful + cached + bleed-isolated; shared stateful compile path `PinderLlmAdapter.SendStatefulAsync` for both datee and avatar; a new persisted `AvatarHistory` field exists (no back-compat — wipe owned by #1129).
- #1124: ONE shared GM system prompt (`SessionSystemPromptBuilder` consolidated onto `AppendGmBase` + `AppendCharacterSpec`); legacy `Build(both)` DELETED; canonical `GmOutputContract` (Emit/Parse) + `GmTurnOutput` value type exist in src/Pinder.LlmAdapters/. The datee `[SIGNALS]` parse path already routes through `GmOutputContract`.

## The ticket — #1125 Collapse delivery into a commit step; options become full sendable lines

> Part of the Pinder two-session Game-Master refactor. Depends on #1123 (MERGED) and #1124 (MERGED).

### Why
`delivery` is today a hidden SECOND creative LLM call that expands a picked intent (`IntendedText`, a gist) into the actual sent words modulated by the roll. Collapse it: options become full sendable messages, and "delivery" becomes a non-LLM **commit** step.

### New per-turn flow (target)
1. Avatar GM generates N **full, sendable** candidate lines (ephemeral).
2. Human picks one; pinder-core resolves the dice roll.
3. If roll degrades/corrupts → stateless **overlay** mutates the picked line (ephemeral, NON-LLM).
4. **Commit** the final line into the transcript (the only write).
5. Datee GM generates its response (committed).

### Clean-history rule
Persistent history holds **delivered (committed) lines only**. Option-generation, steering, and degradation/corruption overlays read context but are **pruned after the pick** — never written back.

### Scope (within pinder-core only)
- Remove the `delivery` LLM call as a creative-generation surface: `DeliverMessageAsync` (across PinderLlmAdapter / Anthropic / OpenAi / Null adapters + IStatefulLlmAdapter / ILlmAdapter interface), `SessionDocumentBuilder.BuildDeliveryPrompt` / `BuildDeliveryPromptEx`, and the Success+Failure delivery-instruction creative surface. Recon first: confirm every caller and the interface members before deleting; remove dead trace/options config that only existed to feed the delivery LLM call.
- Option generation produces full final text — replace the `IntendedText` gist semantics so a chosen option already carries the full sendable line. (Rename/repurpose as the recon shows is cleanest; keep it behavior-correct.)
- Pick → optional **overlay** (horniness/shadow/trap-style mutation, keyed off roll outcome + DC margin) → **commit**. The overlay must be a deterministic, NON-LLM transformation of the picked line. Failure tiers still degrade/corrupt the committed line via this overlay (parity with the prior degradation behavior).
- Update `TurnOrchestrator` / `DeliveryStage` / the datee response stage: `DeliveryStage` becomes a **commit/overlay stage**, NOT an LLM stage. Only the committed line is written to history; the datee GM then responds (committed).

### Acceptance (assert in tests — xUnit)
- No LLM call labeled `delivery` in the trace; the trace shows: avatar option-gen (ephemeral) → overlay if any (ephemeral) → commit → datee response. Add/extend a trace assertion.
- Transcript contains only committed lines — assert option/steering/overlay text NEVER persists to history.
- Failure tiers still degrade the committed line via overlay — parity test on corruption (the corruption that previously came from the delivery LLM now comes from the deterministic overlay; assert the committed line is degraded for a failure outcome).
- Options carry full text; downstream consumers updated (no remaining reader expects a gist needing LLM expansion).
- `dotnet build Pinder.Core.sln` succeeds (capture output).
- `dotnet test Pinder.Core.sln` green. Baseline before this ticket = 4442 passed / 0 failed / 27 skipped. Your new tests ADD to the passing count. If ANY test fails, run that scope 3× and report deterministic-vs-flake, and whether the same failure exists on origin/main. Do NOT label a regression as a flake.

### Key files (recon map — verify before editing)
- `src/Pinder.Core/Conversation/DeliveryStage.cs`, `DeliveryContext.cs`, `TurnOrchestrator.cs` / `TurnOrchestrator.Resolve.cs` / `TurnOrchestrator.Helpers.cs`, `DateeResponseStage.cs`, `SteeringEngine.cs`, `SteeringContext.cs`.
- `src/Pinder.LlmAdapters/PinderLlmAdapter.cs` (`DeliverMessageAsync` ~line 75/84), `Anthropic/AnthropicLlmAdapter.cs`, `OpenAi/OpenAiLlmAdapter.cs`, `src/Pinder.Core/Conversation/NullLlmAdapter.cs`.
- `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` (`BuildDeliveryPrompt`), `SessionDocumentBuilder.Trace.cs` (`BuildDeliveryPromptEx`, the `{chosen_option}` / `{intended_message}` substitutions).
- `src/Pinder.Core/Interfaces/IStatefulLlmAdapter.cs` and the ILlmAdapter interface (remove the delivery member; update all implementers).
- The option value type carrying `IntendedText` (search for `IntendedText`).
- Tests: search tests/ for delivery/IntendedText/overlay/corruption coverage to update.

### Out of scope (do NOT do here)
- #1126 (slim prompt-fragment config to minimal variable set) — even though this ticket touches prompt fragments, only remove what is dead because the delivery call is gone; do not pursue the broader minimal-variable-set refactor.
- #1129 (persistence schema rename / data reset) — if you ADD or change any persisted field, follow the no-back-compat / wipe-owned-by-#1129 convention and document it in the Research Log so #1129 carries it forward.
- Unity client / pinder-web frontend / docs sweep (#1130). DO fix any pinder-core/docs reference that becomes outright false; note what you changed.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1125` on its own line, plus `## DoD Evidence` (build + full test output) and `## Research Log` (how you removed the delivery LLM call and what its callers/interface members were, how options now carry full text, the overlay design (deterministic mapping from roll outcome + DC margin → mutation), what DeliveryStage became, the clean-history enforcement, and any follow-ups filed).
- Do NOT merge. Do NOT push to main. Report the PR URL + commit SHA when done.
- Append to /tmp/work-1125/agent.log a `started` JSONL line at entry and a `completed` line with PR URL + SHA at exit (`bash /root/projects/eigentakt/scripts/log.sh` if it works, else manual JSONL).

Report back: PR URL, commit SHA, build result, full test result (counts; with rerun analysis if any failures), list of files changed with line counts, how you implemented each scope item, the overlay design, and any follow-up tickets you filed.
