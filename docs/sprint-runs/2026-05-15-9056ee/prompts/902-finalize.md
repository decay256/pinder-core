You are a backend engineer subagent finalizing **pinder-core ticket #902**. A previous implementer landed 5 commits on branch `fix/902-meta-prefix-stripper-r1` in worktree `/tmp/work-902-r1` and was cut off mid-test-run. Your job: verify, finalize, push, open PR. **DO NOT redo the work.**

## State on entry

```
cd /tmp/work-902-r1
git log --oneline origin/main..HEAD
```

Should show 5 commits:
- `feat(#902): add MetaPrefixStripper — shared strip for LLM meta-prefix labels`
- `refactor(#902): route DialogueOptionParsers through shared MetaPrefixStripper`
- `fix(#902): apply MetaPrefixStripper after every overlay-producing LLM call`
- `test(#902): add MetaPrefixStripper unit tests + WOULD-YOU-RATHER regression`
- `docs(#902): capture sanitization-invariants-must-run-after-each-stage lesson`

There is also uncommitted work:
- `tests/Pinder.Core.Tests/MetaPrefixStripperTests.cs` — one-test assertion correction (`"CONTEXT:  "` now expects `string.Empty`, not `" "`)
- `agent.log` — has the "started" log entry the implementer wrote

## Your task

1. **`cd /tmp/work-902-r1`**
2. **Verify the 5 commits + uncommitted state matches the description above.** If anything is different, STOP and report.
3. **Run the unit tests for MetaPrefixStripper** to confirm green:
   ```
   dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --filter "FullyQualifiedName~MetaPrefixStripper" --no-restore > /tmp/test-902-meta.txt 2>&1
   tail -10 /tmp/test-902-meta.txt
   ```
4. **Run the FULL test suite** to confirm no regressions:
   ```
   dotnet test --no-restore > /tmp/test-902-full.txt 2>&1
   tail -10 /tmp/test-902-full.txt
   grep -iE "fail|error" /tmp/test-902-full.txt | grep -vE "0 Failed|: 0 |info:" | head -20
   ```
5. **If full suite green:**
   - Commit the test assertion fix as `test(#902): correct MetaPrefixStripper whitespace-only-label assertion (\s* greediness)`.
   - Commit ONLY the test file change. The `agent.log` change stays uncommitted (it's mid-stream; you'll rewrite it below).
   - `git push -u origin fix/902-meta-prefix-stripper-r1`
   - `gh pr create --repo decay256/pinder-core --base main --head fix/902-meta-prefix-stripper-r1 --fill` with `Closes #902` in body.
6. **If full suite has failures unrelated to #902:** STOP and report — that's a pre-existing main breakage, not #902's problem.
7. **If full suite has failures caused by #902:** report the failure tail and STOP. Do not attempt to fix.
8. **Log:** after PR open:
   ```
   cd /tmp/work-902-r1 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#902" "core/text" "completed" "PR <#N> opened (finalized by resume subagent)" "<commit-sha>"
   ```

## DoD Evidence block (mandatory, real output):

- `git log --oneline origin/main..HEAD` showing all 6 commits.
- Test-tail (meta + full).
- `git push` confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log entries.

## DO NOT

- Do not edit any source file beyond confirming the existing changes compile.
- Do not redo any of the 5 existing commits.
- Do not bump submodule pointer.
- Do not merge.
- Do not work outside `/tmp/work-902-r1/`.
