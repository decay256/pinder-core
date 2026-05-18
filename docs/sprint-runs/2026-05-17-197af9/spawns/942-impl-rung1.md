You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#942** as **two coordinated PRs** — one in pinder-core, one in pinder-web. This is a **P0** cross-repo state-mutation bug.

## Cross-repo nature (CRITICAL)

The ticket spans:
- **pinder-core:** `src/Pinder.Core/Conversation/GameSession.cs:791` — `StartTurnAsync` start-of-turn outcome check that throws `GameEndedException(Ghosted)`. Must become **transactional**: if it throws, no observable state mutation.
- **pinder-web:** `src/Pinder.GameApi/Services/ActiveSession.cs:1402` (drain fallback) + `:1848` (prefetch wrapper) — must NOT silently re-run when the speculative prefetch already threw `GameEndedException`; must surface the outcome to the SPA instead.

You will open **two PRs** in lockstep:
1. **pinder-core PR** (branch: `rung-1-deepseek-v4-pro/942-core`) — the core engine change + invariant guard + reverse-verification test.
2. **pinder-web PR** (branch: `rung-1-deepseek-v4-pro/942-web`) — the prefetch-fallback fix, which references the new pinder-core sha via submodule bump in the same PR.

The pinder-web PR's submodule bump locks in the core sha after the core PR merges. Open the core PR first; merge it; then bump + open the web PR.

## Workspace isolation (CRITICAL)

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-rung-1-942-core origin/main
cd /tmp/work-rung-1-942-core
git checkout -b rung-1-deepseek-v4-pro/942-core

# Web worktree (don't start work here yet — wait until core PR merges)
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-rung-1-942-web origin/main
cd /tmp/work-rung-1-942-web
git checkout -b rung-1-deepseek-v4-pro/942-web
```

**Do NOT touch the canonical clones (`/root/projects/pinder-{core,web}/`) directly.**

## Cold-start

1. Read eigentakt backend-engineer spec at `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on REGRESSION-TESTS-ON-BUGS, BUILD-PIPELINE-DISCIPLINE, WORKSPACE-ISOLATION, SUBMODULE-SYNC-AFTER-REBASE, EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS, EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE, EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP.
3. Read `/root/projects/pinder-core/AGENTS.md` AND `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 942 --repo decay256/pinder-core --json number,title,body,comments` — full body + both comments.
5. **Read the anchors:**
   - `/tmp/work-rung-1-942-core/src/Pinder.Core/Conversation/GameSession.cs` — focus on `StartTurnAsync` (line ~791) and any helpers it calls before the outcome-check throw.
   - `/tmp/work-rung-1-942-web/src/Pinder.GameApi/Services/ActiveSession.cs` — focus on `EnsureTurnStartedLockedAsync` (~line 1402), `ResolveTurnAsync` (~1848), and how prefetch faults are caught.
6. `grep -rn "GameEndedException" /tmp/work-rung-1-942-core/src /tmp/work-rung-1-942-web/src` — map every throw site and catch site.

## Pathspec discipline

Never `git add .` / `-A` / `-u`. Explicit pathspecs only. Don't commit `agent.log`, `.eigentakt-bin/`, build artifacts.

## Diagnostic phase (do this before writing any fix)

1. **Identify the mutating operations between `StartTurnAsync`'s entry and the outcome-check throw.** Probable candidates:
   - Ghost-decay tick that decrements `interest` (this is the leading hypothesis).
   - `turnNumber` increment.
   - Trap-state mutation.
   - Shadow-state evolution.

   Cite the lines. Document under `## Diagnostic findings` in your final reply.

2. **Decide the transactional strategy.** Two viable approaches:
   - **A. Snapshot-and-restore.** Capture `(interest, turnNumber, activeTraps, shadowState, …)` at function entry; on `GameEndedException` throw, restore + rethrow.
   - **B. Reorder.** Move the outcome check **before** any state mutation. If the ghost-decay itself is what triggers Bored→Ghosted, this means computing the decay into a local variable, checking the would-be outcome against it, throwing if Ghosted, and only committing the decay if we proceed.

   **Pick B if feasible** (cleaner semantics — no compensating action). Fall back to A only if multiple unavoidable mutations precede the check. Document the choice and why.

3. **Repro test FIRST (REGRESSION-TESTS-ON-BUGS).** Before changing production code, write a failing xUnit test in `tests/Pinder.Core.Tests/Issue942_StartTurnTransactionalTests.cs`:
   ```csharp
   [Fact]
   public async Task StartTurnAsync_ThrowsGameEnded_LeavesSessionStateUnchanged() {
       var session = BuildBoredSession(interestBeforeDecay: 6); // crosses Ghosted threshold on decay
       var snapshotBefore = SessionSnapshot.Capture(session);
       await Assert.ThrowsAsync<GameEndedException>(() => session.StartTurnAsync(...));
       var snapshotAfter = SessionSnapshot.Capture(session);
       Assert.Equal(snapshotBefore, snapshotAfter); // interest, turnNumber, traps, shadow all unchanged
   }
   ```
   Confirm it FAILS on current main (proving the bug exists). Then implement the fix. Then confirm it PASSES.

4. **Per-turn invariant guard.** Add to wherever turn records are validated before persist (likely `GameSession.PersistTurnAsync` or a `TurnRecord` constructor / static validator):
   ```csharp
   if (turnRecord.Roll.IsSuccess && turnRecord.Interest.DeltaFromRoll < 0)
       throw new InvariantViolationException($"successful roll with negative delta_from_roll on turn {turnRecord.TurnNumber}");
   ```
   Or, more conservatively, log + skip persist. Pick the more reversible option (log+skip, with a clear ERROR log). Add a regression test.

