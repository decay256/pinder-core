You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **core#924** in pinder-core: apply `[JsonConverter(typeof(JsonStringEnumConverter))]` + `[JsonPropertyName]` consistently across `RollResult`'s enum properties so direct serialization produces a consistent snake_case/string shape.

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-924 origin/main
cd /tmp/work-924
git checkout -b chore/924-rollresult-enum-serialization-consistency
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on #906 / #903 / wire-DTO lessons.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 924 --repo decay256/pinder-core --json number,title,body,comments`.

## Scope decision (orchestrator's choice — implementer should NOT relitigate)

The ticket lists two options:
- (1) Drop `[JsonConverter]` from `DefendingStat` to match the rest.
- (2) Apply `[JsonConverter]` + `[JsonPropertyName]` consistently to all enum properties.

**This PR does option (2).** Per the ticket's own recommendation — moves toward consistent self-serializability and dovetails with the recently-merged #920 (Phase-2 prep).

## Diagnosis

```bash
cd /tmp/work-924
grep -n "JsonConverter\|JsonPropertyName\|public.*Stat\|public.*Tier\|public.*RiskTier" src/Pinder.Core/Rolls/RollResult.cs 2>&1 | head -30
# Find every enum property on RollResult
# Check what snake_case name the wire DTO (RollResultDto.From in pinder-web) uses for each field
```

The wire DTO (in pinder-web's `Pinder.GameApi/Dto`) is the canonical naming reference for snake_case. Match what `RollResultDto.From()` emits per field.

## Implementation

For each enum property on `RollResult` that lacks the converter:
1. Add `[JsonConverter(typeof(JsonStringEnumConverter))]`.
2. Add `[JsonPropertyName("snake_case_name")]` matching what the wire DTO uses.

Specifically (per the ticket):
- `Stat` → likely `[JsonPropertyName("stat")]`.
- `Tier` → likely `[JsonPropertyName("tier")]`.
- `RiskTier` → likely `[JsonPropertyName("risk_tier")]`.
- `DefendingStat` already has the converter; verify it has the right `[JsonPropertyName("defending_stat")]` too.

Add a test in `tests/Pinder.Core.Tests/Rolls/` that asserts direct `JsonSerializer.Serialize(rollResult)` produces a fully snake_case, fully string-enum, internally-consistent JSON shape — pin field-by-field.

## Tests

```bash
cd /tmp/work-924
dotnet build pinder-core.sln 2>&1 | tail -10
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -20
```

**Watch for the wire DTO pin:** if any pinder-web test snapshot fixture (in `pinder-core/data/fixtures/` or pinder-web side) compares against a direct-serialization shape, you'll see new failures — those are pinning the OLD inconsistent shape and should be updated. Most fixtures pin the `RollResultDto.From()` shape which is unaffected.

## DoD evidence + Research Log

PR body MUST include:

```markdown
Closes #924

## DoD Evidence
- [ ] `[JsonConverter(typeof(JsonStringEnumConverter))]` on every enum prop of `RollResult` (`Stat`, `Tier`, `RiskTier`, `DefendingStat`)
- [ ] `[JsonPropertyName("snake_case")]` on every prop to match the wire DTO naming
- [ ] New regression test: direct `JsonSerializer.Serialize(RollResult)` produces a fully consistent snake_case + string-enum shape
- [ ] `dotnet build`: clean
- [ ] `dotnet test Pinder.Core.Tests`: <N/N pass>

## Research Log
<1 paragraph: which props lacked the attributes, what snake_case names the wire DTO uses for them, why option (2) instead of dropping the attribute from DefendingStat>
```

## Open PR

```bash
gh pr create --repo decay256/pinder-core --base main --head chore/924-rollresult-enum-serialization-consistency \
  --title "chore(#924): consistent JsonStringEnumConverter + JsonPropertyName on RollResult enums" \
  --body "<DoD evidence + Research Log per template>

Closes #924"
```

Report back with the PR URL + commit SHA.

## DO-NOT list
- Do NOT touch `/root/projects/pinder-core` directly. Use the worktree.
- **Do NOT merge the PR yourself.** (The #949 impl violated this — orchestrator logged the breach. Don't repeat it.)
- Do NOT push to `main`.
- Do NOT include unrelated edits.
- Do NOT change the wire-DTO behavior (`RollResultDto.From()` in pinder-web). That path is unchanged by this PR.
- Do NOT touch other classes' enum serialization (e.g. `ShadowCheckResult`, `HorninessCheckResult`) — out of scope, separate ticket if needed.

All upstream events follow USER.md response-style — short, lead with result, no tables.

When done, your final report goes back to the orchestrator. I will handle review/merge.
