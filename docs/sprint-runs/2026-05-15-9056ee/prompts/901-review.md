You are a code-reviewer subagent reviewing **pinder-core PR #918** (Issue #901 — RollEngine unification Phase 1 additive).

You are a **no-context fresh-eye reviewer**. Do NOT read prior subagent transcripts, the implementer's DoD evidence, or commit summaries before forming your own opinion. Read the PR diff cold and judge it on its own merits.

This is the foundational refactor of sprint 2026-05-15-9056ee. Bar is HIGH: byte-identical session-runner snapshots required, no semantics changes to DC math / nat-1 / nat-20 / advantage / disadvantage, all existing tests must pass.

## PR

- URL: https://github.com/decay256/pinder-core/pull/918
- Branch: `fix/901-rollengine-unification`
- Base: `main`
- Size: +1033 / -54, 21 files
- State: OPEN, MERGEABLE, CLEAN

## Cold-start

1. Read `/root/.openclaw/skills/eigentakt/agents/code-reviewer.md` for the Review Checklist + Output Format. Follow it.
2. Read `/root/projects/pinder-core/AGENTS.md` — especially **Snapshot Schema Discipline**.
3. Read the ticket: `gh issue view 901 --repo decay256/pinder-core`. Pay attention to the 3-phase migration plan — this PR should be Phase 1 ONLY (additive).
4. Fetch the PR:
   ```bash
   cd /root/projects/pinder-core
   git fetch origin
   git diff origin/main...origin/fix/901-rollengine-unification > /tmp/918.diff
   wc -l /tmp/918.diff
   ```
   Read it.

## Implementer's declared deviations — verify each one

The implementer flagged four deviations in their PR description. Verify each is actually defensible:

1. **`RandomDiceRollerAdapter` added** because the existing engines used `System.Random` directly, not `IDiceRoller`. Check `src/Pinder.Core/Rolls/RandomDiceRollerAdapter.cs`:
   - Is it minimal (just wraps `Random.Next` into `IDiceRoller.Roll`)?
   - Does it preserve the EXACT random-number sequence? E.g., if the old code called `_rng.Next(1, 21)` once, the adapter must also call `_rng.Next(1, 21)` exactly once per `Roll(20)`. Any extra calls would alter snapshots.
   - Is it injected via DI in a way that doesn't double-wrap (e.g., for the steering RNG used by both `SteeringEngine` and the new `ShadowCheckEngine`)?
   - Check both clone constructors (`GameSession.cs:315,478` per the research log) — does the cloned session preserve the same `Random` instance/seed?

2. **Shadow-growth is a no-op.** `ShadowGrowthEvaluator` doesn't roll its own d20 — it takes `RollResult` as input. Verify by reading `src/Pinder.Core/Conversation/ShadowGrowthEvaluator.cs` start-to-finish. If true, `RollCheckKind.ShadowGrowth` is dead code in the enum; that's fine for Phase 1 (reserved for future) but the ticket DoD said "all four (five-with-growth) check sites call it." Is the deviation justified, or should `ShadowGrowthEvaluator` be refactored to route some now-deterministic check through `ResolveCheck` for consistency? Default judgment: if there's genuinely no d20 in shadow-growth, the deviation is correct.

3. **`DecomposedModuleTests.HorninessEngine_DetermineHorninessTier_ReturnsCorrectTier`** redirected to call `FailureTierLadder.FromMissMargin`. Acceptable IF and ONLY IF the test now exercises the new ladder with the same thresholds as the old one. Read the diff for `tests/Pinder.Core.Tests/DecomposedModuleTests.cs` — is the test still semantically equivalent?

4. **`RollResult.Tier` ≠ `RollResult.Check.Tier` on nat-1.** Implementer says `Legendary` is a nat-20-success promotion that stays in `ResolveFromComponents`; `FailureTierLadder` is failure-side only. **Verify this matches the ticket's intent.** Specifically: when the die rolls a nat-1, what does the OLD code produce for `RollResult.Tier`? What does `RollCheckResult.Tier` produce now? If `RollResult.Tier` is `Catastrophe` (or similar) on nat-1 but `RollCheckResult.Tier` is computed from miss-margin, they will differ when the miss-margin is small. This may be correct, but the field-parity claim then has an asterisk. Read `FieldParityTests.RollResult_Nat1_TierDivergence_IsDocumented` — does it actually test the divergence with a real nat-1 scenario, and is the divergence semantically defensible?

