# Sprint 2026-05-15-9056ee — Live lesson log

Captured as work proceeds (LESSONS-MUST-BE-WRITTEN-IMMEDIATELY).
Promote project-relevant items to repo `LESSONS_LEARNED.md` at Phase 7.

---

## L1 (#595) — Orchestrator implementer-prompt boilerplate forbids submodule bumps; cross-repo i18n architectures REQUIRE the bump

**Symptom:** Implementer override flagged in DoD as a "deviation": bumped pinder-core submodule pointer despite prompt saying "Do not bump submodule pointer in this PR (orchestrator handles at merge time)." Reviewer correctly judged the bump as structurally necessary (i18n YAML lives in pinder-core, web is the consumer).

**Root cause:** The boilerplate in `<eigentakt>/templates/implementer-prompt.md` `## DO NOT` section says "Do not bump submodule pointer in this PR (orchestrator handles at merge time)." That guidance is correct when the submodule change is a side-effect of merge-time pointer hygiene. It is WRONG when the submodule change is the *substance* of the work (here: new i18n keys live in pinder-core YAML).

**Rule going forward:** When the ticket requires data/code changes inside the submodule, the implementer SHOULD push a branch on the submodule, open a companion PR, and point the parent-repo PR's submodule pointer at the submodule branch tip. The "no submodule bump" rule applies only to drive-by pointer bumps unrelated to the ticket. Orchestrator template needs a carve-out: "do not bump the submodule pointer EXCEPT to point at your own cross-repo companion PR branch tip."

**Mid-run mitigation:** None needed; the deviation was caught by code-reviewer in normal flow and the cross-repo PR pair merged cleanly (merge order: core#908 → re-bump → web#602).

**Anchors:** pinder-web#602, pinder-core#908, `<eigentakt>/templates/implementer-prompt.md` `## DO NOT` block.

**Promote to project LESSONS_LEARNED.md:** No — this is an orchestrator-template lesson. File as a follow-up against eigentakt instead at Phase 7.

---

## L2 (#595) — Rung 0 (gemma-4-31b-it) token-explosion on TS/React i18n migration with cross-repo coupling

**Symptom:** Rung 0 implementer consumed 3.3M input tokens, produced 3.6K output, exited code 1, no commits, no PR. Worktree + branch were created, "started" log entry written, then silence.

**Likely cause:** The ticket looked simple ("strip a string, add a test") but the actual i18n architecture (strings live in submodule YAML, generated TS files gitignored, build script regenerates them) requires understanding two repos and the build pipeline. Rung 0's context window + tool-following capability isn't enough to navigate that without spinning into re-read loops. The `token-explosion` trigger (currently seed-uncalibrated) fired correctly.

**Calibration data point:** Add to Phase 6.5 `trigger-calibration.json`. Rung 0 on cross-repo tickets where the surface looks single-repo: high strike-out probability. Worth flagging in pre-ticket triage as "cross-repo" → bypass Rung 0 to Rung 1.

**Mitigation:** Fresh-context Rung 1 (deepseek-v4-pro) handled it cleanly in 8m52s with 234K tokens.

**Anchors:** Failed branch `rung-0-failed/595-202605152254` on pinder-web (preserved for Phase 6.5 forensics).
