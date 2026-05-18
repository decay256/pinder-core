# Sprint 2026-05-17-197af9 — Continuation Context #2

**Trigger:** orchestrator at high context utilization mid-#943 (after dispatching core PR
#958, before reviewing it). Handing off per CONTEXT-BUDGET-GUARD §0.4 to avoid losing
state mid-#943 review.

**Predecessor context handoff:** `continuation-context.md` (first orchestrator → this
one). This file is the second handoff (this orchestrator → the next).

## State at handoff (2026-05-18T02:00 UTC, approx)

- Sprint id: `2026-05-17-197af9`
- pinder-core HEAD: `1be887c` (after #942 core PR; pinder-web bumped beyond this to `0392731`)
- Yaml sha (model-routing): `257f980a0ac94034cbd5af7fafc3ce281388dac6457a3a94abbd0965e161c0b5` (unchanged)
- Pricing snapshot: same path as before
- Cron watchdog: enabled, 15min, silent when done

## Tickets — current status

### Merged this orchestrator (3)

- **`core#884`** — PR #954 merged at `90942ee`. xUnit `[Collection]` test isolation. 2-line fix.
- **`core#942`** [P0] — PR #955 (core, sha `1be887c`) + pinder-web #656 (sha `c9c4f2a`). Transactional `StartTurnAsync` + invariant guard + web prefetch-surfaces-Ghosted + growth reapplication. **Closed** after both halves merged.
- **`core#944`** — pinder-web PR #657 merged at `0392731`. Wire-DTO `trap_activated` always-emits-null fix. **Closed** manually (cross-repo close doesn't auto-fire).

### In-flight (1) — needs fix-pass before resume

- **`core#943`** — core PR #958 is **DRAFT** with CHANGES_REQUESTED review (Rung 2 sonnet override). **4 blockers** identified by reviewer:

  1. **pinder-core itself doesn't compile.** 9 `CS0117` errors in `Pinder.LlmAdapters.Tests` and `Pinder.Rules.Tests` — the implementer missed them in the rename sweep. The DoD-claimed "build succeeded" was false. Verify by running `dotnet build -c Release` on the branch.
  2. **pinder-web compile fails at submodule bump.** 17 C# symbol references to `FailureTier.None`: 3 in production (`TurnAuditWriter.cs:439/466/488`), 14 in test files. No companion web PR exists.
  3. **Frontend silent runtime regressions.** 57 frontend TypeScript references to `tier === 'None'` for success/miss discrimination (in `collapsedHeader.ts`, `rollAdapters.ts`, `ModifierBagRollFormula.tsx`). Wire emits `"Success"` after rename → SPA silently renders success as miss.
  4. **AC not actually met on the wire.** The production wire path is pinder-web's `TurnAuditWriter` which does `Tier == FailureTier.None ? null : ToString()`. After the rename, this becomes `Tier == FailureTier.Success ? null : ToString()` — **still emitting null for the success case**. The bug isn't fixed.

  **Recommended fix path** (per reviewer): Add `[EnumMember(Value="None")]` on `FailureTier.Success` + `[Obsolete] public const FailureTier None = FailureTier.Success` alias. This lets pinder-web compile unchanged, keeps frontend string comparisons valid. THEN the real wire fix is in `TurnAuditWriter.cs` (pinder-web): change the null-on-Success logic so success tier is actually emitted.

  **Action for next orchestrator:** spawn a fix-pass implementer on this branch. Recommend Rung 2 (sonnet direct) — the original Rung 0 implementer made a false DoD claim, indicating capability mismatch. Fix-pass spec should explicitly say: (a) don't rename, (b) preserve `FailureTier.None` as a symbol AND wire string, (c) the actual wire bug is in pinder-web's `TurnAuditWriter.cs` — that fix goes in the web PR, not the core PR. The core PR's job is only to add the Success enum value with EnumMember(Value="None") compat shim and update `RollResult`/`RollCheckResult` constructors to assign `FailureTier.Success` on success.

### Closed-as-superseded / fold-into / split (0 new)

None this round.

### Remaining to drain (22)

**Thread 1 — Staging-test fallout (still 11 left after #942 + #944):**

After #943 lands (both halves):
- `core#945` [P1] OfferedOption wire DTO emits dc=null and modifier=null on every option.
- `core#948` [P1] All sessions show "outcome unknown" — outcome column NULL.
- `core#950` [P1] Psychological stake never surfaces in chat (do A+B both per resolved scope on ticket comment).
- `core#951` [P1] Opening message contains literal "scene" instead of opponent's name.
- `web#647` [P1] EventBox renders box for text-only mods (foundational for #648/#649).
- `web#648` [P1] Folded EventBox header uninformative (likely absorbs #655).
- `web#649` [P1] Expanded EventBox lacks consequence/roll/formula breakdown.
- `web#650` [P1] Weakness-window hit lacks global FoldableHintBanner trigger.
- `web#651` [P1] Replays unavailable for all sessions (companion to core#948).
- `web#652` [P1] Main-roll formula should fold UNDER the success/miss EventBox.
- `web#655` [P1] Shadow check folded header doesn't surface shadow type.

**Thread 3 — Long-tail chores (still 13 left):**

- `core#920`, `core#921`, `core#924`, `core#925`, `core#927`, `core#947`, `core#949`.
- `web#646`, `web#653`, `web#654`, `web#619`, `web#621`, `web#612`.

## Follow-ups filed by THIS orchestrator (4)

- **`core#956`** — [#942 follow-up] `GameEndedException.ShadowGrowthEvents` should expose
  a structured record list (typed `ShadowGrowthEffect(Stat, Amount, Reason)`) so callers
  don't have to parse strings. P2.
- **`core#957`** — [#942 follow-up] `Wait()` / `CheckInterestEndConditions()` /
  `CheckGhostTrigger()` are still mutate-before-throw — inconsistent with the new
  transactional `StartTurnAsync`. P1.
- **`web#658`** — [#944 follow-up] `TurnAuditWriter` symmetric serializer fix for
  `trap_activated` and any other always-required nullable wire fields. P2.

Plus from previous orchestrator:
- `core#953` — [bug][test-infra] Pinder.Rules.Tests 46 pre-existing failures (still open).

## ⚠️ Sediment cleanup performed this round

- `/root/projects/pinder-web/` canonical clone had unstaged changes left by a leaked Rung-0
  attempt that violated WORKSPACE-ISOLATION (edited `pinder-core` submodule pointer and
  `TurnResultPayloadMapper.cs` directly instead of in `/tmp/work-944/`). Reset with
  `git checkout HEAD -- pinder-core src/.../TurnResultPayloadMapper.cs` and
  `git submodule update --init pinder-core`. Confirmed clean before continuing.

**Next orchestrator: re-verify** `/root/projects/pinder-{core,web}/` are both clean at
sprint resume. Run:
```bash
cd /root/projects/pinder-core && git status && git pull origin main
cd /root/projects/pinder-web && git status && git submodule status && git pull origin main
```

## OpenRouter degradation — calibration signal for tomorrow

Cumulative OpenRouter Rung 0/1 flake count this sprint: **8** (token_underrun /
stream_cut / FailoverError on:
- #929 ×3 (carried over, all closed via inline-finish or genuine work-via-tool-calls)
- #884 ×1 (inline-finished)
- #942 core ×3 (escalated to Rung 2 sonnet direct)
- #942 web ×1 (escalated to Rung 2)
- #944 ×2 (escalated to Rung 2)
- #944 reviewer Rung 1 deepseek ×0 (succeeded with real stats)
- #943 core ×0 surprisingly succeeded at Rung 0, **but with a flawed design choice — see above**

Direct Anthropic flake count: **0** out of 4 calls (Rung 2 ×4, Rung 3 ×2 — Stats line is
often 0/0 which is a DISPLAY ISSUE not a real flake; the work is always real and the
recovery script accepts `stats-reparse` source).

**Strong recommendation for the next orchestrator:** when OpenRouter Rung 0 flakes
twice in a row on the same ticket, skip the same-rung retry and go direct to Rung 2.
That's the empirically-justified deviation; document the trigger as
`provider-instability` and log it. This is NOT a violation of PER-TICKET-RUNG-ISOLATION
(every NEW ticket still starts at Rung 0); it's a sprint-level provider-health override
applied per-ticket after observed flake count.

**For Phase 6.5 calibration:** propose `provider_flake_retry_policy.same_rung_retries` =
0 for OpenRouter rungs when sprint-cumulative OpenRouter flake count > 5. That's the
adaptive-threshold pattern.

## Recovery-source distribution this round

- `operator-provided`: 7 (most flakes; tokens estimated)
- `stats-reparse`: 2 (the cases where Stats came back populated — both reviewers on #944 and #942-web)

All recoveries clean per LOGGING-GATE-WITH-RECOVERY.

## How to resume

1. Read this file. Read the original `continuation-context.md` for context that's
   still relevant (the pricing-snapshot path, orphan-recovery history, etc.).
2. Read `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/kickoff.md`.
3. **Pull both repos:**
   ```bash
   cd /root/projects/pinder-core && git pull origin main
   cd /root/projects/pinder-web && git status && git pull origin main
   ```
   Verify pinder-web's pinder-core submodule status — if it shows drift, `git submodule update --init pinder-core`.
4. Verify routing yaml sha unchanged: `sha256sum /root/projects/eigentakt/model-routing.yaml`.
5. **Fix-pass PR #958 (core#943).** Review is already in (CHANGES_REQUESTED, 4 blockers). Spawn a fix-pass implementer at Rung 2 (sonnet direct) on the existing branch `fix/943-roll-tier-success-value`. Spec should be:
   - Don't keep the rename. ADD `Success` while preserving `None`. Options:
     - **Preferred:** rename `None` → `Success` AT THE SYMBOL LEVEL but apply `[System.Runtime.Serialization.EnumMember(Value="None")]` to preserve wire serialization. Add `[Obsolete] public const FailureTier None = FailureTier.Success` (or a `[Obsolete] field-aliased-as-Success`) so pinder-web symbol references compile unchanged.
     - **Alternative:** keep both `None` (deprecated) and `Success` as separate enum entries with the same int value via `public const FailureTier Success = FailureTier.None`. Same effect, different shape.
   - Fix the 9 CS0117 compile errors the original implementer missed in `Pinder.LlmAdapters.Tests` and `Pinder.Rules.Tests`. Verify by actually running `dotnet build -c Release` and inspecting the tail.
   - Constructors of `RollResult` and `RollCheckResult` continue assigning `FailureTier.Success` on success (which now serializes as wire string `"None"` due to EnumMember, but the SYMBOL is `Success`).
   - **Do NOT change the pinder-web `TurnAuditWriter` logic in this PR.** That's the web PR's job. The core PR's job is just to provide the symbol + working build. The web PR will then change `TurnAuditWriter` to actually emit the tier on success (instead of the null-on-None logic that this PR's rename failed to address).
6. **After core PR #958 (or its successor) merges:** spawn the web PR per the existing `943-impl.md` task file's Piece 2 section. The web PR is where the real wire fix lands.
7. After #943 closes (both halves): proceed in the order listed under "Remaining" above.
8. Cron watchdog every 15min will surface any stall.

## Files of interest

- `/root/projects/pinder-core/docs/sprint-runs/2026-05-17-197af9/spawns/`
  - `884-impl.md`, `942-impl.md` / `942-impl-rung1.md` / `942-impl-rung2.md`,
    `942-review.md`, `942-web-impl.md` / `942-web-impl-rung2.md`, `942-web-review.md`,
    `944-impl.md` / `944-impl-rung2.md`, `944-review.md`, `943-impl.md` — these are
    templates the next orchestrator can clone/sed for similar tickets.
- `/root/projects/pinder-core/agent.log` — full event stream with all 11 attempts +
  recoveries logged this round.
- `/root/projects/pinder-web/agent.log` — pinder-web event stream (separate file, used
  by implementer scripts).