## Verification points (must check each)

### Phase-discipline (additive-only)

- [ ] All wire DTOs unchanged. Inspect `Pinder.GameApi.*` (look under `src/Pinder.GameApi/`) — no `TurnResult` / `TurnStart` fields renamed or removed.
- [ ] Each per-check wrapper (`RollResult`, `HorninessCheckResult`, `SteeringRollResult`, `ShadowCheckResult`) GAINED a `Check: RollCheckResult` property — existing fields still present and still populated.
- [ ] No `TurnSnapshot` mirror added for `Check` (per the prompt: Phase 2 wires it). If a mirror WAS added, that's a Phase-2 leak.

### Single source of truth for the ladder

- [ ] `FailureTierLadder.FromMissMargin` exists and matches `<= 0 → None; <= 2 → Fumble; <= 5 → Misfire; <= 9 → TropeTrap; else Catastrophe`.
- [ ] `HorninessEngine.DetermineHorninessTier` is DELETED (not just `[Obsolete]`).
- [ ] `RollEngine.cs` lines 188-200 (the old inline ladder) replaced by a call to `FailureTierLadder.FromMissMargin`.
- [ ] The TierLadderAuditTest at `tests/Pinder.Core.Tests/Rolls/TierLadderAuditTest.cs` actually searches sources and FAILS if a hand-rolled `miss <= 2` pattern appears anywhere outside `FailureTierLadder.cs`. Read it. Verify the regex is non-trivially specific (not so loose it false-positives, not so tight it false-negatives).

### Single entry point for d20

- [ ] `RollEngine.ResolveCheck(RollCheckKind, IDiceRoller, IReadOnlyList<NamedModifier>, dc, hasAdv, hasDis)` exists with that signature (or `C# 8`-compatible equivalent — the project is `LangVersion=8.0` per research log).
- [ ] `HorninessEngine.CheckAsync` (or `PeekAsync` per the commit message) routes its d20 through `ResolveCheck`. Inspect the diff.
- [ ] `SteeringEngine` routes its d20 through `ResolveCheck`.
- [ ] The inline shadow check at `GameSession.cs:~1596` is EXTRACTED into `src/Pinder.Core/Conversation/ShadowCheckEngine.cs` and `GameSession` calls the new engine.
- [ ] `ShadowCheckEngine` itself routes through `ResolveCheck`.
- [ ] No engine has a `_rng.Next(1, 21)` or `dice.Roll(20)` call outside `RollEngine.ResolveCheck`. Grep the diff: `grep -nE '\.Roll\(20\)|\.Next\(1,\s*21\)' src/`.

### Semantic preservation

- [ ] Nat-1 / nat-20 detection works on the USED die roll, not raw d20 (with advantage/disadvantage, `IsNatOne` should reflect the kept die).
- [ ] Advantage: rolls twice, keeps higher. Disadvantage: rolls twice, keeps lower. Both stored in `DieRoll` + `SecondDieRoll`, with `UsedDieRoll` = the one applied. Verify by reading `ResolveCheck` body.
- [ ] DC math: `Total = UsedDieRoll + ModifierSum`; `IsSuccess = Total >= Dc` (or `>= Dc` with a nat-20 carve-out — match existing behavior).
- [ ] `MissMargin = max(0, Dc - Total)` (success → 0).
- [ ] Clone constructors (`GameSession.cs:315,478`): both updated to instantiate `ShadowCheckEngine` and preserve the RNG state. Verify the cloned session's RNG isn't reseeded.

### RNG byte-identical

The load-bearing question. Read each refactored engine's d20-call diff:

- OLD: did the engine do `_rng.Next(1, 21)` directly?
- NEW: does the engine call `RollEngine.ResolveCheck(..., dice: new RandomDiceRollerAdapter(_rng), ...)`, where `RandomDiceRollerAdapter.Roll(20)` internally does `_rng.Next(1, 21)` exactly once?

If yes, the random-number stream consumed per turn is identical, and a deterministic sim with the same seed should produce byte-identical snapshots.

If `ResolveCheck` calls `IDiceRoller.Roll(20)` twice (advantage/disadvantage) where the old code only rolled once: byte-mismatch. Check the diff specifically: under what conditions does `ResolveCheck` consume 2 d20s? Is `hasAdvantage` defaulted to false everywhere it's called from horniness/steering/shadow? If yes, byte-identical holds.

