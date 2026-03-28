# Vision Review — Sprint 2 (RPG Rules Complete) — Attempt 4

## Alignment: ⚠️

This sprint is correctly scoped to complete all remaining RPG mechanical systems (§5–§15) and the async-time layer. It serves the product vision of a fully playable engine. However, 17 feature issues with 4-level dependency chains remain high-risk for cascading failures. The 19 open vision concerns represent thorough coverage — most are well-specified and actionable. Three concerns require PO decisions before safe implementation.

## Vision Concern Audit

### Well-Specified (no edits needed) — 15 issues
| # | Title | Status |
|---|---|---|
| #57 | Sprint scope is massive | Advisory — well-specified split recommendation |
| #59 | #53 Lukewarm reference | ✅ Resolved — #53 body already updated with correct 6-value table |
| #60 | ILlmAdapter breaking changes | ✅ Specific AC — flags DialogueOption gap (HasWeaknessWindow, IsHorninessForced) not in #63 |
| #62 | QA before features | Advisory — recommends #38 in early wave |
| #64 | TrapState.HasActive missing | Clear — trivial addition, specific fix |
| #65 | DC 12 for Read/Recover | Clear — 3 options with recommended approach (a) |
| #66 | Shadow growth tracking counters | Clear — lists all 6 required counters |
| #67 | IGameClock interface | Clear — specific interface + FixedGameClock pattern. #56 already references it |
| #68 | Roll bonus composition | Clear — recommends `rollBonus` param with code sample |
| #69 | TurnResult expansion | Clear — specific code sample, lists all 7 new fields |
| #70 | C# record types | ✅ Resolved — #47, #55, #56 already updated to sealed class |
| #71 | Chaos threshold undefined | ✅ Resolved — #55 already updated with "Chaos base stat ≥ 4" |
| #73 | #49/#50 missing #63 dependency | Clear — #49 and #50 need dependency edit |
| #75 | Energy system ownership | Clear — recommends GameClock owns energy |
| #39, #40 | Test gaps | Clear — specific test requirements |

### Updated This Pass — 1 issue
| # | Title | Change |
|---|---|---|
| #58 | StatBlock immutability | **Added sequencing problem**: #43 (Read/Recover) also needs shadow mutation (Overthinking +1 on Read fail), not just #44. Decision must happen before #43, creating: #58 resolution → #43 → #44 chain. Recommended approach (b) — SessionShadowTracker. |

### Require PO Decision — 3 issues
| # | Title | What PO Must Decide |
|---|---|---|
| #61 | #42 risk tier re-introduction | Are Hard +1 / Bold +2 values final for prototype? If yes, close #30. If not, mark provisional. |
| #74 | Horniness roll vs shadow stat | Is Horniness level = shadow stat + time-of-day modifier (no dice roll)? Or is dice.Roll(10) intentional? |
| #75 | Energy ownership | Confirm: GameClock owns energy, ConversationRegistry delegates to IGameClock.ConsumeEnergy()? |

### Stale (from Sprint 1/3/6, superseded) — 10 issues
| # | Title | Notes |
|---|---|---|
| #4, #5, #8, #9, #10, #15, #16, #17 | Sprint 1/3 concerns | Referenced issues (#1–#7) are merged. Concerns addressed or superseded. |
| #28, #29, #30, #31 | Sprint 6 concerns | #28 → FailureScale implemented. #29 → shadow growth now in #44. #30 → re-scoped as #42/#61. #31 → GameSession split done. |

These are not blocking but add noise. PO should review and close the resolved ones.

## Data Flow Traces

### Feature: Read Action (#43)
- Player selects Read → `GameSession.ReadAsync()` → `RollEngine.Resolve(SA, player, ???, traps, level, registry, dice)` → check vs DC 12
- **⚠️ BLOCKING: RollEngine.Resolve computes DC from defender StatBlock** — there's no fixed DC parameter. #65 flags this. Implementer needs either a new overload or a dummy StatBlock hack.
- On fail: Interest -1 via `InterestMeter.Apply(-1)` → **Overthinking +1** via ??? 
- **⚠️ BLOCKING: StatBlock._shadow is private readonly** — #58 flags this. No mutation path exists. Must be resolved before #43 starts.
- Required fields: SA stat, player StatBlock, fixed DC 12, TrapState, level, dice
- Missing: Fixed DC parameter on RollEngine, shadow mutation mechanism

### Feature: Risk Tier Bonus (#42)
- Player resolves turn → `RollEngine.Resolve()` → `RollResult` → compute `Need = DC - (statMod + levelBonus)`
- Need ≤5 = Safe, 6–10 = Medium, 11–15 = Hard (+1), ≥16 = Bold (+2)
- `GameSession.ResolveTurnAsync` adds bonus to interest delta
- Required fields: RollResult needs `RiskTier` property (not currently present)
- ⚠️ RollResult constructor has 9 params, adding RiskTier makes 10 — acceptable but note #69 wants coordinated expansion

### Feature: Combo System (#46) → Tell Bonus (#50) → Callback Bonus (#47)
- All three inject a hidden roll bonus into `RollEngine.Resolve`
- **⚠️ No `rollBonus` parameter exists on RollEngine.Resolve** — #68 flags this
- Flow: GameSession accumulates bonuses (combo +1, tell +2, callback +1/+2/+3) → passes sum to RollEngine
- RollResult needs `ExternalBonus` field for UI display
- These three features share the same mechanism — must be designed together

