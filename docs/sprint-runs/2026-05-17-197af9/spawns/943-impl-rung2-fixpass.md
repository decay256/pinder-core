You are a backend engineer subagent in the Pinder dev swarm. **Fix-pass on PR #958** (#943).

## Workspace isolation

Reuse the existing worktree (branch already exists with the previous attempt):
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree list | grep work-943-core || git worktree add /tmp/work-943-core fix/943-roll-tier-success-value
cd /tmp/work-943-core
git checkout fix/943-roll-tier-success-value
git status   # should be clean on branch fix/943-roll-tier-success-value
git rebase origin/main || { echo "REBASE-CONFLICT"; exit 1; }
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — especially REGRESSION-TESTS-ON-BUGS, BUILD-PIPELINE-DISCIPLINE, EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS, EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. Read the existing branch state: `git log --oneline origin/main..HEAD` and `git show HEAD --stat`.
5. Read PR #958 reviewer comment (already on GitHub) for full blocker context:
   ```bash
   gh pr view 958 --repo decay256/pinder-core --json reviews
   ```

## Context

The original Rung 0 implementer made a false DoD claim ("build succeeded") and chose a rename that caused 17 C# + 57 frontend breakages in pinder-web. Reviewer left 4 blockers. Your job: apply the **compat-shim fix path** the reviewer recommended.

Branch: `fix/943-roll-tier-success-value`. PR #958 already open as draft with CHANGES_REQUESTED. After your commit + push, mark PR ready for review and add a reply comment summarizing fixes.

## Goal: compat shim — symbol `Success`, wire `"None"`, source alias `None`

The reviewer's Recommended Fix Path is canonical. Apply it verbatim:

### Required changes

1. **In `src/Pinder.Core/Conversation/RollResult.cs` (or wherever the `FailureTier` enum is defined — verify with `grep -rn "enum FailureTier" src/`):**

   ```csharp
   using System.Runtime.Serialization;

   public enum FailureTier
   {
       [EnumMember(Value = "None")]
       Success = 0,   // wire string stays "None" for back-compat; symbol Success is explicit
       Fumble,
       Misfire,
       TropeTrap,
       Catastrophe,
       Legendary
       // … keep whatever other variants exist; only rename None→Success and add the EnumMember
   }
   ```

   **Verify the exact enum members first.** Use `grep -n "FailureTier" src/Pinder.Core/Conversation/RollResult.cs` and the existing enum body. Preserve every other variant byte-for-byte; only `None` becomes `Success` with the EnumMember attribute.

2. **Add `None = 0` as a duplicate-value enum member** to preserve source compat for pinder-web's 17 `FailureTier.None` references:

   ```csharp
   public enum FailureTier
   {
       [EnumMember(Value = "None")]
       Success = 0,
       [Obsolete("Use FailureTier.Success — alias preserved for source compat after rename.")]
       None = 0,   // intentional duplicate value — pinder-web compiles unchanged
       Fumble, Misfire, TropeTrap, Catastrophe, Legendary
   }
   ```

   Duplicate-value enum members are legal in C# and serialize whichever has the matching name. With `[EnumMember(Value = "None")]` on `Success`, JSON serializer emits `"None"` for either symbol. **This is what the reviewer's recommended-fix-path block implies.** Take this path unless you can prove it breaks something.

   First check what pinder-web actually uses:
   ```bash
   grep -rn "FailureTier\\.None" /root/projects/pinder-web/src/ 2>&1 | head -20
   ```

3. **Update all in-repo (pinder-core) call sites that assign or compare `FailureTier.Success`** — the previous PR already did most of this. Verify by running build first.

4. **Fix the 9 CS0117 errors in `Pinder.LlmAdapters.Tests` and `Pinder.Rules.Tests`** that the previous implementer missed:
   - `tests/Pinder.LlmAdapters.Tests/EngineInjectionBlockTests.cs` (x2)
   - `tests/Pinder.LlmAdapters.Tests/Issue544_EngineInjectionSpecTests.cs` (x2)
   - `tests/Pinder.LlmAdapters.Tests/Issue372_ArchetypeDirectiveDeliveryTests.cs`
   - `tests/Pinder.LlmAdapters.Tests/Anthropic/Issue241_LegendaryFailVoiceTests.cs`
   - `tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderTests.cs`
   - `tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderSpecTests.cs`
   - `tests/Pinder.Rules.Tests/EquivalenceTests.cs`

   These should compile cleanly **without edits** once you add the `None = 0` duplicate alias to the enum. **Preferred: leave them as `FailureTier.None` to exercise the alias.** If they still fail, fix per case (suppress obsolete warnings via `#pragma warning disable CS0618` around the references, or switch to `FailureTier.Success`).

