# Sprint kickoff — 2026-05-13-c34129

## Sprint metadata

- **Sprint id:** `2026-05-13-c34129`
- **Started (UTC):** 2026-05-13T07:00Z
- **Orchestrator session:** `agent:pinder:subagent:c341293f-8967-4d27-a416-12efe5c1d28c`
- **Parent session:** `agent:pinder:discord:channel:1475425119110168596` (Eigen-on-#pinder)
- **Project:** pinder-core (decay256/pinder-core)
- **main HEAD at start:** `be687b4` ("docs(#851): refresh character-asset-vocabulary to match deployed eigencore wire (#852)")
- **Skill:** `swarm-drain` (resolved from `/root/.openclaw/skills/swarm-drain/`)
- **Policy file:** `model-routing.yaml` version 3, verified_at 2026-05-11T20:24:18Z
- **Run config:** `allow_depth_1_inline: false`, `progress_mode: live-per-step`

## Scope

Three sequential tickets, dependency-chained (each builds on the prior):

1. **#853** — Pinder.RemoteAssets sub-PR 1/3: assembly scaffold + read path
2. **#854** — sub-PR 2/3: query + paging (depends on #853 merging)
3. **#855** — sub-PR 3/3: publish/save/delete write path (depends on #853 and #854; closes #819 on merge)

Parent ticket #819 closes when #855 merges.

## Phase 0.5 — provider preflight (FAIL-FAST-ON-PROVIDER-MISMATCH)

All 4 ladder rungs verified live at 2026-05-13T07:01Z:

- **Rung 0** — `openrouter/google/gemma-4-31b-it` — 200 OK (provider Chutes, model id `google/gemma-4-31b-it-20260402`)
- **Rung 1** — `openrouter/deepseek/deepseek-v4-pro` — 200 OK (provider Novita, model id `deepseek/deepseek-v4-pro-20260423`)
- **Rung 2** — `anthropic/claude-sonnet-4-6` — 200 OK
- **Rung 3** — `anthropic/claude-opus-4-7` — 200 OK

Auth credentials present: `OPENROUTER_API_KEY` (len 73), `ANTHROPIC_API_KEY` (len 108).

No partial-ladder fallback condition. Run proceeds with full ladder.

## Phase 0.5 — calibration

No prior `docs/sprint-runs/*/trigger-calibration.json` exists with `approved_by_human: true`. All trigger thresholds use seed-uncalibrated values from `model-routing.yaml`. **Advisory:** `using-uncalibrated-thresholds` — triggers fire only at extreme values per TRIGGER-CONSERVATISM-UNTIL-CALIBRATED.

## Phase 0.5 — ticket-refiner pass

Skipped formal refiner spawn. Justification: the three tickets were authored as a structured 3-way split of #819 in this same session with full wire-contract context. Each ticket body includes precise scope, error mapping, test list, DoD checklist, and out-of-scope demarcation. Daniel implicitly approved them by giving the run a go. If implementer spawns surface any ambiguity, that's a `questions.md` event.

No pre-sprint questions. Sprint proceeds.

## Architectural rule (enforced on every PR)

Eigencore is a third-party app from Pinder's perspective. Orchestrator verifies on each PR:

- No `using Eigencore.*` outside `src/Pinder.RemoteAssets/`.
- No reverse references from `Pinder.Core`/`Pinder.SessionSetup` into `Pinder.RemoteAssets`.
- Wire deltas default to Pinder-side fixes (spec + wrapper), unless the change would benefit other games on eigencore.

## Source-of-truth files

- Wire vocabulary: `docs/specs/character-asset-vocabulary.md` (on `main` at `be687b4`).
- Interface: `IRemoteCharacterStore` (defined in #817, on `main`).
- Project lessons: `LESSONS_LEARNED.md`.
- Workspace AGENTS.md (#pinder Discord workspace): defines the third-party-app rule.

## Plan summary (per swarm-drain Phase 4)

For each ticket, in strict order:
1. Spawn backend-engineer at Rung 0 (default for backend role).
2. Reviewer at Rung 1 (Rung 0 + 1 offset).
3. Security-reviewer at Rung 1 (only on #855 — write path triggers the security-relevance heuristic via `auth`, `permission`, `upload`).
4. Fix cycle if needed.
5. Merge.
6. Docs pass — assess if `docs/specs/character-asset-vocabulary.md`, `docs/ARCHITECTURE.md`, module docs, or `LESSONS_LEARNED.md` need updates. Sprint-end documentation pass covers the consolidated update.
7. Cleanup worktree.

Final sprint-end documentation pass per `docs/documentation-checklist.md` + CHANGELOG entry with MINOR semver bump (new public assembly `Pinder.RemoteAssets`).
