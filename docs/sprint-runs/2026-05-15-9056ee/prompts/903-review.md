You are a code-reviewer subagent reviewing **pinder-core PR #914** (Issue #903 — opponent defense snapshot on TurnStart).

You are a **no-context fresh-eye reviewer**. Do not read prior conversation, prior subagent transcripts, or commit summaries. Read the PR diff cold and judge it on its own merits.

## PR

- URL: https://github.com/decay256/pinder-core/pull/914
- Branch: `fix/903-opponent-defense-snapshot`
- Base: `main`
- Worktree (read-only for review): `/tmp/work-903` (the implementer's worktree; do NOT mutate)
- Files: 7 (1 new record, 1 GameSession edit, 1 TurnStart edit, 1 session-runner snapshot edit, 1 session-runner Program edit, 1 test file, 1 spec doc) — +520/-4

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/code-reviewer.md` for the Review Checklist + Output Format. Follow it.
2. Read `/root/projects/pinder-core/AGENTS.md` — pay attention to **Snapshot Schema Discipline** (any new player-visible field on `GameSession`/`TurnStart` MUST also be on `TurnSnapshot`).
3. Read the ticket: `gh issue view 903 --repo decay256/pinder-core`.
4. Fetch the PR diff: `cd /root/projects/pinder-core && git fetch origin && git diff origin/main..origin/fix/903-opponent-defense-snapshot` (or read each file in `/tmp/work-903` directly).
5. Read the recently-merged comparable PR #913 (#905 — ghost_probability_per_turn) as the template the implementer was instructed to follow: `gh pr view 913 --repo decay256/pinder-core --json title,body,files`. Use it to spot-check that the pattern was actually mirrored.

## Specific verification points (must check each)

1. **`OpponentDefenseSnapshot` + `OpponentDefenseEntry` record shapes** match the ticket's exact prescription.
2. **`StartTurnAsync` populates 6 entries**, one per `StatType`, mapped via `StatBlock.DefenceTable[attackerStat]`.
3. **`EffectiveModifier` actually reflects effective state** — shadow, traps. NOT just `_opponent.Stats.Get(defenderStat)` masquerading as effective.
4. **`BaseModifier` is the raw stat value**, NOT the post-effective value.
5. **Wire serialization**: `opponent_defense_snapshot`, with nested `by_attacker_stat`, `defending_stat`, `effective_modifier`, `base_modifier` (all snake_case).
6. **`TurnSnapshot` (session-runner) parity** per Snapshot Schema Discipline — the new field IS in `session-runner/Snapshot/SessionSnapshot.cs` AND is populated by `BuildTurnSnapshot` in `session-runner/Program.cs`. The comment block at top of `SessionSnapshot.cs` lists `#903`.
7. **Casing convention on session-runner side**: PR #913 (#905) deliberately used PascalCase on the TurnSnapshot side while keeping snake_case on the wire — verify #914 follows the same convention. (Mismatch is a non-blocker; consistency with the recent precedent is the bar.)
8. **No drive-by changes**. Files touched should be exactly: the new record file, GameSession.cs (StartTurnAsync only), TurnStart.cs (added param), SessionSnapshot.cs, Program.cs (BuildTurnSnapshot call site only), the new test file, the new spec doc. NOTHING ELSE. Reject any drive-by edits to other files, version bumps, `using` reorderings outside the touched method, etc.
9. **No mutation of `StatBlock.DefenceTable` or `_opponent.GetEffective` semantics**. The ticket explicitly forbids this.
10. **No submodule pointer bump** (this PR is core-only; no companion repo needed).
11. **Tests are real, not tautological**:
    - 6-entry assertion present.
    - DefenceTable mapping verified per entry.
    - Shadow-reflected: test constructs a real shadow on a defender stat, asserts the matching attacker-row's `EffectiveModifier` differs from `BaseModifier`.
    - Trap-reflected: test constructs a real `OpponentDCIncrease` trap, asserts the relevant attacker-row's `EffectiveModifier` is higher than `BaseModifier`.
    - Serialization test asserts the actual JSON contains the snake_case keys (not just that the record serializes).
12. **Default-trailing-param pattern** for `TurnStart` addition matches #905's precedent. Adding a non-default param to a positional record would break call sites.
13. **PR body** contains `Closes #903` on its own line (GitHub auto-close requires one-per-line).

## Run the tests yourself

```bash
cd /root/projects/pinder-core
git fetch origin
git checkout origin/fix/903-opponent-defense-snapshot --detach 2>&1
git submodule update --init --recursive 2>&1
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/review-914-tests.txt 2>&1
tail -10 /tmp/review-914-tests.txt
```

Pinder.Core.Tests must be green. (`Pinder.LlmAdapters.Tests` has 72 pre-existing failures per #909 — ignore those; don't run that project.)

Also confirm `dotnet build src/Pinder.Core/Pinder.Core.csproj` and the session-runner project build cleanly.

## Verdict

Per SELF-APPROVE-BLOCKED, you cannot use `gh pr review --approve` from a swarm-bot token that matches the PR author. Use:

```bash
gh pr review 914 --repo decay256/pinder-core --comment --body "$(cat <<'EOF'
**Verdict: APPROVE** (or **Verdict: CHANGES_REQUESTED**)

<your structured review>
EOF
)"
```

The reviewer-spec output format is the source of truth for the comment body structure.

## Authority

This is the FIRST-PASS review. The orchestrator decides next steps based on your verdict.

- APPROVE, no blockers → orchestrator merges.
- APPROVE with non-blocking findings → orchestrator files them as follow-up tickets, then merges.
- CHANGES_REQUESTED → orchestrator spawns a fix subagent, then a second-pass reviewer.

## Logging

```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#903" "core/snapshot" "started" "Reviewing PR #914 (first pass)"
```

At exit:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#903" "core/snapshot" "completed" "Verdict: <APPROVE|CHANGES_REQUESTED>, blockers: <count>, follow-ups: <count>"
```

## Output

Your final response is the same body you posted to the PR. Keep it structured per the code-reviewer.md output format.
