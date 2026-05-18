You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#950** in pinder-core: psychological stake never surfaces in chat — zero references during a 3-turn test despite explicit prompt instructions.

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-950-r2 origin/main
cd /tmp/work-950-r2
git checkout -b fix/950-stake-not-surfacing-r2
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 950 --repo decay256/pinder-core --json number,title,body,comments`.

## Diagnosis

The ticket gives two plausible root causes:

**(A) Stake content is too generic** — `LlmStakeGenerator` produces lines that lack concrete biographical anchors. The per-turn option generator has nothing concrete to surface.

**(B) Stake injection order issue** — `Player.AppendToSystemPrompt` puts the stake somewhere the option generator's `BIOGRAPHICAL SPECIFICITY` rule doesn't link back to it. Even with good stake content, downstream prompt doesn't reference it.

**The ticket asks for BOTH options A and B per resolved scope** (see prior orchestrator's continuation-context-2 — "do A+B both per resolved scope on ticket comment"). Implement both:

1. **Strengthen `LlmStakeGenerator` prompt** to insist on concrete biographical anchors (named relationships, specific events, specific years).
2. **Add a per-turn debug log** when the option-generator response omits all stake content for N consecutive turns. This won't fix the underlying issue but gives the team a signal.

If your investigation reveals B is actually a structural prompt-positioning problem, fix that too — but don't refactor the whole prompt assembly system in this PR.

## Investigation steps

```bash
cd /tmp/work-950-r2
# Read the stake generator
cat src/Pinder.SessionSetup/LlmStakeGenerator.cs | head -120
# Read Player.AppendToSystemPrompt
grep -n "AppendToSystemPrompt\|PSYCHOLOGICAL STAKE\|psychological_stake" src/Pinder.Core/Characters/Player.cs src/Pinder.LlmAdapters/ data/prompts/ 2>&1 | head -30
# Read the BIOGRAPHICAL SPECIFICITY rule
grep -n -A5 "BIOGRAPHICAL SPECIFICITY\|engine-options-block" data/prompts/templates.yaml 2>&1 | head -40
# Read the option-generation user message construction
grep -n "BuildOptionsBeatPrompt\|BuildOpenAi\|engine-options\|GetOptions\|GenerateOptions" src/Pinder.LlmAdapters/ 2>&1 | head -20
```

Find:
- What does the current `LlmStakeGenerator` system prompt say about specificity?
- Does `templates.yaml` instruction force the option generator to USE the stake, or does it just suggest?
- Is the stake placed in the same context block as the BIOGRAPHICAL SPECIFICITY rule, or far away?

## Goal

After the fix:
- Generated stakes contain concrete biographical anchors (when the LLM honors the prompt; this is a stochastic improvement, not a guarantee).
- A debug log surfaces when N consecutive turns produce options with zero stake reference, so the team can detect regressions without manual session inspection.
- An integration test pins the behaviour: a 3-turn fixture with a fake stake containing concrete fragments (e.g. "her name was Margot, 2019") produces options where at least one option per session contains "Margot" OR "2019".

## Implementation

### Path A — LlmStakeGenerator prompt strengthening

File: `src/Pinder.SessionSetup/LlmStakeGenerator.cs`

Find the system-prompt string (likely a `const string` field or a `PromptTemplates.StakeSystem` reference). Add explicit instructions that the stake MUST contain:

- At least one named relationship (e.g. "her name was Margot", not "an ex-partner").
- At least one specific year or date (e.g. "2019", "the summer before college", not "a few years ago").
- At least one named place or concrete event ("the kitchen at the lake house", "the night they fired me", not "a difficult time").

Phrase it as a hard constraint: "EVERY generated stake fragment must contain at least one of: a proper name, a specific year, a concrete event reference. Avoid abstract themes ('fear of abandonment'); use concrete biography ('the night Margot left, 2019').".

### Path B — Stake debug log

File: probably `src/Pinder.LlmAdapters/PinderLlmAdapter.cs` or wherever the option generator's response is parsed. After parsing the options, count how many options contain a substring from the active stake (stake fragments split on commas / periods / newlines, lowercased, ≥ 8 chars). If 0/N options contain any stake fragment, log a warning:

```
LogWarning("option_generator_skipped_stake session={SessionId} turn={Turn} stake_fragments={Count} stake_hits=0")
```

Keep this lightweight — no per-fragment iteration in hot paths, just a single Contains check loop over fragments.

### Integration test

File: `src/Pinder.Core.Tests/Conversation/StakeIntegrationTests.cs` (new) or extend an existing integration test in `src/Pinder.LlmAdapters.Tests/`.

The test should:
- Build a `Player` with a stake containing "her name was Margot. They broke up in 2019."
- Drive a 3-turn conversation against a fake LLM that returns options containing the stake fragments.
- Assert at least one option across the session references "Margot" or "2019".

This test guards the **prompt path**, not the live LLM output (which is stochastic). Use a fake adapter that captures the prompt string and asserts the stake is present in the prompt sent to the LLM.

## Build + test

```bash
dotnet build -c Release 2>&1 | tail -10
# expect: 0 errors
dotnet test -c Release src/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-build 2>&1 | tail -10
dotnet test -c Release src/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-build 2>&1 | tail -10
# expect: green (or no regressions vs main baseline)
```

## Commit + push + PR

```bash
git add -A
git commit -m "fix(#950): force concrete biographical anchors in stake + warn when option generator skips stake

Closes #950.

- LlmStakeGenerator system prompt now mandates: named relationship + specific year + concrete event in every stake fragment.
- Option-generator response handler logs a warning when 0/N options reference the active stake.
- New integration test pins the prompt path: fake stake with 'Margot' + '2019' surfaces in offered options.

DoD: build clean, all targeted tests pass."
git push -u origin fix/950-stake-not-surfacing-r2
gh pr create --repo decay256/pinder-core --base main --head fix/950-stake-not-surfacing-r2 \
  --title "fix(#950): concrete-anchor stakes + zero-stake-reference warning" \
  --body "Closes #950.

## Root cause
<one-line summary>

## Fix
<bullets — both paths>

## Test
<integration test name>

## DoD
- Build: clean
- Tests: <pass/fail counts>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
result: pr-opened  pr=<N>  sha=<commit-sha>  build=clean  tests=<N>/<N>
```

## Reminders

Correlation id: `2026-05-17-197af9-950-backend-engineer-<your-id>`.
Sprint id: `2026-05-17-197af9`.
Worktree: `/tmp/work-950-r2`.

Per WORKSPACE-ISOLATION: only operate inside `/tmp/work-950-r2/`. Never edit `/root/projects/pinder-core/` directly.

Per NO-FALSE-DOD-CLAIMS: run the build yourself and inspect the tail before claiming DoD. If anything is red, your final message MUST say so.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the result, no markdown tables.
