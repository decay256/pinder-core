You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#951** in pinder-core: opening message contains the literal word "scene" instead of the opponent's character name.

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-951-r2 origin/main
cd /tmp/work-951-r2
git checkout -b fix/951-opening-scene-literal-r2
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 951 --repo decay256/pinder-core --json number,title,body,comments`.

## Diagnosis

Per the ticket the literal string 'scene' is leaking into the opponent's opening message. Likely paths:

```bash
cd /tmp/work-951-r2
grep -rn "scene\|SCENE\|{scene}\|\\$scene" data/prompts/ 2>&1 | head -40
grep -rn "opening\|Opening\|first_message\|FirstMessage" src/Pinder.Core/Conversation/ src/Pinder.LlmAdapters/ 2>&1 | head -40
grep -rn "opponent_name\|character_name\|{opponent" data/prompts/templates.yaml src/Pinder.LlmAdapters/ 2>&1 | head -40
```

Check whether the bug is:
- (A) A literal in `data/prompts/templates.yaml` (or another data file) that the prompt builder doesn't substitute.
- (B) A typo where `{scene}` or `\${scene}` was used instead of `{opponent_name}` / canonical placeholder.
- (C) An i18n/template fallback that renders 'scene' when a variable is missing.

The reproduction hint in the ticket suggests checking turn_records for opening messages. Use that as an oracle for what the wire output looks like.

## Goal

Fix the substitution so the opening message contains the opponent's character name. Do not change unrelated prompt behaviour. Add a regression test.

## Implementation steps

1. **Find the source.** Audit `data/prompts/templates.yaml` and any prompt-building code paths for the literal 'scene' string. Identify whether it's a template literal that should be a placeholder, or a placeholder that's not being substituted.
2. **Fix the template / substitution.** Smallest surgical change that solves the case. Don't refactor the prompt-building system in this PR.
3. **Add a regression test.** In `src/Pinder.LlmAdapters.Tests/` or `src/Pinder.Core.Tests/Conversation/` (wherever the opening-message builder is tested), add a test that asserts the rendered opening contains the opponent's `CharacterName` (or `Name`) and does NOT contain the literal substring "scene" (case-insensitive check fine, but be careful — "scene" could legitimately appear in a longer word like "obscenity"; consider asserting "scene" as a whole word, e.g., `\bscene\b`).
4. **Build + run targeted tests.**

```bash
dotnet build -c Release 2>&1 | tail -10
# expect: 0 errors
dotnet test -c Release src/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-build 2>&1 | tail -5
# expect: green (or no regressions vs main)
```

## Acceptance criteria

- The literal substring "scene" (as a standalone word in the opening) is replaced with the opponent's character name.
- Regression test guards the fix.
- Build clean; no new test failures vs main baseline.

## Commit + push + PR

```bash
git add -A
git commit -m "fix(#951): substitute opponent character name into opening message instead of literal 'scene'

Closes #951.

- <one-line root cause>: <file>:<line>
- Added regression test in <test-file>.cs

DoD: build clean, all targeted tests pass."
git push -u origin fix/951-opening-scene-literal-r2
gh pr create --repo decay256/pinder-core --base main --head fix/951-opening-scene-literal-r2 \
  --title "fix(#951): opening message uses opponent name, not literal 'scene'" \
  --body "Closes #951.

## Root cause
<one-line summary with file:line>

## Fix
<bullet — what changed>

## Test
<regression test name>

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

Correlation id: `2026-05-17-197af9-951-backend-engineer-<your-id>` — pass through to logs.
Sprint id: `2026-05-17-197af9`.
Worktree: `/tmp/work-951-r2`.

Per WORKSPACE-ISOLATION: only operate inside `/tmp/work-951-r2/`. Never edit `/root/projects/pinder-core/` directly.

Per NO-FALSE-DOD-CLAIMS: if the build fails or tests fail, your final message MUST say so. Recent history this sprint includes a Rung 0 implementer who claimed "tests passed" with 11 build errors — don't repeat that mistake.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the result, no markdown tables.
