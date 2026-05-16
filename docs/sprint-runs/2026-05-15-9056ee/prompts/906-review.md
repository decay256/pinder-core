You are a code-reviewer subagent reviewing **pinder-core PR #923** (Issue #906 — expose `defending_stat` on option rolls).

You are a **no-context fresh-eye reviewer**. Read the PR diff cold before forming opinions on the implementer's deviations.

## PR

- URL: https://github.com/decay256/pinder-core/pull/923
- Branch: `fix/906-defending-stat`
- Base: `main`
- 4 commits

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/code-reviewer.md`.
2. Read `/root/projects/pinder-core/AGENTS.md` — **Snapshot Schema Discipline** is in scope.
3. Read the ticket: `gh issue view 906 --repo decay256/pinder-core`.
4. Fetch the PR:
   ```bash
   cd /root/projects/pinder-core && git fetch origin
   git diff origin/main...origin/fix/906-defending-stat > /tmp/923.diff
   ```

## Specific verification points

### Wire contract

1. **`RollResult.DefendingStat` exists** with type `StatType`.
2. **Value mapping:** populated as `StatBlock.DefenceTable[Stat]` at every construction site.
   - `RollEngine.ResolveFromComponents` (line ~272 per implementer's evidence).
   - `GameSession.CreateForcedFailResult` (line ~2117).
   - Any other `new RollResult(...)` site. Independently verify by running `grep -rn "new RollResult" --include="*.cs" src/ tests/` and checking that production-path sites (not test helpers) all set `DefendingStat`. Test-helper constructors may pass a hardcoded value — that's fine.
3. **Wire serialization:** the JSON field is `"defending_stat"`, value is the enum NAME (e.g. `"Chaos"`), not an int. Confirm by reading the test that asserts serialization.
4. **NOT on `RollCheckResult`.** Confirm `src/Pinder.Core/Rolls/RollCheckResult.cs` was NOT touched (per the prompt scoping: option-roll-specific extras live on `RollResult` only).

### Implementer's declared deviations — verify each

5. **TurnSnapshot field named `DefendingRollStat` (not `DefendingStat`).** Implementer says this disambiguates from `TurnDefenseEntry.DefendingStat` (from #903).
   - Read `session-runner/Snapshot/SessionSnapshot.cs` and confirm both fields exist with their respective names.
   - Is the disambiguation actually a problem in TurnSnapshot land? If both are properties on the same class, yes — name collision. If they're on different records (`TurnDefenseEntry` vs the roll record), the rename may be unnecessary but harmless.
   - **Crucially:** confirm the WIRE field on `RollResult` is still `defending_stat` (not `defending_roll_stat`). The TurnSnapshot rename is internal-only; the wire contract MUST match the ticket spec.

6. **`[JsonConverter(typeof(JsonStringEnumConverter))]`** added on `DefendingStat`.
   - Read `RollResult.cs`. Does the existing `Stat` property have the same converter? If yes, consistent. If no, this is a mixed-enum-serialization smell.
   - Two acceptable resolutions:
     - (a) `Stat` already serializes as string somewhere (maybe globally configured at the `JsonSerializerOptions` level — search for `JsonStringEnumConverter` in `src/`). In that case, the converter on the new property is redundant but harmless.
     - (b) `Stat` does NOT serialize as string today. Then adding the converter ONLY on `DefendingStat` is inconsistent — either both should have it or there's a global setting we're missing.
   - The serialization test in the PR will tell us which: if `JsonSerializer.Serialize(rollResult)` already produces `"stat":"Honesty"` (not `"stat":1`) on `origin/main`, there's a global setting. If on `main` it produces `"stat":1`, the implementer's change creates inconsistency.
   - **Easy independent check:** check out `origin/main` and serialize a `RollResult` — see what `stat` looks like.

### Tests

7. **`tests/Pinder.Core.Tests/.../Issue906_DefendingStatTests.cs`:**
   - Per-stat mapping covered for all 6 `StatType` values.
   - Serialization test asserts `"defending_stat":"<enum-name>"` literally appears in the JSON output.
   - Invariant test: every `RollResult` produced by `Resolve`/`ResolveFromComponents` satisfies `result.DefendingStat == StatBlock.DefenceTable[result.Stat]`.
   - **Reverse-verify ONE test:** temporarily change the mapping in `ResolveFromComponents` to `defendingStat: StatBlock.DefenceTable[StatType.Wit]` (hardcoded wrong). Confirm at least one test fails. Revert.

### Drive-bys / scope

8. **Files touched:**
   - `src/Pinder.Core/Rolls/RollResult.cs` (property + populate).
   - `src/Pinder.Core/Rolls/RollEngine.cs` (population at `ResolveFromComponents`).
   - `src/Pinder.Core/Conversation/GameSession.cs` (population at `CreateForcedFailResult`).
   - `session-runner/Snapshot/SessionSnapshot.cs` (TurnSnapshot mirror).
   - `session-runner/Program.cs` (BuildTurnSnapshot — populates the mirror).
   - `tests/Pinder.Core.Tests/.../Issue906_DefendingStatTests.cs` (new file).
   - `docs/specs/issue-906-defending-stat.md` (new file).
   - Possibly `src/Pinder.Core/Stats/StatBlock.cs` only if a getter was needed — implementer should NOT have changed this; verify it wasn't.

   Confirm no other files touched. No version bumps. No submodule pointer change.

9. **Snapshot Schema Discipline:** `// Fields covered by TurnSnapshot:` comment block at top of `SessionSnapshot.cs` should list `#906`. Verify.

10. **PR body** contains `Closes #906` on its own line.

## Run the tests yourself

```bash
cd /root/projects/pinder-core
git fetch origin
git checkout origin/fix/906-defending-stat --detach 2>&1
git submodule update --init --recursive 2>&1
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/review-923-tests.txt 2>&1
tail -10 /tmp/review-923-tests.txt
```

Expected: 2777 passed, 0 failed, 18 skipped.

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909) — don't run.

## Verdict

```bash
gh pr review 923 --repo decay256/pinder-core --comment --body "$(cat <<'EOF'
**Verdict: APPROVE** (or **CHANGES_REQUESTED**)

<structured review>
EOF
)"
```

Pay particular attention to the `JsonStringEnumConverter` question. If the existing `Stat` property serializes as int and the new `DefendingStat` as string, that's a real wire inconsistency the frontend can trip on — blocker. If the existing `Stat` already serializes as string (global config or attribute), the new attribute is redundant but not a blocker.

## Authority

First-pass review. Orchestrator decides next.

## Logging

```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#906" "core/rolls" "started" "Reviewing PR #923 (first pass)"
```

At exit:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#906" "core/rolls" "completed" "Verdict: <V>, blockers: <N>, follow-ups: <N>"
```

## Output

Final response is the same body posted to the PR.
