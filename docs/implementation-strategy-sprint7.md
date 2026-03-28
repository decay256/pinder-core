# Implementation Strategy — Sprint 7: RPG Rules Complete (Continuation)

## Implementation Order (strict dependency chain)

### Wave 0: Infrastructure (MUST ship first)
**Issue #139** — SessionShadowTracker, IGameClock interface, RollEngine extensions, GameSessionConfig, InterestMeter overload, TrapState.HasActive

Estimated size: ~300 lines C# + ~200 lines tests. One agent, one session.

### Wave 1: Independent features (can be built in any order after Wave 0)

| Issue | Component | Size estimate | Notes |
|-------|-----------|--------------|-------|
| #54 | GameClock impl + FixedGameClock | ~150 lines + tests | Self-contained |
| #55 | PlayerResponseDelayEvaluator | ~80 lines + tests | Pure function, zero deps |
| #46 | ComboTracker | ~200 lines + tests | Self-contained class |
| #48 | XpLedger | ~60 lines + tests | Very small |
| #47 | Callback bonus | ~50 lines in GameSession + tests | Small, mostly wiring |
| #49 | Weakness windows | ~50 lines in GameSession + tests | Small, mostly wiring |
| #50 | Tells | ~50 lines in GameSession + tests | Small, mostly wiring |
| #43 | Read/Recover/Wait | ~150 lines in GameSession + tests | Needs ResolveFixedDC from #139 |

**Recommended parallel groupings** (if agents work sequentially):
1. #55 (pure function, fast win)
2. #54 (GameClock, needed by Wave 2)
3. #46 + #48 (ComboTracker + XpLedger — independent, no GameSession conflicts)
4. #43 (Read/Recover/Wait — touches GameSession)
5. #47 + #49 + #50 (all touch GameSession — do these together or sequentially to avoid merge conflicts)

### Wave 2: Dependent features (need Wave 1 outputs)

| Issue | Depends on | Size estimate |
|-------|-----------|--------------|
| #44 | #139, #43 | ~200 lines (ShadowGrowthProcessor + SessionCounters + wiring) |
| #45 | #139, #44 | ~150 lines (ShadowThresholdEvaluator + GameSession wiring) |
| #51 | #45, #54 | ~80 lines (option post-processing in GameSession) |
| #56 | #54, #44 | ~300 lines (ConversationRegistry — largest piece) |

**Order**: #44 → #45 → #51, and #56 can start once #54 + #44 are done.

### Wave 3: QA
**Issue #38** — Runs after all features land.

## Critical dependency: #52 (Trap taint injection)

Per issue comments, #52 was already implemented in PRs #122 and #123 (approved by code review). These PRs need to be **merged** before this sprint begins. Check status:
- PR #122: trap taint implementation
- PR #123: trap taint tests

If not merged, merge them first as Wave -1.

## Tradeoffs

### Taking shortcuts
1. **GameSession god object**: Adding all features to one class is a shortcut for prototype maturity. Acceptable now; refactor at MVP.
2. **Option post-processing chain**: Tells, weakness, combos, horniness all modify options after LLM returns them. This creates a chain of mutations in StartTurnAsync. For prototype, this is fine. At MVP, extract to `OptionPostProcessor`.
3. **ConversationRegistry calling GameSession internals**: FastForward needs to apply interest decay, which requires a new `ApplyExternalInterestDelta` method on GameSession. This breaks the "GameSession owns its own state" pattern slightly but is necessary for the multi-session system.

### Building foundations
1. **SessionShadowTracker**: Investing in a proper mutable layer rather than hacking StatBlock. This pays off across 5+ features.
2. **ShadowGrowthProcessor as stateless pure function**: Easy to test, easy to extend with new triggers.
3. **XpLedger as separate class**: Keeps XP logic out of GameSession, even though it's small.
4. **ComboTracker as separate class**: Keeps combo detection logic isolated and testable.

## Risk mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| RollResult.IsSuccess change breaks existing tests | Medium | High | Make IsSuccess a computed property; since ExternalBonus defaults to 0, FinalTotal == Total for all existing tests |
| GameSession merge conflicts from parallel feature work | High | Medium | Sequence features that touch GameSession; use separate methods (Read/Recover/Wait don't touch ResolveTurnAsync) |
| ConversationRegistry scope creep | Medium | Medium | Strictly limit to scheduling + fast-forward + cross-chat events. No game logic. |
| Shadow threshold option filtering removes all options | Low | Medium | Always keep at least one option. Fail-safe: first option survives all filters |

## Sprint plan changes

**SPRINT PLAN CHANGES:**

1. **ADD new issue #139** (Wave 0 prerequisites) — created above. This MUST be the first issue implemented. All other issues depend on it.
2. **Issue #52 is already done** — PRs #122 and #123 are approved. Just need merge. Remove from active sprint work.
3. **Recommended implementation order**: #139 → #55 → #54 → #46 → #48 → #43 → #47 → #49 → #50 → #44 → #45 → #51 → #56 → #38
4. **#56 (ConversationRegistry) is the highest-risk issue** due to its complexity and dependencies on multiple other features. Consider descoping to a future sprint if the sprint runs long.
