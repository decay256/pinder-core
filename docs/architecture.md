# Pinder.Core — Architecture

## Overview

Pinder.Core is a **pure C# (.NET Standard 2.0) RPG engine** for a comedy dating game where players are sentient penises on a Tinder-like app. It has **zero external dependencies** and is designed to drop into Unity or any .NET host.

The engine is **stateless at the roll level** — all state is passed in via parameters. State management (whose turn, what happened last) lives in the host (Unity game loop) or in `GameSession` (the first stateful component in the engine). The engine owns the math, data models, and single-conversation orchestration.

### Module Map

```
Pinder.Core/
├── Stats/          — StatType, ShadowStatType, StatBlock, SessionShadowTracker, ShadowThresholdEvaluator
├── Rolls/          — RollEngine (stateless), RollResult, FailureTier, SuccessScale, FailureScale, RiskTier, RiskTierBonus
├── Traps/          — TrapDefinition, TrapState, ActiveTrap (trap lifecycle)
├── Progression/    — LevelTable, XpLedger (XP accumulation)
├── Conversation/   — InterestMeter, InterestState, TimingProfile, GameSession, GameSessionConfig,
│                     ComboTracker, PlayerResponseDelayEvaluator, ConversationRegistry,
│                     ReadResult, RecoverResult, DelayPenalty, ConversationEntry, ConversationLifecycle, CrossChatEvent, GameClock
├── Characters/     — CharacterAssembler, FragmentCollection, CharacterProfile, ItemDefinition, AnatomyTierDefinition, TimingModifier
├── Prompts/        — PromptBuilder (assembles LLM system prompt from fragments + traps)
├── Interfaces/     — IDiceRoller, IFailurePool, ITrapRegistry, IItemRepository, IAnatomyRepository, ILlmAdapter, IGameClock, TimeOfDay
└── Data/           — JsonItemRepository, JsonAnatomyRepository, JsonParser (hand-rolled JSON parser)
```

### Data Flow (full turn — Sprint 8)

```
Host creates GameSession(player, opponent, llm, dice, trapRegistry, config?)
  → session owns InterestMeter, TrapState, ComboTracker, SessionShadowTracker (player+opponent),
    XpLedger, history, turn counter, active Tell/WeaknessWindow
  → config optionally injects IGameClock, custom starting interest

Per turn (Speak action):
  1. StartTurnAsync()
     → check end conditions → ghost trigger if Bored
     → determine adv/disadv from interest state + traps + shadow thresholds
     → compute Horniness level (shadow + time-of-day)
     → peek combos on each option, set tell/weakness markers
     → call ILlmAdapter.GetDialogueOptionsAsync() → return TurnStart

  2. ResolveTurnAsync(optionIndex)
     → validate index
     → compute externalBonus (callback + tell + triple combo)
     → compute dcAdjustment (weakness window)
     → RollEngine.Resolve() with adv/disadv + externalBonus + dcAdjustment
     → SuccessScale or FailureScale → interest delta
     → add RiskTierBonus, momentum, combo interest bonus
     → shadow growth events
     → XP recording
     → InterestMeter.Apply(total delta)
     → ILlmAdapter.DeliverMessageAsync()
     → ILlmAdapter.GetOpponentResponseAsync() → store Tell/WeaknessWindow for next turn
     → ILlmAdapter.GetInterestChangeBeatAsync() if threshold crossed
     → return TurnResult (with shadow events, combo, XP, etc.)

Per turn (Read/Recover/Wait):
  3a. ReadAsync()
     → RollEngine.ResolveFixedDC(SA, 12) → reveal interest on success, −1 + Overthinking on fail
  3b. RecoverAsync()
     → RollEngine.ResolveFixedDC(SA, 12) → clear trap on success, −1 on fail
  3c. Wait()
     → −1 interest, advance trap timers

Multi-session (ConversationRegistry):
  ConversationRegistry owns N ConversationEntry instances
    → ScheduleOpponentReply, FastForward (advance IGameClock), ghost/fizzle checks
    → Cross-chat shadow bleed via SessionShadowTracker
```

