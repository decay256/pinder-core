You are a backend engineer subagent in the Pinder dev swarm. Implement ticket **#943** as **two PRs** — pinder-core first (engine emits `Success` tier on success), then pinder-web (submodule bump + wire DTO emits tier on every roll + SPA exhaustiveness handler for Success).

## Workspace isolation
```bash
# Core worktree
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-943-core origin/main
cd /tmp/work-943-core
git checkout -b fix/943-roll-tier-success-value

# Web worktree (start after core PR merges)
cd /root/projects/pinder-web
git fetch origin
git worktree add /tmp/work-943-web origin/main
cd /tmp/work-943-web
git checkout -b fix/943-roll-tier-wire-on-success
```

## Cold-start
1. Read `/root/projects/eigentakt/agents/backend-engineer.md`.
2. Read `/root/projects/pinder-core/LESSONS_LEARNED.md` — focus on REGRESSION-TESTS-ON-BUGS, BUILD-PIPELINE-DISCIPLINE, EIGENTAKT-IMPLEMENTER-EXPLICIT-PATHSPECS, EIGENTAKT-DEAD-CODE-NEEDS-CROSS-REPO-GREP.
3. Read `/root/projects/pinder-core/AGENTS.md` AND `/root/projects/pinder-web/AGENTS.md`.
4. `gh issue view 943 --repo decay256/pinder-core --json number,title,body,comments`.

## Strategy: Option A from the ticket

Per the ticket: "every roll has a tier; success is just a particular tier value." Add `FailureTier.Success` (or similar — match whatever the existing enum is called) and emit it when `IsSuccess == true`. SPA handles it as a discrete case.

Three pieces:

### Piece 1 — pinder-core

Files (verify in worktree):
- `src/Pinder.Core/Conversation/RollResult.cs` — find the existing `FailureTier` enum or whatever type carries `TropeTrap`. Add a `Success` value. Verify case ordering — most enum-discriminating switches in pinder-core use exhaustive `switch`es; new value forces a compile error there until handled.
- `src/Pinder.Core/Conversation/RollCheckResult.cs` — same field, same fix.
- Whichever method computes the tier (look for "TropeTrap" and `FailureTier` assignment sites) — emit `FailureTier.Success` when the roll succeeded instead of leaving `Tier = default` / null / absent.

Tests: `tests/Pinder.Core.Tests/Issue943_RollTierOnSuccessTests.cs` (new). Cases:
- Successful roll → `result.Tier == FailureTier.Success` (or whatever the enum name turns out to be).
- Failed roll (existing behavior) → `result.Tier == FailureTier.TropeTrap` (or whatever the original tier was).
- Snapshot/JSON test: serialize and assert key present on both.

### Piece 2 — pinder-web (after core merges)

- Submodule bump to the core merge sha.
- `src/Pinder.GameApi/Models/TurnDtos.cs` — verify the wire DTO emits `tier` on success. May need `[JsonIgnore(Condition=Never)]` similar to #944.
- Mapper update if applicable.
- `frontend/src/components/FailureTierDisplay.tsx` — add a `case "Success":` branch that returns the success label (or null, or whatever the existing success rendering uses).
- `frontend/src/utils/deriveCollapsedHeader.ts` (or wherever) — same `case "Success":` addition.

Frontend test:
- Vitest case in `FailureTierDisplay.test.tsx`: passes `tier: "Success"` and asserts the success-label renders without hitting the exhaustiveness default.

### Piece 3 — co-located docs/comments

Update any XMLDoc on the `FailureTier` enum to describe `Success` as the "no-failure" tier.

## Pathspec discipline + co-located test mirrors

Explicit pathspecs only. Co-located mirror tests (`.test.tsx` next to `.tsx`) are in scope per EIGENTAKT-COLOCATED-MIRROR-TESTS-IN-SCOPE.

## Acceptance criteria (from ticket)

- [ ] `RollResult` / `RollCheckResult` carries `Tier` even when `IsSuccess=true`.
- [ ] Wire DTO emits `tier` on every roll, regardless of success/failure.
- [ ] Snapshot test: serialize a successful `RollCheckResult` → assert `tier` key present and equal to the success value.
- [ ] SPA `FailureTierDisplay.Label` handles `tier === "Success"` (or the chosen variant name) without hitting the exhaustiveness guard's default.

## Workflow rules
- Open core PR first. STOP and report DoD evidence after core PR is opened. The orchestrator will merge and then dispatch a separate run for the web PR.
- For THIS run: only do the core PR. Do NOT start web work.

## Build evidence
```bash
cd /tmp/work-943-core
dotnet build -c Release 2>&1 | tail -5    # 0 errors required
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --filter "FullyQualifiedName~Issue943|FullyQualifiedName~RollResult|FullyQualifiedName~RollCheck" --no-build 2>&1 | tail -10
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-build 2>&1 | tail -5
```

## Commit + PR (core)

Commit message: `fix(#943): emit FailureTier.Success on successful rolls (engine + wire)`.

PR via:
```bash
gh pr create --repo decay256/pinder-core --base main --head fix/943-roll-tier-success-value \
  --title "fix(#943): emit FailureTier.Success on successful rolls" \
  --fill
```

Body must include `Partially addresses #943 (engine side; web PR follows)`, DoD evidence (build tail, test tail, JSON before/after for a successful roll).

## DO NOT
- Do not merge.
- Do not work in `/root/projects/pinder-core/` — only in `/tmp/work-943-core/`.
- Do not start the web PR in this run. The orchestrator will spawn a follow-up after core merges.
- Do not break existing exhaustiveness switches without adding the new case (this is the whole point of adding an enum value — find every switch and add the `Success` case).

## Logging
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#943" "roll-tier-success" "started" "Adding FailureTier.Success enum value + emitting on success rolls"
```
After PR:
```bash
bash /root/projects/eigentakt/scripts/log.sh backend-engineer "#943" "roll-tier-success-core-opened" "completed" "core PR #N opened, awaiting orchestrator merge before web PR" "<sha>"
```

## Output requirements

End with:
- `## Diagnostic findings` — current state of FailureTier enum + tier emission code, line citations.
- `## Implementation summary` — enum value added, emission sites, exhaustive switch sites updated.
- `## DoD Evidence` — PR URL, build tail, test tail, JSON sample showing tier:"Success" on a successful roll, agent.log entries.
- `## Research Log` — what you read, what you found.
- `## Filed follow-ups` — none expected.
