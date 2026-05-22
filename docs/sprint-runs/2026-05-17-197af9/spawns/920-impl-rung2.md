You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **core#920** in pinder-core: synthesise a `RollCheckResult` in `GameSession.CreateForcedFailResult` (and any other null-passing site) so `RollResult.Check` is never null at construction time.

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-920 origin/main
cd /tmp/work-920
git checkout -b chore/920-rollresult-check-non-null
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on #901 / #903 / #918 lessons.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 920 --repo decay256/pinder-core --json number,title,body,comments`.
5. `gh pr view 918 --repo decay256/pinder-core --json title,body` (origin context — Phase 1 RollEngine unification).

## Scope decision (orchestrator's choice — implementer should NOT relitigate)

The ticket lists two options:
- (1) Change `RollResult.Check` to nullable.
- (2) Synthesise a `RollCheckResult` in every null-passing site so `Check` is never null.

**This PR does option (2).** Per the ticket's own recommendation — preserves the eventual goal of bespoke-field deletion in Phase 3.

## Diagnosis

```bash
cd /tmp/work-920
grep -rn "RollResult.Check\|check!\|RollCheckResult? = null" src 2>&1 | head -30
grep -rn "CreateForcedFailResult" src 2>&1
grep -rn "new RollResult(" src tests 2>&1 | head -30
# Find all sites that pass null for the check parameter
```

Specifically find:
1. `GameSession.CreateForcedFailResult` — the main production null-passing site.
2. The ~12 test constructors named in the ticket — these are in `tests/Pinder.Core.Tests/`.

## Implementation

### Phase A — synthesise helper

Add a small helper, ideally on `RollCheckResult` itself or in `GameSession` private scope, that builds a forced-fail `RollCheckResult` from the bespoke fields a forced-fail `RollResult` carries. The shape should be whatever the wire serializer (slated for Phase 2) will need:
- `Verdict = Miss` (or whatever the forced-fail equivalent is — read existing `RollCheckResult` enum).
- `Tier` = the forced fail tier (mirror what the bespoke `RollResult.Tier` field would have been).
- Stat / die / total / DC fields filled from the same bespoke fields.

Name it `RollCheckResult.ForForcedFail(...)` or similar; one factory method, no logic duplication.

### Phase B — replace null call sites

Update every `new RollResult(..., check: null, ...)` (or positional null) to pass the synthesised value. Concretely:
- `GameSession.CreateForcedFailResult` — primary site.
- Each null-passing test constructor — update to the helper.

### Phase C — tighten the constructor signature

Once all sites pass non-null, change `RollResult`'s constructor parameter from `RollCheckResult? = null` to `RollCheckResult` (required). Drop the `check!` null-suppression. The `Check` property stays non-nullable.

### Phase D — tests

Add an explicit test that asserts `CreateForcedFailResult` returns a `RollResult` whose `Check` is non-null AND whose fields agree with the bespoke fields (Stat, Tier, etc.). This guards Phase 2.

Run:
```bash
cd /tmp/work-920
dotnet build pinder-core.sln 2>&1 | tail -10
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -20
```

## DoD evidence + Research Log

PR body MUST include:

```markdown
Closes #920

## DoD Evidence
- [ ] `RollCheckResult.ForForcedFail` (or equivalent factory) added
- [ ] `GameSession.CreateForcedFailResult` uses the factory; passes non-null `check`
- [ ] All ~12 null-passing test constructors updated
- [ ] `RollResult` constructor signature tightened — no `RollCheckResult?`, no `check!`
- [ ] New regression test: `CreateForcedFailResult().Check` is non-null and consistent with bespoke fields
- [ ] `dotnet build`: clean
- [ ] `dotnet test Pinder.Core.Tests`: <N/N pass>

## Research Log
<2 paragraphs: where the null-passing sites lived, what the synthesised shape was (which RollCheckResult fields mirror which bespoke RollResult fields), why option 2 keeps Phase 3 simpler than option 1>
```

## Open PR

```bash
gh pr create --repo decay256/pinder-core --base main --head chore/920-rollresult-check-non-null \
  --title "chore(#920): synthesise RollCheckResult in CreateForcedFailResult; tighten RollResult.Check to non-null" \
  --body "<DoD evidence + Research Log per template>

Closes #920"
```

Report back with the PR URL + commit SHA.

## DO-NOT list
- Do NOT touch `/root/projects/pinder-core` directly. Use the worktree.
- Do NOT merge the PR yourself.
- Do NOT push to `main`.
- Do NOT include unrelated edits (the #649 reviewer caught 2 of these and forced a revert — don't repeat).
- Do NOT change the wire-JSON shape (Phase 2 hasn't happened yet — wire DTOs still serialize from bespoke fields in pinder-web).
- Do NOT delete bespoke `RollResult` fields (Stat / Tier / etc.) — that's Phase 3 cleanup, not this PR.

All upstream events follow USER.md response-style — short, lead with result, no tables.

When done, your final report goes back to the orchestrator. I will handle review/merge.
