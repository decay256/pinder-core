# Pinder.Core — Architecture

## Overview

Pinder.Core is a **pure C# (.NET Standard 2.0) RPG engine** for a comedy dating game where players are sentient penises on a Tinder-like app. It has **zero external dependencies** and is designed to drop into Unity or any .NET host.

The engine is **stateless at the roll level** — all state is passed in via parameters. State management (whose turn, what happened last) lives in `GameSession`, which orchestrates a single conversation. The engine owns only the math, the data models, and the session orchestration.

### Module Map

```
Pinder.Core/
├── Stats/          — StatType, ShadowStatType, StatBlock (stat pairs, shadow penalties, DC calc)
├── Rolls/          — RollEngine (stateless), RollResult, FailureTier, SuccessScale, FailureScale, RiskTier
├── Traps/          — TrapDefinition, TrapState, ActiveTrap (trap lifecycle)
├── Progression/    — LevelTable (XP thresholds, level bonuses, build points, item slots), XpLedger
├── Conversation/   — InterestMeter, InterestState, TimingProfile, GameSession, ComboTracker, SessionShadowTracker, OpponentTimingCalculator, GameClock, PlayerResponseDelay
├── Characters/     — CharacterAssembler, FragmentCollection, CharacterProfile, ItemDefinition, AnatomyTierDefinition, TimingModifier
├── Prompts/        — PromptBuilder (assembles LLM system prompt from fragments + traps)
├── Interfaces/     — IDiceRoller, IFailurePool, ITrapRegistry, IItemRepository, IAnatomyRepository, ILlmAdapter, IGameClock
└── Data/           — JsonItemRepository, JsonAnatomyRepository, JsonTrapRepository, JsonParser (hand-rolled JSON parser)
```

### Data Flow (full turn — updated Sprint 3: RPG Rules Complete)

```
Host creates GameSession(player, opponent, config)
  → config bundles: ILlmAdapter, IDiceRoller, ITrapRegistry, IGameClock (optional)
  → session owns InterestMeter, TrapState, SessionShadowTracker, ComboTracker, XpLedger, history, turn counter

Per turn:
  1. StartTurnAsync()
     → check end conditions → ghost check → shadow threshold effects
     → determine adv/disadv from interest state + traps + shadow thresholds
     → horniness-forced Rizz option injection
     → call ILlmAdapter.GetDialogueOptionsAsync() → return TurnStart with options

  2. ResolveTurnAsync(optionIndex) — "Speak" action
     → validate index → compute external bonus (callback, tell, Triple combo)
     → RollEngine.Resolve() with adv/disadv + external bonus
     → SuccessScale + risk tier bonus OR FailureScale → interest delta
     → combo detection → combo bonus
     → update momentum streak
     → activate trap if TropeTrap+ tier
     → weakness window DC reduction (applied before roll via context)
     → InterestMeter.Apply(delta)
     → shadow growth events
     → XP recording
     → ILlmAdapter.DeliverMessageAsync() → player text (post-degradation)
     → check interest threshold crossing → narrative beat
     → ILlmAdapter.GetOpponentResponseAsync() → OpponentResponse (with tell, weakness)
     → append both to history → increment turn → return TurnResult

  3. ReadAsync() — "Read" action (§8)
     → Roll SA vs fixed DC 12 → reveal interest + modifiers on success, -1 interest on fail
     → shadow growth: Overthinking +1 on fail

  4. RecoverAsync() — "Recover" action (§8)
     → only if trap active → Roll SA vs fixed DC 12 → clear trap on success, -1 on fail

  5. Wait() — "Wait" action (§8)
     → skip turn, traps advance, -1 interest
```

