You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#884** as one PR in pinder-core.

## Workspace isolation (CRITICAL)
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-884 origin/main
cd /tmp/work-884
git checkout -b fix/884-issue527-bioformat-flake-isolation
```

**Do NOT touch `/root/projects/pinder-core/` directly. All work in `/tmp/work-884/`.**

## Cold-start
1. Read eigentakt backend-engineer spec at `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — especially WORKSPACE-ISOLATION, SUBMODULE-SYNC-AFTER-REBASE, SELF-APPROVE-BLOCKED, APPROVED-WORK-IS-IMMUTABLE, REGRESSION-TESTS-ON-BUGS, BUILD-PIPELINE-DISCIPLINE, EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS, EIGENTAKT-CHECK-TRACKED-BEFORE-GITIGNORE, EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE, EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP.
3. Read `/root/projects/pinder-core/AGENTS.md` — project schema rules.
4. `gh issue view 884 --repo decay256/pinder-core --json number,title,body,comments`.

## Pathspec discipline
- Never run `git add .`, `git add -A`, or `git add -u`. Explicit pathspecs only.
- Do NOT commit `agent.log` (tracked, orchestrator-managed) or `.eigentakt-bin/`.

## Tracked-file caution
Before .gitignoring or deleting any file: `git ls-tree origin/main <path>`.

## Co-located test mirrors
Updates to a co-located `.test.ts` / `.test.tsx` / `.cs` test mirror are part of the source change when test assertions hardcode literals the source edit reconciles. Document under `## Mirror test updates` in the Research Log.

## Cross-repo grep for dead-code deletions
Before declaring any pinder-core symbol unused, grep BOTH `/root/projects/pinder-core/src,tests/` AND `/root/projects/pinder-web/src,tests/`. Document in Research Log.

## Build evidence requirement
Run the exact deploy build (`dotnet publish src/Pinder.GameApi/Pinder.GameApi.csproj -c Release`), not just the test runner. Capture tail in PR body under `## DoD Evidence`.

## The bug

`Issue527_SessionRunnerBioFormatTests.BioFormattedAsBoldItalicParagraph_NotTableRow` produces empty output on ~2/3 runs when run alongside Issue873 tests. Pre-existing assembly-load/AppDomain interaction between session-runner.dll reflection (Issue527) and YamlDotNet (loaded by `PromptCatalog.LoadFromDirectory` in Issue873).

## Approach (think before coding)

This is **test isolation**, not a production bug. The likely fixes, in order of preference:

1. **xUnit `[Collection]` serialization** — put both Issue527 and Issue873 tests in the same xUnit collection so they cannot run concurrently. xUnit defaults to parallelizing test classes across the same assembly; a shared collection serializes them.
2. **`[CollectionDefinition(DisableParallelization = true)]`** — heavier hammer that disables parallelization for an entire collection.
3. **AssemblyInfo.cs `[assembly: CollectionBehavior(DisableTestParallelization = true)]`** — nuclear option; only if (1) and (2) don't fix it. Hurts overall test wall-time.
4. **Eager-load YamlDotNet in a test fixture** so by the time Issue527 reflects on SessionRunner.dll, YamlDotNet is already loaded and static state is settled.

Try (1) first. It's the smallest, most targeted change.

## Implementation steps

1. Find the test files: `grep -rn "Issue527_SessionRunnerBioFormatTests\|Issue873" tests/Pinder.Core.Tests/`. Read them.
2. Reproduce the flake. From `/tmp/work-884`:
   ```bash
   for i in 1 2 3 4 5; do
     echo "=== Run $i ===" >> /tmp/884-baseline.txt
     dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj \
       --filter "FullyQualifiedName~Issue527|FullyQualifiedName~Issue873" \
       --no-build 2>&1 | tail -30 >> /tmp/884-baseline.txt
   done
   ```
   Confirm failure rate matches the ticket (~2/3 runs fail with empty output). If it's actually 0/5 fails on current main, document that and either (a) close as won't-repro or (b) write the isolation anyway as defensive hardening and move on.
3. Apply the chosen isolation strategy (likely `[Collection("session-runner-reflection")]` on Issue527 + a collection-definition class, OR add Issue873 to the same collection).
4. Re-run the same filter 10 times:
   ```bash
   for i in $(seq 1 10); do
     echo "=== Run $i ===" >> /tmp/884-fixed.txt
     dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj \
       --filter "FullyQualifiedName~Issue527|FullyQualifiedName~Issue873" \
       --no-build 2>&1 | tail -5 >> /tmp/884-fixed.txt
   done
   ```
   Confirm 10/10 pass.
5. Run the full Pinder.Core.Tests suite once to confirm no broader regression: `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-build 2>&1 | tee /tmp/884-fullsuite.txt`. Tail/grep only.
6. Run the deploy build: `dotnet publish src/Pinder.GameApi/Pinder.GameApi.csproj -c Release 2>&1 | tee /tmp/884-publish.txt`. Tail.

## Acceptance criteria (from ticket)
- [ ] Reproduce with `dotnet test --filter "FullyQualifiedName~Issue527|FullyQualifiedName~Issue873"` running 5 times. Failure rate documented in PR body.
- [ ] Identify the root cause (xUnit parallelization of Issue527 reflection alongside YamlDotNet load is the working hypothesis).
- [ ] Apply isolation (`[Collection]` is the smallest viable fix).
- [ ] Test passes 10/10 consecutive runs.

## Workflow rules
- Atomic commit(s). One commit per logical change.
- Tail/grep test output. Never read full test output into your reasoning context.
- Open PR via `gh pr create --repo decay256/pinder-core --base main --head fix/884-issue527-bioformat-flake-isolation --fill`. Include `Closes #884` on its own line.

## DO NOT
- Do not merge.
- Do not push to main.
- Do not modify unrelated files.
- Do not work in `/root/projects/pinder-core/` — work in `/tmp/work-884/`.
- Do not skip the failing test as a "fix". Skip is last-resort with an explicit tracking-ticket comment.

## Logging to agent.log
Task entry (after cold-start reads):
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#884" "Issue527-flake-isolation" "started" "Reproducing flake then applying xUnit Collection isolation"
```
Task exit (after PR opened):
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#884" "Issue527-flake-isolation" "completed" "PR #N opened" "<commit-sha>"
```

## Output requirements

End your final reply with:
- `## DoD Evidence` block: PR URL; 10/10 test-pass tail; full-suite tail; deploy build tail; `git log -1 --oneline`; push confirmation; `gh pr view N` output; agent.log entries.
- `## Research Log` block: what you read, baseline flake rate observed, isolation strategy chosen and why, alternatives rejected and why.
- `## Filed follow-ups` block: any new tickets if you discovered something else along the way.
