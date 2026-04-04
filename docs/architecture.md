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

## Sprint 9: Anthropic LLM Adapter — Architecture Briefing

### What's changing

**Previous architecture (Sprint 8):** Pinder.Core is a single zero-dependency .NET Standard 2.0 library. All LLM interaction is abstracted behind `ILlmAdapter` (Interfaces/). The only concrete implementation is `NullLlmAdapter` (Conversation/) which returns hardcoded responses for testing. No real LLM calls exist.

**Sprint 9 additions** — 1 new project, minor Pinder.Core DTO extensions:

#### New Project: `Pinder.LlmAdapters`

A **separate** .NET Standard 2.0 project that depends on `Pinder.Core` and `Newtonsoft.Json`. This project contains all concrete LLM adapter implementations. The dependency is strictly one-way: `LlmAdapters → Core`. Core has zero knowledge of this project.

```
Pinder.LlmAdapters/
├── Anthropic/
│   ├── Dto/           — MessagesRequest, MessagesResponse, ContentBlock, etc.
│   ├── AnthropicClient.cs        — HTTP transport + retry logic
│   ├── AnthropicLlmAdapter.cs    — ILlmAdapter implementation
│   ├── AnthropicOptions.cs       — Configuration carrier
│   └── AnthropicApiException.cs  — Typed exception
├── SessionDocumentBuilder.cs     — Formats conversation history + prompts for LLM calls
├── PromptTemplates.cs            — Static §3.2–3.8 instruction templates
└── Anthropic/CacheBlockBuilder.cs — Builds cache_control blocks for Anthropic prompt caching
```

#### Data Flow (Anthropic adapter — per turn)

```
GameSession calls ILlmAdapter.GetDialogueOptionsAsync(DialogueContext)
  → AnthropicLlmAdapter receives context
  → CacheBlockBuilder.BuildCachedSystemBlocks(playerPrompt, opponentPrompt)
  → SessionDocumentBuilder.BuildDialogueOptionsPrompt(history, traps, interest, turn, names)
  → AnthropicClient.SendMessagesAsync(request) → HTTP POST to Anthropic Messages API
  → ParseDialogueOptions(responseText) → DialogueOption[]
  → Return to GameSession
```

Caching strategy: character system prompts (~6k tokens) are placed in `cache_control: ephemeral` blocks. Turns 2+ read from cache at 10% of normal input cost. Prompt caching is GA — no beta header required.

#### Pinder.Core DTO Extensions (Vision #211)

`DialogueContext`, `DeliveryContext`, `OpponentContext` gain `PlayerName`, `OpponentName`, `CurrentTurn` fields (with backward-compatible defaults). `GameSession` wires `_player.DisplayName`, `_opponent.DisplayName`, `_turnNumber` through.

#### Tell/WeaknessWindow Signal Generation (Vision #214)

`GetOpponentResponseAsync` instructs Claude to optionally include a `[SIGNALS]` block with Tell/WeaknessWindow data. The adapter parses this structured output. This enables the Tell (+2 roll bonus) and WeaknessWindow (DC −2/−3) mechanics that `GameSession` already consumes from `OpponentResponse`.

### Key Design Decisions

#### ADR: Separate project for LLM adapters
**Context:** Pinder.Core has zero external dependencies (critical for Unity compat). LLM adapters need Newtonsoft.Json for Anthropic API serialization.
**Decision:** Create `Pinder.LlmAdapters` as a separate project. Core stays dependency-free. Unity host references both projects.
**Consequences:** Two assemblies to deploy. Clear boundary: Core = game rules, LlmAdapters = LLM integration.

#### ADR: No anthropic-beta header
**Context:** Issue #206 spec included `anthropic-beta: prompt-caching-2024-07-31`. Anthropic docs (verified 2025-03-30) state prompt caching is GA.
**Decision:** Remove beta header per #213. Use `cache_control` in request body only.

#### ADR: LLM-generated Tell/WeaknessWindow signals
**Context:** #208 initially specified Tell/WeaknessWindow come from context, not LLM. But GameSession reads them from OpponentResponse (#214).
**Decision:** LLM generates signals in structured `[SIGNALS]` block. Adapter parses them leniently (null on parse failure).

### Implicit assumptions for implementers

1. **netstandard2.0 + LangVersion 8.0**: No `record` types. Use `sealed class`. `Enum.Parse` must use `(StatType)Enum.Parse(typeof(StatType), value, true)` — no generic overload.
2. **Newtonsoft.Json allowed in LlmAdapters ONLY**: `Pinder.Core` must not reference it.
3. **Backward compatibility**: All context DTO changes use optional constructor params with defaults. Existing 1118+ tests must pass unchanged.
4. **NullLlmAdapter unchanged**: It stays in Pinder.Core as the test double. Returns null for Tell/WeaknessWindow (by design for testing).

### What is NOT changing
- All Pinder.Core game logic (Stats, Rolls, Traps, Progression, Characters, Prompts, Data)
- GameSession orchestration logic
- Existing NullLlmAdapter behavior
- Any existing test behavior

