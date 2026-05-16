You are a backend engineer subagent implementing **pinder-core #903** in one PR. Pure additive — adds `OpponentDefenseSnapshot` to `TurnStart`.

## Ticket summary

Add two records: `OpponentDefenseSnapshot` + `OpponentDefenseEntry` (in `Pinder.Core`). Populate in `StartTurnAsync` (6 entries, one per `StatType`, mapped via `StatBlock.DefenceTable`). Attach to `TurnStart` DTO. Wire field: `opponent_defense_snapshot`. Tests + spec doc. Closes #903.

Full issue: `gh issue view 903 --repo decay256/pinder-core`.

## Workspace

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-903 origin/main
cd /tmp/work-903
git checkout -b fix/903-opponent-defense-snapshot
```

**Work in `/tmp/work-903/` only.**

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/AGENTS.md` — Snapshot Schema Discipline applies (you ARE adding a player-visible field). Verify `TurnSnapshot` (session-runner) picks up `TurnStart` payload changes.
3. Read the ticket.
4. Read `src/Pinder.Core/Conversation/GameSession.cs` — find `StartTurnAsync` and the existing `TurnStart` construction. Understand the existing enrichment pattern (e.g. how OTHER additive fields were added — there will be precedent in recent commits).
5. Read `src/Pinder.Core/Stats/StatBlock.cs` — find `DefenceTable` (static dict mapping attacker stat → defender stat). Confirm the public surface.
6. Read `src/Pinder.Core/Characters/CharacterProfile.cs` (or wherever `_opponent.GetEffective(StatType)` is defined) — confirm signature.
7. Read `src/Pinder.Core/Conversation/TurnStart.cs` (or the file that defines the `TurnStart` record) — understand the existing field shape + serialization conventions.
8. Read the most recently merged comparable PR for reference. **PR #913 (#905 — `GhostProbabilityPerTurn`)** just landed and follows the exact same additive-field pattern; use it as the template for tests + spec doc + snake_case `[JsonPropertyName]`.

## Approach

1. **New records** in `src/Pinder.Core/Conversation/OpponentDefenseSnapshot.cs` (new file):
   ```csharp
   public sealed record OpponentDefenseSnapshot(
       IReadOnlyDictionary<StatType, OpponentDefenseEntry> ByAttackerStat
   );
   public sealed record OpponentDefenseEntry(
       StatType DefendingStat,
       int EffectiveModifier,
       int BaseModifier
   );
   ```
   Add `[JsonPropertyName("by_attacker_stat")]` etc. as needed to match wire convention.

2. **Populate in `StartTurnAsync`** per the ticket's exact code snippet:
   ```csharp
   var defenseSnapshot = new OpponentDefenseSnapshot(
       Enum.GetValues<StatType>().ToDictionary(
           attackerStat => attackerStat,
           attackerStat => {
               var defenderStat = StatBlock.DefenceTable[attackerStat];
               return new OpponentDefenseEntry(
                   DefendingStat:     defenderStat,
                   EffectiveModifier: _opponent.GetEffective(defenderStat),
                   BaseModifier:      _opponent.Stats.Get(defenderStat)
               );
           }
       )
   );
   ```
   (If `Enum.GetValues<StatType>()` doesn't compile on the project's target framework, fall back to `(StatType[])Enum.GetValues(typeof(StatType))`.)

3. **Attach to `TurnStart` DTO.** Add as a new property/positional record param. Default-trailing-param pattern (matching #905). Wire JSON key: `opponent_defense_snapshot`.

4. **`TurnSnapshot` (session-runner) — Snapshot Schema Discipline.** Open `session-runner/Snapshot/SessionSnapshot.cs` + `session-runner/Program.cs` (look for `BuildTurnSnapshot` around line ~1758, where #905's `GhostProbabilityPerTurn` was added). Add `OpponentDefenseSnapshot` to `TurnSnapshot` in the same shape. Casing follows the session-runner convention (PascalCase per the #905 precedent — that mismatch with `GameStateSnapshot`'s snake_case is deliberate and documented).

5. **Tests.** New file `tests/Pinder.Core.Tests/Conversation/Issue903_OpponentDefenseSnapshotTests.cs`:
   - 6 entries one per `StatType`.
   - Each entry's `DefendingStat` matches `StatBlock.DefenceTable[attackerStat]`.
   - `EffectiveModifier` reflects shadow corruption (build a session with the opponent under a known shadow that affects Charm; assert the Charm-row modifier differs from `BaseModifier`).
   - `EffectiveModifier` reflects active trap on opponent DC (build a session with a trap raising Wit DC; assert the Rizz-attacker row's `EffectiveModifier` is higher than `BaseModifier`).
   - Serialization: `JsonSerializer.Serialize(turnStart)` contains `"opponent_defense_snapshot"` key.

   For shadow/trap construction: study how existing tests construct these (`grep -l "Shadow" tests/Pinder.Core.Tests/Conversation/` and pick the simplest precedent).

6. **Spec doc:** `docs/specs/issue-903-opponent-defense-snapshot.md`. Mirror the style of `docs/specs/issue-905-ghost-probability-per-turn.md` (which #905 just landed).

## Acceptance criteria

- [ ] `OpponentDefenseSnapshot` + `OpponentDefenseEntry` records exist.
- [ ] `StartTurnAsync` populates a snapshot with 6 entries.
- [ ] `TurnStart` carries the field; serializes as `opponent_defense_snapshot`.
- [ ] `TurnSnapshot` (session-runner) includes the field.
- [ ] Tests cover: entry count, DefenceTable mapping, shadow-reflected, trap-reflected, serialization.
- [ ] Spec doc added.
- [ ] `Pinder.Core.Tests` green.
- [ ] `dotnet build` clean.

## Workflow rules

- Commit incrementally (one commit per logical step).
- Run `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore` after each commit affecting test surface.
- Open PR: `gh pr create --repo decay256/pinder-core --base main --head fix/903-opponent-defense-snapshot --fill` with `Closes #903`.

## Pre-existing breakage (NOT yours)

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909). Run `Pinder.Core.Tests` only.

## DO NOT

- Do not merge.
- Do not change `StatBlock.DefenceTable` or `_opponent.GetEffective` semantics.
- Do not bump submodule pointer.
- Do not modify unrelated files.

## Logging

```bash
cd /tmp/work-903 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#903" "core/snapshot" "started" "Implementing #903 per branch fix/903-opponent-defense-snapshot"
```

At exit:
```bash
cd /tmp/work-903 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#903" "core/snapshot" "completed" "PR <#N> opened" "<commit-sha>"
```

## Output

### `## DoD Evidence` block (mandatory):
- PR URL.
- `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` tail.
- `dotnet build` tail.
- `git log --oneline origin/main..HEAD`.
- Push confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log lines.

### `## Research Log` block (mandatory):
| Topic | Source | Key finding |
|---|---|---|
