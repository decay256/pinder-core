# Vision Review — Sprint 3: RPG Rules Complete (Architect)

## Architectural Decisions Made

### ADR-1: SessionShadowTracker wraps StatBlock (resolves VC-58)
**Context**: StatBlock is immutable. Shadow growth (#44) needs mutable shadow tracking per session.
**Decision**: New `SessionShadowTracker` class wraps StatBlock, tracks per-session growth deltas. StatBlock remains immutable.
**Consequences**: Clean separation. Extra object per session. RollEngine still takes StatBlock — GameSession must construct effective StatBlocks when needed.

### ADR-2: RollEngine gets `fixedDc` and `externalBonus` (resolves VC-65, VC-68)
**Context**: Read/Recover need DC 12 (fixed). Combos/callbacks/tells need flat roll bonuses. RollEngine has no parameter for either.
**Decision**: Add optional params: `int? fixedDc = null`, `int externalBonus = 0`.
**Consequences**: Backward compatible. All existing callers unaffected. Single unified entry point for all bonus composition.

### ADR-3: GameSessionConfig bundles dependencies (resolves VC-82)
**Context**: 4-5 new components need injection into GameSession without breaking 98 tests.
**Decision**: `GameSessionConfig` class bundles all injectables. Old constructor preserved as convenience overload.
**Consequences**: No test breakage. Clean extensibility path.

### ADR-4: InterestMeter configurable starting value (resolves VC-79)
**Context**: Dread ≥ 18 requires starting interest of 8 instead of 10.
**Decision**: Constructor overload `InterestMeter(int startingValue)`.
**Consequences**: Trivial change. No impact on existing code.

### ADR-5: Defer ConversationRegistry (#56) to next sprint
**Context**: Most complex piece, deepest dependency chain, single-session sufficient for prototype.
**Decision**: Defer to Sprint 4.
**Consequences**: Multi-session gameplay delayed. Single-session is feature-complete.

### ADR-6: PlayerResponseDelay uses wall-clock time (resolves VC-81)
**Context**: Unclear whether delay is wall-clock or game-clock.
**Decision**: Wall-clock. Host measures real elapsed time and passes `TimeSpan` to engine.
**Consequences**: GameClock remains purely for simulated opponent delays.

## Vision Concerns Addressed
- VC-58: Resolved by ADR-1 (SessionShadowTracker)
- VC-65: Resolved by ADR-2 (fixedDc parameter)
- VC-68: Resolved by ADR-2 (externalBonus parameter)
- VC-79: Resolved by ADR-4 (InterestMeter overload)
- VC-80: #43 dependency corrected (needs fixedDc from ADR-2, not #42)
- VC-81: Resolved by ADR-6 (wall-clock)
- VC-82: Resolved by ADR-3 (GameSessionConfig)
- VC-74: Clarified — Horniness reads from SessionShadowTracker, same shadow stat value

## Vision Concerns NOT Addressed (accepted risk)
- VC-57: Sprint scope is large. Mitigated by deferring #56 and wave-based ordering.
- VC-75: Energy system ownership. Deferred — not needed for prototype.