## Known Gaps (as of Sprint 9)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — shadows are per-session via SessionShadowTracker. Host must persist deltas. |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed post-Sprint 8 |
| Energy system consumers | #144 | IGameClock.ConsumeEnergy() exists but nothing calls it this sprint |
| GameSession god object trajectory | #87 | Acknowledged — extraction planned for next maturity level |
| Prompt template content not yet sourced | §3.2–3.8 | PromptTemplates.cs needs content from character-construction.md |
| AnthropicOptions model string may need updating | — | Default `claude-sonnet-4-20250514` — verify model availability |

---

## Sprint 10: LLM Adapter Bug Fixes — Architecture Briefing

### What's changing

**This sprint continues the existing architecture with no structural changes.** Three bugs in the LLM adapter layer are being fixed. All changes are confined to `Pinder.LlmAdapters` (PromptTemplates, SessionDocumentBuilder, CacheBlockBuilder, AnthropicLlmAdapter) with minor wiring fixes in `Pinder.Core.Conversation.GameSession`.

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates turns, calling `ILlmAdapter` methods with context DTOs (`DialogueContext`, `DeliveryContext`, `OpponentContext`). `Pinder.LlmAdapters` implements `ILlmAdapter` via `AnthropicLlmAdapter`, which uses `SessionDocumentBuilder` for prompt assembly, `CacheBlockBuilder` for Anthropic prompt caching, and `PromptTemplates` for instruction text. The dependency is strictly one-way: `LlmAdapters → Core`.

### Components being extended

1. **`PromptTemplates`** (LlmAdapters) — gains explicit output format in `DialogueOptionsInstruction`, player identity framing in `FailureDeliveryInstruction`, and shadow taint text constants
2. **`SessionDocumentBuilder`** (LlmAdapters) — gains `{player_name}` substitution in delivery, optional `shadowThresholds` parameter on all three build methods
3. **`CacheBlockBuilder`** (LlmAdapters) — gains `BuildPlayerOnlySystemBlocks` (mirrors existing opponent-only method)
4. **`AnthropicLlmAdapter`** (LlmAdapters) — `DeliverMessageAsync` switches to player-only system blocks; all methods pass shadow thresholds through to builder
5. **`GameSession`** (Core) — wires `playerName`, `opponentName`, `currentTurn`, `shadowThresholds` to `DeliveryContext` and `OpponentContext` constructors (fields already exist on DTOs, just not populated)

### What is NOT changing
- All Pinder.Core game logic (Stats, Rolls, Traps, Progression, Characters, Prompts, Data)
- Context DTO class signatures (all new params are already optional with defaults)
- Existing NullLlmAdapter behavior
- Existing test behavior (all changes are backward-compatible via optional params)

### Implicit assumptions for implementers
1. **netstandard2.0 + LangVersion 8.0**: No `record` types. `Dictionary<ShadowStatType, int>?` is the shadow threshold carrier.
2. **Context DTOs already have the fields**: `DeliveryContext.PlayerName`, `.OpponentName`, `.CurrentTurn`, `.ShadowThresholds` all exist with defaults. GameSession just needs to pass values.
3. **`SessionDocumentBuilder` methods are static and pure**: New optional params must have defaults so existing callers (including all tests) compile unchanged.
4. **Shadow taint is cosmetic (flavor text), not mechanical**: Taint blocks instruct the LLM on tone — they don't change game rules or roll math. The mechanical effects (disadvantage at tier 2+) are already handled by GameSession.
5. **`ShadowStatType` enum is in `Pinder.Core.Stats`**: LlmAdapters already references Pinder.Core, so this import is available.

### Known Gaps (as of Sprint 10)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — shadows are per-session via SessionShadowTracker |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | IGameClock.ConsumeEnergy() exists but nothing calls it |
| GameSession god object trajectory | #87 | Acknowledged — extraction planned for next maturity level |
| Opponent shadow threshold computation | §3.6 | GameSession computes player shadow thresholds; #242 adds opponent threshold computation for opponent prompt taint |


---

## Sprint 11: Rules Compliance Fixes — Architecture Briefing

### What's changing

**This sprint continues the existing architecture with no structural changes.** Ten issues fix or complete game-rules logic within the existing module boundaries. All changes are confined to `Pinder.Core` — no `Pinder.LlmAdapters` changes.

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation turns, delegating to `RollEngine` (stateless roll resolution), `InterestMeter`, `TrapState`, `SessionShadowTracker`, `ComboTracker`, and `XpLedger`. State flows in via constructor params; per-turn state is owned by `GameSession`. Data loading is via `JsonParser` → repository classes.

### Components being extended

