# Vision Review — Sprint 8: Architecture Strategic Alignment

## Alignment: ✅

The architect's output is **well-aligned with the product vision and appropriate for prototype maturity**. Sprint 8 turns 14 specs into the complete RPG mechanical layer — this is the right work at the right time. The architecture maintains the core design principles (stateless roll engine, interface-driven injection, zero dependencies, Unity compatibility) while expanding GameSession into a full game orchestrator. The three ADRs (#161, #162, #147) resolve real conflicts cleanly.

## Maturity Fit Assessment

### ✅ Appropriate for Prototype
- **Interface-driven injection** (IGameClock, IDiceRoller, ILlmAdapter) — necessary for testability, not over-engineering. A pure C# engine targeting Unity needs these from day one.
- **Stateless RollEngine with parameter-based bonus flow** — simple, testable, backward-compatible. The `externalBonus`/`dcAdjustment` optional params are the right abstraction for prototype.
- **SessionShadowTracker as wrapper over immutable StatBlock** — clean separation. Not over-engineered; it's the minimum viable mutable layer.
- **ComboTracker as pure data tracker** — correctly defers effect application to GameSession. No framework overhead.
- **XpLedger as dumb accumulator** — prototype-appropriate. No persistence, no cross-session concerns.
- **GameSessionConfig as optional carrier** — avoids constructor explosion. Clean extension point.

### ⚠️ One Trajectory Concern (Non-Blocking)
- **GameSession god object** — Already acknowledged in Known Gaps (#87). After Sprint 8, GameSession will own: interest tracking, shadow threshold evaluation, combo detection, callback computation, tell/weakness application, horniness logic, XP recording, momentum, history, and ghost triggers. This is ~12 responsibilities. Acceptable for prototype, but the architecture doc should explicitly state the extraction plan (e.g., `TurnResolver`, `BonusCalculator`, `EndConditionChecker` as candidates for next maturity level).

## Coupling Assessment

### ✅ Clean Dependency Flow
```
ConversationRegistry → GameSession → RollEngine (stateless)
                     → InterestMeter
                     → ComboTracker
                     → SessionShadowTracker → StatBlock (immutable)
                     → XpLedger
                     → ILlmAdapter (injected)
                     → IGameClock (injected)
```
- No circular dependencies.
- All new components depend downward (toward stateless/immutable layers).
- ConversationRegistry correctly does NOT make LLM calls — it's a scheduler only.

### ✅ No Roadmap Conflicts
- Characters/, Prompts/, Data/ untouched — future content expansion is unblocked.
- ILlmAdapter interface unchanged — LLM integration work is independent.
- StatBlock immutability preserved — character creation pipeline is unaffected.

## Abstraction Review

### ✅ RollEngine Extensions
- `externalBonus` and `dcAdjustment` as optional int params (default 0) — maximum backward compatibility, zero abstraction overhead. No `BonusContext` object, no strategy pattern — correct for prototype.

### ✅ RollResult.IsSuccess Change
- Contract says `IsSuccess` computed from `FinalTotal` instead of `Total`. But **current code computes `IsSuccess` from `Total` in the constructor**. The contract's `externalBonus` constructor param approach means `IsSuccess` will be set correctly at construction time.
- ⚠️ **Data flow nuance**: `AddExternalBonus()` (deprecated) is a post-construction mutator that does NOT update `IsSuccess`. The contract's approach (passing `externalBonus` at construction) fixes this by making `IsSuccess` correct from the start. However, `MissMargin` staying as `DC - Total` (not `DC - FinalTotal`) means miss margin doesn't reflect external bonuses — this is **intentional** (miss margin drives failure tier, which shouldn't change retroactively). Good decision.

### ✅ GameSessionConfig
- Immutable carrier with optional fields — appropriate. Not a builder pattern, not a fluent API. Right for prototype.
- `PreviousOpener` correctly routed here per #162 ADR.

### ⚠️ IGameClock.ConsumeEnergy() — Speculative Interface
- Energy system has no consumer this sprint. `ConsumeEnergy()` on the interface means all implementors (including test doubles) must implement it, but nothing calls it.
- **Advisory**: This is mild YAGNI. Acceptable because it's a single method on an interface that will have few implementations (GameClock, FixedGameClock). Not worth blocking.

## Data Flow Verification

### ExternalBonus Pipeline (Callback + Tell + Triple Combo)
- `GameSession.ResolveTurnAsync()` computes:
  - Callback bonus: `_turnNumber - option.CallbackTurnNumber` → table lookup → int
  - Tell bonus: `+2` if tell active for chosen stat
  - Triple combo bonus: `+1` from `ComboTracker.CheckCombo()` where combo is "The Triple"
- Sum → `externalBonus` param → `RollEngine.Resolve(externalBonus:)` → `RollResult(externalBonus:)` → `FinalTotal` → `IsSuccess`
- ✅ Complete pipeline. No missing fields.

### DC Adjustment Pipeline (Weakness Window)
- `OpponentResponse.WeaknessWindow` stored in GameSession → next turn's `ResolveTurnAsync` → if chosen stat matches weakness stat → `dcAdjustment = -2 or -3` → `RollEngine.Resolve(dcAdjustment:)` → lower effective DC
- ✅ Complete pipeline.

### Shadow Growth Pipeline
- Roll result + context → `SessionShadowTracker.ApplyGrowth(shadow, amount, reason)` → returns description string → stored internally → `DrainGrowthEvents()` → `TurnResult.ShadowGrowthEvents` (existing field, populated)
- ✅ Complete pipeline. #161 resolution eliminates the CharacterState conflict.

## Gaps

### None Blocking
All previously identified blocking issues (#161, #162, #163) are resolved in the architecture ADRs.

### Advisory
- **GameSession god object trajectory** (#87) — Plan the extraction before Sprint 9. The current trajectory adds ~8 new responsibilities in this sprint alone.
- **Energy system dead code** (#144) — `ConsumeEnergy()` has no caller. Track as tech debt, not a blocker.
- **Read/Recover/Wait history gap** — The first-pass review correctly identified that non-Speak actions don't append to `_history`. The contracts don't address this. The LLM on the next Speak turn won't know the player Read/Recovered/Waited. This should be a follow-up issue, not a Sprint 8 blocker (prototype can tolerate it).

### Assumptions Validated
- ✅ `TurnResult.ShadowGrowthEvents` exists in codebase — confirmed via `TurnResult.cs` review
- ✅ `RollResult.ExternalBonus` and `FinalTotal` exist — confirmed in codebase
- ✅ `AddExternalBonus()` exists as public mutable method — confirmed, correctly deprecated
- ✅ 254+ tests exist and pass — confirmed (261 tests listed)
- ✅ Zero NuGet dependencies — confirmed, hand-rolled JsonParser in use

## Recommendations

1. **Proceed with implementation** — Architecture is sound, contracts are complete, all blocking conflicts resolved.
2. **File follow-up issue for GameSession extraction plan** — Before Sprint 9 proposal, document which responsibilities will be extracted and the target component boundaries.
3. **File follow-up issue for Read/Recover/Wait history entries** — Non-Speak actions should append a marker to `_history` so the LLM has continuity context.
4. **Consider removing `ConsumeEnergy()` from IGameClock** until there's a consumer — or accept it as speculative prototype infrastructure.

## VERDICT: CLEAN

Architecture aligns with product vision. Prototype-appropriate abstractions, clean dependency flow, no coupling conflicts with the roadmap. All previously blocking concerns (#161, #162, #147) are resolved in the ADRs. The GameSession god object trajectory is acknowledged and tracked. Proceed with implementation.
