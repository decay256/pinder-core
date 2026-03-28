# Pinder.Core — Architecture

## Overview

Pinder.Core is a **pure C# (.NET Standard 2.0) RPG engine** for a comedy dating game where players are sentient penises on a Tinder-like app. It has **zero external dependencies** and is designed to drop into Unity or any .NET host.

The engine is **stateless at the roll level** — all state is passed in via parameters. `GameSession` is the sole stateful orchestrator that owns a single conversation's mutable state and sequences calls to stateless components and injected interfaces.

### Module Map

```
Pinder.Core/
├── Stats/          — StatType, ShadowStatType, StatBlock (stat pairs, shadow penalties, DC calc)
├── Rolls/          — RollEngine (stateless), RollResult, FailureTier, SuccessScale, FailureScale, RiskTier, RiskTierBonus
├── Traps/          — TrapDefinition, TrapState, ActiveTrap (trap lifecycle)
├── Progression/    — LevelTable (XP thresholds, level bonuses, build points, item slots), XpLedger (NEW)
├── Conversation/   — InterestMeter, InterestState, TimingProfile, GameSession, GameClock (NEW), ComboTracker (NEW), PlayerResponseDelayEvaluator (NEW), ConversationRegistry (NEW)
├── Characters/     — CharacterAssembler, FragmentCollection, CharacterProfile, ItemDefinition, AnatomyTierDefinition, TimingModifier
├── Prompts/        — PromptBuilder (assembles LLM system prompt from fragments + traps)
├── Interfaces/     — IDiceRoller, IFailurePool, ITrapRegistry, IItemRepository, IAnatomyRepository, ILlmAdapter, IGameClock (NEW)
└── Data/           — JsonItemRepository, JsonAnatomyRepository, JsonParser, JsonTrapRepository, JsonTimingRepository
```

### Data Flow (full turn — updated for Sprint 7)

```
Host creates GameSession(player, opponent, llm, dice, trapRegistry, config)
  config includes: IGameClock?, SessionShadowTracker?
  → session owns InterestMeter, TrapState, history, turn counter, ComboTracker, XpLedger, topic list

Per turn:
  1. StartTurnAsync()
     → check end conditions (interest 0/25, ghost)
     → evaluate shadow thresholds (via SessionShadowTracker) → disadvantage flags, option constraints
     → compute horniness level (shadow + time-of-day modifier)
     → determine adv/disadv from interest state + traps + shadow thresholds
     → check active weakness window → modify DC context
     → build DialogueContext with thresholds, horniness, callbacks, weakness, tells
     → call ILlmAdapter.GetDialogueOptionsAsync() → post-process for horniness forcing
     → return TurnStart with options

  2. ResolveTurnAsync(optionIndex)
     → compute externalBonus (tell +2, callback +1/+2/+3, The Triple +1)
     → RollEngine.Resolve() with adv/disadv + externalBonus
     → SuccessScale/FailureScale → interest delta
     → ComboTracker.CheckCombo() → combo interest bonus
     → CallbackBonus (interest delta addition on success)
     → Momentum streak → interest bonus
     → InterestMeter.Apply(total delta)
     → XpLedger.Record(xp event)
     → SessionShadowTracker.Apply(growth events) → evaluate triggers
     → ILlmAdapter.DeliverMessageAsync() → player text
     → ILlmAdapter.GetInterestChangeBeatAsync() if threshold crossed
     → ILlmAdapter.GetOpponentResponseAsync() → opponent reply with Tell? + WeaknessWindow?
     → store Tell + WeaknessWindow for next turn
     → return TurnResult (with shadow events, combo, callback, tell, xp)

  3. ReadAsync() / RecoverAsync() / Wait()
     → fixed-DC roll (DC 12) or skip
     → interest/trap effects
     → shadow growth on failure
     → XP recording
```

### Key Design Patterns
- **Stateless engine**: `RollEngine` is a static class. All mutable state is owned by `GameSession` or caller.
- **Interface-driven injection**: Dice, failure pools, trap registries, item/anatomy repos, LLM adapters, game clock are all interfaces.
- **Fragment assembly**: Character identity built by summing stat modifiers and concatenating text fragments.
- **No external dependencies**: Custom `JsonParser` avoids NuGet for Unity compat.
- **GameSession as orchestrator**: Owns a single conversation's mutable state. Delegates to stateless components and injected interfaces.
- **SessionShadowTracker as mutable shadow layer**: Wraps immutable `StatBlock`, provides mutable shadow tracking without breaking RollEngine's snapshot contract.

---

## Sprint 7: RPG Rules Complete (Continuation) — Architecture Briefing

### What's changing

