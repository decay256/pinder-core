You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **core#927** in pinder-core: surface `FinalVerdict` + `FinalTier` on `RollCheckResult` so the post-shadow-corruption verdict is a single source of truth (engine-side), eliminating the three-place derivation in frontend / replay tool / simulator.

## Workspace isolation
```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-927 origin/main
cd /tmp/work-927
git checkout -b chore/927-rollcheckresult-final-verdict-tier
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on #598 / #901 / #920 / shadow-corruption lessons.
3. Read `/root/projects/pinder-core/AGENTS.md`.
4. `gh issue view 927 --repo decay256/pinder-core --json number,title,body,comments`.
5. Read pinder-web's collapsed event-box contract (sprint origin context):
   ```bash
   grep -rn "shadow_check.is_miss\|shadow_check.overlay_applied\|FinalVerdict\|FinalTier" pinder-web/src 2>&1 | head -20
   ```

## Scope decision (orchestrator's choice — implementer should NOT relitigate)

The ticket proposes adding two fields to `RollCheckResult` (NOT `RollResult` — keep that distinction tight):
- `FinalVerdict: enum { SUCCESS, MISS }` — post-shadow-corruption verdict.
- `FinalTier: FailureTier` — post-demotion tier (`None` if no demotion).

Existing `IsSuccess` / `Tier` keep pre-demotion semantics for back-compat. New fields are computed at the shadow-corruption resolution point in `GameSession`.

**Scope is engine-side only.** Frontend / wire-DTO consumption is a separate follow-up ticket (this PR just makes the fields exist + populated correctly).

## Diagnosis

```bash
cd /tmp/work-927
# Find RollCheckResult class
find src -name "RollCheckResult.cs" 2>&1
# Find the shadow-corruption block in GameSession
grep -n "shadow.*corrupt\|shadow.*overlay\|ApplyShadow\|ShadowCheckResult" src/Pinder.Core/Conversation/GameSession.cs 2>&1 | head -20
# Find the FailureTier enum + None sentinel (if it exists)
grep -rn "enum FailureTier\|FailureTier\." src/Pinder.Core/Rolls 2>&1 | head -10
```

## Implementation

### Phase A — add fields to RollCheckResult

In `RollCheckResult.cs`:
- Add `FinalVerdict` enum (or reuse `RollVerdict` if it exists): `{ SUCCESS, MISS }`.
- Add `FinalTier: FailureTier` (or `FailureTier?` if `None` doesn't exist as a sentinel — prefer the sentinel for serializability).
- Make both required in the constructor or default them sensibly (FinalVerdict defaults to the pre-shadow IsSuccess→ SUCCESS/MISS; FinalTier defaults to the pre-shadow Tier or `None`).
- Add `[JsonConverter(typeof(JsonStringEnumConverter))]` + `[JsonPropertyName("final_verdict")]` / `final_tier` for direct-serialization consistency (matches the #924 pattern just merged).

### Phase B — populate at the resolution point

In `GameSession.cs`'s shadow-corruption block (where `ShadowCheckResult` is applied and overlay decision is made):
- When `shadow_check.IsMiss && shadow_check.OverlayApplied && roll.IsSuccess` (shadow-demotion case): set `FinalVerdict = MISS` and `FinalTier = shadow_check.Tier`.
- All other cases: `FinalVerdict` = (IsSuccess ? SUCCESS : MISS), `FinalTier` = pre-shadow Tier (or `None` for successes).

Mirror the same population in the simulator (`session-runner/Simulator/*.cs` if there's a parallel resolution path) and any replay-tool path that constructs `RollCheckResult`.

### Phase C — tests

Add tests in `tests/Pinder.Core.Tests/Rolls/`:
1. **Plain success** — `FinalVerdict=SUCCESS`, `FinalTier=None`.
2. **Plain miss** — `FinalVerdict=MISS`, `FinalTier=<original-tier>`.
3. **Nat 20 → Catastrophe demotion** (shadow-corruption case) — `FinalVerdict=MISS`, `FinalTier=Catastrophe`, but pre-demotion `IsSuccess=true` and `Tier=None` remain unchanged.
4. **Shadow miss without overlay applied** — overlay didn't trigger, so `FinalVerdict` = original verdict.
5. **Multiple shadow-check passes** — only the final overlay decision flows into `FinalVerdict`/`FinalTier`.

Plus update any existing shadow-corruption test that asserted the workaround derivation; it should now directly assert `FinalVerdict` / `FinalTier`.

Run:
```bash
cd /tmp/work-927
dotnet build pinder-core.sln 2>&1 | tail -10
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj 2>&1 | tail -20
```

## DoD evidence + Research Log

PR body MUST include:

```markdown
Closes #927

## DoD Evidence
- [ ] `RollCheckResult.FinalVerdict` (enum) + `FinalTier` (FailureTier) added
- [ ] Direct-serialization attributes: `[JsonConverter(JsonStringEnumConverter)]` + `[JsonPropertyName]` snake_case
- [ ] `GameSession` shadow-corruption block populates the fields correctly
- [ ] Simulator parallel resolution path mirrors the population (if exists)
- [ ] 5+ new tests covering plain success / plain miss / shadow demotion / shadow-no-overlay / multi-shadow-pass
- [ ] Pre-existing `IsSuccess` / `Tier` semantics unchanged (back-compat)
- [ ] `dotnet build`: clean
- [ ] `dotnet test Pinder.Core.Tests`: <N/N pass>

## Research Log
<2 paragraphs: where the derivation was duplicated (frontend / replay / simulator paths cited), how the engine-side single source of truth is now defined, what the back-compat contract on IsSuccess / Tier is, what's deferred to a follow-up (frontend / wire-DTO consumption)>
```

## Open PR

```bash
gh pr create --repo decay256/pinder-core --base main --head chore/927-rollcheckresult-final-verdict-tier \
  --title "chore(#927): RollCheckResult.FinalVerdict + FinalTier — engine-side single source of truth for post-shadow-corruption" \
  --body "<DoD evidence + Research Log per template>

Closes #927"
```

Report back with the PR URL + commit SHA. Mention if the frontend / wire-DTO consumption follow-up should be filed as a separate ticket (it should — file it).

## DO-NOT list
- Do NOT touch `/root/projects/pinder-core` directly. Use the worktree.
- **Do NOT merge the PR yourself.** (The #949 impl violated this — orchestrator logged the breach. Don't repeat it.)
- Do NOT push to `main`.
- Do NOT include unrelated edits.
- Do NOT change the existing `IsSuccess` / `Tier` semantics on `RollResult` or `RollCheckResult` — back-compat is load-bearing.
- Do NOT touch pinder-web (frontend consumption is a separate follow-up).
- Do NOT change wire-DTO shape (the existing snake_case fields stay; new ones get their own snake_case names).

All upstream events follow USER.md response-style — short, lead with result, no tables.

When done, your final report goes back to the orchestrator. I will handle review/merge.
