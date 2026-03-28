# Sprint 3: RPG Rules Complete — Implementation Strategy

## Wave Plan (Dependency-Ordered)

### Wave 1: Prerequisites (no dependencies, can run in parallel)
| Issue | Component | Depends on |
|-------|-----------|------------|
| **#63** | ILlmAdapter expansion, OpponentResponse, stub types | None |
| **#78** | TurnResult expansion, RiskTier enum | None |

**Rationale**: These are pure type additions with defaults. They don't change behavior. All 98 tests must still pass.

### Wave 2: Infrastructure (enables all feature waves)
| Issue | Component | Depends on |
|-------|-----------|------------|
| **#42** | RiskTier computation + interest bonus in GameSession | #78 |
| **#43** | Read/Recover/Wait + RollEngine `fixedDc` + `externalBonus` params | #63 |
| **#44** | SessionShadowTracker + shadow growth events | #78 |
| **#54** | GameClock + IGameClock + TimeOfDay | None (but logically Wave 2) |

**Rationale**: #43 adds the `fixedDc` and `externalBonus` parameters to RollEngine (ADR-2) which #46, #47, #50 all need. #44 creates SessionShadowTracker which #45, #51, #53, #55 all need. These are the load-bearing infrastructure changes.

**Critical**: #43 modifies `RollEngine.Resolve` signature. This must be done carefully to maintain backward compat (optional params with defaults).

### Wave 3: LLM Plumbing
| Issue | Component | Depends on |
|-------|-----------|------------|
| **#52** | Trap taint injection | #63 |

**Rationale**: Pure plumbing — passes existing data through new context fields. Small, isolated change.

### Wave 4: Game Mechanics (can run in parallel)
| Issue | Component | Depends on |
|-------|-----------|------------|
| **#46** | Combo system | #43 (externalBonus param), #78 |
| **#47** | Callback bonus | #43 (externalBonus param), #63, #78 |
| **#48** | XP tracking | #78 |
| **#49** | Weakness windows | #63 |
| **#50** | Tells | #43 (externalBonus param), #63, #78 |

**Rationale**: These are all "plug into GameSession.ResolveTurnAsync" features. Each is isolated — they read from existing data and add a delta or bonus. They can be implemented in parallel as long as they coordinate on the GameSession integration points.

### Wave 5: Shadow System
| Issue | Component | Depends on |
|-------|-----------|------------|
| **#45** | Shadow thresholds | #44 |
| **#51** | Horniness-forced Rizz | #44, #63 |

**Rationale**: These depend on SessionShadowTracker being available.

### Wave 6: Time & Delay
| Issue | Component | Depends on |
|-------|-----------|------------|
| **#53** | OpponentTimingCalculator | #44, #54 (optional) |
| **#55** | PlayerResponseDelay | #44 (optional) |

**Rationale**: These are pure functions that can be implemented and tested independently. They integrate into GameSession but don't affect each other.

### Wave 7: QA
| Issue | Component | Depends on |
|-------|-----------|------------|
| **#38** | QA review | All of the above |

### DEFERRED
| Issue | Component | Reason |
|-------|-----------|--------|
| **#56** | ConversationRegistry | Too complex, deepest dependency chain, per ADR-5 |

---

## GameSession Constructor Evolution

**Current** (5 params):
```csharp
GameSession(CharacterProfile player, CharacterProfile opponent, ILlmAdapter llm, IDiceRoller dice, ITrapRegistry trapRegistry)
```

**Proposed** — add `GameSessionConfig` overload:
```csharp
// New config-based constructor
GameSession(CharacterProfile player, CharacterProfile opponent, GameSessionConfig config)

// GameSessionConfig holds:
public sealed class GameSessionConfig
{
    public ILlmAdapter Llm { get; }
    public IDiceRoller Dice { get; }
    public ITrapRegistry TrapRegistry { get; }
    public IGameClock? GameClock { get; }              // #54 — null = no time tracking
    public SessionShadowTracker? ShadowTracker { get; } // #44 — null = no shadow tracking
}
```

**Old constructor stays** (wraps into config):
```csharp
public GameSession(CharacterProfile player, CharacterProfile opponent, ILlmAdapter llm, IDiceRoller dice, ITrapRegistry trapRegistry)
    : this(player, opponent, new GameSessionConfig(llm, dice, trapRegistry))
{ }
```

This ensures all 98 existing tests compile without changes.

---

## Risk Mitigation

### Risk: RollEngine signature change breaks things
**Mitigation**: `fixedDc` and `externalBonus` are optional parameters with defaults. All existing callers are unaffected. Unit tests for RollEngine should be extended, not modified.

### Risk: GameSession becomes too large
**Mitigation**: Each feature is a self-contained block in ResolveTurnAsync. The ordering is:
1. Pre-roll: compute external bonus (callback + tell + Triple combo), compute DC override (weakness window)
2. Roll: `RollEngine.Resolve(..., fixedDc: dcOverride, externalBonus: totalBonus)`
3. Post-roll: SuccessScale/FailureScale → risk bonus → combo bonus → momentum → apply interest
4. Post-interest: shadow growth, XP recording
5. LLM calls: deliver, opponent response
6. Post-response: store tell, store weakness, record callback topics

Each block is 5–10 lines. The method will be ~120 lines, which is acceptable for an orchestrator.

### Risk: Sprint scope is too large (VC-57)
**Mitigation**: Wave plan + deferring #56. If time runs short, Wave 6 (timing/delay) can also be deferred — they're nice-to-have for prototype. Core value is Waves 1–5.

### Fallback priority (if we must cut)
1. **Must ship**: Waves 1–2 (#63, #78, #42, #43, #44, #54)
2. **Should ship**: Waves 3–4 (#52, #46, #47, #48, #49, #50)
3. **Nice to have**: Waves 5–6 (#45, #51, #53, #55)
4. **Already deferred**: #56

---

## Refactoring Opportunities

1. **GameSession internal methods**: Extract `ComputeExternalBonus()`, `ApplyShadowGrowth()`, `RecordXp()` as private methods to keep ResolveTurnAsync readable.

2. **TimingProfile.ComputeDelay obsolescence**: Once OpponentTimingCalculator (#53) exists, consider deprecating the instance method on TimingProfile and routing all calls through the static calculator.

3. **Context type construction**: GameSession builds 4 different context objects per turn. Consider a private `BuildDialogueContext()`, `BuildDeliveryContext()` etc. to reduce duplication.