**Previous architecture (Sprint 6)**: GameSession orchestrates Speak turns with roll → interest delta → momentum → trap → deliver → opponent response. Shadow stats are immutable on `StatBlock`. No concept of game clock, combos, callbacks, tells, weakness windows, XP, or shadow growth. All these features had stub types (Tell, WeaknessWindow, CallbackOpportunity) and TurnResult fields added in prior PRs but no wiring.

**New architecture**: This sprint wires up the remaining RPG mechanics. The key structural changes are:

#### Wave 0: Prerequisites (MUST ship first as #130)

Three infrastructure pieces that multiple features depend on:

1. **`SessionShadowTracker`** — New class in `Pinder.Core.Stats`. Wraps a `StatBlock` reference + a mutable `Dictionary<ShadowStatType, int>` of deltas. Provides `GetEffectiveShadow(ShadowStatType)` (base + delta) and `ApplyGrowth(ShadowStatType, int amount)`. Does NOT modify `StatBlock._shadow`. Used by #43, #44, #45, #51.

2. **`IGameClock` interface** — New interface in `Pinder.Core.Interfaces`. Provides `Now`, `Advance()`, `GetTimeOfDay()`, `GetHorninessModifier()`, energy tracking. `GameClock` implementation in `Pinder.Core.Conversation`. `FixedGameClock` test helper. Used by #54, #51, #55, #56.

3. **`RollEngine` extensions** — Add `ResolveFixedDC()` overload for Read/Recover (DC 12, no opponent stat block needed). Add `externalBonus` parameter to existing `Resolve()`. Used by #43, #46, #47, #50.

4. **`GameSessionConfig`** — Optional config class passed to `GameSession` constructor to carry optional dependencies (`IGameClock?`, `SessionShadowTracker?`) without breaking the existing constructor signature. Backward-compatible: existing tests pass null config.

#### Wave 1: Independent features (can be built in parallel after Wave 0)

- **#52 Trap taint injection** — Already merged (PR #122/#123). Complete.
- **#54 GameClock** — `IGameClock` + `GameClock` + `TimeOfDay` + `FixedGameClock`. Self-contained after Wave 0 defines the interface.
- **#43 Read/Recover/Wait** — Three new methods on `GameSession`. Uses `ResolveFixedDC`, `SessionShadowTracker`.
- **#46 ComboTracker** — New class in `Conversation/`. Pure logic, no external deps beyond stat history.
- **#47 Callback bonus** — Topic tracking in `GameSession`, interest delta addition.
- **#49 Weakness windows** — Store `WeaknessWindow` from opponent response, apply DC reduction next turn.
- **#50 Tells** — Store `Tell` from opponent response, apply +2 via `externalBonus`.
- **#55 PlayerResponseDelay** — Pure function `(TimeSpan, StatBlock, InterestState) → DelayPenalty`. Zero deps.
- **#48 XP tracking** — `XpLedger` class + wiring in `GameSession.ResolveTurnAsync`.

#### Wave 2: Depends on Wave 1 features

- **#44 Shadow growth events** — Depends on #43 (Read/Recover for Overthinking triggers), #130 (`SessionShadowTracker`). Wires growth triggers into `GameSession`.
- **#45 Shadow thresholds** — Depends on #44 (shadow values must be mutable). Evaluates threshold effects on gameplay.
- **#51 Horniness-forced Rizz** — Depends on #45 (Horniness shadow threshold levels), #54 (`IGameClock.GetHorninessModifier()`).
- **#56 ConversationRegistry** — Depends on #54 (`IGameClock`), #44 (shadow bleed). Multi-session manager. Most complex piece.

#### Wave 3: QA

- **#38 QA review** — Runs last after all features land.

### What is NOT changing
- `StatBlock` remains immutable. Shadow mutation goes through `SessionShadowTracker`.
- `RollEngine.Resolve()` signature gains one optional parameter (`externalBonus = 0`) — backward compatible.
- `InterestMeter` gains one constructor overload `(int startingValue)` — backward compatible.
- All existing 254 tests continue to pass.

### Arch concern: GameSession god object (#87)

GameSession is accumulating responsibilities. This sprint adds Read/Recover/Wait, combo checking, callback tracking, tell/weakness storage, shadow growth, horniness option forcing, and XP recording. For **prototype maturity** this is acceptable — the priority is getting mechanics working. For MVP, GameSession should be refactored into:
- `TurnOrchestrator` (sequences the turn)
- `BonusCalculator` (external bonuses: combo, callback, tell, weakness)
- `ShadowGrowthProcessor` (evaluates growth triggers)
- `OptionPostProcessor` (horniness forcing, option filtering)

This refactoring is explicitly NOT in scope for this sprint. File as future work.

---

