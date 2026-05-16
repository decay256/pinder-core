You are a backend engineer subagent implementing **pinder-core #906** in one PR. Small additive — expose the option-roll defending stat on `RollResult` + wire DTO.

## Ticket summary

Read the full issue: `gh issue view 906 --repo decay256/pinder-core`. Short version:

- Add `DefendingStat: StatType` property on `RollResult`.
- Populated at roll resolution via `StatBlock.DefenceTable[stat]`.
- Wire DTO field: `defending_stat` (snake_case, value is the `StatType` enum name).
- Tests, spec doc, `TurnSnapshot` mirror per Snapshot Schema Discipline (this IS a player-visible field).

## Scope clarification (important — read before coding)

The ticket says "after #901, this field lives on the unified `RollCheckResult` for option-roll kind." **Do NOT put `DefendingStat` on `RollCheckResult`.** Per #901's spec, option-roll-specific extras (`Stat`, `RiskTier`, `ActivatedTrap`) live on `RollResult`, not on `RollCheckResult` — `RollCheckResult` is the kind-agnostic check shape. `DefendingStat` is option-roll-specific (other kinds don't have an attacker stat in the same sense), so it goes on `RollResult` only.

**Final placement:**
- `RollResult.DefendingStat: StatType` — yes.
- `RollCheckResult.DefendingStat` — NO (kind-agnostic shape).
- Wire DTO field `defending_stat` on the option-roll DTO surface — yes.
- `TurnSnapshot` mirror — yes (player-visible).

