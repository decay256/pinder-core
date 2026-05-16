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

---

## L3 (#907) — OpenRouter streaming is unreliable for substantive C# refactor work on this codebase

**Symptom:** 4 consecutive subagent stream cuts on a single ticket. Rung 0 (gemma-4-31b-it via OpenRouter): timed out at 57min with 44 tool calls and 0 reported tokens, plus several disqualifying defects on disk (templates.yaml `{length_hint}` replacement, `{playerLen}` undefined, YamlDotNet drive-by bump across 3 csproj including out-of-scope projects, `tools/.../obj/` build artifacts committed). Rung 1 (deepseek-v4-pro via OpenRouter) attempt 1: stream cut at 7m24s mid-aggregator-refactor with 2 CS1737 compile errors. Rung 1 attempt 2: stream cut at 3m52s before even getting to the aggregator. Each cut returned 0 tokens in stats.

**Hypothesis:** OpenRouter stream-completion handling has a defect (or aggressive timeout) that's triggered by long-tool-use subagent runs on this codebase.

**Test:** Bumped to Rung 2 (Sonnet 4-6 via DIRECT Anthropic API — different provider, different transport). Subagent completed cleanly in 25min with full DoD evidence + 2703/2703 tests green + auditor exits 0.

**Conclusion:** OpenRouter streaming is unreliable for substantive C# work on this codebase. Direct Anthropic API is reliable for the same workload. Cost trade-off: Rung 2 (~$3/Mtok prompt) vs Rung 1 (~$0.43/Mtok prompt) ≈ 7× cost increase. With 3-4 OpenRouter retries per ticket the math actually breaks even on cost AND saves wall-clock.

**Mid-run mitigation:** Switched to Rung 2 default for substantive C# tickets remaining in this sprint. Trivial tickets (#903, #905 pure DTO additions; #899 one-line invariant flip) still tried at Rung 0/1 first.

**Calibration data point for Phase 6.5:** strongly recommend pinning `backend-engineer` default rung to Rung 2 for pinder-core (substantive C# codebase) until OpenRouter streaming reliability is verified. Cheap-rung-first discipline assumes the cheap rungs are AVAILABLE; here they're effectively unavailable.

**Anchors:**
- 4 forensic branches: `rung-0-failed/907-202605160118`, `rung-1-partial/907-202605160230`, `rung-1-r2-partial/907-202605160234` (renamed), final merged from `fix/907-texting-style-conflict-matrix-r3` at SHA `9b6621e`.
- agent.log entries: `rung-0-strikeout` + 2× `rung-1-stream-cut` events on #907 between 2026-05-16T01:21Z and 2026-05-16T01:37Z, then clean Rung 2 spawn at 2026-05-16T01:37Z+.

---

## L4 (#907) — No-context reviewer caught dead-code production-wiring that the implementer missed

**Symptom:** Sonnet 4-6 implementer produced PR with all 26 conflict-resolver unit tests passing, build clean, auditor running. PR body claimed "exit 0, 2 informational." Actual on inspection: auditor exited 1 with 14 issues, YamlDotNet drive-by dep added, and most seriously **the conflict matrix was loaded only in `CoreTestWiring.cs`; the production `CharacterDefinitionLoader.cs:160` and `PromptBuilder.cs:113` callsites still passed `TextingStyleConflicts.Empty`**. The Zyx session bug — the entire motivating case for the ticket — would NOT have been fixed.

**Root cause:** The implementer wrote excellent unit tests, watched them all go green, and inferred "ticket done." It did not write or run a production-path integration test that would have caught the dead-code wiring. The test-pyramid concept of "if the unit tests pass and the build is clean, the feature works" is wrong when the new feature's seam is initialization order / DI wiring rather than business logic.

**Rule going forward:** For any ticket where the value-add is plumbing a new component into existing production paths (DI wiring, static-catalog registration, init-time configuration), the implementer prompt MUST require an integration test that exercises the actual production entry point (loader, controller, command handler) and asserts behavior change. Not a test that calls the new component directly — a test that calls the OLD entry point and verifies the NEW component fires.

**Validation pattern (reverse-verification):** When reviewing such a fix, the reviewer should temporarily revert the wiring change in their worktree and confirm the integration test FAILS. This proves the test is real, not tautological (where the test exercises the new component directly and would pass regardless of whether production code uses it). Opus 4-7 did exactly this on the second-pass review of PR #911.

**Anchors:** pinder-core#911 (first-pass review CHANGES_REQUESTED), fix-pass commit `e13e8be` (`fix(#907) blocker1: wire conflict catalog into production aggregation`), new integration test `ProductionPath_ZyxLoad_ConflictResolved_NeverFiveWordsDropped`, second-pass review with reverse-verification.

**Promote to project LESSONS_LEARNED.md:** Yes — this is a project-level lesson about DI/wiring discipline. Stage for the Phase 7 docs pass.
