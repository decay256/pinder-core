You are a technical writer subagent completing ONE docs ticket (#1130) end-to-end in an isolated git worktree, then opening a docs PR. This is DOCS-ONLY and is the FINAL docs sweep of the two-session-GM sprint.

## Workspace setup (isolated worktree)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1130 origin/main
cd /tmp/work-1130
git submodule update --init 2>/dev/null || true
git checkout -b docs/1130-prompt-graph-architecture-sweep
```

All edits happen inside /tmp/work-1130. This is a PURE-DOCS ticket — do NOT touch any `.cs` or `.yaml` file. (Markdown files including the project lessons file are in scope.)

## Role spec

Read and follow /root/projects/eigentakt/agents/technical-writer.md. Your PR body MUST include a DoD section confirming docs-only diff and resolved cross-links.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1130/LESSONS_LEARNED.md. Key ones:
- DOCS-FOLLOW-CODE: every cadence/phase/event-name/method-name in the doc MUST exist in `src/` at the merge HEAD, else the doc is fiction. Verify before writing: grep `src/` for `ResolveTurnAsync`, the commit step, the GM output contract types, and confirm `DeliverMessageAsync`/`BuildDeliveryPrompt` are NOT live creative calls anymore (delivery was collapsed in #1125, LlmPhase.Delivery retired in #1129).
- Append new LESSONS_LEARNED entries at the BOTTOM; do NOT edit landed numbered lessons.
- HORNINESS-OVERLAY-MUST-BE-LAST-TEXT-LAYER (#899 invariant): the ARCHITECTURE.md pipeline must preserve the horniness-last / shadow-before-horniness ordering documentation after the #1125 delivery-collapse re-sequence. Do NOT silently drop it.

## AGENTS.md (project rules)

Honor the project AGENTS.md: docs-only, pinder-core only. No Unity, no pinder-web edits. No build/test gate for pure-docs, but DoD MUST confirm docs-only diff (no `.cs`/`.yaml`) and that all relative links resolve (no dead links — and prompt-graph.md MUST exist after this PR so #1128's forward-link resolves).

## Context — merged sprint state
#1121 (OPPONENT→DATEE), #1122 (PLAYER→PLAYER AVATAR), #1123 (symmetric two-session GM: stateful+cached+bleed-isolated avatar session), #1124 (shared GM puppeteer prompt + GmOutputContract/GmTurnOutput parse contract), #1125 (delivery collapsed into a deterministic non-LLM commit/overlay step — NO delivery LLM call), #1126 (slimmed prompt-fragment config), #1129 (LlmPhase.Delivery retired-but-retained + schema rename), #1127 (apiVersion contract), #1133 (yaml key player_avatar_role_description), #1128 (unity-integration.md version-bumped to v1) are ALL MERGED. This sweep documents the post-#1125 reality.

## Scope — #1130 Docs sweep (DOCS-ONLY, pinder-core) — REWRITE in place, CREATE prompt-graph

1. **CREATE `docs/prompt-graph.md`** (does NOT exist — this ticket OWNS it; #1128 forward-links to exactly this path). Canonical per-turn prompt graph for the new model: avatar session, datee session, ephemeral branches, the commit step, **no delivery LLM call**, bleed isolation between the two sessions. First run `find docs -iname '*prompt*graph*'` to confirm none exists under another name; if one does, rewrite that instead and note the path — otherwise create `docs/prompt-graph.md`.
2. **`docs/ARCHITECTURE.md`** (exists, ~32 KB) — rewrite the affected pipeline sections in place to the two-session/commit model. **PRESERVE the horniness-last / shadow-before-horniness ordering invariant** (HORNINESS-OVERLAY-MUST-BE-LAST-TEXT-LAYER / #899). Keep the step-numbered pipeline accurate after the #1125 delivery-collapse re-sequence; do NOT drop the horniness-last documentation.
3. **`docs/modules/game-session.md`** (exists) — rewrite to reflect `ResolveTurnAsync` re-wiring (commit step, ephemeral pruning). Verify `ResolveTurnAsync` exists in `src/` and describe it accurately.
4. **`docs/modules/llm-adapters.md`** (exists) — rewrite to drop the delivery creative-call surface and reflect the shared GM puppeteer call set. No stale references to `DeliverMessageAsync`/`BuildDeliveryPrompt` as a LIVE creative call.
5. **`LESSONS_LEARNED.md`** (exists) — APPEND (at bottom) a project lesson capturing the two-session/commit-model invariants: the clean-history rule (only COMMITTED lines persist; ephemeral option/steering/overlay text is pruned) and two-session bleed isolation. Do not edit landed numbered lessons.

## Acceptance
- `docs/prompt-graph.md` exists and reflects: avatar session, datee session, ephemeral branches, commit step, NO delivery LLM call, bleed isolation.
- `docs/ARCHITECTURE.md` pipeline section matches the post-#1125 turn flow AND preserves the horniness-last / shadow ordering invariant.
- `docs/modules/game-session.md` + `docs/modules/llm-adapters.md` updated in place; no stale references to `DeliverMessageAsync`/`BuildDeliveryPrompt` as a live creative call.
- `LESSONS_LEARNED.md` gains the clean-history / two-session-bleed-isolation lesson at the bottom.
- All cross-links resolve: `unity-integration.md` ↔ `prompt-graph.md` ↔ `ARCHITECTURE.md`. After this PR, the forward-link #1128 added to `docs/prompt-graph.md` MUST resolve.
- DoD confirms docs-only diff (no `.cs`/`.yaml`) and no dead relative links.

Optional cheap sweep-ins (only if trivial): #1122 cosmetic test-method-name note and #1124's 2 cosmetic parser notes — skip if not cheap; they are not required.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1130` on its own line, plus a DoD section (docs-only diff confirmation + cross-link check, explicitly noting prompt-graph.md now resolves #1128's link) and a short note of what you rewrote/created.
- Do NOT merge. Report the PR URL + commit SHA + list of doc files touched/created.
- Append to /tmp/work-1130/agent.log a `started` and `completed` (with PR URL + SHA) JSONL line.

Report back: PR URL, commit SHA, list of doc files touched/created (confirm no .cs/.yaml), confirmation prompt-graph.md was created at docs/prompt-graph.md, that the horniness-last invariant is preserved in ARCHITECTURE.md, that no live DeliverMessageAsync/BuildDeliveryPrompt references remain, and the cross-link check result.
