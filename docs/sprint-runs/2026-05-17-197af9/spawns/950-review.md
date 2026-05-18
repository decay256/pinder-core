You are a code reviewer subagent in the Pinder dev swarm. Review pinder-core PR **#961** (fix for #950 — psychological stake not surfacing).

## Workspace isolation
```bash
rm -rf /tmp/review-950
git clone --branch fix/950-stake-not-surfacing-r2 \
  https://github.com/decay256/pinder-core /tmp/review-950
cd /tmp/review-950
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh pr view 961 --repo decay256/pinder-core --json title,body,additions,deletions,files`.
5. `gh issue view 950 --repo decay256/pinder-core --json number,title,body,comments`.

## What you're reviewing

PR #961 is a P1 prompt-engineering + telemetry fix (+307/-4 across 8 files):

1. `src/Pinder.SessionSetup/LlmStakeGenerator.cs` + `data/prompts/stake.yaml` — system prompt now mandates name+year+event in every stake fragment.
2. `data/prompts/templates.yaml` (engine-options-block) — `OPTION_C MUST quote/paraphrase one numbered stake line`.
3. `src/Pinder.Core/Conversation/DialogueContext.cs` — adds `StakeLines` + `StakeLinesReferenced`.
4. `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` — builds "STAKE COVERAGE — N referenced, M untouched" block per turn.
5. `src/Pinder.LlmAdapters/PinderLlmAdapter.cs` + `PinderLlmAdapterOptions.cs` — `WarnIfStakeSkipped` fires `OnStakeSkipWarning` callback + `Trace.TraceWarning` when 0/N options reference any stake token.
6. `tests/Pinder.LlmAdapters.Tests/Issue950_StakeSurfacingTests.cs` — 5 integration tests.

## Heuristic checklist

### 1. AC coverage
- [ ] **Path A** — stake-generator prompt change is in the right file and the new constraint is hard-mandate language (e.g. "EVERY fragment MUST", not "ideally"). Check both the C# string AND the YAML if both exist; they should match.
- [ ] **Path B telemetry** — `WarnIfStakeSkipped` is called from the right place (after option parse, before return). The trigger condition is `stake_hits == 0 && stake_fragments > 0`. Verify the fragment definition is sensible (the brief suggested splitting on `,.\n\r`, lowercased, ≥ 8 chars; the implementer may have picked something different — confirm it's defensible).
- [ ] **OPTION_C mandate** — the templates.yaml change is in the engine-options-block (the user-message template for the option generator). Verify it's not in some unrelated block.
- [ ] **STAKE COVERAGE injection** — the per-turn block is rendered into the prompt sent to the LLM. Check `SessionDocumentBuilder` to see what conditions trigger it.

### 2. Correctness
- [ ] The fragment-matching logic doesn't false-positive on common words. If the stake is "her name was Margot", fragment "name was" (8 chars) is too generic and would match many options that have nothing to do with the stake. Check what the implementer chose as the minimum-fragment length and whether the split filters out stop-word fragments.
- [ ] `Trace.TraceWarning` + `OnStakeSkipWarning` callback are wired in a way that doesn't break tests (i.e., the callback is optional, defaults to null, doesn't throw on null).
- [ ] `DialogueContext` field additions (`StakeLines`, `StakeLinesReferenced`) don't break existing constructors or callers. If `StakeLines` is a new required arg, every caller needs updating; if optional, default needs documenting.
- [ ] Per-turn STAKE COVERAGE block is built from `StakeLinesReferenced` correctly — verify the math (referenced = previously-used lines, untouched = currently-unused lines).

### 3. Don't-break checks
- [ ] No regression in `SessionDocumentBuilder` for sessions WITHOUT a stake (e.g., demo/anonymous flows). The new block should be conditional on `StakeLines != null && StakeLines.Count > 0`.
- [ ] `templates.yaml` change doesn't break a yaml parser anywhere — check for trailing whitespace or YAML-significant characters in the new instruction.

### 4. Build + tests
```bash
cd /tmp/review-950
dotnet build -c Release 2>&1 | tail -10
dotnet test -c Release tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-build --filter "FullyQualifiedName~Issue950" 2>&1 | tail -10
```
- [ ] Build: 0 errors.
- [ ] 5 Issue950 tests pass.
- [ ] No new test failures in the broader Pinder.LlmAdapters.Tests suite or Pinder.Core.Tests (run targeted greps if full-suite run is too slow).

### 5. PR hygiene
- [ ] `Closes #950` in PR body.
- [ ] Commit message describes both fix paths (A + B).

## Verdict

`APPROVE` if all checks pass. `CHANGES_REQUESTED` with specific file:line blockers otherwise.

```bash
gh pr review 961 --repo decay256/pinder-core --approve -b "<body>"
# OR
gh pr review 961 --repo decay256/pinder-core --request-changes -b "<body>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
verdict: APPROVE   (or CHANGES_REQUESTED — N blockers)
```

## Reminders

Correlation id: `2026-05-17-197af9-950-code-reviewer-<your-id>`.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the verdict, no markdown tables, max 5 bullets per section.