### Key Design Patterns
- **Stateless engine**: `RollEngine` is a static class. All mutable state (traps, interest) is owned by the caller.
- **Interface-driven injection**: Dice, failure pools, trap registries, item/anatomy repos, LLM adapters, game clock are all interfaces.
- **Fragment assembly**: Character identity is built by summing stat modifiers and concatenating text fragments from items + anatomy tiers → `FragmentCollection` → `PromptBuilder`.
- **No external dependencies**: Custom `JsonParser` avoids NuGet dependency for Unity compat.
- **GameSession as orchestrator**: Owns all mutable state for a conversation. Sequences calls to stateless components and injected interfaces.
- **Config object pattern**: `GameSessionConfig` bundles all injectable dependencies to keep the constructor stable as new features are added.

---

## Sprint 3: RPG Rules Complete — Architecture Briefing

### What's changing

**Previous architecture (Sprints 1–2)**: GameSession orchestrates a basic Speak-only conversation loop: get options → roll → interest delta (SuccessScale/FailureScale) → momentum → deliver → opponent response. No shadow growth, no risk tier bonus, no alternative turn actions, no combos, no XP, no timing simulation.

**New architecture**: The engine graduates from "basic conversation loop" to "complete RPG rules engine." This requires:

#### New Components

1. **`SessionShadowTracker`** — Mutable per-session shadow stat tracker. Wraps `StatBlock`'s immutable shadow values and tracks growth events. Provides `GetEffectiveShadow(ShadowStatType)` which returns base + session growth. Exposes `Grow(ShadowStatType, int amount)` for growth events. Tracks counters needed for growth conditions (trope trap count, consecutive same-stat count, etc.).

2. **`ComboTracker`** — Tracks last N stats played and whether last roll failed. Detects combo completion per §15 table. Pure state machine with `RecordStat(StatType, bool wasSuccess)` → returns completed combo name or null.

3. **`XpLedger`** — Accumulates XP events per session. `Record(string source, int amount)`. Exposes `Total` and `Events` for end-of-session reporting.

4. **`GameClock`** — Simulated in-game time. `DateTimeOffset Now`, `Advance(TimeSpan)`, `GetTimeOfDay()` → `TimeOfDay` enum, `GetHorninessModifier()`.

