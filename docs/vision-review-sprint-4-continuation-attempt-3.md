# Vision Review — Sprint 4: RPG Rules Complete — Continuation (Attempt 3)

## Alignment: ⚠️

Sprint direction remains correct. Since attempt 2, three resolved concerns were marked (#69, #61, #73) and no new prerequisite infrastructure has shipped. The **same three blocking gaps** persist: shadow mutation architecture, RollEngine externalBonus, and fixed-DC overloads. All are bundled in #130 (Wave 0). This attempt focused on **editing issue bodies** to embed vision concern resolutions directly into the issues agents will read, rather than relying on cross-referencing.

## Issues Edited This Pass

| Issue | What Changed | Vision Concern Resolved |
|---|---|---|
| #54 (GameClock) | Added `IGameClock` interface requirement, `FixedGameClock` test helper, energy ownership note | #67, #75 |
| #51 (Horniness Rizz) | Removed `dice.Roll(10)`, replaced with shadow stat + time-of-day modifier formula | #74 |
| #43 (Read/Recover/Wait) | Changed dependency from #42 to #130, added notes on fixed-DC, TrapState.HasActive, SessionShadowTracker | #80, #65, #64, #58 |
| #55 (PlayerResponseDelay) | Clarified TimeSpan is caller-provided, Evaluator is pure function, dependency on #54 is transitive via ConversationRegistry | #81 |
| #56 (ConversationRegistry) | Added note that `ConsumeEnergy()` delegates to `IGameClock`, added #130 dependency | #75 |
| #46 (Combo system) | Added #130 dependency, noted The Triple's +1 flows through `externalBonus` | #68, #129 |
| #47 (Callback bonus) | Added #130 dependency, noted callback bonus flows through `externalBonus` | #129 |
| #50 (Tells) | Added #130 dependency, noted +2 bonus flows through `externalBonus`, referenced existing Tell type from #63 | #129 |

## Vision Concerns Status Summary

### ✅ Resolved (can be closed by PO)
| # | Title | Resolution |
|---|---|---|
| #69 | TurnResult expansion | PR #117 merged |
| #61 | RiskTier re-introduction | PR #119 merged |
| #73 | #49/#50 depend on #63 | PR #114 merged |
| #71 | 'Chaos >= high' undefined | Clarified in #55 body: Chaos base stat ≥ 4 |
| #81 | PlayerResponseDelay time source | Clarified in #55 body: pure function, caller provides TimeSpan |
| #74 | Horniness roll vs shadow stat | Clarified in #51 body: shadow stat + time-of-day modifier, no dice roll |
| #80 | #43 wrong dependency | Fixed in #43 body: now depends on #130 |
| #59 | Lukewarm in #53 | Already fixed in #53 body |
| #70 | Record types | Already fixed in #47, #55, #56 bodies |

### ⚠️ Still Open — Well-Specified (no edits needed)
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
| #58 | StatBlock immutability | Folded into #128/#130, embedded in #43 body |
| #79 | InterestMeter starting value | Part of #45 implementation |
| #66 | Shadow growth counters | Part of #44 architect scope |
| #68 | Combo +1 composition | Now embedded in #46 body |
| #60 | DialogueOption expansion | Part of Wave 0 or #49/#51 |
| #62 | Test gaps before features | Wave scheduling advisory |
| #57 | Sprint scope massive | Advisory |
| #39 | SuccessScale zero coverage | Part of #38 QA |
| #40 | DateSecured test missing | Part of #38 QA |

## Data Flow Traces (unchanged — all gaps still present in code)

### Shadow Growth: GameSession → shadow mutation
- `ResolveTurnAsync()` → detect trigger → increment shadow → `TurnResult.ShadowGrowthEvents`
- **⚠️ BLOCKING**: `StatBlock._shadow` is `private readonly`. No `SessionShadowTracker` exists. See #130.

### External Roll Bonuses: Combo/Callback/Tell → RollEngine
- `GameSession` accumulates bonuses → passes to `RollEngine.Resolve(externalBonus)` → `RollResult.Total`
- **⚠️ BLOCKING**: `RollEngine.Resolve()` has no `externalBonus` param. See #130.

### Fixed-DC Rolls: Read/Recover → RollEngine
- Player picks Read/Recover → `RollEngine.Resolve()` with DC 12
- **⚠️ BLOCKING**: No fixed-DC overload. See #130.

## Unstated Requirements
- Wave 0 prerequisite PR(s) must ship before any feature agent starts
- Closed-but-unimplemented issues (#43, #46, #47, #49, #50, #38) must be reopened or replaced with new implementation issues
- PRs #122 and #123 (#52 trap taint) should be merged — approved work sitting idle
- `DialogueOption` needs `HasWeaknessWindow` + `IsHorninessForced` fields (per #60)

## Domain Invariants
- `StatBlock` must remain immutable for roll resolution — shadow mutation via separate tracker
- `RollResult.Total` must include ALL bonuses — `IsSuccess` and `MissMargin` derived from it
- Interest delta composition is additive, never multiplicative
- `GameSession` turn sequencing must hold for all action types (Speak, Read, Recover, Wait)
- `IGameClock` must be injectable for deterministic testing
- Energy is per in-game day, owned by `IGameClock`, not duplicated elsewhere
- Horniness level = shadow stat + time-of-day modifier (single source of truth)

## Gaps

### BLOCKING (must resolve before feature agents start)
1. **#130**: Wave 0 prerequisites not yet implemented — `SessionShadowTracker`, `IGameClock`, `RollEngine.externalBonus`, `RollEngine.ResolveFixedDC`, `TrapState.HasActive`, `GameSessionConfig`
2. **#126**: 6 issues (#43, #46, #47, #49, #50, #38) closed with specs only — no implementation code. Must reopen or create new implementation issues.

### Non-Blocking
3. **#127**: PRs #122/#123 approved but unmerged — free work sitting idle
4. **#60**: `DialogueOption` missing `HasWeaknessWindow` + `IsHorninessForced` — should be in Wave 0

### Could Defer
5. **#56 (ConversationRegistry)**: Deepest dependency chain, most complex. Self-contained.
6. **#55 (PlayerResponseDelay)**: Pure function, nothing depends on it.

## Recommendations
1. **Create and ship Wave 0 prerequisite issue(s)** per #130 — this is the single highest-leverage action and has been the top recommendation for 3 attempts
2. **Reopen #43, #46, #47, #49, #50, #38** with bodies noting "spec complete, implementation needed"
3. **Merge PRs #122 and #123** (#52 trap taint — approved, zero errors)
4. **Close the 9 resolved vision concerns** listed above to reduce noise

## VERDICT: ADVISORY

The vision concerns are now well-specified and embedded directly into the issue bodies agents will read. The sprint's structural problems are well-documented. However, the **same two blockers from attempt 1 and 2 persist**: Wave 0 prerequisites (#130) and closed-but-unimplemented issues (#126). Until these are addressed, feature agents will hit walls. No new vision concerns needed — the existing set is comprehensive and actionable.
