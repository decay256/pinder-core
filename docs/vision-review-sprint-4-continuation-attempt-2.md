# Vision Review — Sprint 4: RPG Rules Complete — Continuation (Attempt 2)

## Alignment: ⚠️

The sprint direction remains correct — completing RPG mechanical systems (shadows, combos, XP, async-time) is the right strategic work. Since attempt 1, **two concerns are now resolved** (#69 TurnResult expansion merged via PR #117, #61 RiskTier implemented via PR #119, #73 stub types available via PR #114). The remaining 25 open vision concerns have been reviewed for specificity — all have actionable acceptance criteria. The sprint can proceed **after Wave 0 prerequisites ship**.

## Concern Review Summary

### ✅ Resolved Since Last Review (3 concerns)
| # | Title | Resolution |
|---|---|---|
| #69 | TurnResult expansion | PR #117 merged — all 7 new fields present |
| #61 | RiskTier re-introduction without PO confirmation | PR #119 merged — Hard +1, Bold +2 implemented |
| #73 | #49/#50 should depend on #63 | PR #114 merged — stub types available |

Issues edited to note resolved status. PO can close at their discretion.

### ⚠️ Still Open — Well-Specified (no edits needed)
| # | Title | Blocking? |
|---|---|---|
| #130 | Wave 0 prerequisites needed | **YES** — gates all feature work |
| #128 | Shadow mutation architecture unresolved (3 sprints) | **YES** — blocks #43, #44, #45, #51 |
| #129 | RollResult.Total excludes externalBonus | **YES** — blocks #46, #47, #50 |
| #126 | 6 closed issues have no implementation code | **YES** — sprint assumes done work that isn't |
| #127 | #52 PRs approved but unmerged | Housekeeping — free work sitting idle |
| #65 | RollEngine fixed-DC overload missing | Folded into #130 Wave 0 |
| #64 | TrapState.HasActive missing | Folded into #130 Wave 0 |
| #82 | GameSession constructor needs injection points | Folded into #130 Wave 0 |
| #58 | StatBlock immutability vs shadow growth | Folded into #128 |
| #79 | InterestMeter starting value not configurable | Part of #45 implementation |
| #67 | IGameClock interface needed | Part of #54 implementation |
| #66 | Shadow growth session counters unspecified | Part of #44 architect scope |
| #68 | Combo +1 composition unclear | Part of #46 architect scope |
| #75 | Energy system ownership unclear | Part of #54/#56 architect scope |
| #74 | Horniness roll vs shadow stat conflict | Needs PO input |
| #81 | PlayerResponseDelay time source unclear | Needs PO input |
| #71 | 'Chaos >= high' threshold undefined | Needs PO input |
| #70 | Record types in issue bodies vs netstandard2.0 | Implementer guidance |
| #80 | #43 dependency on #42 incorrect | Dependency correction |
| #62 | Test gaps should be addressed early | Wave scheduling |
| #57 | Sprint scope massive (14 issues) | Advisory |

### ✅ Updated for Clarity (3 concerns edited)
| # | What changed |
|---|---|
| #69 | Added "Status: ✅ RESOLVED" with verification of all 7 fields present |
| #61 | Added "Status: ✅ RESOLVED" with verification of RiskTier implementation |
| #60 | Updated to note #63 is merged; remaining gap (DialogueOption fields) now references Wave 0 as the right place to add them |
| #73 | Added "Status: ✅ RESOLVED" — stub types available after PR #114 merge |

## Data Flow Traces (unchanged from attempt 1 — all gaps still present)

### Shadow Growth: GameSession → StatBlock mutation
- `ResolveTurnAsync()` → detect growth trigger → increment shadow → `TurnResult.ShadowGrowthEvents`
- **⚠️ BLOCKING**: `StatBlock._shadow` is `private readonly Dictionary`. No mutation API. No `SessionShadowTracker`. See #128.

### External Roll Bonuses: Combo/Callback/Tell → RollEngine
- `GameSession` accumulates bonuses → passes to `RollEngine.Resolve()` → `RollResult.Total`
- **⚠️ BLOCKING**: `RollEngine.Resolve()` has no `externalBonus` parameter. `Total = die + stat + level` only. See #129.

### Fixed-DC Rolls: Read/Recover → RollEngine
- Player picks Read/Recover → `RollEngine.Resolve()` with DC 12
- **⚠️ BLOCKING**: No fixed-DC overload exists. See #65, bundled in #130.

## Unstated Requirements (unchanged)
- Wave 0 prerequisite PR(s) must ship before any feature agent starts
- Closed-but-unimplemented issues (#43, #46, #47, #49, #50, #38) must be reopened or replaced
- PRs #122 and #123 (#52 trap taint) should be merged — approved work sitting idle
- DialogueOption needs `HasWeaknessWindow` + `IsHorninessForced` fields (see updated #60)

## Domain Invariants (unchanged)
- `StatBlock` must remain immutable for roll resolution — shadow mutation via separate tracker
- `RollResult.Total` must include ALL bonuses — `IsSuccess` and `MissMargin` derived from it
- Interest delta composition is additive, never multiplicative
- `GameSession` turn sequencing must hold for all action types (Speak, Read, Recover, Wait)
- `GameClock` must be injectable via `IGameClock` for deterministic testing
- Energy is per in-game day, not per session

## Gaps

### BLOCKING (must resolve before feature agents start)
1. **#130**: Wave 0 prerequisites not created as issues yet — need `SessionShadowTracker`, `IGameClock`, `RollEngine.externalBonus`, `RollEngine.ResolveFixedDC`, `TrapState.HasActive`, `GameSessionConfig`
2. **#126**: 6 issues closed with specs only — no implementation code. Must reopen or create new implementation issues.
3. **#128/#58**: Shadow mutation architecture decision needed before #43 can start (it needs Overthinking +1 on Read fail)

### Non-Blocking (PO input helpful but agents can use defaults)
4. **#74**: Horniness — is it shadow stat + time modifier, or dice roll + time modifier? Agent can default to shadow stat.
5. **#81**: PlayerResponseDelay — wall-clock vs game-clock? Agent can default to host-provided TimeSpan.
6. **#71**: 'Chaos >= high' — undefined threshold. Agent can default to `Chaos base stat >= 4`.

### Could Defer to Next Sprint
7. **#56 (ConversationRegistry)**: Deepest dependency chain, most complex. Self-contained subsystem.
8. **#55 (PlayerResponseDelay)**: Pure function, nothing depends on it. Low urgency.

## Recommendations

1. **Create Wave 0 prerequisite issue(s)** per #130's specification — this is the single highest-leverage action
2. **Merge PRs #122 and #123** — #52 trap taint is done, just needs merge
3. **Reopen #43, #46, #47, #49, #50, #38** with updated bodies noting "spec complete, implementation needed"
4. **Defer #56 and #55** to a dedicated async-time sprint — reduces scope from 14 to 12 issues
5. **Resolve #74 (Horniness source)** — PO decision, 1-line answer needed

## VERDICT: ADVISORY

All 28 open vision concerns now have specific acceptance criteria. Three concerns resolved since attempt 1. The sprint's **three BLOCKING gaps** (#130 Wave 0 prereqs, #126 closed-but-unimplemented issues, #128 shadow mutation) are well-defined with clear resolution paths. No new concerns discovered — the existing concern set is comprehensive. Sprint can proceed once the orchestrator addresses the BLOCKING items.