Confirm explicitly: each of the 4 (or 5) refactored call sites passes `hasAdvantage: false, hasDisadvantage: false` (or omits them, picking the defaults). Document the answer in your review.

### Tests

- [ ] `tests/Pinder.Core.Tests/Rolls/FailureTierLadderTests.cs` exhaustively covers boundaries (≤0 None; 1,2 Fumble; 3,5 Misfire; 6,9 TropeTrap; 10,99 Catastrophe).
- [ ] `tests/Pinder.Core.Tests/Rolls/RollEngineCheckTests.cs` covers each `RollCheckKind` value (except possibly `ShadowGrowth` if it's a no-op-reserved enum).
- [ ] `tests/Pinder.Core.Tests/Rolls/FieldParityTests.cs` proves bespoke-vs-Check parity for each wrapper. Read the test bodies — are they ACTUALLY constructing real results and asserting field equivalence, or are they tautological (constructing `RollResult` with already-equal values)? **Reverse-verification recommended:** temporarily mutate one of the wrappers' bespoke fields to a wrong value (in your reviewer worktree only, do NOT commit), confirm the parity test FAILS. Then revert. If the parity test doesn't fail when the wrapper is broken, the test is tautological.
- [ ] `tests/Pinder.Core.Tests/Rolls/TierLadderAuditTest.cs` — run it. Confirm it actually walks `src/Pinder.Core/` and would fail on a hand-rolled ladder.

### Drive-bys

- [ ] No files touched outside the ticket's surface (`src/Pinder.Core/Rolls/`, `src/Pinder.Core/Conversation/{Game,Horniness,Steering,Shadow}*.cs`, the test files, `docs/specs/issue-901-*.md`, `LESSONS_LEARNED.md`).
- [ ] No version bumps, no dependency adds, no `using`-reordering outside touched methods.
- [ ] No `[Obsolete]` attribute spam (per Phase 1: existing fields STAY without obsolescence markings — Phase 3 deletes them).
- [ ] No submodule pointer bump (this is core-only).

### PR body

- [ ] Contains `Closes #901` on its own line.

## Run the tests yourself

```bash
cd /root/projects/pinder-core
git fetch origin
git checkout origin/fix/901-rollengine-unification --detach 2>&1
git submodule update --init --recursive 2>&1
dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj --no-restore > /tmp/review-918-tests.txt 2>&1
tail -15 /tmp/review-918-tests.txt
dotnet build src/Pinder.Core/Pinder.Core.csproj --no-restore 2>&1 | tail -5
dotnet build session-runner/session-runner.csproj --no-restore 2>&1 | tail -5
```

Expected: 2752 passed, 0 failed, 18 skipped (per implementer's evidence). Verify.

`Pinder.LlmAdapters.Tests` has 72 pre-existing failures (#909) — do NOT run it. The flaky `Issue527_SessionRunnerBioFormatTests` is tracked as #884.

## Verdict

Per SELF-APPROVE-BLOCKED, post via `--comment` not `--approve`:

```bash
gh pr review 918 --repo decay256/pinder-core --comment --body "$(cat <<'EOF'
**Verdict: APPROVE** (or **Verdict: CHANGES_REQUESTED**)

<structured review per code-reviewer.md output format>
EOF
)"
```

## Authority

This is the FIRST-PASS review. The orchestrator decides next steps based on your verdict.

- APPROVE, no blockers → orchestrator merges.
- APPROVE with non-blocking findings → orchestrator files them as follow-ups, then merges.
- CHANGES_REQUESTED → orchestrator spawns a fix subagent, then a second-pass reviewer.

This is the most consequential PR of the sprint. **Spend the time. Reverse-verify the parity tests. Confirm the RNG byte-identical claim by reading the adapter and counting `Roll(20)` calls per turn.** A wrong APPROVE here costs the rest of the sprint; a wrong CHANGES_REQUESTED costs only a second pass.

## Logging

```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#901" "core/rolls" "started" "Reviewing PR #918 (first pass)"
```

At exit:
```bash
bash /root/.openclaw/skills/eigentakt/scripts/log.sh code-reviewer "#901" "core/rolls" "completed" "Verdict: <APPROVE|CHANGES_REQUESTED>, blockers: <count>, follow-ups: <count>"
```

## Output

Your final response is the same body you posted to the PR.
