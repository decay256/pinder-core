You are a backend engineer subagent implementing **pinder-core #905** in one PR. Pure additive DTO change.

## Ticket summary

Add `GhostProbabilityPerTurn` (double, 0.0..1.0) to `GameStateSnapshot`. Derive today as `ResolveInterestState() == InterestState.Bored ? 0.25 : 0.0`. Serialize as `ghost_probability_per_turn` (snake_case). Tests + spec doc. Closes #905.

Full issue: `gh issue view 905 --repo decay256/pinder-core`.

## Workspace

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-905-r1 origin/main
cd /tmp/work-905-r1
git checkout -b fix/905-ghost-probability-per-turn-r1
```

**Work in `/tmp/work-905-r1/` only.**

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/AGENTS.md` — Snapshot Schema Discipline applies (you ARE adding a player-visible field to GameSession-adjacent state).
3. Read the ticket.
4. Read `src/Pinder.Core/Conversation/GameStateSnapshot.cs` — understand the existing shape + naming + serialization pattern.
5. Read `src/Pinder.Core/Conversation/GameSession.cs` around L1980 — find `ResolveInterestState()` and the `_dice.Roll(4) == 1` ghosting check.
6. Read one existing snapshot test in `tests/Pinder.Core.Tests/Conversation/` to mirror style.
7. Read `session-runner/Snapshot/SessionSnapshot.cs` (per AGENTS.md) — does `TurnSnapshot` include `GameStateSnapshot`? If yes, the new field flows through automatically; verify and document. If no, this might mean the new field isn't snapshot-visible; raise as a concern.

## Approach

1. **Add `GhostProbabilityPerTurn`** to `GameStateSnapshot`:
   - Property: `public double GhostProbabilityPerTurn { get; }` (or whatever immutable pattern the existing fields use).
   - Constructor parameter, matching existing constructor shape.
   - JSON: `[JsonPropertyName("ghost_probability_per_turn")]` (or whatever attribute the file uses for snake_case).
2. **Derive at the call site** that builds `GameStateSnapshot` — likely in `GameSession.cs`. Use the formula from the ticket:
   ```csharp
   var ghostProb = ResolveInterestState() == InterestState.Bored ? 0.25 : 0.0;
   ```
   Pass to the constructor.
3. **Update `session-runner/Snapshot/SessionSnapshot.cs`** if needed — per AGENTS.md, any new player-visible field on GameSession state must also be added to `TurnSnapshot`. Open that file and check whether `state_after` already serializes the full `GameStateSnapshot` (in which case the new field flows through) or whether you need to add the field explicitly.
4. **Tests:** add to whatever existing test class covers `GameStateSnapshot`:
   - At Bored interest: `GhostProbabilityPerTurn == 0.25`.
   - At Lukewarm / above: `GhostProbabilityPerTurn == 0.0`.
   - Serialization: round-trip through `JsonSerializer` produces `"ghost_probability_per_turn": 0.25` in the JSON.
5. **Spec doc:** add an entry to `docs/specs/` for the new wire field. If no existing file matches, create `docs/specs/wire-fields-ghost-probability-per-turn.md` (or a similarly-named file mirroring the doc style for #903 / similar if existing specs follow a convention). Document the formula + range + future-flexibility intent (per ticket §Why on the wire).

## Acceptance criteria

- [ ] `GhostProbabilityPerTurn` property exists on `GameStateSnapshot`.
- [ ] Serializes as `ghost_probability_per_turn`.
- [ ] Tests added (Bored vs not-Bored + serialization round-trip).
- [ ] Spec doc added under `docs/specs/`.
- [ ] `TurnSnapshot` (per AGENTS.md Snapshot Schema Discipline) reflects the new field — either automatically (if it inlines `GameStateSnapshot`) or explicitly. Document which.
- [ ] `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` green.
- [ ] `dotnet build` clean.

## Workflow rules

- Commit incrementally (one commit per logical step minimum).
- Run tests after the property is added + after tests are added.
- Open PR: `gh pr create --repo decay256/pinder-core --base main --head fix/905-ghost-probability-per-turn-r1 --fill` with `Closes #905`.

## Pre-existing breakage (NOT yours)

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (tracked #909). Run only `Pinder.Core.Tests` for the relevant signal.

## DO NOT

- Do not merge.
- Do not bump submodule pointer.
- Do not change the ghost-roll rule itself (`_dice.Roll(4) == 1`); only ADD a new derivation for the snapshot.
- Do not modify unrelated files.

## Logging

```bash
cd /tmp/work-905-r1 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#905" "core/snapshot" "started" "Implementing #905 per branch fix/905-ghost-probability-per-turn-r1"
```

At exit:
```bash
cd /tmp/work-905-r1 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#905" "core/snapshot" "completed" "PR <#N> opened" "<commit-sha>"
```

## Output

### `## DoD Evidence` block (mandatory):
- PR URL.
- `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore` tail (green).
- `dotnet build` tail (zero errors).
- `git log --oneline origin/main..HEAD`.
- Push confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log lines.
- One-line answer to: "Does TurnSnapshot pick up the new field automatically (inlined GameStateSnapshot) or did you add it explicitly?"

### `## Research Log` block (mandatory):
| Topic | Source | Key finding |
|---|---|---|