### Key Design Patterns
- **Stateless engine**: `RollEngine` is a static class. All mutable state (traps, interest) is owned by the caller.
- **Interface-driven injection**: Dice, failure pools, trap registries, item/anatomy repos, LLM adapters, game clock are all interfaces — Unity provides ScriptableObject impls, standalone uses JSON repos / null adapters / fixed clocks.
- **Fragment assembly**: Character identity is built by summing stat modifiers and concatenating text fragments from items + anatomy tiers → `FragmentCollection` → `PromptBuilder`.
- **No external dependencies**: Custom `JsonParser` avoids NuGet dependency for Unity compat.
- **GameSession as orchestrator**: `GameSession` owns a single conversation's mutable state and sequences calls to stateless components and injected interfaces.
- **SessionShadowTracker wraps immutable StatBlock**: Shadow mutation during a session goes through `SessionShadowTracker`, preserving `StatBlock` immutability for the roll engine.

---

## Sprint 8: RPG Rules Complete — Architecture Briefing

### What's changing

**Previous architecture (Sprint 7)**: GameSession orchestrates Speak turns only. Shadow stats are read-only via StatBlock. No XP tracking. No time system. No multi-session management. External bonuses (tell, callback, combo) are typed as fields on TurnResult/DialogueOption but never populated.

**Sprint 8 additions** — 6 new components, significant GameSession expansion:

#### New Components

1. **`SessionShadowTracker`** (Stats/) — Mutable shadow tracking layer wrapping immutable `StatBlock`. Tracks in-session delta per shadow stat. Provides `GetEffectiveStat()` that accounts for session-grown shadows. Includes `DrainGrowthEvents()` for collecting growth event descriptions. This is the **canonical shadow-tracking wrapper** — replaces the `CharacterState` concept from the #44 spec (see ADR: #161 resolution below).

2. **`ShadowThresholdEvaluator`** (Stats/) — Pure static utility: given a shadow value, returns tier (0/1/2/3). Thresholds: 6=T1, 12=T2, 18+=T3.

3. **`IGameClock` + `GameClock`** (Interfaces/ + Conversation/) — Simulated in-game time. Owns `TimeOfDay`, Horniness modifier, energy system. Injectable for testing via `FixedGameClock`.

4. **`ComboTracker`** (Conversation/) — Tracks last N stat plays and detects 8 named combo sequences. Pure data tracker — returns combo name/bonus, GameSession applies the effect.

5. **`XpLedger`** (Progression/) — Accumulates XP events per session. GameSession records events; host reads total at session end.

6. **`PlayerResponseDelayEvaluator`** (Conversation/) — Pure function: `(TimeSpan, StatBlock, InterestState) → DelayPenalty`. No state, no clock dependency.

7. **`ConversationRegistry`** (Conversation/) — Multi-session scheduler. Owns a collection of `ConversationEntry`. Delegates to `IGameClock` for time. Orchestrates fast-forward, ghost/fizzle triggers, cross-chat shadow bleed. Does NOT make LLM calls.

#### Extended Components

8. **`RollEngine`** — Gains `ResolveFixedDC()` overload (for DC 12 rolls). Existing `Resolve()` gains `externalBonus` and `dcAdjustment` optional params (backward-compatible defaults of 0).

9. **`RollResult`** — `IsSuccess` becomes computed from `FinalTotal` (= Total + ExternalBonus) instead of `Total`. Backward-compatible when ExternalBonus=0 (default).

