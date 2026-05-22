You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **core#925** in pinder-core: rename `TurnSnapshot.DefendingRollStat` → `DefendingStat` to match the wire field name (`defending_stat`).

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-925 origin/main
cd /tmp/work-925
git checkout -b chore/925-turnsnapshot-defendingstat-rename
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` (top of file + any #906 / #903 lessons).
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 925 --repo decay256/pinder-core --json number,title,body,comments`.

## Scope decision (orchestrator's choice — implementer should NOT relitigate)

The ticket lists two options:
- (1) Rename `TurnSnapshot.DefendingRollStat` → `DefendingStat` to match the wire field name.
- (2) Leave as-is.

**This PR does option (1).** That's the ticket's own recommendation. Mechanical rename.

## Diagnosis

```bash
cd /tmp/work-925
grep -rn "DefendingRollStat" src tests data 2>&1
grep -rn "TurnDefenseEntry.DefendingStat" src tests 2>&1
# Confirm no collision with TurnDefenseEntry.DefendingStat — they live on different classes per the ticket
```

## Implementation

1. Rename the property in `src/Pinder.Core/.../TurnSnapshot.cs` (path: find via grep above).
2. Update all call sites discovered in the grep.
3. If a `[JsonPropertyName("defending_stat")]` or similar attribute exists on the field, keep it pointing at `"defending_stat"` (wire shape stays identical).
4. Update tests / fixtures.

## Tests

```bash
cd /tmp/work-925
dotnet build pinder-core.sln 2>&1 | tail -10
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -20
```

## DoD evidence + Research Log

PR body MUST include:

```markdown
Closes #925

## DoD Evidence
- [ ] `TurnSnapshot.DefendingRollStat` renamed to `DefendingStat`
- [ ] Wire JSON shape unchanged (`defending_stat`)
- [ ] No collision with `TurnDefenseEntry.DefendingStat` (different class)
- [ ] All call sites updated
- [ ] `dotnet build`: clean
- [ ] `dotnet test Pinder.Core.Tests`: <N/N pass>

## Research Log
<1 paragraph: where the rename was, what call sites existed, what wire-attribute (if any) survived>
```

## Open PR

```bash
gh pr create --repo decay256/pinder-core --base main --head chore/925-turnsnapshot-defendingstat-rename \
  --title "chore(#925): rename TurnSnapshot.DefendingRollStat -> DefendingStat" \
  --body "<DoD evidence + Research Log per template>

Closes #925"
```

Report back with the PR URL + commit SHA.

## DO-NOT list
- Do NOT touch `/root/projects/pinder-core` directly. Use the worktree.
- Do NOT merge the PR yourself.
- Do NOT push to `main`.
- Do NOT include unrelated edits.
- Do NOT touch `TurnDefenseEntry.DefendingStat` — that's a different class, ticket explicitly says no collision.
- Do NOT change the wire-JSON shape — `defending_stat` stays.

All upstream events follow USER.md response-style — short, lead with result, no tables.

When done, your final report goes back to the orchestrator. I will handle review/merge.