5. **`IGameClock`** interface — Injectable clock abstraction for deterministic testing (#67).

6. **`OpponentTimingCalculator`** — Static pure function: `(TimingProfile, int interest, SessionShadowTracker, IDiceRoller) → int delayMinutes`. Replaces the current `TimingProfile.ComputeDelay()` which is too simple for the new shadow-aware formula.

7. **`PlayerResponseDelay`** — Static pure function: `(TimeSpan delay, StatBlock opponentStats, int currentInterest) → int interestDelta`. Penalty table from §async-time.

8. **`RiskTier`** enum — Safe/Medium/Hard/Bold. Computed from `need = DC - (statMod + levelBonus)`.

9. **`OpponentResponse`** — Return type replacing `Task<string>` on `GetOpponentResponseAsync`. Contains `MessageText`, `Tell?`, `WeaknessWindow?`.

10. **`Tell`**, **`WeaknessWindow`**, **`CallbackOpportunity`** — Stub data types for LLM-detected features.

11. **`GameSessionConfig`** — Groups all injectable dependencies for GameSession constructor stability.

#### What's NOT changing (must remain untouched)
- `StatBlock` — remains immutable. Shadow mutation is handled by `SessionShadowTracker` which wraps it.
- `RollResult` constructor — but adds `RiskTier` as a new property computed from existing fields.
- `SuccessScale`, `FailureScale` — unchanged.
- `TrapDefinition`, `TrapState`, `ActiveTrap` — unchanged.
- `LevelTable` — unchanged.
- `CharacterAssembler`, `FragmentCollection`, `PromptBuilder` — unchanged.

#### Key Architectural Decisions

**ADR-1: SessionShadowTracker wraps StatBlock (resolves #58)**
- `StatBlock` stays immutable. `SessionShadowTracker` holds a reference to the original `StatBlock` and a `Dictionary<ShadowStatType, int>` of session growth deltas.
- `GetEffectiveShadow(shadow)` = `statBlock.GetShadow(shadow) + sessionGrowth[shadow]`
- GameSession uses `SessionShadowTracker` for shadow-aware operations. `RollEngine` still takes `StatBlock` but GameSession can create a "view" StatBlock with updated shadow values when needed.

**ADR-2: RollEngine gets `fixedDc` and `externalBonus` parameters (resolves #65, #68)**
- Add optional parameters to `RollEngine.Resolve()`: `int? fixedDc = null`, `int externalBonus = 0`
- When `fixedDc` is set, skip DC computation and use it directly.
- `externalBonus` is added to the total (for callbacks +1/+2/+3, tells +2, Triple combo +1).
- This is backward-compatible — all existing callers pass neither parameter.

**ADR-3: GameSessionConfig bundles dependencies (resolves #82)**
- Instead of adding more constructor parameters (breaks 98 tests), introduce `GameSessionConfig`:
  ```csharp
  public sealed class GameSessionConfig
  {
      public ILlmAdapter Llm { get; }
      public IDiceRoller Dice { get; }
      public ITrapRegistry TrapRegistry { get; }
      public IGameClock? GameClock { get; }  // optional, null = no time tracking
  }
  ```
- Old constructor signature stays for backward compat (wraps into config internally).

**ADR-4: InterestMeter gets configurable starting value (resolves #79)**
- Add constructor overload: `InterestMeter(int startingValue)`
- `GameSession` computes starting value based on shadow thresholds (Dread ≥ 18 → 8).

**ADR-5: Defer #56 ConversationRegistry to next sprint**
- ConversationRegistry is the most complex piece (multi-session, cross-chat shadow bleed, fast-forward).
- Single-session gameplay is sufficient at prototype maturity.
- All other 17 issues provide value without it.

**ADR-6: PlayerResponseDelay uses wall-clock time (resolves #81)**
- Player response delay is measured in real time by the host. The host passes `TimeSpan elapsed` when calling `ResolveTurnAsync`.
- GameClock is for simulated opponent delays, not player measurement.
- `PlayerResponseDelay` is a pure static function: host calls it, passes result to GameSession.

### Migration concerns
- **Backward compat**: Old `GameSession` constructor signature preserved as convenience overload.
- **ILlmAdapter breaking change**: `GetOpponentResponseAsync` return type changes from `Task<string>` to `Task<OpponentResponse>`. This breaks all implementors. Since only `NullLlmAdapter` exists in the repo, migration is trivial.
- **RollEngine**: New optional parameters are backward-compatible.
- **Test impact**: Existing 98 tests should pass after prerequisites (#63, #78) with minimal updates to NullLlmAdapter mock.

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
| §5 Fail tier enum | None, Fumble, Misfire, TropeTrap, Catastrophe, Legendary | `Rolls/FailureTier.cs` | `FailureTier` enum |
| §5 Success scale | Beat DC by 1–4→+1, 5–9→+2, 10+→+3, Nat20→+4 | `Rolls/SuccessScale.cs` | `SuccessScale.GetInterestDelta()` |
| §5 Failure scale | Fumble→-1, Misfire→-2, TropeTrap→-3, Catastrophe→-4, Legendary→-5 | `Rolls/FailureScale.cs` | `FailureScale.GetInterestDelta()` |
| §5 Risk tiers | Need ≤5→Safe, 6–10→Medium, 11–15→Hard, ≥16→Bold | `Rolls/RiskTier.cs` | `RiskTier` enum + `RollResult.RiskTier` — **NEW** |
| §5 Risk tier bonus | Hard success→+1 interest, Bold success→+2 interest | `Conversation/GameSession.cs` | Risk tier bonus in `ResolveTurnAsync` — **NEW** |
| §6 Interest range | 0–25 | `Conversation/InterestMeter.cs` | `InterestMeter.Max = 25`, `InterestMeter.Min = 0` |
| §6 Starting interest | 10 (or 8 if Dread ≥ 18) | `Conversation/InterestMeter.cs` | Constructor overload — **NEW** |
| §6 Interest states | Unmatched(0), Bored(1–4), Interested(5–15), VeryIntoIt(16–20), AlmostThere(21–24), DateSecured(25) | `Conversation/InterestMeter.cs` | `GetState()`, `InterestState` enum |
| §6 Advantage from interest | VeryIntoIt/AlmostThere → advantage; Bored → disadvantage | `Conversation/InterestMeter.cs` | `GrantsAdvantage`/`GrantsDisadvantage` |
| §7 Shadow pairs | Charm↔Madness, Rizz↔Horniness, Honesty↔Denial, Chaos↔Fixation, Wit↔Dread, SA↔Overthinking | `Stats/StatBlock.cs` | `StatBlock.ShadowPairs` |
| §7 Shadow penalty | -1 per 3 shadow points | `Stats/StatBlock.cs` | `GetEffective()` — `shadowVal / 3` |
| §7 Shadow growth table | See #44 issue body | `Conversation/SessionShadowTracker.cs` | Growth event methods — **NEW** |
| §7 Shadow thresholds | 6/12/18 per shadow stat | `Conversation/GameSession.cs` | Threshold checks — **NEW** |
| §8 Turn actions | Speak, Read, Recover, Wait | `Conversation/GameSession.cs` | Public methods — **NEW** |
| §8 Read/Recover DC | 12 (fixed) | `Conversation/GameSession.cs` | Passed as `fixedDc: 12` to RollEngine — **NEW** |
| §10 XP thresholds | L1=0..L11=3500 | `Progression/LevelTable.cs` | `XpThresholds` array |
| §10 XP sources | Success=5/10/15, Fail=2, Nat20=25, Nat1=10, DateSecured=50, TrapRecovery=15, ConvComplete=5 | `Progression/XpLedger.cs` | XP recording — **NEW** |
| §10 Level bonuses | L1–2=+0..L11=+5 | `Progression/LevelTable.cs` | `LevelBonuses` array |
| §10 Build points | L1=0(12 at creation)..L11=0(prestige) | `Progression/LevelTable.cs` | `BuildPointsGranted` array |
| §10 Item slots | L1–2=2..L9–11=6 | `Progression/LevelTable.cs` | `ItemSlots` array |
| §10 Stat caps | Creation=4, Base=6 | `Progression/LevelTable.cs` | `CreationStatCap`, `BaseStatCap` |
| §14/§3.7 Trap taint | Active trap LLM instructions in ALL messages | `Conversation/GameSession.cs` | Trap instruction plumbing — **NEW** |
| §15 Combos | 8 combos with stat sequences | `Conversation/ComboTracker.cs` | Combo detection — **NEW** |
| §15 Callbacks | 2-turn→+1, 4+→+2, opener→+3 hidden bonus | `Conversation/GameSession.cs` | Callback bonus calc — **NEW** |
| §15 Weakness windows | Crack → DC -2/-3 for one turn | `Conversation/GameSession.cs` | Weakness DC reduction — **NEW** |
| §15 Tells | Tell match → +2 hidden roll bonus | `Conversation/GameSession.cs` | Tell bonus calc — **NEW** |
| §15 Horniness Rizz | ≥6→one option, ≥12→always one, ≥18→all Rizz | `Conversation/GameSession.cs` | Option injection — **NEW** |
| Momentum | 3-streak→+2, 4→+2, 5+→+3, reset on fail | `Conversation/GameSession.cs` | Momentum logic |
| Ghost trigger | Bored → 25% chance per turn (dice.Roll(4)==1) | `Conversation/GameSession.cs` | Ghost check |
| Player delay | <1min→0, 1–15→0, 15–60→-1(if≥16), 1–6h→-2, 6–24h→-3, 24h+→-5 | `Conversation/PlayerResponseDelay.cs` | Penalty calc — **NEW** |

### Known Gaps (as of Sprint 3)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| ConversationRegistry (multi-session) | §async-time | Deferred to Sprint 4 per ADR-5 |
| Energy system | §async-time | Deferred — ownership unclear per VC-75 |
| Failure scale values are prototype defaults | §5 | VC-28 — PO should confirm |

---

## Component Boundaries

### Stats (`Pinder.Core.Stats`)
- **Owns**: Stat types, shadow stat types, stat block with effective modifier calculation, defence table, DC calculation
- **Public API**: `StatType` enum, `ShadowStatType` enum, `StatBlock` class
- **Does NOT own**: Shadow growth tracking (that's SessionShadowTracker), roll resolution

### Rolls (`Pinder.Core.Rolls`)
- **Owns**: d20 roll resolution, failure tier determination, advantage/disadvantage logic, trap activation during rolls, success scale, failure scale, risk tier
- **Public API**: `RollEngine.Resolve()`, `RollResult`, `FailureTier`, `RiskTier`, `SuccessScale`, `FailureScale`
- **Does NOT own**: Interest tracking, stat storage, trap definitions, game session orchestration, combo/callback/tell bonus calculation

### Traps (`Pinder.Core.Traps`)
- **Owns**: Trap data model, active trap tracking, turn countdown, trap clearing
- **Public API**: `TrapDefinition`, `TrapState`, `ActiveTrap`, `TrapEffect`
- **Does NOT own**: Trap activation logic (that's in RollEngine), trap content (loaded from JSON), trap taint plumbing (that's GameSession)

### Conversation (`Pinder.Core.Conversation`)
- **Owns**: Interest meter, game session orchestration, combo tracking, session shadow tracking, opponent timing calculation, player response delay, game clock
- **Public API**: `InterestMeter`, `InterestState`, `TimingProfile`, `GameSession`, `GameSessionConfig`, `TurnStart`, `TurnResult`, `GameStateSnapshot`, `GameOutcome`, `ComboTracker`, `SessionShadowTracker`, `OpponentTimingCalculator`, `PlayerResponseDelay`, `GameClock`, `TimeOfDay`
- **Does NOT own**: Roll math (delegates to RollEngine), LLM communication (delegates to ILlmAdapter), character assembly, XP thresholds (that's LevelTable)

### Progression (`Pinder.Core.Progression`)
- **Owns**: XP→level resolution, level bonus, build points, item slot counts, failure pool tier, XP event ledger
- **Public API**: `LevelTable` (static), `FailurePoolTier`, `XpLedger`
- **Does NOT own**: XP source definitions (hardcoded in GameSession), character creation validation

### Characters (`Pinder.Core.Characters`)
- **Owns**: Item/anatomy data models, fragment assembly pipeline, archetype ranking, character profile
- **Public API**: `CharacterAssembler`, `FragmentCollection`, `CharacterProfile`, `ItemDefinition`, `AnatomyTierDefinition`, `TimingModifier`
- **Does NOT own**: Item loading (that's Data/), prompt generation (that's Prompts/)

### Prompts (`Pinder.Core.Prompts`)
- **Owns**: System prompt string construction from fragments + traps
- **Public API**: `PromptBuilder.BuildSystemPrompt()`
- **Does NOT own**: LLM calling, fragment assembly, trap state management

### Data (`Pinder.Core.Data`)
- **Owns**: JSON parsing, item/anatomy/trap deserialization
- **Public API**: `JsonItemRepository`, `JsonAnatomyRepository`, `JsonTrapRepository`
- **Does NOT own**: File I/O (caller passes JSON string), data validation beyond parsing

### Interfaces (`Pinder.Core.Interfaces`)
- **Owns**: Abstraction contracts for injection points
- **Public API**: `IDiceRoller`, `IFailurePool`, `ITrapRegistry`, `IItemRepository`, `IAnatomyRepository`, `ILlmAdapter`, `IGameClock`
- **Does NOT own**: Any implementation (except `NullLlmAdapter` and `GameClock` for defaults)
