# Vision Review — Sprint 5: RPG Rules Complete (Attempt 2)

## Alignment: ✅

This sprint is well-aligned with the product vision. The continuation sprint has matured significantly since the first vision review: Wave 0 now has a concrete implementation issue (#139), all feature contracts are written, and the wave dependency ordering is sound. The remaining work — implementing the 12 feature issues plus QA — is exactly the right next step to complete the RPG engine's mechanical layer. The key risk (cascading failures from the massive scope) is mitigated by the wave plan and the existence of detailed contracts for every feature.

## Vision Concern Triage

### Resolved (18 concerns)
| # | Title | Resolution |
|---|---|---|
| #58 | StatBlock immutability | #139 defines SessionShadowTracker |
| #59 | #53 Lukewarm reference | #53 merged without Lukewarm |
| #60 | ILlmAdapter breaking changes | PR #114 merged; DialogueOption expansion in #49/#51 contracts |
| #61 | #42 descoped then reintroduced | #42 merged (PR #119) |
| #64 | TrapState.HasActive missing | #139 includes it |
| #65 | RollEngine fixed DC missing | #139 includes ResolveFixedDC |
| #66 | #44 session counters | Contract defines SessionCounters class |
| #67 | IGameClock interface needed | #139 defines IGameClock |
| #68 | The Triple stacking | Contracts clarify: additive via externalBonus |
| #71 | Chaos >= high undefined | Contract #55 defines Chaos base ≥ 4 |
| #74 | Horniness roll vs shadow stat | Contract #51: shadow + time-of-day, no dice roll |
| #75 | Energy system ownership | Contract #54: GameClock owns energy |
| #79 | InterestMeter starting value | #139 includes constructor overload |
| #80 | #43 wrong dependency | #139 replaces old dependency chain |
| #81 | PlayerResponseDelay time source | Contract #55: pure function, host provides TimeSpan |
| #82 | GameSession constructor | #139 includes GameSessionConfig |
| #127 | #52 unmerged | #52 closed, PRs merged |
| #128, #129, #130, #136, #137 | Wave 0 infrastructure | All resolved by #139 |

### Still Actionable (6 concerns)
| # | Title | Status | Severity |
|---|---|---|---|
| #39 | SuccessScale zero test coverage | Open — should be covered by #38 QA | Low (advisory) |
| #40 | DateSecured test missing | Open — should be covered by #38 QA | Low (advisory) |
| #57 | Sprint scope massive | Open — mitigated by wave plan | Low (advisory) |
| #62 | QA before features | Open — wave plan puts #38 last which is suboptimal | Low (advisory) |
| #70 | Record types in issue bodies | Mitigated — contracts use sealed class, issue bodies still wrong | Low |
| #126 | Specs exist but no impl code | Partially resolved — issues reopened, awaiting impl | Tracking |

### Arch Concerns (not vision-concern label)
| # | Title | Status |
|---|---|---|
| #86 | OpponentShadowTracker premature | Still relevant — #139 contract includes it but no growth events defined. Not blocking at prototype. |
| #87 | GameSession god object | Acknowledged, deferred to MVP. Acceptable. |

## Data Flow Traces

### Wave 0 (#139) → All Features
- `GameSession` constructor → `GameSessionConfig?` → stores `IGameClock`, `SessionShadowTracker`, `StartingInterest`
- `RollEngine.Resolve()` → `externalBonus` → `RollResult.AddExternalBonus()` → `FinalTotal` → `IsSuccess` (computed)
- Required: `IsSuccess` must use `FinalTotal >= DC` (not `Total >= DC`)
- ⚠️ **Verify at implementation**: The contract says `IsSuccess` becomes a computed property. Currently it's set in the constructor (line 101 of RollResult.cs). The implementer must change it from `{ get; }` to `=> IsNatTwenty || (!IsNatOne && FinalTotal >= DC)`. `MissMargin` should also use `FinalTotal`.

### #43 Read/Recover/Wait → Interest/Shadow
- Player chooses Read → `GameSession.ReadAsync()` → `RollEngine.ResolveFixedDC(SA, player, 12, ...)` → success: opponent reveal (interest +0) / fail: Overthinking +1 via `SessionShadowTracker.ApplyGrowth()`
- Player chooses Recover → guard `TrapState.HasActive` → `RollEngine.ResolveFixedDC(SA, player, 12, ...)` → success: clear oldest trap / fail: Overthinking +1
- Player chooses Wait → no roll → interest -1, skip turn
- Required fields: StatType.SA, fixedDc=12, ShadowStatType.Overthinking
- ✅ All dependencies in #139

### #44 Shadow Growth → SessionShadowTracker
- Per-turn: `ShadowGrowthProcessor.EvaluateTurn(stat, rollResult, counters, interestState)` → list of growth events
- Each event → `SessionShadowTracker.ApplyGrowth(shadow, amount, reason)` → delta stored
- End-of-game: `ShadowGrowthProcessor.EvaluateEndOfGame(outcome, counters)` → final growth
- Required: SessionCounters updated every turn with stat used, TropeTrap count, success/fail, highest-% pick
- ✅ Contract `issue-44-shadow-growth.md` defines SessionCounters and ShadowGrowthProcessor

### #51 Horniness → Option Post-Processing
- `StartTurnAsync()` → compute `horninessLevel = shadowTracker.GetEffectiveShadow(Horniness) + gameClock.GetHorninessModifier()`
- Apply thresholds: ≥6 add Rizz, ≥12 force one Rizz, ≥18 all Rizz
- Mark forced options with `IsHorninessForced = true`
- Pass `HorninessLevel` and `RequiresRizzOption` to LLM context (already in ILlmAdapter via #63)
- ✅ Complete data flow traced

## Unstated Requirements
- **Shadow persistence between sessions**: Shadow growth events modify `SessionShadowTracker` in-session, but the delta must persist to the character's profile between conversations. No serialization mechanism exists. Acceptable for prototype (caller can read `GetDelta()` and persist externally).
- **Read/Recover turn sequencing**: `ReadAsync()` and `RecoverAsync()` are alternatives to `ResolveTurnAsync()` — the `StartTurnAsync → ResolveTurnAsync` alternation invariant must be preserved. The contracts specify this but GameSession's internal `_currentOptions` guard needs updating.

## Domain Invariants
- `RollResult.IsSuccess` must reflect `FinalTotal` (base + external bonus), not `Total` alone — this is the single most important correctness invariant for this sprint
- `SessionShadowTracker` never modifies the underlying `StatBlock` — all mutations are in the delta dictionary
- Shadow growth applies AFTER the current roll is resolved — never mid-resolution
- Interest delta composition is additive: `SuccessScale/FailureScale + riskBonus + momentum + combo + callback`
- `InterestMeter.Current` stays in [0, 25] regardless of delta magnitude
- Turn actions are mutually exclusive per turn: Speak (StartTurn → Resolve) XOR Read XOR Recover XOR Wait
- `TrapState.HasActive` must be true for Recover to be callable — checked before roll, not after

## Gaps

### Missing (but acceptable for prototype)
- **Shadow persistence/serialization** — no mechanism to save shadow deltas between sessions. Caller reads `GetDelta()` manually.
- **#38 QA wave ordering** — QA (#38) is scheduled last (Wave 3) but should ideally run earlier to catch foundation bugs. Advisory only — wave plan has clear dependencies that make this hard to change.
- **DialogueOption expansion** — `HasWeaknessWindow` and `IsHorninessForced` fields are in individual contracts but not in a Wave 0 prerequisite. This means #49 and #51 each modify `DialogueOption.cs` independently — minor merge conflict risk.

### Unnecessary
- **`OpponentShadowTracker` in `GameSessionConfig`** (#86) — no opponent shadow growth events are defined. The field exists but does nothing. Not harmful (it's nullable), but it's dead API surface.

### Assumptions to validate
- **`RollResult.IsSuccess` refactor is backward-compatible**: When `ExternalBonus = 0`, `FinalTotal == Total`, so `IsSuccess` result is identical. Verify all 254 tests pass after the change.
- **Energy range 15–20 (d6+14)**: PO should confirm this is the right range. Currently in contract but flagged as "PO should confirm" in architecture doc.

## Recommendations
1. **Proceed with the sprint.** The wave plan, contracts, and #139 Wave 0 issue provide sufficient structure for implementation agents.
2. **Implement #139 first** — it's the keystone. Every other feature depends on it. Verify all 254 tests pass before proceeding to Wave 1.
3. **After #139, verify `RollResult.IsSuccess` uses `FinalTotal`** — write a specific test: roll that misses by 1 with `Total`, add `externalBonus = 2`, assert `IsSuccess == true`.
4. **Consider adding `HasWeaknessWindow` and `IsHorninessForced` to DialogueOption in #139** (minor addition, prevents merge conflicts between #49 and #51).
5. **PO should close resolved vision concerns** — 18 of 28 open vision concerns are now resolved. Closing them reduces noise for future sprints.

## VERDICT: CLEAN

All critical concerns from the first pass are now addressed by #139 (Wave 0) and detailed contracts. The sprint has a clear implementation path: #139 → Wave 1 (independent features) → Wave 2 (dependent features) → Wave 3 (QA). No blocking gaps remain. Proceed to architect/implementation.
