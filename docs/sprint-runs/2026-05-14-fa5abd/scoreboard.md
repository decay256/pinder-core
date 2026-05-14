# Sprint 2026-05-14-fa5abd — Final Scoreboard

**Authorization:** Daniel — "Drain on all open issues, full throttle!" (2026-05-14)
**Mode:** sequential eigentakt drain, single subagent at a time, Rung 0 default with escalation
**Outcome:** **16/16 implementable tickets merged**, 7 follow-ups filed during the run

## Tickets shipped

### Pinder.RemoteAssets security (2)
- **#859** — HTTPS scheme enforcement on `Configuration.BaseUrl` → PR #876 → `760c900`
- **#860** — `HttpClient.MaxResponseContentBufferSize` cap → PR #878 → `7b3c7b2`

### Yaml-migration epic #871 (4 + 1 cross-repo)
- **#872** — Phase 2: PromptTemplates.cs → templates.yaml (37 entries) → PR #879 → `7be6750`
- **#874** — Phase 3: PromptBuilder → structural.yaml (7 entries, cross-assembly delegate) → PR #881 → `baaa6e9`
- **#873** — Phase 4: ArchetypeCatalog → archetypes.yaml (20 archetypes) → PR #882 → `aebff9a`
- **#875** — Phase 5: const fallbacks deleted + PromptWiring.Wire() in prod → PR #887 → `ccdb79d`
- **pinder-web#590** — companion: submodule bump + GameApi Program.cs wiring + fail-fast guard → `0ffa537`

### Prompt-tuning / quality (9)
- **#868** — 15-stem stake prompt (locked in #826 comment) → PR #889 → `3cc8195`
- **#862** — meta-prefix strip in option intended_text → PR #890 → `38f2110`
- **#863** — HARD RULE preserve paragraph count → PR #891 → `dd1102c`
- **#864** — horniness Catastrophe word-soup guard → PR #892 → `984a5d8`
- **#865** — shadow Catastrophe length cap (audit pass, 6 stats) → PR #893 → `fabb5f8`
- **#866** — opponent response length cap (relative window + 600-char ceiling) → PR #894 → `49931d2`
- **#867** — delivery prompt token audit (OpponentFriction/Curiosity stripped from BuildPlayer) → PR #895 → `a7df49e`
- **#869** — opponent texting-style parity (WORD & PATTERN REPETITION + self-check) → PR #896 → `a7abc0a`
- **#870** — opponent voice-isolation CONTEXT BOUNDARY guard → PR #897 → `f6640a3`

### Infrastructure (pinder-web)
- **#583** — GameApi stale yaml fix (Dockerfile copies pinder-core/data) → PR #584 → `25a1054`
- **pinder-web docs #586** — docs/ARCHITECTURE.md + deployment-and-staging.md + documentation-checklist.md → `03dc709`

### Sprint-end docs pass
- **pinder-core#898** — ARCHITECTURE.md prompt catalog section + CHANGELOG.md (v0.9.0) + documentation-checklist.md → `5da9d66`

## Follow-ups filed during the drain

- **pinder-web#585** — workflow-scope PAT needed for data-drift CI gate
- **pinder-web#588** — admin frontend yaml editor for prompt yamls
- **pinder-core#877** — xmldoc for allowInsecureBaseUrl ctor param
- **pinder-core#880** — 63 pre-existing test failures on main in Pinder.LlmAdapters.Tests
- **pinder-core#883** — delete dead-code ArchetypeYamlLoader.LoadFromYaml
- **pinder-core#884** — Issue527 test flake (assembly-load interaction)
- **pinder-core#886** — workflow-scope PAT needed for prompt-content grep gate

## Rung escalation summary

- **Rung 0 first attempts:** 11 (583, 859, 860, 862, 863, 864, 865, 868, 869, 870, 872, 874)
- **Escalated to Rung 1:** 4 (#860 wall-clock-overrun + 1 test math wrong; #872 broke 515 tests with wrong pattern; #874 tool-validation error mid-edit; pre-emptive for #875/#873/#866/#867 per the 0/3 success-rate observation on migration tickets)
- **OpenRouter rate-limited mid-run:** 2 (#864, #869, #870 implementer also dying; #869's death partial)
- **Runtime hiccups (subagent died mid-thought, no progress):** 3 (#875 twice, #867 twice)
- **Orchestrator manual finishes:** 4 (#875, #864, #865, #869, #867, #870 partial cleanups when subagents died after edits but before commit/push/PR)

## Review pass summary

- **First-pass APPROVE:** 3 (#583 second-pass after fix; #859, #863, #864, #865, #868, #874, #866, #869, #870 first-pass)
- **CHANGES_REQUESTED (orchestrator fix-pass needed):** #583 (Dockerfile order + dead CI gate), #860 (bare catch), #862 (scope-creep TestCatalogSetup.cs), #867 (test/code contradiction TWICE, edit-tool collision), #870 (LESSONS_LEARNED nuked + hardcoded path), #873 (test-pollution on static), #875 (GameApi wiring missing)
- **Security-review passes:** #583, #859 — both APPROVE
- **Three-pass reviews:** #867 (twice), #875 (cross-repo wiring needed)

## Lessons added to LESSONS_LEARNED.md during sprint

- **§36 CONTENT-FILES-MUST-FOLLOW-CODE-DEPLOYMENT-PIPELINE** (#583, pinder-web)
- **PROMPT-BLOAT-FROM-CROSS-ROLE-SECTIONS** (#867, pinder-core) — role-affiliation rule for GameDefinition sections
- **PROMPT-ENFORCEMENT-PARITY** (#869, pinder-core) — symmetry rule for prompt-discipline rules
- **OPPONENT-LENGTH-RECIPROCITY** (#866, pinder-core) — reciprocal length budget
- **PLAYER-PROFILE-IS-AUTHORIAL-CONTEXT** (#870, pinder-core) — CONTEXT BOUNDARY for cross-character prompt data

## Architectural changes

- `Pinder.SessionSetup.PromptWiring` — new public class, single source of truth for prompt-catalog startup wiring.
- `data/prompts/` — new content directory with 4 yamls (stake, templates, structural, archetypes) totaling ~64 prompt entries.
- `Pinder.Core.Prompts.PromptBuilder.StructuralFragmentLookup` — new public delegate (cross-assembly boundary).
- `Pinder.Core.Characters.ArchetypeCatalog.BehaviorResolver` — new public delegate (cross-assembly boundary).
- `Pinder.LlmAdapters.PromptTemplates.Catalog` — new public property (same-assembly DI).
- `Pinder.GameApi/Program.cs` — fail-fast guard if `data/prompts/` not in build artifact.

## Orchestrator anti-stall notes

Following the pre-sprint commit "Drain on all open issues, full throttle!" the orchestrator:
- Acted on documented defaults instead of asking, per the 2026-05-14 anti-stall rules.
- Self-merged 2 docs PRs (pinder-web#586, pinder-core#898) per rule #4 (reversible, log decision).
- Filed all 7 follow-up issues during the run rather than pausing.
- Made one mistake worth flagging: committed the submodule bump for #583 fix WITHOUT the actual Program.cs change. Reviewer caught it. Lesson reinforced: always `git status` before commit when work spans multiple files.
- Made a separate mistake on #867: edited the wrong copy of a duplicated code pattern twice. Lesson: when pattern appears in `Build()` and `BuildPlayer()`, use unique context spanning more lines.