5. **Web side (separate PR, after core merges).** In `ActiveSession.cs`:
   - The prefetch wrapper at ~line 1848 catches `GameEndedException` and currently falls through to the sync path (which then re-enters and corrupts).
   - Fix: when the speculative prefetch throws `GameEndedException`, **propagate the outcome to the session-ended wire path** (the existing path the SPA already uses for normal end-of-game). Do NOT silently re-run `StartTurnAsync`.
   - Mirror in the drain fallback at ~line 1402.

## Implementation steps — core PR

1. Build pinder-core baseline: `cd /tmp/work-rung-1-942-core && dotnet build -c Release 2>&1 | tail -10`.
2. Run targeted tests baseline (record failures): `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --filter "FullyQualifiedName~GameSession|FullyQualifiedName~StartTurn|FullyQualifiedName~Ghost" --no-build 2>&1 | tail -10`.
3. Write the failing repro test. Confirm it fails.
4. Refactor `StartTurnAsync` (strategy B or A as documented). Confirm the repro test passes.
5. Add the invariant guard + its regression test.
6. Full Pinder.Core.Tests suite: must remain green (or only pre-existing skips). Tail/grep only.
7. Solution-wide Release build: `dotnet build -c Release 2>&1 | tail -5`. Must be 0 errors.
8. Commit with explicit pathspecs (one logical commit per change: refactor + test for the transactional fix; one for the invariant guard + its test). Push.
9. Open core PR: `gh pr create --repo decay256/pinder-core --base main --head rung-1-deepseek-v4-pro/942-core --fill`. Body must include `Closes #942` and the `## DoD Evidence` block per spec.

## Implementation steps — web PR (AFTER core PR merges)

The orchestrator will tell you the core merge sha. Do NOT proceed to web until core is green-merged.

1. In `/tmp/work-rung-1-942-web`: `git submodule update --init --remote pinder-core` (or whichever submodule path is used). Verify `git -C pinder-core log -1 --oneline` shows the core merge sha.
2. Fix `ActiveSession.cs:1402` and `:1848` per the strategy above.
3. Add an integration test that exercises the prefetch-fault path (if test harness exists). If not, add a unit test on the new branch logic.
4. `cd /tmp/work-rung-1-942-web && pnpm install && pnpm test` (or whatever the test runner is — check `package.json` and `AGENTS.md`).
5. Run the C# tests too: `dotnet test src/Pinder.GameApi.Tests/Pinder.GameApi.Tests.csproj`.
6. Commit + push + open PR. Body must include `Closes #942` (re-references the same issue), the submodule-bump line, and `## DoD Evidence`.

## Acceptance criteria (from ticket)

- [ ] `GameSession.StartTurnAsync` is transactional: if it throws `GameEndedException`, no observable state mutation. Reverse-verification test proves this.
- [ ] `ActiveSession.ResolveTurnAsync` sync-fallback: if speculative prefetch threw `GameEndedException`, surface the outcome to the SPA via existing session-ended wire path; do NOT silently re-run.
- [ ] Per-turn invariant guard: `roll.is_success == true` ⇒ `interest.delta_from_roll >= 0`. Log + reject if violated.
- [ ] Both PRs build clean + tests green.

## Workflow rules

- Atomic commits. Mandatory commit message body explaining the transactional strategy choice.
- Tail/grep test output. Never read raw full test logs into reasoning.
- One xUnit test per AC item, named after the AC.
- Open BOTH PRs from the SAME spawn. Don't end your task mid-way.

## DO NOT

- Do not merge either PR. The orchestrator merges after review.
- Do not push to main.
- Do not modify unrelated files (don't sneak in unrelated cleanup).
- Do not work in `/root/projects/pinder-{core,web}/` — only in `/tmp/work-942-{core,web}/`.
- Do not skip the failing-test-first step. The bug exists; prove it before fixing it.
- Do not add a `[Fact(Skip=...)]` anywhere; this is a production bug, not test infra.

## Logging to agent.log

Entry:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#942" "P0-transactional-fix" "started" "Diagnosing then writing failing repro test before fixing"
```

After core PR opened:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#942" "P0-core-PR-opened" "in-progress" "core PR #N opened, awaiting orchestrator merge before web PR" "<core-sha>"
```

After web PR opened:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#942" "P0-web-PR-opened" "completed" "web PR #M opened" "<web-sha>"
```

## Output requirements

End your final reply with:

- `## Diagnostic findings` — what mutations happen between StartTurnAsync entry and the outcome-check throw, with line citations.
- `## Strategy choice` — A (snapshot-and-restore) vs B (reorder), and why.
- `## DoD Evidence (core PR)` — PR URL, repro test fail-then-pass output, full-suite tail, build tail, `git log --oneline -3`, `gh pr view` output, agent.log entries.
- `## DoD Evidence (web PR)` — same shape, plus submodule bump confirmation.
- `## Research Log` — what you read in each repo, what you tried, what alternatives you rejected.
- `## Filed follow-ups` — any new tickets if you discovered adjacent bugs.

If at ANY point you cannot proceed (the fix needs an architectural decision you can't make, repro fails to materialize, etc.), STOP and emit a structured BLOCKED block:

```
## BLOCKED
phase: <diagnostic | core-fix | web-fix>
blocker: <one-line headline>
context: <2-4 lines>
options considered:
- A: ...
- B: ...
recommendation: <which, why>
```

The orchestrator escalates BLOCKED to the next rung or files it as a question.