10. **`GameSession`** — Major expansion:
    - New constructor overload accepting `GameSessionConfig` (optional clock, shadow trackers, starting interest)
    - Three new action methods: `ReadAsync()`, `RecoverAsync()`, `Wait()`
    - Shadow growth event detection and recording (#44)
    - Shadow threshold effects on options/rolls (#45)
    - Combo detection via ComboTracker (#46)
    - Callback bonus computation (#47)
    - Tell/WeaknessWindow application (#49, #50)
    - XP recording via XpLedger (#48)
    - Horniness-forced Rizz option logic (#51)

11. **`GameSessionConfig`** (Conversation/) — Optional configuration carrier: IGameClock, SessionShadowTracker (player/opponent), starting interest override, previousOpener.

12. **`InterestMeter`** — Gains `InterestMeter(int startingValue)` constructor overload for Dread≥18 effect.

13. **`TrapState`** — Gains `HasActive` boolean property.

### What is NOT changing
- Stats/StatBlock (remains immutable — SessionShadowTracker wraps it)
- Characters/, Prompts/, Data/ modules remain untouched
- Existing context types (DialogueContext, DeliveryContext, etc.) — already have the fields needed
- Existing TurnResult — already has ShadowGrowthEvents, ComboTriggered, XpEarned etc. fields

### Key Architectural Decisions

#### ADR: Resolve #161 — SessionShadowTracker is canonical, CharacterState is dropped

**Context:** #44 spec introduces `CharacterState(CharacterProfile)` while #139 introduces `SessionShadowTracker(StatBlock)`. Both wrap immutable data with mutable shadow deltas. 5 issues reference `SessionShadowTracker`, only #44 references `CharacterState`.

**Decision:** Keep `SessionShadowTracker` as the sole shadow-tracking wrapper. Add `DrainGrowthEvents()` method to it (from `CharacterState` design). Update #44 implementation to use `SessionShadowTracker` instead of creating `CharacterState`.

**Consequences:**
- `SessionShadowTracker` gains: `DrainGrowthEvents() → IReadOnlyList<string>` (returns accumulated growth event descriptions, clears internal log)
- `SessionShadowTracker.ApplyGrowth()` already returns a description string — `DrainGrowthEvents()` collects these
- #44 implementer uses `GameSessionConfig.PlayerShadows` (a `SessionShadowTracker`) for all shadow mutation
- No `CharacterState` class is created

#### ADR: Resolve #162 — previousOpener goes into GameSessionConfig

**Context:** #44 spec adds `previousOpener` as a GameSession constructor parameter. #139 establishes `GameSessionConfig` as the extension point for optional configuration.

**Decision:** Add `string? PreviousOpener` to `GameSessionConfig`. GameSession reads it from config, not from a dedicated constructor parameter.

#### ADR: Resolve #147 — Read/Recover/Wait action routing

**Decision:** `ReadAsync()`, `RecoverAsync()`, and `Wait()` are self-contained turn actions. They do NOT require `StartTurnAsync()` first. If called after `StartTurnAsync()`, they clear `_currentOptions`. Each method independently checks end conditions and ghost triggers. The `StartTurnAsync → ResolveTurnAsync` invariant only applies to the Speak action path.

#### ADR: Resolve #146 — AddExternalBonus() deprecated

**Decision:** `AddExternalBonus()` remains for backward compatibility but is DEPRECATED. All new external bonuses flow through `RollEngine.Resolve(externalBonus)` parameter. Removal deferred to a cleanup sprint post-Sprint 8.

### Implicit assumptions for implementers

1. **netstandard2.0 + LangVersion 8.0**: No `record` types. Use `sealed class`. `Task<T>` available.
2. **Zero NuGet dependencies**: Do not add any packages.
3. **Nullable reference types enabled**: Use `?` annotations.
4. **`RollEngine.Resolve` mutates `TrapState`**: On TropeTrap, `attackerTraps.Activate()` is called.
5. **`SessionShadowTracker` does NOT replace `StatBlock` in `RollEngine.Resolve()`**: Roll engine receives `StatBlock`. `SessionShadowTracker.GetEffectiveStat()` is used by `GameSession` for threshold/horniness checks.
6. **Existing 254 tests must continue to pass**: All changes must be backward-compatible.
7. **`AddExternalBonus()` is DEPRECATED**: All new external bonuses flow through `RollEngine.Resolve(externalBonus)`.
8. **`TurnResult.ShadowGrowthEvents` already exists** (from PR #117): Implementers populate the existing field, not add a new one.

---

## Rules-to-Code Sync

### Source of Truth

| Layer | Location | Authority |
|-------|----------|-----------|
| Game rules | `design/systems/rules-v3.md` (external repo) | **Authoritative** — all mechanical values originate here |
| Content data | `design/settings/` (external repo) | Authoritative for items, anatomy, traps content |
| C# engine | `src/Pinder.Core/` (this repo) | **Derived** — must match rules exactly |

### Constant Sync Table

| Rules Section | Rule Value | C# Location | C# Constant/Expression |
|---|---|---|---|
| §3 Defence pairings | Charm→SA, Rizz→Wit, Honesty→Chaos, Chaos→Charm, Wit→Rizz, SA→Honesty | `Stats/StatBlock.cs` | `StatBlock.DefenceTable` |
| §3 Base DC | 13 | `Stats/StatBlock.cs` | `StatBlock.GetDefenceDC()` — hardcoded `13 +` |
| §5 Fail tiers | Nat1→Legendary, miss 1–2→Fumble, 3–5→Misfire, 6–9→TropeTrap, 10+→Catastrophe | `Rolls/RollEngine.cs` | Boundary checks in `Resolve()` |
| §5 Success scale | Beat DC by 1–4→+1, 5–9→+2, 10+→+3, Nat20→+4 | `Rolls/SuccessScale.cs` | `SuccessScale.GetInterestDelta()` |
| §5 Failure scale | Fumble→-1, Misfire→-2, TropeTrap→-3, Catastrophe→-4, Legendary→-5 | `Rolls/FailureScale.cs` | `FailureScale.GetInterestDelta()` |
| §5 Risk tier | Need ≤5→Safe, 6–10→Medium, 11–15→Hard, ≥16→Bold | `Rolls/RollResult.cs` | `ComputeRiskTier()` |
| §5 Risk bonus | Hard→+1, Bold→+2 | `Rolls/RiskTierBonus.cs` | `GetInterestBonus()` |
| §6 Interest range | 0–25 | `Conversation/InterestMeter.cs` | `Max = 25`, `Min = 0` |
| §6 Starting interest | 10 | `Conversation/InterestMeter.cs` | `StartingValue = 10` |
| §6 Interest states | Unmatched(0), Bored(1–4), Interested(5–15), VeryIntoIt(16–20), AlmostThere(21–24), DateSecured(25) | `Conversation/InterestMeter.cs` | `GetState()`, `InterestState` enum |
| §6 Advantage from interest | VeryIntoIt/AlmostThere → advantage; Bored → disadvantage | `Conversation/InterestMeter.cs` | `GrantsAdvantage`/`GrantsDisadvantage` |
| §7 Shadow growth table | 20+ trigger conditions | `Conversation/GameSession.cs` | Shadow growth logic |
| §7 Shadow thresholds | 6=T1, 12=T2, 18+=T3 | `Stats/ShadowThresholdEvaluator.cs` | `GetThresholdLevel()` |
| §8 Read/Recover/Wait | DC 12, SA stat, −1 interest on fail | `Conversation/GameSession.cs` | `ReadAsync`/`RecoverAsync`/`Wait` |
| §10 XP thresholds | L1=0...L11=3500 | `Progression/LevelTable.cs` | `XpThresholds` array |
| §10 XP sources | Success 5/10/15, Fail 2, Nat20 25, Nat1 10, Date 50, Recovery 15, Complete 5 | `Progression/XpLedger.cs` | XP award methods |
| §15 Combos | 8 named combos with sequences and bonuses | `Conversation/ComboTracker.cs` | Combo definitions |
| §15 Callback bonus | 2 turns→+1, 4+→+2, opener→+3 | `Conversation/GameSession.cs` | Callback logic |
| §15 Tell bonus | +2 hidden roll bonus | `Conversation/GameSession.cs` | Tell logic |
| §15 Weakness DC | −2 or −3 per crack type | `Conversation/GameSession.cs` | Weakness window logic |
| §15 Horniness forced Rizz | ≥6→1 option, ≥12→always 1, ≥18→all Rizz | `Conversation/GameSession.cs` | Horniness logic |
| §async-time Horniness mod | Morning −2, Afternoon +0, Evening +1, LateNight +3, After2AM +5 | `Conversation/GameClock.cs` | `GetHorninessModifier()` |
| §async-time Delay penalty | <1m→0, 1–15m→0, 15–60m→-1(if≥16), 1–6h→-2, 6–24h→-3, 24h+→-5 | `Conversation/PlayerResponseDelayEvaluator.cs` | Penalty table |
| Momentum | 3-streak→+2, 4-streak→+2, 5+→+3, reset on fail | `Conversation/GameSession.cs` | `GetMomentumBonus()` |
| Ghost trigger | Bored state → 25% chance per turn (dice.Roll(4)==1) | `Conversation/GameSession.cs` | `StartTurnAsync()` |

---

## Component Boundaries

### Stats (`Pinder.Core.Stats`)
- **Owns**: Stat types, shadow stat types, stat block (immutable), session shadow tracking (mutable), shadow threshold evaluation
- **Public API**: `StatType`, `ShadowStatType`, `StatBlock`, `SessionShadowTracker`, `ShadowThresholdEvaluator`
- **Does NOT own**: Roll resolution, interest tracking, character assembly

### Rolls (`Pinder.Core.Rolls`)
- **Owns**: d20 roll resolution, failure tier determination, advantage/disadvantage logic, trap activation during rolls, success/failure scale, risk tier
- **Public API**: `RollEngine.Resolve()`, `RollEngine.ResolveFixedDC()`, `RollResult`, `FailureTier`, `SuccessScale`, `FailureScale`, `RiskTier`, `RiskTierBonus`
- **Does NOT own**: Interest tracking, stat storage, trap definitions, game session orchestration

### Traps (`Pinder.Core.Traps`)
- **Owns**: Trap data model, active trap tracking, turn countdown, trap clearing
- **Public API**: `TrapDefinition`, `TrapState`, `ActiveTrap`, `TrapEffect`
- **Does NOT own**: Trap activation logic (that's in RollEngine), trap content (loaded from JSON)

### Conversation (`Pinder.Core.Conversation`)
- **Owns**: Interest meter, timing profile, game session orchestration, combo tracking, player response delay evaluation, conversation registry (multi-session), game clock implementation
- **Public API**: `InterestMeter`, `InterestState`, `TimingProfile`, `GameSession`, `GameSessionConfig`, `TurnStart`, `TurnResult`, `ReadResult`, `RecoverResult`, `GameStateSnapshot`, `GameOutcome`, `ComboTracker`, `PlayerResponseDelayEvaluator`, `DelayPenalty`, `ConversationRegistry`, `ConversationEntry`, `ConversationLifecycle`, `CrossChatEvent`, `GameClock`
- **Does NOT own**: Roll math (delegates to RollEngine), LLM communication (delegates to ILlmAdapter), character assembly

### Progression (`Pinder.Core.Progression`)
- **Owns**: XP→level resolution, level bonus, build points, item slot counts, failure pool tier, XP ledger
- **Public API**: `LevelTable` (static), `FailurePoolTier`, `XpLedger`
- **Does NOT own**: XP tracking decisions (GameSession decides when to award), character creation validation

### Characters (`Pinder.Core.Characters`)
- **Owns**: Item/anatomy data models, fragment assembly pipeline, archetype ranking, character profile
- **Public API**: `CharacterAssembler`, `FragmentCollection`, `CharacterProfile`, `ItemDefinition`, `AnatomyTierDefinition`, `TimingModifier`
- **Does NOT own**: Item loading (Data/), prompt generation (Prompts/)

### Prompts (`Pinder.Core.Prompts`)
- **Owns**: System prompt string construction from fragments + traps
- **Public API**: `PromptBuilder.BuildSystemPrompt()`
- **Does NOT own**: LLM calling, fragment assembly, trap state management

### Data (`Pinder.Core.Data`)
- **Owns**: JSON parsing, item/anatomy deserialization
- **Public API**: `JsonItemRepository`, `JsonAnatomyRepository`
- **Does NOT own**: File I/O (caller passes JSON string), data validation beyond parsing

### Interfaces (`Pinder.Core.Interfaces`)
- **Owns**: Abstraction contracts for injection points
- **Public API**: `IDiceRoller`, `IFailurePool`, `ITrapRegistry`, `IItemRepository`, `IAnatomyRepository`, `ILlmAdapter`, `IGameClock`, `TimeOfDay`
- **Does NOT own**: Any implementation (implementations live in their owning modules)

---

## Known Gaps (as of Sprint 8)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — shadows are per-session via SessionShadowTracker. Host must persist deltas. |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed post-Sprint 8 |
| Energy system consumers | #144 | IGameClock.ConsumeEnergy() exists but nothing calls it this sprint |
| GameSession god object trajectory | #87 | Acknowledged — extraction planned for next maturity level |
