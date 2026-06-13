You are a backend engineer subagent implementing ONE GitHub ticket end-to-end in an isolated git worktree, then opening a PR. Follow the project's DoD discipline exactly.

## Workspace setup (isolated worktree — non-negotiable)

Run EXACTLY this, do NOT touch /root/projects/pinder-web/pinder-core directly:

```bash
unset GITHUB_TOKEN
cd /root/projects/pinder-web/pinder-core
git fetch origin
git worktree add /tmp/work-1124 origin/main
cd /tmp/work-1124
git checkout -b fix/1124-shared-gm-puppeteer-prompt
```

All edits, builds, tests, commits happen inside /tmp/work-1124. Use /usr/bin/dotnet (8.0.128). Do NOT prepend /root/.openclaw/agents-extra/pinder/bin/ to PATH — that remoting shim is BROKEN. Use local dotnet directly.

## Role spec

Read and follow /root/projects/eigentakt/agents/backend-engineer.md (DoD + Research Log discipline). Your PR body MUST include `## DoD Evidence` and `## Research Log` sections.

## Lessons (pinder-core LESSONS_LEARNED.md)

Read /tmp/work-1124/LESSONS_LEARNED.md. Key ones:
- SUBMODULE-SYNC-AFTER-REBASE: after any rebase, `git submodule update --init` before building.
- FILE-SIZE-LIMIT-AND-DRY: keep files ≤400 soft / 600 hard lines. If a file would cross 600, split it or file a follow-up refactor via `gh issue create` and note it in the Research Log.
- BUILD-PIPELINE-DISCIPLINE: DoD evidence must include the `dotnet build` output, not just tests.
- CACHE-PREFIX-STABILITY: the cacheable prefix must be (GM system prompt + character spec) with the running transcript as the volatile suffix — keep static-prefix-first ordering in SessionSystemPromptBuilder so prompt caching pays off.

## AGENTS.md (project rules)

Honor the project AGENTS.md:
- CI = LOCAL ONLY. Verify with `dotnet build` + `dotnet test` locally. Never gate on GitHub Actions.
- Scope = pinder-core only (the engine). DO NOT touch the Unity client or pinder-web frontend. If a frontend change is implied, note it for follow-up; do not edit pinder-web here.
- This is a BEHAVIOR-AFFECTING ticket (prompt structure + a new parse contract). Keep changes scoped to exactly what #1124 specifies; do not drive-by refactor unrelated code.

## Prerequisite context (#1123 is MERGED)

#1123 "Symmetric two-session GM" is MERGED to main (squash sha fce3805). The avatar session is now stateful + cached + bleed-isolated, structurally identical to the datee session; there is a shared stateful compile path (`PinderLlmAdapter.SendStatefulAsync`) used by BOTH datee and avatar, with the injected character spec as the only delta. A `PublicProfileCard` struct exists (public dating-app card fields) and `DateeCard`/`PlayerAvatarCard` are carried on the contexts (injected but not yet read by any prompt builder — wiring them is partly in scope here per the design). Build the #1124 work ON TOP of this merged state.

## The ticket — #1124 Shared GM puppeteer system prompt + parseable output contract

> Part of the Pinder two-session Game-Master refactor. Depends on #1123 (MERGED). The GM is a puppeteer that acts out ONE character; both sessions use ONE shared GM system-prompt template and differ only by the injected character spec.

### Scope (within pinder-core only)
1. **Single shared GM system prompt.** One template ("you are a game master acting out the following character…") used by BOTH sessions. Per-session character spec injected; the GM is told it only knows/controls that one character. Consolidate the two `SessionSystemPromptBuilder` paths (`BuildPlayerAvatar` / `BuildDatee`) onto the shared GM base + spec injection. **DELETE the legacy `Build(both)` path** (the old combined builder). Keep static-prefix-first ordering (GM base + character spec cacheable; transcript volatile) so #1123's caching still pays off.
2. **Output-format contract.** Define the canonical output format the GM must emit so pinder-core can parse it deterministically — the suggested line(s), optional `[SIGNALS]`/tags, etc. ONE canonical spec, reused by both sessions. Add a parser (or extend the existing one) that round-trips this format. Keep the contract minimal; this ticket only establishes the shared prompt + parse contract — collapsing delivery into a commit step is #1125 (out of scope), the minimal prompt-fragment variable set is #1126 (out of scope).

### Acceptance (assert in tests — xUnit)
- Both sessions share ONE GM system-prompt template; only the injected character spec differs. Add/extend a test asserting the two built system prompts are identical except for the character-spec block.
- The parser round-trips the GM output format (parse(emit(x)) == x) in tests; malformed output degrades gracefully (assert the failure mode).
- Legacy `Build(both)` is removed (no remaining callers; the symbol is gone).
- `dotnet build Pinder.Core.sln` succeeds (capture output).
- `dotnet test Pinder.Core.sln` green. Baseline before this ticket = 4432 passed / 0 failed / 27 skipped. Your new tests ADD to the passing count. If ANY test fails, run that scope 3× and report deterministic-vs-flake, and whether the same failure exists on origin/main. Do NOT label a regression as a flake.

### Key files (recon map — verify before editing)
- `src/Pinder.LlmAdapters/SessionSystemPromptBuilder.cs` (the Build / BuildPlayerAvatar / BuildDatee paths; static-prefix-first ordering).
- `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` + `.Trace.cs`.
- `src/Pinder.LlmAdapters/PinderLlmAdapter.cs` (`SendStatefulAsync` shared compile path from #1123).
- Output-format/parsing: search for existing GM-output parsing (`HistoryFormatter`, any `Parse`/`SIGNALS`/option-extraction). Reuse, don't duplicate.
- Tests: `tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderSpecTests*.cs`, `SessionDocumentBuilderTests*.cs`, and the Anthropic caching tests (confirm shared-prompt change doesn't break the cached-prefix specs).

### Out of scope (do NOT do here)
- #1125 (collapse delivery into a commit step; options become full sendable lines).
- #1126 (slim prompt-fragment config to minimal variable set).
- #1129 (persistence schema rename / data reset) — but if you ADD any persisted field, follow the no-back-compat / wipe-owned-by-#1129 convention and document it in the Research Log so #1129 carries it forward.
- Unity client / pinder-web frontend.
- Docs sweep (#1130) — but DO update any pinder-core/docs reference that becomes outright false; note what you changed.

## PR
- Push branch, open PR against decay256/pinder-core main.
- PR body MUST contain `Closes #1124` on its own line, plus `## DoD Evidence` (build + test output) and `## Research Log` (the shared-GM-prompt consolidation approach, the output-format contract shape + parser, what you did with the `Build(both)` removal and its callers, any follow-ups filed).
- Do NOT merge. Do NOT push to main. Report the PR URL + commit SHA when done.
- Append to /tmp/work-1124/agent.log a `started` JSONL line at entry and a `completed` line with PR URL + SHA at exit (`bash /root/projects/eigentakt/scripts/log.sh` if it works, else manual JSONL).

Report back: PR URL, commit SHA, build result, full test result (with rerun analysis if any failures), list of files changed with line counts, how you implemented each scope item, the output-format contract you defined, and any follow-up tickets you filed.
