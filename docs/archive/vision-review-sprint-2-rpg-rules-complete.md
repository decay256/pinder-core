# Vision Review — Sprint 2: RPG Rules Complete (Attempt 1)

## Alignment: ⚠️

This sprint is the correct strategic direction — implementing the remaining RPG mechanical systems (§7 shadows, §8 non-Speak actions, §10 XP, §15 advanced mechanics, async-time) is the logical next step after the core game loop shipped. However, the sprint carries **significant architectural risk** from unresolved prerequisite decisions. Five open vision concerns from the previous review (#57–#62) remain unaddressed, and this review identifies five additional gaps (#64–#69). The most critical: StatBlock immutability (#58), RollEngine fixed-DC API for Read/Recover (#65), the flat roll bonus API needed by three features (#68), and TurnResult expansion merge conflicts (#69).

The sprint added #63 (ILlmAdapter expansion) as a prerequisite, which correctly addresses #60. That's good engineering. But the same pattern needs to be applied to TurnResult (#69) and RollEngine's API (#65, #68) before feature implementation begins.

## Data Flow Traces

### #42: Risk Tier → Interest Bonus
- `RollEngine.Resolve()` → `RollResult` → `GameSession.ResolveTurnAsync` computes `need = DC - (StatModifier + LevelBonus)` → maps to RiskTier enum → adds +1 (Hard) or +2 (Bold) on success → `InterestMeter.Apply(totalDelta)`
- Required new: `RollResult.RiskTier` enum property, computed at construction from `DC - StatModifier - LevelBonus`
- ✅ Data flows cleanly. `RollResult` already has all fields needed to derive risk tier.

### #43: Read/Recover/Wait → Interest/Trap State
- Player chooses Read → `GameSession.ReadAsync()` → roll SA vs **fixed DC 12** → success reveals `InterestMeter.Current` + opponent modifiers; fail → `InterestMeter.Apply(-1)` + Overthinking +1
- ⚠️ **API GAP**: `RollEngine.Resolve()` always computes DC from `defender.GetDefenceDC(stat)`. No fixed DC overload exists. See #65.
- ⚠️ `TrapState.HasActive` referenced in AC but property doesn't exist. See #64.

### #44: Shadow Growth → StatBlock Mutation
- `GameSession.ResolveTurnAsync()` → detect growth event → increment shadow stat on `_player.Stats`
- ⚠️ **BLOCKING**: `StatBlock._shadow` is `private readonly Dictionary`. No public mutation API. See #58.
- ⚠️ Requires 6+ per-session tracking counters (TropeTrap count, Honesty success flag, stats-used history, etc.) not specified in GameSession. See #66.

### #49/#50: Weakness Windows + Tells → Structured OpponentResponse
- `ILlmAdapter.GetOpponentResponseAsync()` → `OpponentResponse { MessageText, WeaknessWindow?, Tell? }` → stored in GameSession → applied on next turn's roll
- ✅ Addressed by #63 (ILlmAdapter expansion). OpponentResponse type includes both fields.

### #46/#47/#50: Flat Roll Bonuses → RollEngine
- Combo Triple (+1 all rolls), Callback (+1/+2/+3), Tell (+2) all need to inject a flat bonus into the roll
- ⚠️ **API GAP**: `RollEngine.Resolve` has no `rollBonus` parameter. Three issues need the same capability but none specifies how. See #68.

### #52: Trap Taint → LLM Contexts  
- `TrapState.AllActive` → `.Definition.LlmInstruction` → injected into `DialogueContext.ActiveTrapInstructions`, `DeliveryContext.ActiveTrapInstructions`, `OpponentContext.ActiveTrapInstructions`
- ✅ #63 adds `ActiveTrapInstructions` fields to all context types. `DeliveryContext` already uses instructions (not names). #52 just needs to wire DialogueContext and OpponentContext similarly.

### #56: ConversationRegistry → Multi-Session Orchestration
- `ConversationRegistry.FastForward()` → find earliest pending reply → advance GameClock → check ghost/fizzle on all sessions → apply decay → return active session
- ⚠️ `GameSession` has no public accessor for `InterestMeter` or last activity timestamp. Registry needs read access to both.
- ⚠️ Depends on #53 (timing), #54 (clock), #44 (shadow growth) — deepest dependency chain in the sprint.

## Unstated Requirements

- **Unified roll bonus API**: Three features (#46 Triple, #47 Callback, #50 Tell) all need to add flat bonuses to rolls. The player expects these to compose (Triple +1 AND Tell +2 = +3). This API should be designed once, not three times independently.
- **TurnResult must carry all new fields without merge conflicts**: 7+ issues add fields to TurnResult. Players expect the UI to show combos, XP, shadow events, tell reads — all in one turn summary. A single expansion PR (like #63 for ILlmAdapter) prevents merge hell.
- **Shadow growth must be visible**: If shadows grow during a session (#44), the player expects to see it ("Dread +1 — you felt the sting of rejection"). `TurnResult.ShadowGrowthEvents` is mentioned in #44 but not defined as a type.
- **Read action should show opponent's actual stats**: #43 says "reveal exact Interest + opponent modifiers" on successful Read. This implies exposing `StatBlock` data the player hasn't seen before — a UI/UX contract that isn't specified.

## Domain Invariants

- `RollResult` must remain immutable — risk tier, bonuses computed at construction time, not mutated
- Shadow growth must not affect the current roll's resolution (apply AFTER the roll)
- Interest delta = SuccessScale/FailureScale + risk bonus + momentum + combo bonus + any future modifiers — these MUST compose additively
- `GameSession` turn sequencing (StartTurn → Resolve alternation) must hold even with Read/Recover/Wait as alternative actions
- `RollEngine` remains stateless — no session-aware logic leaks into it
- A trap's LLM instruction text must be identical regardless of which context type carries it

## Gaps

### Missing (filed as vision concerns)
- **#64** — `TrapState.HasActive` referenced in #43 but doesn't exist (minor, easy fix)
- **#65** — `RollEngine.Resolve` has no fixed-DC overload; #43 Read/Recover need DC 12 (architectural)
- **#66** — #44 shadow growth requires 6+ tracking counters not specified in GameSession design
- **#67** — GameClock (#54) should be injectable (IGameClock) for deterministic testing
- **#68** — Three features need flat roll bonus injection into RollEngine — design once, not thrice
- **#69** — TurnResult needs coordinated expansion like ILlmAdapter got via #63

### Still open from previous review
- **#58** — StatBlock immutability blocks #44 → #45 → #48 → #51 → #56 chain. **MOST CRITICAL.**
- **#59** — #53 references Lukewarm InterestState (trivial fix, still not applied)
- **#61** — Risk tier values (#42) were previously descoped per #30, re-introduced without PO confirmation

### Could defer
- **#56 (ConversationRegistry)**: Most complex issue, deepest dependency chain (depends on #53, #54, #44). Self-contained subsystem. Deferring to Sprint 3 significantly reduces sprint risk.
- **#55 (PlayerResponseDelay)**: Depends on #54 (GameClock). Could ship with #56 in a dedicated async-time sprint.

### Assumptions to validate
- #42 risk tier thresholds (Need ≤5/6-10/11-15/≥16) assumed final — previously explicitly descoped (#30)
- #43 assumes SA is the attacking stat for Read/Recover — should be explicit about which SA (attacker's SA modifier + level bonus vs DC 12)
- #44 assumes "same opener twice in a row" is detectable from history — requires cross-session persistence not in scope

## Wave Plan

```
Wave 1: #63, #38
Wave 2: #42, #49, #50, #52, #53, #54
Wave 3: #43, #46, #47, #55
Wave 4: #44
Wave 5: #45, #48, #51
Wave 6: #56
```

Rationale:
- **Wave 1**: #63 is the prerequisite (ILlmAdapter expansion). #38 (QA audit) has no dependencies and should run early.
- **Wave 2**: #42 (risk tier) is root dependency for many features. #49, #50 depend only on #63. #52, #53, #54 are independent subsystems.
- **Wave 3**: #43 depends on #42 (and needs #65 resolved). #46, #47 depend on #42. #55 depends on #54.
- **Wave 4**: #44 depends on #43 and needs #58 resolved. Most blocked issue.
- **Wave 5**: #45 depends on #44. #48 depends on #42+#43+#44. #51 depends on #45.
- **Wave 6**: #56 depends on #53+#54+#44. Most complex, most dependencies.

## Role Assignment Check

All roles are correctly assigned:
- #38: qa-engineer ✅ (test audit)
- All others: backend-engineer ✅ (pure C# engine work)

No corrections needed.

## Recommendations

1. **Resolve #58 (StatBlock mutability) before Wave 4** — this is the longest-blocking decision. Recommend option (b): `SessionShadowTracker` wrapper that holds mutable shadow deltas without touching StatBlock's immutable contract.
2. **Expand #63 scope (or create #69-fix) to include TurnResult expansion** — add all nullable fields at once before feature PRs land.
3. **Add `int rollBonus` parameter to `RollEngine.Resolve`** (#68) — design this in Wave 1 so #46, #47, #50 can all use it cleanly in Waves 2-3.
4. **Add `RollEngine.ResolveFixedDC` overload** (#65) — needed by Wave 3 (#43).
5. **Fix #53's Lukewarm reference** (#59) — trivial edit, prevents implementer confusion.
6. **Consider deferring #55 and #56 to Sprint 3** — the async-time subsystem (#53, #54, #55, #56) is self-contained. Shipping core RPG mechanics (#42-#51) without async-time reduces sprint risk while still delivering high value.

## VERDICT: ADVISORY

The sprint direction is correct and the issues are well-specified individually. Six new vision concerns filed (#64–#69). The **StatBlock immutability question (#58)** remains the most critical blocker — it was raised last review and still has no resolution. The **RollEngine API gaps (#65, #68)** are new architectural decisions that must be made before Waves 2-3. The **TurnResult expansion (#69)** is a process concern that prevents merge conflicts.

No single concern is sprint-blocking on its own, but collectively they represent significant architectural decisions that should be resolved in Wave 1 alongside #63. The sprint should proceed with these concerns added to the backlog.