- `Data/` — new `data/traps/traps.json` file (#265)
- `Rolls/FailureScale` — fix interest deltas to match rules §5 (#266)
- `Rolls/RollEngine` — Catastrophe + Legendary trap activation (#267)
- `Conversation/GameSession` — 7 issues:
  - Momentum as roll bonus (#268)
  - Horniness always rolled (#269)
  - Read/Recover shadow disadvantage (#260)
  - 5 shadow reduction events (#270)
  - Nat 20 crit advantage (#271)
  - Denial +1 on skipped Honesty (#272)
  - Madness T3 option replacement (#273)
- `Conversation/DialogueOption` — gains `IsUnhinged` property (#273)

### What is NOT changing
- Stats module (StatBlock, SessionShadowTracker, ShadowThresholdEvaluator) — no signature changes
- Characters, Prompts, Data modules — untouched
- Pinder.LlmAdapters — untouched
- All context DTOs — no signature changes
- NullLlmAdapter — untouched

### Implicit assumptions for implementers
1. **netstandard2.0 + LangVersion 8.0**: No `record` types, no generic `Enum.Parse<T>`
2. **Zero NuGet dependencies in Pinder.Core**
3. **`ApplyGrowth()` throws on amount ≤ 0** — use `ApplyOffset()` for shadow reductions
4. **`AddExternalBonus()` is DEPRECATED** — use `externalBonus` param on `RollEngine.Resolve()`
5. **Read/Recover are self-contained** — they do NOT call `StartTurnAsync()`
6. **All 1146 existing tests must continue to pass** — changes are backward-compatible
7. **Sequential implementation of GameSession issues** — 7 issues touch the same file

### Known Gaps (as of Sprint 11)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — shadows are per-session |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | `IGameClock.ConsumeEnergy()` exists but nothing calls it |
| GameSession god object trajectory | #87 | Growing — extraction planned for MVP |
| Opponent shadow threshold computation | §3.6 | Player shadow thresholds only |
| FailureScale values diverged from rules | §5 | Fixed this sprint (#266) |
| Catastrophe/Legendary skip trap activation | §5 | Fixed this sprint (#267) |
| Momentum applied as interest delta | §15 | Fixed this sprint (#268) |


---

## Sprint 12: Rules Compliance Round 2 — Architecture Briefing

### What's changing

**This sprint continues the existing architecture with no structural changes.** Ten issues fix or complete game-rules logic within the existing module boundaries. Changes span `Pinder.Core` (InterestState, InterestMeter, RollEngine, RollResult, SuccessScale, GameSession, DialogueOption, traps.json data) and `Pinder.LlmAdapters` (PromptTemplates, SessionDocumentBuilder). No new components, projects, or dependencies.

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation turns, delegating to `RollEngine` (stateless roll resolution), `InterestMeter`, `TrapState`, `SessionShadowTracker`, `ComboTracker`, and `XpLedger`. `Pinder.LlmAdapters` depends on `Pinder.Core` and implements `ILlmAdapter` via `AnthropicLlmAdapter`, using `SessionDocumentBuilder` for prompt assembly and `PromptTemplates` for instruction text constants. Dependency is strictly one-way: `LlmAdapters → Core`.

### Components being extended

- `Data/` — traps.json verification/fix (#306)
- `Rolls/RollEngine` — failure tier uses FinalTotal (#309)
- `Rolls/SuccessScale` — margin uses FinalTotal (#309)
- `Rolls/RollResult` — MissMargin uses FinalTotal (#309)
- `Conversation/InterestState` — gains Lukewarm (5-9) (#313)
- `Conversation/InterestMeter` — GetState() split (#313)
- `Conversation/DialogueOption` — gains IsUnhingedReplacement (#310)
- `Conversation/GameSession` — 6 issues:
  - Shadow raw values instead of tiers (#307)
  - Wire shadowThresholds to Delivery/Opponent contexts (#308)
  - beatDcBy uses FinalTotal (#309)
  - Madness T3 unhinged option (#310)
  - Triple bonus on Read/Recover (#312)
  - XP risk-tier multiplier (#314)
- `LlmAdapters/PromptTemplates` — tell categories (#311)

### What is NOT changing
- Stats module (StatBlock, SessionShadowTracker, ShadowThresholdEvaluator)
- Characters, Prompts modules
- NullLlmAdapter
- Existing context DTO class signatures (all params already optional)

### Implicit assumptions for implementers
1. **netstandard2.0 + LangVersion 8.0**: No `record` types, no generic `Enum.Parse<T>`
2. **Zero NuGet dependencies in Pinder.Core**
3. **`AddExternalBonus()` is DEPRECATED** — all new bonuses via `externalBonus` param
4. **All 1718 existing tests must continue to pass** — changes backward-compatible when externalBonus=0
5. **Shadow thresholds change from tier (0-3) to raw values (0-30+)** after #307 — all T3 checks become `>= 18`
6. **Lukewarm enum insertion shifts ordinals** — acceptable at prototype maturity
7. **Sequential implementation within waves** — multiple issues touch GameSession

### Known Gaps (as of Sprint 12)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — shadows are per-session |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | `IGameClock.ConsumeEnergy()` exists but nothing calls it |
| GameSession god object trajectory | #87 | Growing — extraction planned for MVP |
| Opponent shadow threshold computation | §3.6 | #308 adds opponent shadows to OpponentContext |
| Shadow taint never fired | §11 | Fixed this sprint (#307) |
| FinalTotal not used for tier/scale | §5 | Fixed this sprint (#309) |
| Lukewarm state missing | §6 | Fixed this sprint (#313) |
| XP risk multiplier missing | §10 | Fixed this sprint (#314) |
| Madness T3 not implemented | §7 | Fixed this sprint (#310) |
| Tell categories not in prompt | §15 | Fixed this sprint (#311) |
| Triple bonus on Read/Recover | §15 | Fixed this sprint (#312) |


---

## Sprint 2 (Player Agent + Sim Runner Fixes) — Architecture Briefing

### What's changing

**This sprint continues the existing architecture with no structural changes to Pinder.Core.** New components are added to the `session-runner/` project only. Bug fixes touch GameSession (Fixation probability), InterestChangeContext (opponent prompt for beats), and AnthropicLlmAdapter (beat voice).

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation turns, delegating to `RollEngine` (stateless), `InterestMeter`, `TrapState`, `SessionShadowTracker`, `ComboTracker`, and `XpLedger`. `Pinder.LlmAdapters` implements `ILlmAdapter` via `AnthropicLlmAdapter`. The session runner (`session-runner/`) is a .NET 8 console app that drives `GameSession` + `AnthropicLlmAdapter` for automated playtesting.

### New components (session-runner/ only)

1. **`IPlayerAgent`** — Decision-making interface for sim agents. Takes `TurnStart` + `PlayerAgentContext`, returns `PlayerDecision` (index, reasoning, scores). Per #355, lives in session-runner, NOT Pinder.Core.

2. **`ScoringPlayerAgent`** — Deterministic expected-value scoring. Pure math, no LLM. Scores all options using success probability × expected gain − failure cost. Applies strategic adjustments for momentum, interest state, trap exposure.

3. **`LlmPlayerAgent`** — Anthropic-backed agent. Formats full game state into LLM prompt, parses `PICK: [A/B/C/D]` response. Falls back to `ScoringPlayerAgent` on any failure.

4. **`PlayerDecision`**, **`OptionScore`**, **`PlayerAgentContext`** — Supporting data types for the agent interface.

### Bug fixes

5. **File counter** (#354 + #359) — Fix glob from `session-???.md` to `session-*.md` and parsing from `Substring(8,3)` to `Split('-')[1]`.

6. **Trap registry** (#353 + #356) — Replace `NullTrapRegistry` with `JsonTrapRepository(File.ReadAllText(path))`.

7. **Fixation probability** (#349) — Replace `optionIndex == 0` proxy with actual probability comparison across all options.

8. **Interest beat voice** (#352 + #357) — Add optional `opponentPrompt` to `InterestChangeContext`, wire through GameSession, include in adapter system blocks.

### Session runner enhancements

9. **Shadow tracking** (#350 + #360) — Wire `SessionShadowTracker(sableStats)` via `GameSessionConfig`. Display growth events per turn and delta table at session end.

10. **Pick reasoning output** (#351) — Display `PlayerDecision.Reasoning` and score table in playtest markdown.

### Components being extended
- `session-runner/Program.cs` — #354, #353, #350, #351, #348
- `Pinder.Core/Conversation/GameSession.cs` — #349
- `Pinder.Core/Conversation/InterestChangeContext.cs` — #352
- `Pinder.LlmAdapters/Anthropic/AnthropicLlmAdapter.cs` — #352

### What is NOT changing
- All Pinder.Core game logic (Stats, Rolls, Traps, Progression, Characters, Prompts, Data)
- GameSession public API (no new public methods)
- Existing NullLlmAdapter behavior
- Existing context DTO class signatures (optional params only)

### Implicit assumptions for implementers
1. **netstandard2.0 + LangVersion 8.0** in Pinder.Core; net8.0 in session-runner (but LangVersion 8.0 per csproj)
2. **Zero NuGet dependencies in Pinder.Core**
3. **`JsonTrapRepository(string json)` takes content, not path** (#356)
4. **`SessionShadowTracker(StatBlock)` takes StatBlock, not Dictionary** (#360)
5. **All 1977 existing tests must pass** unchanged
6. **Context DTO changes use optional params with defaults** (#357)
7. **Player agent types go in session-runner, not Pinder.Core** (#355)
8. **File counter glob must be `session-*.md`** (#359)

### ADR: ScoringPlayerAgent reuses engine methods (#386)

**Context:** ScoringPlayerAgent needs callback, momentum, and tell bonus values for its EV formula. These same values are computed inside `GameSession.ResolveTurnAsync()` and `CallbackBonus.Compute()`.

**Decision:** ScoringPlayerAgent MUST call `CallbackBonus.Compute()` directly (it's public static in `Pinder.Core.Conversation`). Momentum bonus logic duplicates `GameSession.GetMomentumBonus()` (which is private static) with a `// SYNC: GameSession.GetMomentumBonus()` comment. Tell bonus is hardcoded `2` with a `// SYNC: GameSession ResolveTurnAsync tellBonus` comment.

**Consequences:** Callback bonus is guaranteed in sync. Momentum and tell bonuses have sync comments that flag potential drift during code review. Acceptable at prototype maturity.

### Known Gaps (as of Sprint 2)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — per-session via SessionShadowTracker |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | `IGameClock.ConsumeEnergy()` exists but nothing calls it |
| GameSession god object trajectory | #87 | Growing — extraction planned for MVP |
| Fixation uses option index as proxy | §7 | Fixed this sprint (#349) |
| Interest beat lacks character voice | §3.8 | Fixed this sprint (#352) |
| Session runner NullTrapRegistry | — | Fixed this sprint (#353) |
| File counter glob mismatch | — | Fixed this sprint (#354 + #359) |
| No automated player agent | — | Added this sprint (#346, #347, #348) |
| ScoringPlayerAgent bonus constant sync | #386 | Mitigated via CallbackBonus.Compute() + SYNC comments |


---

## Sprint (Sim Runner + Scorer Improvements) — Architecture Briefing

### What's changing

**This sprint continues the existing architecture with no structural changes to Pinder.Core or Pinder.LlmAdapters.** All changes are confined to `session-runner/` (the .NET 8 console app), plus copying data files from the external `pinder` repo into `pinder-core` so the CharacterAssembler pipeline can run standalone.

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `GameSession` orchestrates single-conversation turns. `Pinder.LlmAdapters` implements `ILlmAdapter` via `AnthropicLlmAdapter`. The session runner (`session-runner/`) is a .NET 8 console app that drives `GameSession` + `AnthropicLlmAdapter` for automated playtesting. Player agent types (`IPlayerAgent`, `ScoringPlayerAgent`, `LlmPlayerAgent`) live in `session-runner/`.

### New components (session-runner/ only)

1. **`CharacterLoader`** — Parses pre-assembled prompt files (`design/examples/{name}-prompt.md`), extracts stat block, shadow values, level, system prompt. Returns `CharacterProfile` for GameSession.

2. **`CharacterDefinitionLoader`** — Loads character definition JSON, runs the full `CharacterAssembler` + `PromptBuilder` pipeline, returns `CharacterProfile`. Bridges the `FragmentCollection` → `CharacterProfile` gap (#419).

3. **`DataFileLocator`** — Resolves data file paths by walking up from base directory. Follows the same pattern as `TrapRegistryLoader`.

4. **`OutcomeProjector`** — Pure function: given interest, momentum, and turn count at cutoff, returns projected outcome text.

### Extended components

5. **`SessionFileCounter`** — Bug fix for path resolution (#418).

6. **`ScoringPlayerAgent`** — Shadow growth risk scoring: Fixation growth penalty, Denial skip penalty, Fixation threshold EV reduction, stat variety bonus (#416).

7. **`PlayerAgentContext`** — Gains `LastStatUsed`, `SecondLastStatUsed`, `HonestyAvailableLastTurn` fields (#416).

8. **`Program.cs`** — CLI arg parsing (`--player`, `--opponent`, `--player-def`, `--opponent-def`, `--max-turns`, `--agent`), projected outcome reporting, CharacterAssembler pipeline wiring (#414, #415, #417).

### Data files added

- `data/items/starter-items.json` — copied from external repo (#415, #421)
- `data/anatomy/anatomy-parameters.json` — copied from external repo (#415, #421)
- `data/characters/{gerald,velvet,sable,brick,zyx}.json` — character definitions (#415)

### What is NOT changing
- All Pinder.Core game logic (Stats, Rolls, Traps, Progression, Characters, Prompts, Data)
- Pinder.LlmAdapters
- GameSession public API
- NullLlmAdapter
- Existing test behavior

### Key Architectural Decisions

#### ADR: Copy data files into pinder-core repo
**Context:** #415 requires item/anatomy JSON for CharacterAssembler. Files only exist in external repo (#421).
**Decision:** Copy into `data/items/` and `data/anatomy/`. Matches existing `data/traps/traps.json` pattern.
**Consequences:** Data duplication across repos. Acceptable at prototype.

#### ADR: --max-turns owned by #414 with default 20
**Context:** #414 and #417 both add `--max-turns` with conflicting defaults (#422).
**Decision:** #414 adds the arg with default 20. #417 only adds projection logic.
**Consequences:** #414 adopts #417's recommended default, eliminating merge conflict.

#### ADR: CharacterAssembler → CharacterProfile bridging in loader
**Context:** `CharacterAssembler.Assemble()` returns `FragmentCollection`, not `CharacterProfile` (#419).
**Decision:** `CharacterDefinitionLoader` bridges: `Assemble()` → `PromptBuilder.BuildSystemPrompt()` → `new CharacterProfile(...)`.
**Consequences:** Loader needs `TrapState` for prompt building — uses `new TrapState()` (empty).

### Implicit assumptions for implementers
1. **netstandard2.0 + LangVersion 8.0** in Pinder.Core; net8.0 + LangVersion 8.0 in session-runner
2. **Zero NuGet dependencies in Pinder.Core**
3. **Session runner CAN use System.Text.Json** (built into net8.0) for character definition parsing
4. **`CharacterAssembler.Assemble()` returns `FragmentCollection`** — NOT `CharacterProfile`
5. **`PromptBuilder.BuildSystemPrompt()` requires `TrapState`** — use `new TrapState()` for initial generation
6. **`PlayerAgentContext` changes must be backward-compatible** — new params have defaults
7. **All existing tests must pass**
8. **External data files** at `/root/.openclaw/agents-extra/pinder/data/`

### Known Gaps (as of this sprint)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — per-session via SessionShadowTracker |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | `IGameClock.ConsumeEnergy()` exists but nothing calls it |
| GameSession god object trajectory | #87 | Growing — extraction planned for MVP |
| ScoringPlayerAgent bonus constant sync | #386 | Mitigated via CallbackBonus.Compute() + SYNC comments |
| File counter still producing session-001 | — | Fixed this sprint (#418) |
| Scorer ignores shadow growth risk | §7 | Fixed this sprint (#416) |
| Characters hardcoded in runner | — | Fixed this sprint (#414) |
| CharacterAssembler never tested E2E | — | Fixed this sprint (#415) |
| Max turns too low (15) | — | Fixed this sprint (#417) |
| Data files missing from pinder-core | #421 | Fixed this sprint (#415) |


---

## Sprint (Rules DSL + Rule Engine) — Architecture Briefing

### What's changing

**This sprint introduces a Rules DSL pipeline (Python tooling fixes + enrichment) and a new `Pinder.Rules` project (C# rule engine).** Changes span the external `pinder` repo Python tools, YAML data files, and a new .NET project.

**Previous architecture**: Game constants (failure deltas, interest thresholds, risk bonuses, shadow thresholds) are hardcoded in static C# classes (`FailureScale`, `SuccessScale`, `InterestMeter`, `RiskTierBonus`, `ShadowThresholdEvaluator`). Python tooling in the external repo extracts markdown → YAML → regenerated markdown for round-trip validation, and generates C# test stubs from enriched YAML. Only `rules-v3-enriched.yaml` has structured `condition`/`outcome` fields.

#### New Project: `Pinder.Rules`

A **separate** .NET Standard 2.0 project that depends on `Pinder.Core` + `YamlDotNet`. Contains a generic rule engine that loads enriched YAML and evaluates conditions against game state snapshots. Dependency is strictly one-way: `Pinder.Rules → Pinder.Core`. Core has zero knowledge of this project.

```
src/Pinder.Rules/
├── Pinder.Rules.csproj        — netstandard2.0, refs Pinder.Core + YamlDotNet
├── RuleEntry.cs               — POCO for a single rule entry
├── RuleBook.cs                — Loads YAML, indexes by id and type
├── GameState.cs               — Snapshot carrier for condition evaluation
├── ConditionEvaluator.cs      — Static: Evaluate(condition, GameState) → bool
├── OutcomeDispatcher.cs       — Static: Dispatch(outcome, GameState, IEffectHandler)
└── IEffectHandler.cs          — Callback interface for outcome effects
```

#### Python tooling fixes (#443)
- `extract.py` gains block-order preservation (paragraphs, tables, code blocks stored as ordered list)
- `generate.py` reproduces blocks in original order, preserves table column widths

#### Enrichment of all 9 YAML files (#444)
- 8 additional YAML files gain structured `condition`/`outcome` fields using the same vocabulary as `rules-v3-enriched.yaml`

#### Test stub integration (#445)
- 54 generated C# test stubs integrated into `tests/Pinder.Core.Tests/RulesSpec/`
- Method paths corrected to use real Pinder.Core APIs
- 17 stubs marked `[Fact(Skip = "...")]` for non-testable rules

### Key Architectural Decisions

#### ADR: Pinder.Rules as separate project
**Context:** Rule engine needs YAML parsing (YamlDotNet). Pinder.Core must remain zero-dependency.
**Decision:** Create `Pinder.Rules` as a separate project, same pattern as `Pinder.LlmAdapters`.
**Consequences:** Three assembly ecosystem. Clear boundary: Core = game rules (hardcoded), Rules = data-driven rule evaluation, LlmAdapters = LLM integration.

#### ADR: No direct GameSession integration this sprint
**Context:** #446 AC says "GameSession uses the engine for those two sections." Direct integration requires either Core referencing Rules (breaks zero-dep invariant) or complex delegate wiring.
**Decision:** Prove equivalence via tests. Rule engine evaluates §5/§6 rules identically to hardcoded C#. Direct GameSession wiring deferred to follow-up sprint.
**Consequences:** Hardcoded constants remain in C# this sprint. Rule engine is validated but not yet wired into game loop.

#### ADR: Untyped condition/outcome dictionaries
**Context:** YAML condition/outcome fields have heterogeneous key sets per rule type.
**Decision:** Use `Dictionary<string, object>` at prototype maturity. Type-safe condition classes deferred to MVP.
**Consequences:** Runtime type checking in ConditionEvaluator. Flexible but not compile-time safe.

### Implicit assumptions for implementers

1. **netstandard2.0 + LangVersion 8.0** in Pinder.Rules — no `record` types
2. **YamlDotNet 16.3.0** — supports netstandard2.0 with zero transitive deps
3. **Pinder.Core MUST NOT reference Pinder.Rules** — dependency is one-way only
4. **All 2453 existing tests must pass unchanged**
5. **Python 3 with PyYAML** for tooling — already used in existing pipeline
6. **YAML files are loaded as content strings** — caller handles file I/O

### What is NOT changing
- All Pinder.Core game logic (Stats, Rolls, Traps, Progression, Characters, Prompts, Data)
- Pinder.LlmAdapters
- GameSession public API
- NullLlmAdapter
- Session runner

### Known Gaps (as of this sprint)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — per-session via SessionShadowTracker |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | `IGameClock.ConsumeEnergy()` exists but nothing calls it |
| GameSession god object trajectory | #87 | Growing — extraction planned for MVP |
| Rule engine not wired into GameSession | — | Equivalence proven via tests; integration deferred |
| Hardcoded constants duplicated in C# + YAML | §5, §6 | Intentional at prototype; YAML becomes source of truth at MVP |
| 8 YAML files lack enrichment | — | Fixed this sprint (#444) |
| Round-trip diffs ~1251 lines | — | Fixed this sprint (#443) |
| Generated test stubs not in CI | — | Fixed this sprint (#445) |


---

## Sprint (Wire GameSession to Rule Engine) — Architecture Briefing

### What's changing

**This sprint introduces one structural addition**: an `IRuleResolver` interface in `Pinder.Core.Interfaces` that bridges the dependency gap between `Pinder.Core` (zero deps) and `Pinder.Rules` (YamlDotNet) via Dependency Inversion. Core defines the abstraction, Rules provides the implementation. GameSession gains optional data-driven rule resolution with null-safe fallback to existing hardcoded statics.

**Previous architecture**: GameSession calls hardcoded static classes (`FailureScale`, `SuccessScale`, `RiskTierBonus`, `ShadowThresholdEvaluator`) and private methods (`GetMomentumBonus`) for all game constants. The `Pinder.Rules` project exists with `RuleBook`, `ConditionEvaluator`, and `OutcomeDispatcher` but is not connected to GameSession.

#### New Components

1. **`IRuleResolver`** (Interfaces/) — Abstraction for data-driven game constant resolution. Methods return nullable values — null means "no rule matched, use hardcoded fallback". Covers §5 (failure/success deltas), §6 (interest states), §7 (shadow thresholds), §15 (momentum bonuses, risk-tier XP multipliers).

2. **`RuleBookResolver`** (Pinder.Rules/) — Implements `IRuleResolver` using `RuleBook` + `ConditionEvaluator`. Accepts one or more RuleBooks (for multi-file YAML). Thread-safe after construction.

#### Extended Components

3. **`GameSessionConfig`** — Gains `IRuleResolver? Rules` property (optional, null default).

4. **`GameSession`** — 5 call sites gain `_rules?.GetX() ?? hardcoded` pattern:
   - `FailureScale.GetInterestDelta()` → `_rules.GetFailureInterestDelta()`
   - `SuccessScale.GetInterestDelta()` → `_rules.GetSuccessInterestDelta()`
   - `_interest.GetState()` → `_rules.GetInterestState()`
   - `ShadowThresholdEvaluator.GetThresholdLevel()` → `_rules.GetShadowThresholdLevel()`
   - `GetMomentumBonus()` → `_rules.GetMomentumBonus()`
   - `ApplyRiskTierMultiplier()` → `_rules.GetRiskTierXpMultiplier()`

### Key Architectural Decisions

#### ADR: IRuleResolver via Dependency Inversion (resolves deferred integration from Rules DSL sprint)

**Context:** The Rules DSL sprint deferred GameSession integration because Core can't reference Rules. The issue (#463) now requires wiring.

**Decision:** Define `IRuleResolver` in `Pinder.Core.Interfaces`. `Pinder.Rules` implements it as `RuleBookResolver`. GameSession accepts it via `GameSessionConfig.Rules`. All methods return nullable — null triggers hardcoded fallback.

**Consequences:**
- Core remains zero-dependency (interface only, no YAML knowledge)
- Rules project gains one new file implementing the interface
- GameSession call sites gain ~2 lines each for the fallback pattern
- Host (session-runner) is responsible for loading YAML and creating the resolver

#### ADR: Multi-file RuleBook merge

**Context:** §5/§6/§7 rules live in `rules-v3-enriched.yaml`. §15 momentum/risk-tier rules live in `risk-reward-and-hidden-depth-enriched.yaml`.

**Decision:** `RuleBookResolver` accepts multiple `RuleBook` instances. Host loads both YAML files. Later books' entries are additive (no id collision expected across files).

**Consequences:** Host must know which YAML files to load. Acceptable at prototype maturity.

### What is NOT changing
- Static classes (FailureScale, SuccessScale, etc.) — remain as fallback
- InterestMeter.GetState() signature — unchanged
- Pinder.LlmAdapters — untouched
- All existing tests — pass unchanged

### Implicit assumptions for implementers

1. **netstandard2.0 + LangVersion 8.0** — no records, no generic Enum.Parse
2. **Pinder.Core MUST NOT reference Pinder.Rules** — IRuleResolver in Core, implementation in Rules
3. **All 2651 existing tests must pass unchanged**
4. **YAML files loaded by host, not GameSession** — GameSession receives IRuleResolver via config
5. **Null-return = use hardcoded fallback** — every IRuleResolver method returns nullable
6. **InterestMeter class NOT modified** — GameSession wraps calls externally
7. **Shadow thresholds are generic** — IRuleResolver returns tier (0-3), not per-shadow effects

### Known Gaps (as of this sprint)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — per-session via SessionShadowTracker |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | `IGameClock.ConsumeEnergy()` exists but nothing calls it |
| GameSession god object trajectory | #87 | Growing — extraction planned for MVP |
| Hardcoded constants duplicated in C# + YAML | §5, §6, §15 | Intentional — YAML is primary, C# is fallback |
| Rule engine not wired for all sections | §8-§14 | Only §5/§6/§7/§15 wired this sprint |
| Per-shadow-type threshold effects | §7 | IRuleResolver returns generic tier, not per-shadow effects |


---

## Sprint (Rules DSL Completeness) — Architecture Briefing

### What's changing

**This sprint continues the existing architecture with no structural changes.** Three issues improve the Rules DSL pipeline (Python tooling + YAML data + generated C# tests). No new projects, no new C# components, no dependency changes. All changes are confined to `rules/tools/` (Python), `rules/extracted/` (YAML data), and `tests/Pinder.Core.Tests/RulesSpec/` (generated test stubs).

**Existing architecture summary**: Pinder.Core is a zero-dependency .NET Standard 2.0 RPG engine. `Pinder.Rules` (YamlDotNet) provides data-driven rule evaluation via `RuleBook`, `ConditionEvaluator`, `OutcomeDispatcher`. `IRuleResolver` in Core bridges to Rules via dependency inversion. The Rules DSL pipeline (`rules/tools/`) transforms authoritative Markdown docs into YAML (`rules/extracted/`) and back (`rules/regenerated/`), with enrichment adding structured `condition`/`outcome` fields.

### Components being extended

- `rules/tools/extract.py` — #443: block-order preservation, table column width metadata
- `rules/tools/generate.py` — #443: ordered block rendering, separator row fidelity
- `rules/tools/enrich.py` — #444: enrichment patterns for 8 additional YAML files
- `rules/extracted/*-enriched.yaml` — #444: 351 enriched entries across 9 files
- `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs` — #445: 54 test stubs (37 active + 17 skipped)

### What is NOT changing

- All Pinder.Core game logic (Stats, Rolls, Traps, Progression, Characters, Prompts, Data)
- Pinder.Rules project (RuleBook, ConditionEvaluator, etc.)
- Pinder.LlmAdapters
- GameSession, IRuleResolver wiring
- NullLlmAdapter
- Session runner

### Implicit assumptions for implementers

1. **Python 3 + PyYAML** for all pipeline tooling
2. **YAML enrichment vocabulary** shared across all 9 files — condition/outcome keys must match existing patterns from `rules-v3-enriched.yaml`
3. **All 2716 existing C# tests must pass** — test stubs are additive only
4. **17 skipped stubs** are for LLM/qualitative rules — must remain as `[Fact(Skip = "...")]`
5. **Round-trip diffs < 50 lines per doc** — whitespace-only diffs acceptable

### Known Gaps (as of this sprint)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow persistence across sessions | §8 | Not addressed — per-session via SessionShadowTracker |
| `AddExternalBonus()` deprecated but not removed | — | Cleanup issue needed |
| Energy system consumers | #144 | `IGameClock.ConsumeEnergy()` exists but nothing calls it |
| GameSession god object trajectory | #87 | Growing — extraction planned for MVP |
| Hardcoded constants duplicated in C# + YAML | §5, §6, §15 | Intentional — YAML is primary, C# is fallback |
| Rule engine not wired for all sections | §8-§14 | Only §5/§6/§7/§15 wired |
| Per-shadow-type threshold effects | §7 | IRuleResolver returns generic tier, not per-shadow effects |
| enrich.py is 1839 lines of pattern matching | — | Brittle but acceptable at prototype |
| Round-trip diffs not zero | — | <50 per doc, whitespace-only — within tolerance |
