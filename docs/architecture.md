# Pinder.Core — Architecture

## Overview

Pinder.Core is a **pure C# (.NET Standard 2.0) RPG engine** for a comedy dating game where players are sentient penises on a Tinder-like app. It has **zero external dependencies** and is designed to drop into Unity or any .NET host.

The engine is **stateless at the roll level** — all state is passed in via parameters. State management (whose turn, what happened last) lives in the host (Unity game loop). The engine owns only the math and the data models.

### Module Map

```
Pinder.Core/
├── Stats/          — StatType, ShadowStatType, StatBlock (stat pairs, shadow penalties, DC calc)
├── Rolls/          — RollEngine (stateless), RollResult, FailureTier, SuccessScale, FailureScale
├── Traps/          — TrapDefinition, TrapState, ActiveTrap (trap lifecycle)
├── Progression/    — LevelTable (XP thresholds, level bonuses, build points, item slots)
├── Conversation/   — InterestMeter (0–25 interest tracker), InterestState, TimingProfile, GameSession
├── Characters/     — CharacterAssembler, FragmentCollection, CharacterProfile, ItemDefinition, AnatomyTierDefinition, TimingModifier
├── Prompts/        — PromptBuilder (assembles LLM system prompt from fragments + traps)
├── Interfaces/     — IDiceRoller, IFailurePool, ITrapRegistry, IItemRepository, IAnatomyRepository, ILlmAdapter
└── Data/           — JsonItemRepository, JsonAnatomyRepository, JsonParser (hand-rolled JSON parser)
```

### Data Flow (full turn — NEW as of Sprint 6)

```
Host creates GameSession(player, opponent, llm, dice, trapRegistry)
  → session owns InterestMeter, TrapState, history, turn counter

Per turn:
  1. StartTurnAsync()
     → check end conditions → determine adv/disadv from interest state + traps
     → call ILlmAdapter.GetDialogueOptionsAsync() → return TurnStart with options

  2. ResolveTurnAsync(optionIndex)
     → validate index → RollEngine.Resolve() with adv/disadv
     → SuccessScale.GetInterestDelta() or FailureScale.GetInterestDelta() → interest delta
     → update momentum streak → activate trap if TropeTrap+ tier
     → InterestMeter.Apply(delta)
     → ILlmAdapter.DeliverMessageAsync() → player text (post-degradation)
     → check interest threshold crossing → ILlmAdapter.GetInterestChangeBeatAsync() if crossed
     → ILlmAdapter.GetOpponentResponseAsync() → opponent reply
     → append both to history → increment turn → return TurnResult
```

### Key Design Patterns
- **Stateless engine**: `RollEngine` is a static class. All mutable state (traps, interest) is owned by the caller.
- **Interface-driven injection**: Dice, failure pools, trap registries, item/anatomy repos, LLM adapters are all interfaces — Unity provides ScriptableObject impls, standalone uses JSON repos / null adapters.
- **Fragment assembly**: Character identity is built by summing stat modifiers and concatenating text fragments from items + anatomy tiers → `FragmentCollection` → `PromptBuilder`.
- **No external dependencies**: Custom `JsonParser` avoids NuGet dependency for Unity compat.
- **GameSession as orchestrator**: `GameSession` is the first stateful component in the engine. It owns a single conversation's mutable state and sequences calls to stateless components (RollEngine, SuccessScale, FailureScale) and injected interfaces (ILlmAdapter, IDiceRoller, ITrapRegistry).

---

## Sprint 6: Game Session + LLM Adapter — Architecture Briefing

### What's changing

**Previous architecture**: The engine was a collection of stateless utilities and data models. The host (Unity) was responsible for orchestrating the game loop: calling RollEngine, tracking interest, managing traps, calling the LLM. The engine had no concept of a "turn" or a "session."

**New architecture**: Two new components are introduced:

