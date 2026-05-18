You are a code reviewer subagent in the Pinder dev swarm. Review pinder-core PR **#960** (fix for #951 — opening message contains literal "scene" instead of opponent character name).

## Workspace isolation
```bash
rm -rf /tmp/review-951
git clone --branch fix/951-opening-scene-literal-r2 \
  https://github.com/decay256/pinder-core /tmp/review-951
cd /tmp/review-951
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md`.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh pr view 960 --repo decay256/pinder-core --json title,body,additions,deletions,files`.
5. `gh issue view 951 --repo decay256/pinder-core --json number,title,body,comments`.

## What you're reviewing

PR #960 is a P1 prompt-construction bug fix (81 add / 4 del):

1. `src/Pinder.LlmAdapters/SessionDocumentBuilder.cs` — `AppendConversationHistory` and `BuildInterestChangeBeatPrompt` now filter out `Senders.IsScene(entry.Sender)` entries before formatting.
2. Test file(s) — 2 new regression tests asserting opponent name appears and `\bscene\b` does not.

## Heuristic checklist

### 1. Root cause / fix correctness
- [ ] `Senders.Scene` = "[scene]" is the synthetic sender for bio/outfit entries (Issue #333). They're seeded into history as context, not as actual conversation turns.
- [ ] Pre-fix: `AppendConversationHistory` rendered them as `[T1|OPPONENT|[scene]] "..."` because `entry.Sender != playerName` falls through to OPPONENT. The LLM then echoes "scene" as the opponent's name in opening message.
- [ ] Fix: `Senders.IsScene(entry.Sender)` filter skips these entries. Verify the filter is applied **before** turn numbering, not after, so turn indices stay aligned with the remaining (real) conversation entries.
- [ ] `BuildInterestChangeBeatPrompt` also filters before the last-6 window. Verify the filter is applied **before** the windowing, not after (otherwise the window could be empty if last 6 entries are all scene tags).

### 2. AC coverage
- [ ] AC1: literal "scene" no longer appears in opening message contexts. The regression test should assert `\bscene\b` (whole-word match) against the rendered prompt for both code paths.
- [ ] AC2: opponent's character name is present in opening message contexts. The regression test should assert opponent name appears.
- [ ] Behavioral test: a session with one scene entry + one real opponent message → rendered history contains only the real message, properly numbered, no scene literal.

### 3. Don't-break checks
- [ ] `ConversationIndexing` already has scene-aware logic (lines 50-115 per grep). Confirm the new filter in `SessionDocumentBuilder` doesn't conflict — i.e., the prompt builder filter is its own layer, doesn't affect `_history` mutation in `GameSession`.
- [ ] Other callers of `AppendConversationHistory` (grep for usages) still get the correct behaviour. The function signature didn't change.
- [ ] Turn-numbering math: previously `int turn = (i / 2) + 1` used the raw index. New code uses a `filteredIndex` counter. Verify it produces `T1, T2, T3, ...` for the real entries (each PLAYER+OPPONENT pair = one turn).

### 4. Build + tests
```bash
cd /tmp/review-951
dotnet build -c Release src/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj 2>&1 | tail -5
dotnet test -c Release src/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj --no-build 2>&1 | tail -10
```
- [ ] Build: 0 errors.
- [ ] Tests: pass count matches implementer's claim (1069/1069). Capture in Stats.

### 5. PR hygiene
- [ ] PR body has `Closes #951` (case-insensitive somewhere in the body or title).
- [ ] Commit message describes root cause + fix + tests.

## Verdict

`APPROVE` if all blockers clear and tests green. `CHANGES_REQUESTED` with specific file:line blockers otherwise.

```bash
gh pr review 960 --repo decay256/pinder-core --approve -b "<body>"
# OR
gh pr review 960 --repo decay256/pinder-core --request-changes -b "<body>"
```

## DoD evidence

```
Stats: tokens_in=<N> tokens_out=<N> tokens_cache_read=<N or n/a> tokens_cache_write=<N or n/a> wall_clock_seconds=<N>
verdict: APPROVE   (or CHANGES_REQUESTED — N blockers)
```

## Reminders

Correlation id: `2026-05-17-197af9-951-code-reviewer-<your-id>`.
Sprint id: `2026-05-17-197af9`.

Per the response-style rules in `/root/.openclaw/agents-extra/pinder/USER.md`: short, lead with the verdict, no markdown tables, max 5 bullets per section.