5. **Verify the Issue943 test asserts wire string `"None"` (not `"Success"`):** the new `Issue943_RollTierOnSuccessTests.cs` from the previous attempt may assert `"Tier":0`. The wire contract demands the *string* `"None"` when emitting tier on success. Look at how `RollResult` is normally serialized:
   ```bash
   grep -rn "JsonStringEnumConverter\\|FailureTier" src/Pinder.Core/ | head -20
   ```

   If production serializes enum as string via `JsonStringEnumConverter`, the test should assert `"Tier":"None"` to prove `[EnumMember(Value="None")]` works. If production serializes as int, assert `0`.

### DO NOT change pinder-web `TurnAuditWriter.cs` in this PR

That's the **companion web PR's** job. The reviewer's blocker #4 ("AC not met on wire") is correct, but the fix for it lives in pinder-web. The core PR's only job is to ship the enum compat shim + working build so the web PR can land safely.

## Build evidence (MANDATORY — do not skip)

```bash
cd /tmp/work-943-core

# Clean build
dotnet build -c Release 2>&1 | tee /tmp/943-build.log | tail -10
# MUST show "Build succeeded" + 0 Errors. NO CS0117. If any errors, fix them before commit.

# Full test suite (NOT just Issue943)
dotnet test -c Release --no-build 2>&1 | tee /tmp/943-test.log | tail -20
# Compare to baseline (#929 cleared LlmAdapters; expect ~46 still-failing in Pinder.Rules.Tests per #953).
# Issue943 tests MUST pass.

# Specifically verify the new tests
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --filter "FullyQualifiedName~Issue943" --no-build 2>&1 | tail -10

# Cross-repo sanity — confirm the alias works (do NOT modify pinder-web here, just probe by counting references)
grep -rn "FailureTier\\.None" /root/projects/pinder-web/src/ 2>&1 | wc -l
```

## Commit + push

Pathspecs only — no `git add .` / `-A`. Explicit:
```bash
git add src/Pinder.Core/Conversation/RollResult.cs    # or wherever the enum lives
git add tests/Pinder.Core.Tests/Issue943_RollTierOnSuccessTests.cs   # if updated
# Add any other files you actually touched, explicitly
git status   # verify staged set matches intent
git commit -m "fix(#943): preserve FailureTier.None alias + fix 9 CS0117 compile errors

Per reviewer feedback on PR #958:
- Add [EnumMember(Value=\"None\")] to FailureTier.Success so wire string stays \"None\"
- Add FailureTier.None = 0 as Obsolete alias so pinder-web symbol references compile
- Verify dotnet build -c Release succeeds with 0 errors (was 9 CS0117)
- Verify Issue943 tests assert wire string \"None\" matches production serialization

The wire fix (TurnAuditWriter null-on-Success logic) ships in the companion
pinder-web PR — out of scope here."
git push origin fix/943-roll-tier-success-value
```

## Update PR #958

```bash
gh pr ready 958 --repo decay256/pinder-core   # un-draft
gh pr comment 958 --repo decay256/pinder-core --body "@decay256 fix-pass applied per recommended path:

- \`FailureTier.Success\` with \`[EnumMember(Value=\"None\")]\` — wire stays \`\"None\"\`
- \`FailureTier.None\` Obsolete alias (same int value) preserves source compat across pinder-web (17 C# refs unchanged)
- 9 CS0117 errors resolved; \`dotnet build -c Release\` clean
- Frontend \`tier === 'None'\` comparisons remain correct (wire still emits \`\"None\"\`)
- Issue943 tests updated to assert production wire string \`\"None\"\`
- **TurnAuditWriter null-on-Success fix is out of scope here** — ships in companion pinder-web PR"
```

## Workflow rules
- Do NOT merge. Orchestrator merges after reviewer approval.
- Do NOT touch pinder-web in this run.
- Do NOT modify the original `RollResult` constructors — leave them assigning `FailureTier.Success` on success.
- If the duplicate-value enum approach unexpectedly fails to compile, fall back to a `static readonly FailureTier None = FailureTier.Success` alias on a containing static class, and update the 9 CS0117 sites to use `FailureTier.Success` directly. Document why in the commit message.

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#943" "roll-tier-success-fixpass" "started" "Fix-pass on PR #958: apply EnumMember compat shim + fix 9 CS0117"
```
After push + comment:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#943" "roll-tier-success-fixpass" "completed" "PR #958 updated; compat shim applied; build clean" "<sha>"
```

## Output requirements

End with:
- `## Diagnostic findings` — confirmation of how production serializes FailureTier (string vs int), which alias approach worked.
- `## Implementation summary` — exact enum shape after fix, which CS0117 sites self-resolved via alias vs needed edits.
- `## DoD Evidence` — build tail showing 0 errors, test tail with Issue943 passing, sample JSON before/after.
- `## Research Log` — what you read, what you verified.
- `## Filed follow-ups` — none expected, but flag if you discover anything.

All upstream events follow the response-style rules in USER.md — short, lead with the result, no tables.
