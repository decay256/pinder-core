# Vision Review — Sprint 4: RPG Rules Complete — Continuation (Attempt 4)

## Alignment: ⚠️

Sprint direction remains correct — these features complete the RPG ruleset defined in rules v3.4. Since attempt 3, no new prerequisite infrastructure has shipped. The **same two blocking gaps** persist: Wave 0 prerequisites (#130) and closed-but-unimplemented issues (#126). This attempt focused on **embedding vision concern annotations into #44 and #45**, which were the last two major issues missing explicit SessionShadowTracker and #130 references.

## Issues Edited This Pass

| Issue | What Changed | Vision Concern Resolved |
|---|---|---|
| #44 (Shadow growth) | Added SessionShadowTracker requirement, #130 dependency, per-session counter list from #66, warning not to modify StatBlock directly | #58, #66, #128 embedded |
| #45 (Shadow thresholds) | Added InterestMeter overload note from #79, SessionShadowTracker reference, #130 dependency | #79, #128 embedded |
| #48 (XP tracking) | Clarified TurnResult.XpEarned already exists (PR #117), added note on DC tier for fixed-DC rolls | Minor clarity |

## Vision Concerns Status Summary

### ✅ Resolved (can be closed by PO)
| # | Title | Resolution |
|---|---|---|
| #69 | TurnResult expansion | PR #117 merged |
| #61 | RiskTier re-introduction | PR #119 merged |
| #73 | #49/#50 depend on #63 | PR #114 merged |
| #71 | 'Chaos >= high' undefined | Clarified in #55 body: Chaos base stat ≥ 4 |
| #81 | PlayerResponseDelay time source | Clarified in #55 body: pure function, caller provides TimeSpan |
| #74 | Horniness roll vs shadow stat | Clarified in #51 body: shadow stat + time-of-day modifier |
| #80 | #43 wrong dependency | Fixed in #43 body: now depends on #130 |
| #59 | Lukewarm in #53 | Already fixed in #53 body |
| #70 | Record types | Already fixed in #47, #55, #56 bodies |
| #30 | Hard/Bold risk bonus undefined | Shipped in PR #119 (RiskTier + RiskTierBonus) |

### ⚠️ Still Open — Well-Specified (no further edits needed)
| # | Title | Status |
|---|---|---|
| #130 | Wave 0 prerequisites | **BLOCKING** — gates all feature work |
| #128 | Shadow mutation architecture | **BLOCKING** — folded into #130 |
| #129 | RollResult.Total externalBonus | **BLOCKING** — folded into #130 |
| #126 | 6 closed issues have no implementation | **BLOCKING** — must reopen or create new issues |
| #127 | #52 PRs approved but unmerged | Housekeeping |
| #82 | GameSession constructor injection | Folded into #130 |
| #67 | IGameClock interface | Now embedded in #54 body |
| #75 | Energy ownership | Now embedded in #54 and #56 bodies |
| #65 | Fixed-DC overload | Folded into #130, embedded in #43 body |
| #64 | TrapState.HasActive | Folded into #130, embedded in #43 body |
| #58 | StatBlock immutability | Folded into #128/#130, embedded in #43 AND #44 bodies |
| #79 | InterestMeter starting value | Now embedded in #45 body |
| #66 | Shadow growth counters | Now embedded in #44 body |
| #68 | Combo +1 composition | Now embedded in #46 body |
| #60 | DialogueOption expansion | Part of Wave 0 or #49/#51 |
| #62 | Test gaps before features | Wave scheduling advisory |
| #57 | Sprint scope massive | Advisory |
| #39 | SuccessScale zero coverage | Part of #38 QA |
| #40 | DateSecured test missing | Part of #38 QA |

## Data Flow Traces (unchanged — all gaps still present in code)

### Shadow Growth: GameSession → shadow mutation
- `ResolveTurnAsync()` → detect trigger → `SessionShadowTracker.Increment()` → `TurnResult.ShadowGrowthEvents`
- **⚠️ BLOCKING**: `StatBlock._shadow` is `private readonly`. No `SessionShadowTracker` exists. See #130.
- Now explicitly noted in #43, #44, and #45 issue bodies.

### External Roll Bonuses: Combo/Callback/Tell → RollEngine
- `GameSession` accumulates bonuses → passes to `RollEngine.Resolve(externalBonus)` → `RollResult.Total`
- **⚠️ BLOCKING**: `RollEngine.Resolve()` has no `externalBonus` param. `Total = UsedDieRoll + StatModifier + LevelBonus` — missing term. See #130.
- Now explicitly noted in #46, #47, #50 issue bodies.

### Fixed-DC Rolls: Read/Recover → RollEngine
- Player picks Read/Recover → `RollEngine.ResolveFixedDC()` with DC 12
- **⚠️ BLOCKING**: No fixed-DC overload exists. See #130.
- Now explicitly noted in #43 issue body.

## New Observation: Momentum and Non-Speak Actions

#43 introduces Read, Recover, and Wait actions. The current momentum system (3-streak→+2, 5+→+3) counts consecutive **successes** in `ResolveTurnAsync`. The issue does not specify:
- Does a successful Read/Recover continue the momentum streak?
- Does a Wait break the streak?
- Does a failed Read/Recover break the streak?

**Recommendation**: Read/Recover DO affect momentum (they're rolls). Wait breaks the streak (it's a skip). This should be clarified in #43's AC. Non-blocking — implementer can make a reasonable default, but explicit is better.

## Unstated Requirements
- Wave 0 prerequisite PR(s) must ship before any feature agent starts
- Closed-but-unimplemented issues (#43, #46, #47, #49, #50, #38) must be reopened or replaced
- PRs #122 and #123 (#52 trap taint) should be merged — approved work sitting idle
- `DialogueOption` needs `HasWeaknessWindow` + `IsHorninessForced` fields (per #60)
- Momentum interaction with Read/Recover/Wait should be specified in #43

## Domain Invariants
- `StatBlock` must remain immutable for roll resolution — shadow mutation via separate tracker only
- `RollResult.Total` must include ALL bonuses (stat + level + external) — `IsSuccess` and `MissMargin` are derived from it
- Interest delta composition is additive, never multiplicative
- `GameSession` turn sequencing must hold for all action types (Speak, Read, Recover, Wait)
- `IGameClock` must be injectable for deterministic testing
- Energy is per in-game day, owned by `IGameClock`, not duplicated elsewhere
- Horniness level = shadow stat + time-of-day modifier (single source of truth, no dice roll)
- All per-session counters (trope trap count, honesty success, stats history) reset per new GameSession

## Gaps

### BLOCKING (must resolve before feature agents start)
1. **#130**: Wave 0 prerequisites not yet implemented — `SessionShadowTracker`, `IGameClock`, `RollEngine.externalBonus`, `RollEngine.ResolveFixedDC`, `TrapState.HasActive`, `GameSessionConfig`
2. **#126**: 6 issues (#43, #46, #47, #49, #50, #38) closed with specs only — no implementation code. Must reopen or create new implementation issues.

### Non-Blocking
3. **#127**: PRs #122/#123 approved but unmerged — free work sitting idle
4. **#60**: `DialogueOption` missing `HasWeaknessWindow` + `IsHorninessForced` — should be in Wave 0
5. **Momentum + non-Speak actions**: Not specified in #43 (advisory)

### Could Defer
6. **#56 (ConversationRegistry)**: Deepest dependency chain, most complex. Self-contained enough to be a separate sprint.
7. **#55 (PlayerResponseDelay)**: Pure function, nothing depends on it.

## Recommendations
1. **Create and ship Wave 0 prerequisite issue(s)** per #130 — this has been the top recommendation for 4 consecutive attempts and remains the single highest-leverage action
2. **Reopen #43, #46, #47, #49, #50, #38** with bodies noting "spec complete, implementation needed"
3. **Merge PRs #122 and #123** (#52 trap taint — approved twice, zero errors)
4. **Close the 10 resolved vision concerns** listed above to reduce noise
5. **Add momentum interaction note to #43** — specify Read/Recover affect streak, Wait breaks it

## VERDICT: CLEAN

All vision concerns are now well-specified and embedded directly into the issue bodies that implementation agents will read. The three issues edited this pass (#44, #45, #48) were the last ones missing explicit references to SessionShadowTracker and #130. The structural blockers (#130 Wave 0, #126 closed-but-unimplemented) are well-documented and actionable — they require PO/orchestrator action, not further vision review. No new vision concerns needed. The existing concern set is comprehensive, specific, and embedded where agents will find it.

Sprint may proceed once the orchestrator addresses the two BLOCKING gaps.