## Rules-to-Code Sync

### Source of Truth

| Layer | Location | Authority |
|-------|----------|-----------|
| Game rules | `design/systems/rules-v3.md` (external repo) | **Authoritative** |
| Content data | `design/settings/` (external repo) | Authoritative for items, anatomy, traps |
| C# engine | `src/Pinder.Core/` (this repo) | **Derived** — must match rules exactly |

### Constant Sync Table

| Rules Section | Rule Value | C# Location | C# Constant/Expression |
|---|---|---|---|
| §3 Defence pairings | Charm→SA, Rizz→Wit, Honesty→Chaos, Chaos→Charm, Wit→Rizz, SA→Honesty | `Stats/StatBlock.cs` | `StatBlock.DefenceTable` |
| §3 Base DC | 13 | `Stats/StatBlock.cs` | `StatBlock.GetDefenceDC()` — hardcoded `13 +` |
| §5 Fail tiers | Nat1→Legendary, miss 1–2→Fumble, 3–5→Misfire, 6–9→TropeTrap, 10+→Catastrophe | `Rolls/RollEngine.cs` | Boundary checks in `Resolve()` |
| §5 Success scale | Beat DC by 1–4→+1, 5–9→+2, 10+→+3, Nat20→+4 | `Rolls/SuccessScale.cs` | `SuccessScale.GetInterestDelta()` |
| §5 Failure scale | Fumble→-1, Misfire→-2, TropeTrap→-3, Catastrophe→-4, Legendary→-5 | `Rolls/FailureScale.cs` | `FailureScale.GetInterestDelta()` |
| §5 Risk tier bonus | Hard→+1, Bold→+2 | `Rolls/RiskTierBonus.cs` | `RiskTierBonus.GetInterestBonus()` |
| §6 Interest range | 0–25 | `Conversation/InterestMeter.cs` | `Max = 25`, `Min = 0` |
| §6 Starting interest | 10 (or 8 if Dread ≥18) | `Conversation/InterestMeter.cs` | `StartingValue = 10`, new overload |
| §6 Interest states | Unmatched(0), Bored(1–4), Interested(5–15), VeryIntoIt(16–20), AlmostThere(21–24), DateSecured(25) | `Conversation/InterestMeter.cs` | `GetState()`, `InterestState` enum |
| §6 Adv/disadv | VeryIntoIt/AlmostThere → advantage; Bored → disadvantage | `Conversation/InterestMeter.cs` | `GrantsAdvantage`/`GrantsDisadvantage` |
| §7 Shadow pairs | Charm↔Madness, Rizz↔Horniness, Honesty↔Denial, Chaos↔Fixation, Wit↔Dread, SA↔Overthinking | `Stats/StatBlock.cs` | `StatBlock.ShadowPairs` |
| §7 Shadow penalty | -1 per 3 shadow points | `Stats/StatBlock.cs` | `GetEffective()` — `shadowVal / 3` |
| §7 Shadow thresholds | 6=T1, 12=T2, 18+=T3 | `Stats/ShadowThresholdEvaluator.cs` | **NEW** |
| §7 Shadow growth triggers | See #44 growth table | `Conversation/ShadowGrowthProcessor.cs` | **NEW** |
| §8 Read/Recover/Wait | DC 12, SA stat, effects | `Conversation/GameSession.cs` | `ReadAsync()`, `RecoverAsync()`, `Wait()` — **NEW** |
| §8 Shadow penalty | -1 per 3 shadow points | `Stats/StatBlock.cs` | `GetEffective()` |
| §10 XP sources | Success 5/10/15, Fail 2, Nat20 25, Nat1 10, DateSecured 50, Recovery 15, Complete 5 | `Progression/XpLedger.cs` | **NEW** |
| §10 XP thresholds | L1=0…L11=3500 | `Progression/LevelTable.cs` | `XpThresholds` |
| §10 Level bonuses | L1–2=+0…L11=+5 | `Progression/LevelTable.cs` | `LevelBonuses` |
| §15 Combos | 8 combo sequences + bonuses | `Conversation/ComboTracker.cs` | **NEW** |
| §15 Callbacks | 2 turns→+1, 4+→+2, opener→+3 (interest delta bonus) | `Conversation/GameSession.cs` | Callback logic — **NEW** |
| §15 Tells | +2 roll bonus on matching stat | `Conversation/GameSession.cs` | Tell logic — **NEW** |
| §15 Weakness windows | DC −2/−3 for one turn | `Conversation/GameSession.cs` | Weakness logic — **NEW** |
| §15 Horniness | ≥6 one Rizz, ≥12 forced Rizz, ≥18 all Rizz | `Conversation/GameSession.cs` | Horniness option logic — **NEW** |
| Momentum | 3-streak→+2, 4→+2, 5+→+3 | `Conversation/GameSession.cs` | `GetMomentumBonus()` |
| Ghost trigger | Bored → 25% per turn | `Conversation/GameSession.cs` | Ghost check in `StartTurnAsync` |
| §async-time Horniness mod | Morning −2, Afternoon +0, Evening +1, LateNight +3, After2AM +5 | `Conversation/GameClock.cs` | **NEW** |
| §async-time Delay penalty | <1min 0, 1–15min 0, 15–60min −1 (if ≥16), 1–6h −2, 6–24h −3, 24+h −5 | `Conversation/PlayerResponseDelayEvaluator.cs` | **NEW** |

