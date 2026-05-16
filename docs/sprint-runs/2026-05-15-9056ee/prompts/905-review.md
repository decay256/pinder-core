You are a **no-context code reviewer** for pinder-core PR **#913** covering ticket **#905** (`GhostProbabilityPerTurn` on `GameStateSnapshot`).

Fresh-eye objectivity. Pure additive DTO — tight scope.

## ⚠️ NOT in scope

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909). Run `Pinder.Core.Tests` only.

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/code-reviewer.md`.
2. Read the ticket: `gh issue view 905 --repo decay256/pinder-core`.
3. Read PR: `gh pr view 913 --repo decay256/pinder-core` + `gh pr diff 913 --repo decay256/pinder-core`.
4. **Fresh worktree:**
   ```bash
   cd /root/projects/pinder-core
   git fetch origin
   git worktree add /tmp/review-905 origin/fix/905-ghost-probability-per-turn-r1
   cd /tmp/review-905
   ```

## Critically verify

### A. Acceptance criteria

- [ ] `GhostProbabilityPerTurn` property exists on `GameStateSnapshot`, typed `double`.
- [ ] Range invariant 0.0..1.0 is held by the derivation (Bored → 0.25, else → 0.0). No mid-band values today.
- [ ] Serializes as `ghost_probability_per_turn` (snake_case) when the field is exercised via `JsonSerializer.Serialize`.
- [ ] `TurnSnapshot` includes the new field (per AGENTS.md Snapshot Schema Discipline). Implementer reports `TurnSnapshot` doesn't inline `GameStateSnapshot` — they had to add the field explicitly. Verify.
- [ ] Spec doc added under `docs/specs/`.
- [ ] Tests cover: Bored → 0.25, non-Bored → 0.0, serialization key name.
- [ ] `Pinder.Core.Tests` green (2716/0/18 per implementer's DoD).

### B. Correctness hazards specific to this PR

1. **`GameStateSnapshot` constructor backward-compat.** The implementer added `ghostProbabilityPerTurn` as an OPTIONAL trailing parameter with default. Verify by reading the constructor signature: is the new param at the END, with a default value, so existing callers don't break? If it's anywhere other than the end, callers using positional args break silently. Find all `new GameStateSnapshot(` usages and confirm none broke.
2. **`TurnSnapshot` serialization casing mismatch.** Implementer reports `GameStateSnapshot` serializes the field as `ghost_probability_per_turn` (snake_case via `[JsonPropertyName]`) but `TurnSnapshot` serializes as `GhostProbabilityPerTurn` (PascalCase, no naming policy). That's a real on-the-wire inconsistency: the same field appears under two different keys in two different DTOs. Is that intended? Check whether session-runner snapshots consumers of `TurnSnapshot` and whether the casing matters to them. If the inconsistency is a defect, flag as blocking. If it's deliberate (session-runner is internal debug, the wire-facing DTO is `GameStateSnapshot`), note as non-blocking.
3. **Test rigor for serialization.** The serialization test pattern in `GhostProbabilityTests.cs` should construct a `GameStateSnapshot`, serialize via `JsonSerializer.Serialize(snap)`, and assert the substring `"ghost_probability_per_turn":0.25` appears. Verify the test does exactly this (not e.g. serialize an anonymous object that proxies the field).
4. **`InterestState.Bored` mapping.** Implementer's research log says `InterestState` includes `Bored(1–4)` as a range. Verify the derivation uses `ResolveInterestState() == InterestState.Bored` correctly — it should return Bored when the integer interest is in the Bored band. Confirm `_dice.Roll(4) == 1` (the actual ghost-roll rule) only fires when `ResolveInterestState() == Bored`, matching the 0.25 probability in the snapshot.

### C. Cross-cutting

- **Drive-by changes.** Scope: `GameStateSnapshot.cs`, `GameSessionHelpers.cs`, `session-runner/Program.cs`, `session-runner/Snapshot/SessionSnapshot.cs`, `tests/Pinder.Core.Tests/Conversation/GhostProbabilityTests.cs`, `docs/specs/wire-fields-ghost-probability-per-turn.md`, `agent.log`. Anything else?
- **Schema doc consistency.** Spec doc claims `0.0..1.0` range. Verify the type annotation in the doc and the property declaration agree. Verify "future flexibility" rationale is preserved per ticket §Why on the wire.

### D. Tests soundness

```bash
cd /tmp/review-905
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/test-905-r.txt 2>&1
tail -3 /tmp/test-905-r.txt

# Targeted
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~GhostProbability" > /tmp/test-905-targeted.txt 2>&1
tail -5 /tmp/test-905-targeted.txt
```

## Output

Post review with `gh pr review 913 --repo decay256/pinder-core --approve|--comment ...`.

**Self-approve blocked if gh identity matches PR author.** Post `--comment` with explicit `**Verdict: APPROVE|CHANGES_REQUESTED|NEEDS_DISCUSSION**`.

## Log to agent.log

```bash
cd /tmp/review-905 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#905" "core/snapshot" "review-started" "Starting review for PR #913"
```

At exit:
```bash
cd /tmp/review-905 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#905" "core/snapshot" "review-done" "Verdict=<>, Findings=N"
```

## Reply in this session with

- One-paragraph verdict.
- Specific findings (especially the casing mismatch on the wire — is it a defect or deliberate?).
- Test result tails.
- gh review URL.
- agent.log lines.
