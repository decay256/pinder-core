You are a backend engineer subagent implementing **pinder-core #901** in one PR. This is the foundational refactor of the sprint — collapse four dice-check engines into one.

## Critical invariant: ADDITIVE phase only

This ticket explicitly stages the work into 3 phases. **Implement ONLY Phase 1 (Additive) in this PR.** Phase 2 (wire DTO serialization) and Phase 3 (delete duplicated fields, bump schema) are separate tickets and out of scope here.

**What "additive" means concretely:**
- All wire DTOs unchanged (no field renames, no field removals on `RollResult` / `HorninessCheckResult` / `SteeringRollResult` / `ShadowCheckResult`).
- Each existing per-check result wrapper gains a `Check: RollCheckResult` property in addition to its existing fields.
- Existing bespoke fields (`DieRoll`, `Modifier`, `Total`, `Dc`, `IsSuccess`, `Tier`) stay populated identically.
- **`session-runner` golden snapshots MUST be byte-identical after this PR.** This is the load-bearing acceptance criterion.

## Ticket summary

Read the full issue first: `gh issue view 901 --repo decay256/pinder-core`. The key deliverables:

1. **`RollEngine.ResolveCheck(RollCheckKind, IDiceRoller, IReadOnlyList<NamedModifier>, dc, hasAdv, hasDis)`** — single engine entry point.
2. **`RollCheckResult`** — canonical check result record (per the ticket's exact shape).
3. **`RollCheckKind`** enum — `OptionRoll | Steering | Horniness | Shadow | ShadowGrowth`.
4. **`NamedModifier(string Key, int Value)`** record struct.
5. **`FailureTierLadder.FromMissMargin(int)`** — sole tier-ladder source of truth.
6. **`HorninessEngine.DetermineHorninessTier` deleted**; call sites switch to `FailureTierLadder.FromMissMargin`.
7. **`RollEngine.ResolveFromComponents`** internal ladder (lines 188-200 in `src/Pinder.Core/Rolls/RollEngine.cs`) replaced by `FailureTierLadder.FromMissMargin`.
8. **Inline shadow check** at `src/Pinder.Core/Conversation/GameSession.cs:~1596` extracted into a new `ShadowCheckEngine` class.
9. **Each per-check wrapper gains a `Check: RollCheckResult` property** (`RollResult`, `HorninessCheckResult`, `SteeringRollResult`, `ShadowCheckResult`, and whatever wraps shadow-growth today).
10. **Internally, each of the four engines uses `RollEngine.ResolveCheck`.** No d20-rolling code outside that one entry point.
11. **Audit test:** searches the codebase for hand-rolled `missMargin <= 2` ladders — must find ZERO outside `FailureTierLadder.FromMissMargin`.

## Workspace

```bash
cd /root/projects/pinder-core
git fetch origin
git worktree add /tmp/work-901 origin/main
cd /tmp/work-901
git checkout -b fix/901-rollengine-unification
```

**Work in `/tmp/work-901/` only.** All commands below assume `cd /tmp/work-901`.

## Cold-start reading order

1. `/root/.openclaw/skills/eigentakt/agents/backend-engineer.md` — your role spec.
2. `/root/projects/pinder-core/AGENTS.md` — pay attention to **Snapshot Schema Discipline**. This refactor does NOT add player-visible fields, but the `Check` property surfacing on result wrappers IS a TurnSnapshot-relevant change. Verify whether existing snapshots already include the bespoke fields and whether adding `Check` requires a `TurnSnapshot` mirror. Default answer: **NO mirror this phase** (Phase 2 wires it). But confirm by reading the snapshot file and the AGENTS.md rule.
3. The ticket (`gh issue view 901`).
4. `src/Pinder.Core/Rolls/RollEngine.cs` — full file. Especially lines 161-200 (existing tier ladder).
5. `src/Pinder.Core/Conversation/HorninessEngine.cs` — full file. Lines 77 + 90 (duplicate ladder + the cross-call site from GameSession).
6. `src/Pinder.Core/Conversation/SteeringEngine.cs` — full file.
7. `src/Pinder.Core/Conversation/GameSession.cs` around line 1596 — the inline shadow check that cross-calls `HorninessEngine.DetermineHorninessTier`. Also find the shadow-growth-per-stat-failure inline check (search for "shadow" within `GameSession.cs`).
8. `src/Pinder.Core/Rolls/RollResult.cs`, `Pinder.Core/Conversation/HorninessCheckResult.cs`, `Pinder.Core/Conversation/SteeringRollResult.cs`, `Pinder.Core/Conversation/ShadowCheckResult.cs` — current per-check shapes.
9. `src/Pinder.Core/Rolls/FailureScale.cs` — confirms `FailureTier` enum.
10. `session-runner/Snapshot/SessionSnapshot.cs` + `session-runner/Program.cs` — confirm what the golden snapshots capture today. Run `find session-runner -name '*.snap.json' | head -5` to see snapshot format.

## Implementation plan

### Step 1 — new primitives (no callers yet)

Create:
- `src/Pinder.Core/Rolls/RollCheckKind.cs` — enum with 5 values per ticket.
- `src/Pinder.Core/Rolls/NamedModifier.cs` — `public readonly record struct NamedModifier(string Key, int Value);`.
- `src/Pinder.Core/Rolls/RollCheckResult.cs` — record per ticket's exact shape. All properties as ticket prescribes.
- `src/Pinder.Core/Rolls/FailureTierLadder.cs`:
  ```csharp
  public static class FailureTierLadder
  {
      public static FailureTier FromMissMargin(int missMargin)
      {
          if (missMargin <= 0) return FailureTier.None;
          if (missMargin <= 2) return FailureTier.Fumble;
          if (missMargin <= 5) return FailureTier.Misfire;
          if (missMargin <= 9) return FailureTier.TropeTrap;
          return FailureTier.Catastrophe;
      }
  }
  ```
  Verify thresholds against current code BEFORE writing. If existing code has a `Legendary` branch on success-side, that's NOT failure-tier ladder — it's the nat-20-margin promotion. Leave that in `RollEngine.ResolveFromComponents`; `FailureTierLadder` is failure-side only.

**Commit:** `feat(#901): introduce RollCheckResult, RollCheckKind, NamedModifier, FailureTierLadder`.

### Step 2 — `RollEngine.ResolveCheck`

Add the public static method on `RollEngine`. It handles:
- Roll d20 via `IDiceRoller.Roll()`.
- Advantage/disadvantage: roll twice, pick higher (adv) or lower (dis), record both rolls.
- Sum modifier bag → `ModifierSum`.
- `Total = UsedDieRoll + ModifierSum`.
- `MissMargin = max(0, Dc - Total)` (success → 0).
- `IsSuccess = Total >= Dc` (also `DieRoll == 20` if your code treats nat20 as auto-success — check existing semantics).
- `IsNatOne / IsNatTwenty` — check the **used** die roll, NOT both rolls (existing convention; verify).
- `Tier = IsSuccess ? FailureTier.None : FailureTierLadder.FromMissMargin(MissMargin)`.
- Construct + return `RollCheckResult`.

This must reproduce the exact semantics of the existing per-engine d20 rolls. Read each existing implementation and confirm:
- Does `SteeringEngine` use a separate `_steeringDice` `IDiceRoller`? (Yes — preserve dependency injection.)
- Does `HorninessEngine` use `_steeringDice` too? (Check.)
- Nat-1 / nat-20 — does any engine currently fast-path? Preserve.

**Commit:** `feat(#901): add RollEngine.ResolveCheck single entry point`.

### Step 3 — route `RollEngine.Resolve` / `ResolveFromComponents` through `ResolveCheck` + `FailureTierLadder`

Replace the inline ladder at `RollEngine.cs:188-200` with `tier = FailureTierLadder.FromMissMargin(miss);`. Existing `RollResult` is constructed exactly as before — its bespoke fields stay; ADD a `Check: RollCheckResult` property holding the new shape.

For `ResolveFromComponents`: build a `RollCheckResult` from the same intermediate state, attach to `RollResult`.

**Commit:** `refactor(#901): route RollEngine.Resolve through FailureTierLadder + attach RollCheckResult to RollResult`.

### Step 4 — `HorninessEngine.CheckAsync` routes through `RollEngine.ResolveCheck`

- Replace its bespoke d20 roll with `RollEngine.ResolveCheck(RollCheckKind.Horniness, _steeringDice, Array.Empty<NamedModifier>(), dc: RollEngine.ApplyDcBias(sessionHorniness, _horninessDcBias))`.
- Delete `HorninessEngine.DetermineHorninessTier` (lines ~90+).
- `HorninessCheckResult` gains `Check: RollCheckResult`. Existing fields stay populated from `check.*`.
- All `HorninessEngineTests` must pass unchanged.

**Commit:** `refactor(#901): HorninessEngine.CheckAsync routes through RollEngine.ResolveCheck`.

### Step 5 — `SteeringEngine` routes through `RollEngine.ResolveCheck`

Same pattern. `SteeringRollResult` gains `Check: RollCheckResult`.

**Commit:** `refactor(#901): SteeringEngine routes through RollEngine.ResolveCheck`.

### Step 6 — extract inline shadow check into `ShadowCheckEngine`

- New file: `src/Pinder.Core/Conversation/ShadowCheckEngine.cs`.
- Move the d20 + ladder logic from `GameSession.cs:~1596` into a method on this engine. Takes whatever inputs the inline code currently consumes (dice roller, dc, possibly opponent state).
- `GameSession` calls `_shadowCheckEngine.Check(...)` (inject in ctor; preserve existing DI pattern — read the constructor to see how other engines are injected).
- `ShadowCheckResult` gains `Check: RollCheckResult`.

**Commit:** `refactor(#901): extract inline shadow check into ShadowCheckEngine`.

### Step 7 — shadow-growth-per-stat-failure inline check

Find the shadow-growth check (also in `GameSession.cs`, separate from the main shadow check). Either:
- Put it in `ShadowCheckEngine` as a second method (`CheckGrowth(...)`), OR
- A separate `ShadowGrowthEvaluator` (file already exists — `src/Pinder.Core/Conversation/ShadowGrowthEvaluator.cs`). Inspect — it may already be the right home; just route its internal d20 (if any) through `RollEngine.ResolveCheck` with `RollCheckKind.ShadowGrowth`.

Use the existing class if applicable. Don't create a new one if `ShadowGrowthEvaluator` is the obvious home.

**Commit:** `refactor(#901): shadow-growth check routes through RollEngine.ResolveCheck`.

### Step 8 — tests

- **New `tests/Pinder.Core.Tests/Rolls/FailureTierLadderTests.cs`** — exhaustive boundary coverage:
  - `missMargin <= 0` → `None`
  - `missMargin == 1, 2` → `Fumble`
  - `missMargin == 3, 5` → `Misfire`
  - `missMargin == 6, 9` → `TropeTrap`
  - `missMargin == 10, 99` → `Catastrophe`
- **New `tests/Pinder.Core.Tests/Rolls/RollEngineCheckTests.cs`** — `ResolveCheck` per kind:
  - `OptionRoll` with modifier bag → correct `Total`, `IsSuccess`, `Tier`.
  - `Steering` with disadvantage → correct `UsedDieRoll`, `SecondDieRoll` populated.
  - `Horniness` with empty modifier bag → modifier sum 0.
  - `Shadow` nat-20 → `IsNatTwenty` true.
  - `ShadowGrowth` nat-1 → `IsNatOne` true.
- **Audit test `tests/Pinder.Core.Tests/Rolls/TierLadderAuditTest.cs`:**
  - Walk `src/Pinder.Core/**/*.cs`. For each file, fail if it contains a regex match for `missMargin\s*<=\s*\d` OR `miss\s*<=\s*\d` (case-insensitive) UNLESS the file is `FailureTierLadder.cs`. This is the ZERO-DUPLICATES gate from the ticket.
  - Implementation: `File.ReadAllText` over `Directory.EnumerateFiles(..., "*.cs", SearchOption.AllDirectories)` rooted at the assembly's compiled source dir — pick the simplest reliable approach. If walking source files at test-time is awkward, hardcode the file list to the ~12 known engine files and audit those.
- All existing tests pass unchanged. Run:
  ```bash
  dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/test-901.txt 2>&1
  tail -10 /tmp/test-901.txt
  ```

**Commit:** `test(#901): FailureTierLadder + RollEngineCheck + tier-ladder-audit tests`.

### Step 9 — session-runner snapshot byte-identical verification

- Run the existing session-runner against a deterministic seed (look for how this is invoked in CI or in `session-runner/README.md` or any `Makefile` / `run-*.sh` script in `session-runner/`).
- Diff the output against a baseline run on `main`:
  ```bash
  cd /tmp/work-901
  git stash 2>/dev/null || true
  # capture baseline on main
  git checkout origin/main -- session-runner src
  # ... run a deterministic sim, e.g.:
  # cd session-runner && dotnet run -- --seed 42 --turns 20 --out /tmp/baseline-snap
  # then restore work
  git checkout HEAD -- session-runner src
  ```
  Honestly: the snapshot byte-identical check is the load-bearing one. If you can't get a deterministic sim running, the next best thing is unit-level proof that:
  - `RollResult.DieRoll == RollResult.Check.DieRoll`,
  - `RollResult.Modifier == RollResult.Check.ModifierSum`,
  - `RollResult.Total == RollResult.Check.Total`,
  - `RollResult.Dc == RollResult.Check.Dc`,
  - `RollResult.Tier == RollResult.Check.Tier`,
  - `RollResult.IsSuccess == RollResult.Check.IsSuccess`,
  for every `RollResult` constructed by `RollEngine.Resolve` and `RollEngine.ResolveFromComponents`. Same for `HorninessCheckResult`, `SteeringRollResult`, `ShadowCheckResult`.
- Document in the PR body what verification you ran. If you ran a deterministic sim, include the diff command + result. If you only ran the field-parity unit tests, say so honestly.

**Commit:** `test(#901): per-wrapper bespoke-vs-Check field parity tests`.

### Step 10 — docs

- New file: `docs/specs/issue-901-rollengine-unification.md`.
  - Document the canonical shape (`RollCheckResult`, `RollCheckKind`, `NamedModifier`).
  - Note the 3-phase migration: this PR = Phase 1 (additive). Phase 2 (wire DTO) and Phase 3 (cleanup) are separate tickets.
  - Document that `FailureTierLadder.FromMissMargin` is the sole tier ladder.
- Append to `LESSONS_LEARNED.md` in the repo root (read it first to match style):
  ```
  ## LESSON: All dice checks go through RollEngine.ResolveCheck

  The tier ladder lives in FailureTierLadder.FromMissMargin. Do not
  re-implement either. New check kinds add a value to RollCheckKind
  and route through ResolveCheck — no bespoke d20 + ladder code
  outside Pinder.Core/Rolls/.
  ```

**Commit:** `docs(#901): spec + LESSONS_LEARNED entry`.

## Acceptance criteria (must all pass before PR open)

- [ ] `RollCheckResult` + `RollCheckKind` + `NamedModifier` + `FailureTierLadder` exist.
- [ ] `RollEngine.ResolveCheck` exists; all four (five-with-growth) check sites call it.
- [ ] `HorninessEngine.DetermineHorninessTier` deleted.
- [ ] `GameSession.cs` no longer contains an inline d20 + ladder shadow check (moved to engine).
- [ ] All existing tests pass unchanged.
- [ ] New tests added (FailureTierLadder, RollEngineCheck, audit).
- [ ] Field-parity proven (per-wrapper bespoke fields == `Check.*` projections).
- [ ] `docs/specs/issue-901-rollengine-unification.md` written.
- [ ] `LESSONS_LEARNED.md` entry appended.
- [ ] PR body contains `Closes #901` on its own line.
- [ ] `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` green.
- [ ] `dotnet build` clean (warnings as errors if the project has that setting — check the csproj).

## Workflow rules

- Commit incrementally per the 10 steps above. Each step is one atomic commit.
- Run `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore` after every step that touches code. Capture to `/tmp/test-901-stepN.txt` and read only the tail. NEVER paste full test output into your reasoning.
- If a test starts failing, STOP, find the actual root cause, fix it. Do NOT "adjust" the test to make it green — the requirement is that existing tests pass unchanged.
- After all steps:
  ```bash
  git push -u origin fix/901-rollengine-unification
  gh pr create --repo decay256/pinder-core --base main --head fix/901-rollengine-unification --title "fix/901 RollEngine unification (Phase 1 additive)" --body "$(cat <<EOF
  - feat(#901): introduce RollCheckResult / RollCheckKind / NamedModifier / FailureTierLadder
  - feat(#901): add RollEngine.ResolveCheck single entry point
  - refactor(#901): route Resolve / ResolveFromComponents through FailureTierLadder
  - refactor(#901): HorninessEngine.CheckAsync routes through ResolveCheck
  - refactor(#901): SteeringEngine routes through ResolveCheck
  - refactor(#901): extract inline shadow check into ShadowCheckEngine
  - refactor(#901): shadow-growth routes through ResolveCheck
  - test(#901): FailureTierLadder + RollEngineCheck + tier-ladder-audit
  - test(#901): per-wrapper bespoke-vs-Check field parity
  - docs(#901): spec + LESSONS_LEARNED

  Phase 1 (additive) only. Wire DTO (Phase 2) and cleanup (Phase 3) are separate tickets.

  Closes #901
  EOF
  )"
  ```

## Pre-existing breakage (NOT yours)

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909) and `Issue527_SessionRunnerBioFormatTests.BioFormattedAsBoldItalicParagraph_NotTableRow` flakes alongside YamlDotNet-loading tests (#884). Run `Pinder.Core.Tests` only.

## DO NOT

- Do NOT touch wire DTOs (`TurnResult` / `TurnStart` / GameApi controllers). Phase 2.
- Do NOT delete bespoke fields on `RollResult` / `HorninessCheckResult` / etc. Phase 3.
- Do NOT change `FailureTier` enum values or order.
- Do NOT change DC math, advantage/disadvantage semantics, or nat-1 / nat-20 rules.
- Do NOT bump submodule pointer.
- Do NOT merge.
- Do NOT add a `TurnSnapshot` mirror for `Check` (Phase 2 wires it; in this PR `Check` is internal-only).

## Logging

```bash
cd /tmp/work-901 && bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#901" "core/rolls" "started" "Implementing #901 (RollEngine unification Phase 1 additive) per branch fix/901-rollengine-unification"
```

At exit:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh backend-engineer "#901" "core/rolls" "completed" "PR #<N> opened" "<commit-sha>"
```

## Output

### `## DoD Evidence` block (mandatory):
- PR URL.
- `dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj` tail (last 10 lines).
- `dotnet build` tail.
- `git log --oneline origin/main..HEAD` (expect ~10 commits).
- Push confirmation.
- `gh pr view <N> --json number,title,state,url`.
- agent.log lines.
- Snapshot byte-identical verification: describe what you ran. If a deterministic sim, paste the diff output. If field-parity unit tests only, list them.
- Per-wrapper field parity test names.

### `## Research Log` block (mandatory):
| Topic | Source | Key finding |
|---|---|---|
| (e.g. existing tier ladder location) | RollEngine.cs:188-200, HorninessEngine.cs:90 | confirmed duplication |
| (e.g. dice roller injection) | SteeringEngine.cs ctor | `_steeringDice` is separate from main `_dice` |
| ... | ... | ... |

### Deviations

If anything in this prompt was wrong, ambiguous, or impossible to follow as written, list it under a `## Deviations` block at the end with the orchestrator-default you took. This is normal and expected — surface it; do NOT silently work around it.