### Known Gaps (as of Sprint 7)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Failure scale values are prototype defaults | §5 | PO should confirm -1/-2/-3/-4/-5 |
| Energy system daily amount (15–20) | §async-time | Defined in #54 but PO should confirm range |
| ConversationRegistry cross-chat events | §async-time | Complex; may need future refinement |

---

## Component Boundaries

### Stats (`Pinder.Core.Stats`)
- **Owns**: Stat types, shadow stat types, stat block with effective modifier calculation, defence table, DC calculation, shadow threshold evaluation
- **Public API**: `StatType` enum, `ShadowStatType` enum, `StatBlock` class, `SessionShadowTracker` (NEW), `ShadowThresholdEvaluator` (NEW)
- **Does NOT own**: Roll resolution, interest tracking, character assembly

### Rolls (`Pinder.Core.Rolls`)
- **Owns**: d20 roll resolution, failure tier determination, advantage/disadvantage logic, trap activation during rolls, success/failure scales, risk tier
- **Public API**: `RollEngine.Resolve()`, `RollEngine.ResolveFixedDC()` (NEW), `RollResult`, `FailureTier`, `SuccessScale`, `FailureScale`, `RiskTier`, `RiskTierBonus`
- **Does NOT own**: Interest tracking, stat storage, trap definitions, game session orchestration

### Traps (`Pinder.Core.Traps`)
- **Owns**: Trap data model, active trap tracking, turn countdown
- **Public API**: `TrapDefinition`, `TrapState`, `ActiveTrap`, `TrapEffect`
- **Does NOT own**: Trap activation logic (RollEngine), trap content (Data/)

### Conversation (`Pinder.Core.Conversation`)
- **Owns**: Interest meter, timing, game session orchestration, combo tracking, game clock, response delay evaluation, conversation registry
- **Public API**: `InterestMeter`, `InterestState`, `TimingProfile`, `GameSession`, `TurnStart`, `TurnResult`, `GameStateSnapshot`, `GameOutcome`, `GameClock` (NEW), `IGameClock` (in Interfaces), `ComboTracker` (NEW), `PlayerResponseDelayEvaluator` (NEW), `ConversationRegistry` (NEW), `ConversationEntry` (NEW), `ReadResult` (NEW), `RecoverResult` (NEW)
- **Does NOT own**: Roll math, LLM communication, character assembly

### Progression (`Pinder.Core.Progression`)
- **Owns**: XP→level resolution, level bonus, build points, item slot counts, XP tracking
- **Public API**: `LevelTable`, `FailurePoolTier`, `XpLedger` (NEW)
- **Does NOT own**: XP source determination (that's GameSession)

### Characters (`Pinder.Core.Characters`)
- **Owns**: Item/anatomy data models, fragment assembly, character profile
- **Public API**: `CharacterAssembler`, `FragmentCollection`, `CharacterProfile`, `ItemDefinition`, `AnatomyTierDefinition`, `TimingModifier`
- **Does NOT own**: Item loading (Data/), prompt generation (Prompts/)

### Prompts (`Pinder.Core.Prompts`)
- **Owns**: System prompt string construction from fragments + traps
- **Public API**: `PromptBuilder.BuildSystemPrompt()`
- **Does NOT own**: LLM calling, fragment assembly, trap state

### Data (`Pinder.Core.Data`)
- **Owns**: JSON parsing, item/anatomy/trap/timing deserialization
- **Public API**: `JsonItemRepository`, `JsonAnatomyRepository`, `JsonTrapRepository`, `JsonTimingRepository`
- **Does NOT own**: File I/O (caller passes JSON string)

### Interfaces (`Pinder.Core.Interfaces`)
- **Owns**: Abstraction contracts for injection points
- **Public API**: `IDiceRoller`, `IFailurePool`, `ITrapRegistry`, `IItemRepository`, `IAnatomyRepository`, `ILlmAdapter`, `IGameClock` (NEW)
- **Does NOT own**: Any implementation