1. **`ILlmAdapter`** (Issue #26) — An interface in `Pinder.Core.Interfaces` that abstracts all LLM interactions. Four async methods: get dialogue options, deliver message, get opponent response, get interest change narrative beat. Plus context types that carry exactly the data the LLM needs. Plus `NullLlmAdapter` for testing.

2. **`GameSession`** (Issue #27) — A stateful orchestrator in `Pinder.Core.Conversation` that runs a single Pinder conversation end-to-end. It owns `InterestMeter`, `TrapState`, conversation history, momentum streak, and turn count. It sequences: options → roll → interest delta → trap → deliver → opponent response. It is the first class in the engine that holds mutable state across method calls.

**What is NOT changing**: Stats, Rolls, Traps, Progression, Characters, Prompts, Data modules remain untouched. `RollEngine` stays stateless. `InterestMeter` stays a simple value tracker.

### New dependency: `FailureScale`

Issue #28 identified that failure interest deltas are unspecified. For prototype maturity, we introduce `FailureScale` (companion to `SuccessScale`) with conservative defaults:

| FailureTier | Interest Delta |
|-------------|---------------|
| Fumble | -1 |
| Misfire | -2 |
| TropeTrap | -3 |
| Catastrophe | -4 |
| Legendary (Nat 1) | -5 |

These values are placeholder defaults. The PO can adjust them later. The implementation should use the same pattern as `SuccessScale` — a static method that takes a `RollResult` and returns an `int`.

### Descoped from this sprint

Per vision concerns #29 and #30:
- **Shadow growth triggers** (#29): Explicitly descoped. `GameSession.ResolveTurnAsync` should NOT implement shadow growth. No stub, no TODO — it's a future issue.
- **Hard/Bold risk bonus** (#30): Explicitly descoped. Interest delta = `SuccessScale` or `FailureScale` output only. No risk bonus modifier.

### Implicit assumptions for implementers

1. **netstandard2.0 + LangVersion 8.0**: No `record` types (C# 9+). Use `sealed class` with readonly properties and constructor. `Task<T>` is available via `System.Threading.Tasks`.
2. **Zero NuGet dependencies**: Do not add any packages.
3. **Nullable reference types are enabled**: Use `?` annotations correctly.
4. **`RollEngine.Resolve` mutates `TrapState`**: When a TropeTrap tier activates, the method calls `attackerTraps.Activate()`. `GameSession` must pass its owned `TrapState` and expect mutation.
5. **`InterestMeter` already has `GetState()`, `GrantsAdvantage`, `GrantsDisadvantage`**: These were added in Issue #6 (merged).
6. **`SuccessScale` already exists**: Returns +1/+2/+3/+4 for successes, 0 for failures. Located in `Rolls/SuccessScale.cs`.

---

## Rules-to-Code Sync

### Source of Truth

| Layer | Location | Authority |
|-------|----------|-----------|
| Game rules | `design/systems/rules-v3.md` (external repo) | **Authoritative** — all mechanical values originate here |
| Content data | `design/settings/` (external repo) | Authoritative for items, anatomy, traps content |
| C# engine | `src/Pinder.Core/` (this repo) | **Derived** — must match rules exactly |

### Constant Sync Table

Every numeric constant or structural table in the engine traces back to a rules section. When a rule changes, this table tells you exactly which C# code to update.

| Rules Section | Rule Value | C# Location | C# Constant/Expression |
|---|---|---|---|
| §3 Defence pairings | Charm→SA, Rizz→Wit, Honesty→Chaos, Chaos→Charm, Wit→Rizz, SA→Honesty (each stat appears once as attacker, once as defender) | `Stats/StatBlock.cs` | `StatBlock.DefenceTable` |
| §3 Base DC | 13 | `Stats/StatBlock.cs` | `StatBlock.GetDefenceDC()` — hardcoded `13 +` |
| §5 Fail tiers | Nat1→Legendary, miss 1–2→Fumble, 3–5→Misfire, 6–9→TropeTrap, 10+→Catastrophe | `Rolls/RollEngine.cs` | Boundary checks in `Resolve()` method |
| §5 Fail tier enum | None, Fumble, Misfire, TropeTrap, Catastrophe, Legendary | `Rolls/FailureTier.cs` | `FailureTier` enum |
| §5 Success scale | Beat DC by 1–4→+1, 5–9→+2, 10+→+3, Nat20→+4 | `Rolls/SuccessScale.cs` | `SuccessScale.GetInterestDelta()` |
| §5 Failure scale | Fumble→-1, Misfire→-2, TropeTrap→-3, Catastrophe→-4, Legendary→-5 | `Rolls/FailureScale.cs` | `FailureScale.GetInterestDelta()` — **NEW (prototype defaults)** |
| §6 Interest range | 0–25 | `Conversation/InterestMeter.cs` | `InterestMeter.Max = 25`, `InterestMeter.Min = 0` |
| §6 Starting interest | 10 | `Conversation/InterestMeter.cs` | `InterestMeter.StartingValue = 10` |
| §6 Interest states | Unmatched(0), Bored(1–4), Interested(5–15), VeryIntoIt(16–20), AlmostThere(21–24), DateSecured(25) | `Conversation/InterestMeter.cs` | `GetState()`, `InterestState` enum |
| §6 Advantage from interest | VeryIntoIt/AlmostThere → advantage; Bored → disadvantage | `Conversation/InterestMeter.cs` | `GrantsAdvantage`/`GrantsDisadvantage` |
| §10 XP thresholds | L1=0, L2=50, L3=150, L4=300, L5=500, L6=750, L7=1100, L8=1500, L9=2000, L10=2750, L11=3500 | `Progression/LevelTable.cs` | `XpThresholds` array |
| §10 Level bonuses | L1–2=+0, L3–4=+1, L5–6=+2, L7–8=+3, L9–10=+4, L11=+5 | `Progression/LevelTable.cs` | `LevelBonuses` array, `GetBonus()` |
| §10 Build points | L1=0(12 at creation), L2–3=2, L4=2, L5–6=3, L7=3, L8=4, L9=4, L10=5, L11=0(prestige) | `Progression/LevelTable.cs` | `BuildPointsGranted` array, `CreationBudget = 12` |
| §10 Item slots | L1–2=2, L3–4=3, L5–6=4, L7–8=5, L9–11=6 | `Progression/LevelTable.cs` | `ItemSlots` array |
| §10 Stat caps | Creation cap = 4, Base cap = 6 | `Progression/LevelTable.cs` | `CreationStatCap = 4`, `BaseStatCap = 6` |
| §8 Shadow pairs | Charm↔Madness, Rizz↔Horniness, Honesty↔Denial, Chaos↔Fixation, Wit↔Dread, SA↔Overthinking | `Stats/StatBlock.cs` | `StatBlock.ShadowPairs` |
| §8 Shadow penalty | -1 per 3 shadow points | `Stats/StatBlock.cs` | `GetEffective()` — `shadowVal / 3` |
| §7 Trap effects | Disadvantage, StatPenalty, OpponentDCIncrease | `Traps/TrapDefinition.cs` | `TrapEffect` enum |
| Momentum | 3-streak→+2, 4-streak→+2, 5+→+3, reset on fail | `Conversation/GameSession.cs` | Momentum logic in `ResolveTurnAsync` — **NEW** |
| Ghost trigger | Bored state → 25% chance per turn (dice.Roll(4)==1) | `Conversation/GameSession.cs` | Ghost check in `StartTurnAsync` — **NEW** |

### Drift Detection

1. **Automated**: `tests/Pinder.Core.Tests/RulesConstantsTests.cs` asserts every value in the sync table above. If a rule changes and code is updated without updating the test (or vice versa), CI fails.
2. **Quick grep patterns** to find hardcoded rule values in C#:
   - Base DC: `grep -rn "13 +" src/Pinder.Core/Stats/`
   - Interest bounds: `grep -rn "Max\|Min\|StartingValue" src/Pinder.Core/Conversation/`
   - XP thresholds: `grep -rn "XpThresholds" src/Pinder.Core/Progression/`
   - Shadow penalty divisor: `grep -rn "/ 3" src/Pinder.Core/Stats/`
   - Failure tier boundaries: `grep -rn "miss\|<= 2\|<= 5\|<= 9" src/Pinder.Core/Rolls/`
3. **Manual checklist** when `rules-v3.md` changes:
   - Open this sync table
   - For each changed section, find the C# location
   - Update the constant/logic
   - Update the test assertion
   - Run `dotnet test`

### Sync Process

When a rules document changes:

1. **Identify affected rows** in the sync table above
2. **Update C# constants** at the listed file/location
3. **Update tests** in `RulesConstantsTests.cs` to match new expected values
4. **Run `dotnet test`** — all tests must pass
5. **Update this sync table** if new constants were added or locations changed
6. **Commit** with message: `sync: update <section> constants to rules v<version>`

### Known Gaps (as of Sprint 6)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Shadow growth triggers | §8 | Descoped from Sprint 6 per #29 — needs PO-defined trigger rules |
| Hard/Bold risk bonus | unspecified | Descoped from Sprint 6 per #30 — needs PO definition |
| Failure scale values are prototype defaults | §5 | Filed as #28 — PO should confirm or adjust the -1/-2/-3/-4/-5 scale |

---

## Component Boundaries

### Stats (`Pinder.Core.Stats`)
- **Owns**: Stat types, shadow stat types, stat block with effective modifier calculation, defence table, DC calculation
- **Public API**: `StatType` enum, `ShadowStatType` enum, `StatBlock` class
- **Does NOT own**: Roll resolution, interest tracking, character assembly

### Rolls (`Pinder.Core.Rolls`)
- **Owns**: d20 roll resolution, failure tier determination, advantage/disadvantage logic, trap activation during rolls, success scale, failure scale
- **Public API**: `RollEngine.Resolve()`, `RollResult`, `FailureTier`, `SuccessScale`, `FailureScale`
- **Does NOT own**: Interest tracking, stat storage, trap definitions, game session orchestration

### Traps (`Pinder.Core.Traps`)
- **Owns**: Trap data model, active trap tracking, turn countdown, trap clearing
- **Public API**: `TrapDefinition`, `TrapState`, `ActiveTrap`, `TrapEffect`
- **Does NOT own**: Trap activation logic (that's in RollEngine), trap content (loaded from JSON)

### Conversation (`Pinder.Core.Conversation`)
- **Owns**: Interest meter (value tracking, clamping, state derivation), timing profile (reply delay computation), game session orchestration
- **Public API**: `InterestMeter`, `InterestState`, `TimingProfile`, `GameSession`, `TurnStart`, `TurnResult`, `GameStateSnapshot`, `GameOutcome`
- **Does NOT own**: Roll math (delegates to RollEngine), LLM communication (delegates to ILlmAdapter), character assembly

### Progression (`Pinder.Core.Progression`)
- **Owns**: XP→level resolution, level bonus, build points, item slot counts, failure pool tier
- **Public API**: `LevelTable` (static), `FailurePoolTier`
- **Does NOT own**: XP tracking (that's the host), character creation validation

### Characters (`Pinder.Core.Characters`)
- **Owns**: Item/anatomy data models, fragment assembly pipeline, archetype ranking, character profile
- **Public API**: `CharacterAssembler`, `FragmentCollection`, `CharacterProfile`, `ItemDefinition`, `AnatomyTierDefinition`, `TimingModifier`
- **Does NOT own**: Item loading (that's Data/), prompt generation (that's Prompts/)

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
- **Public API**: `IDiceRoller`, `IFailurePool`, `ITrapRegistry`, `IItemRepository`, `IAnatomyRepository`, `ILlmAdapter`
- **Does NOT own**: Any implementation (except `NullLlmAdapter` for testing)