### Feature: Shadow Thresholds (#45)
- `GameSession.StartTurnAsync` → check each shadow stat vs thresholds (6/12/18) → apply disadvantage, restrict options, modify starting interest
- Flow: StatBlock.GetShadow(type) → threshold level → modify advantage flags, DialogueContext
- Required: `DialogueContext.ShadowThresholds` (added by #63 expansion)
- ⚠️ Horniness threshold effects overlap with #51 — need clear precedence rules

### Feature: ConversationRegistry (#56)
- Player manages multiple chats → `ConversationRegistry.FastForward()` → advance `IGameClock` → check ghost/fizzle/decay on all sessions → return next reply
- Required: IGameClock (#67), GameSession per conversation, cross-chat shadow bleed
- Flow: FastForward → find earliest pending reply → advance clock → iterate all sessions → apply decay/ghost/fizzle → return active session
- ⚠️ Cross-chat shadow bleed writes to shadow stats of OTHER sessions — needs SessionShadowTracker (#58) resolved

## Unstated Requirements
- **Shadow mutation mechanism must exist before #43** — the implementer of Read/Recover will need Overthinking +1 on fail, which requires mutable shadow tracking
- **RollEngine fixed-DC overload must exist before #43** — Read and Recover both use DC 12, which RollEngine can't compute from a defender StatBlock
- **DialogueOption expansion must ship with #63** — #49 needs `HasWeaknessWindow`, #51 needs `IsHorninessForced`; adding them later means touching the constructor twice
- **Trap JSON data files must be created** — #52 references `data/traps/traps.json` which doesn't exist in the repo; either create sample data or clarify the implementer creates it
- **The user expects shadow growth to be visible** — if Overthinking grows on Read fail, TurnResult (or ReadResult) should indicate this to the UI

## Domain Invariants
- StatBlock must remain stable during a single roll resolution — no mid-roll shadow mutation
- Interest clamped to [0, 25] at all times regardless of delta magnitude
- Roll bonuses (combo, tell, callback) are hidden from displayed success percentage but affect the actual roll
- A trap that taints LLM output must taint ALL message types (dialogue options, delivery, opponent response), not selectively
- Shadow growth events persist across the session boundary — they modify the character's permanent state
- Energy is a per-day resource tied to the game clock, not per-session

## Gaps

### Missing from sprint
- **Shadow mutation mechanism** — Neither #43 nor #44 specifies WHO creates the `SessionShadowTracker` (or equivalent). #58 is a vision concern but there's no implementation issue for it. Recommend: add AC item to #43 (or a new tiny issue) for creating the shadow mutation wrapper.
- **RollEngine fixed-DC overload** — #65 identifies the problem but there's no implementation issue. Recommend: add AC item to #43 or create a preparatory issue.
- **DialogueOption expansion in #63** — #60 flags `HasWeaknessWindow` and `IsHorninessForced` as missing from #63's AC. The #63 implementer should add these.
- **Trap data files** — #52 needs `data/traps/traps.json` to exist. No issue creates this data.

### Unnecessary (could defer)
- **#56 ConversationRegistry** — This is the most complex piece and sits at the top of the dependency chain. It has 4 dependencies (#53, #54, #44). At prototype maturity, a single-session game is playable without it. Consider deferring to Sprint 3.
- **#55 PlayerResponseDelay** — Requires GameClock (#54) and is a polish mechanic. Playable without it.

### Assumptions needing validation
- **#42 risk tier values** — PO has not confirmed Hard +1 / Bold +2 are final (#61)
- **#51 Horniness source** — PO has not confirmed whether Horniness = shadow stat + time modifier vs dice roll (#74)
- **#75 energy ownership** — PO has not confirmed GameClock owns energy state

## Recommendations
1. **Resolve #58 (StatBlock mutability) before #43 starts** — the architect should decide on approach (b) SessionShadowTracker and add it as AC to #43 or a preparatory issue. This is **BLOCKING** for #43.
2. **Add RollEngine fixed-DC overload as AC to #43** — per #65, option (a) is cleanest. The architect should confirm.
3. **Expand #63 AC to include DialogueOption fields** — add `HasWeaknessWindow` (bool, false) and `IsHorninessForced` (bool, false) per #60.
4. **Edit #49 and #50** to add `Depends on: #63` per #73.
5. **PO to decide #61, #74, #75** — these three PO-decision items should not block the sprint but the affected issues (#42, #51, #54/#56) should note their values as provisional.
6. **Consider deferring #55 and #56** to Sprint 3 — they're the highest-complexity, highest-dependency items and aren't needed for a playable single-session prototype.

## Verdict: **ADVISORY**

The vision concerns are comprehensive and well-specified. Most have already been incorporated into the feature issue bodies (e.g., #53 fixed Lukewarm, #47/#55/#56 fixed record types, #56 uses IGameClock). Three items need PO confirmation (#61, #74, #75) but can proceed with provisional values.

The **one near-blocking gap** is #58 (shadow mutation mechanism): #43 needs Overthinking +1 on Read fail but StatBlock is immutable and no implementation issue exists for the mutation wrapper. The architect must resolve this before the #43 agent starts. Updated #58 to make this sequencing requirement explicit.

Sprint can proceed if:
1. Architect resolves shadow mutation approach before #43
2. #63 AC expanded to include DialogueOption fields
3. #49/#50 dependencies updated