## Workspace

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-906 origin/main
cd /tmp/work-906
git checkout -b fix/906-defending-stat
```

## Cold-start reading order

1. `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md`.
2. `/root/projects/pinder-core/AGENTS.md` — **Snapshot Schema Discipline** is in scope (`DefendingStat` is a player-visible new field on the option-roll path).
3. The ticket.
4. `src/Pinder.Core/Rolls/RollResult.cs` — read the full file. Find the existing `Stat` property and how it's constructed.
5. `src/Pinder.Core/Rolls/RollEngine.cs` — find both `Resolve` and `ResolveFromComponents` (the two construction sites for `RollResult`).
6. `src/Pinder.Core/Stats/StatBlock.cs` — confirm `DefenceTable` is a static dictionary `IReadOnlyDictionary<StatType, StatType>`.
7. **Look at recent-PR precedent** for additive `RollResult` fields. Specifically, PR #918 (#901) just added a `Check: RollCheckResult` property — see how the constructor was extended (likely a new positional param with a default, or new constructor overload). Use the same pattern.
8. `session-runner/Snapshot/SessionSnapshot.cs` — find where the option-roll part of `TurnSnapshot` lives. If `RollResult` has a `Stat` field already mirrored there, mirror `DefendingStat` next to it. If not, look at the comment block at top of `SessionSnapshot.cs` to see what IS covered today and add `DefendingStat` accordingly.
9. Look at the Pinder.GameApi surface: `grep -rn "RollResult\|Roll.*defending\|defending_stat" --include="*.cs" src/Pinder.GameApi/ 2>/dev/null`. If there's a DTO mapper that builds the wire `roll` object from `RollResult`, add `defending_stat` there.
10. PR #913 (#905 — `ghost_probability_per_turn`) is the most recent comparable additive-field PR. Use it as a template for tests + spec doc + `[JsonPropertyName]` + Snapshot Schema mirror.

## Implementation plan

### Step 1 — add `DefendingStat` to `RollResult`

- Add public property `DefendingStat: StatType`.
- Extend the `RollResult` constructor with the new param (default-trailing, or new overload — match the #918 pattern).
- Add `[JsonPropertyName("defending_stat")]` if `RollResult` carries `[JsonPropertyName]` on its other props.

**Commit:** `feat(#906): add DefendingStat to RollResult`.

### Step 2 — populate at resolution sites

- In `RollEngine.Resolve`: pass `defendingStat: StatBlock.DefenceTable[stat]` when constructing `RollResult`.
- In `RollEngine.ResolveFromComponents`: same.
- In any other `RollResult` construction site (search: `grep -rn "new RollResult" --include="*.cs" src/`). Each site MUST populate `DefendingStat` correctly. If a forced-fail / synthetic `RollResult` constructor (e.g. `GameSession.CreateForcedFailResult`) doesn't have an attacker `Stat` in context, propagate a sensible default — likely `DefenceTable[stat]` if `stat` is in scope, otherwise pass `default(StatType)` and document.

**Commit:** `feat(#906): populate DefendingStat at all RollResult construction sites`.

### Step 3 — wire DTO mapping (if a mapper exists)

If `Pinder.GameApi` has a mapper that builds the wire `roll` object from `RollResult`, add `defending_stat` to the output. If `RollResult` is serialized directly (no mapper), the `[JsonPropertyName]` from step 1 is enough.

Read `src/Pinder.GameApi/` to determine which. If there IS a mapper, this is its own commit.

**Commit:** `feat(#906): expose defending_stat on wire DTO` (or fold into step 1 if no mapper).

### Step 4 — TurnSnapshot mirror (Snapshot Schema Discipline)

- Add `DefendingStat: string` (or `StatType`) to whatever record in `session-runner/Snapshot/SessionSnapshot.cs` holds the option-roll details from a turn.
- Populate in `session-runner/Program.cs`'s snapshot-building code (search for where `Stat` is populated; mirror).
- Add `#906` to the `// Fields covered by TurnSnapshot:` comment block at the top of `SessionSnapshot.cs`.

**Commit:** `feat(#906): TurnSnapshot mirrors DefendingStat`.

### Step 5 — tests

New file: `tests/Pinder.Core.Tests/Rolls/Issue906_DefendingStatTests.cs`:

- **Per-stat mapping:** for each `StatType`, construct a roll with `Stat = X`, assert `result.DefendingStat == StatBlock.DefenceTable[X]`. 6 stats → 6 assertions or one parameterized test.
- **Serialization:** `JsonSerializer.Serialize(rollResult)` contains `"defending_stat":"<expected>"`.
- **Audit / drift guard:** `DefendingStat` always equals `StatBlock.DefenceTable[Stat]` — never independently set. (Cover this by reflection / direct assertion across the constructor path; a single test on `Resolve` is sufficient.)

**Commit:** `test(#906): Issue906_DefendingStatTests`.

### Step 6 — spec doc

New file: `docs/specs/issue-906-defending-stat.md`. Mirror style of `docs/specs/issue-905-ghost-probability-per-turn.md` and `docs/specs/issue-903-opponent-defense-snapshot.md`. Cover:
- Wire field definition.
- Mapping rule (`StatBlock.DefenceTable[Stat]`).
- Placement on `RollResult` (NOT on `RollCheckResult`).
- TurnSnapshot mirror.
- Frontend consumer reference (pinder-web#601).

**Commit:** `docs(#906): spec doc for defending_stat`.

## Acceptance criteria

- [ ] `RollResult.DefendingStat` exists.
- [ ] Populated at every `RollResult` construction site.
- [ ] Wire field `defending_stat` reaches the frontend (verify via serialization test).
- [ ] `TurnSnapshot` mirrors it.
- [ ] Tests added.
- [ ] Spec doc added.
- [ ] All existing tests pass (expect 2763 → 2770+).
- [ ] `RollResult.DefendingStat == StatBlock.DefenceTable[Stat]` invariant holds.
- [ ] **`RollCheckResult` is NOT touched** (option-roll-specific extra; lives on `RollResult` only).
- [ ] PR body contains `Closes #906` on its own line.

## Workflow rules

- Commit incrementally per the 6 steps.
- Test after each code-touching step:
  ```bash
  dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/test-906.txt 2>&1 && tail -10 /tmp/test-906.txt
  ```
- Open PR:
  ```bash
  git push -u origin fix/906-defending-stat
  gh pr create --repo decay256/pinder-core --base main --head fix/906-defending-stat --title "fix/906 expose roll.defending_stat on option rolls" --body "$(cat <<EOF
  - feat(#906): add DefendingStat to RollResult
  - feat(#906): populate DefendingStat at all RollResult construction sites
  - feat(#906): expose defending_stat on wire DTO (if mapper exists)
  - feat(#906): TurnSnapshot mirrors DefendingStat
  - test(#906): Issue906_DefendingStatTests
  - docs(#906): spec doc for defending_stat

  Additive field. Companion to pinder-web#601. Maps via StatBlock.DefenceTable[Stat] — single source of truth.

  Closes #906
  EOF
  )"
  ```

## Pre-existing breakage (NOT yours)

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909). Run `Pinder.Core.Tests` only.

## DO NOT

- Do NOT put `DefendingStat` on `RollCheckResult`. Option-roll-specific extras stay on `RollResult` per #901's spec.
- Do NOT change `StatBlock.DefenceTable` semantics or move it.
- Do NOT alter the option-roll engine math.
- Do NOT bump submodule pointer.
- Do NOT merge.

## Logging

```bash
cd /tmp/work-906 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#906" "core/rolls" "started" "Implementing #906 per branch fix/906-defending-stat"
```

At exit:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#906" "core/rolls" "completed" "PR #<N> opened" "<commit-sha>"
```

## Output

### `## DoD Evidence` block (mandatory):
- PR URL.
- `dotnet test` tail.
- `dotnet build` tail.
- `git log --oneline origin/main..HEAD`.
- Push confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log lines.
- A `grep -n "new RollResult" src/` output showing every construction site, with confirmation each was updated.

### `## Research Log` block (mandatory):
| Topic | Source | Key finding |
|---|---|---|

### Deviations

Document anything you had to deviate from this prompt.
