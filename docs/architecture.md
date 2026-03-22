# Pinder.Core — Architecture

## Overview

Pinder.Core is a **pure C# (.NET Standard 2.0) RPG engine** for a comedy dating game where players are sentient penises on a Tinder-like app. It has **zero external dependencies** and is designed to drop into Unity or any .NET host.

The engine is **stateless at the roll level** — all state is passed in via parameters. State management (whose turn, what happened last) lives in the host (Unity game loop). The engine owns only the math and the data models.

### Module Map

```
Pinder.Core/
├── Stats/          — StatType, ShadowStatType, StatBlock (stat pairs, shadow penalties, DC calc)
├── Rolls/          — RollEngine (stateless), RollResult, FailureTier
├── Traps/          — TrapDefinition, TrapState, ActiveTrap (trap lifecycle)
├── Progression/    — LevelTable (XP thresholds, level bonuses, build points, item slots)
├── Conversation/   — InterestMeter (0–25 interest tracker), TimingProfile (reply delay calc)
├── Characters/     — CharacterAssembler, FragmentCollection, ItemDefinition, AnatomyTierDefinition, TimingModifier
├── Prompts/        — PromptBuilder (assembles LLM system prompt from fragments + traps)
├── Interfaces/     — IDiceRoller, IFailurePool, ITrapRegistry, IItemRepository, IAnatomyRepository
└── Data/           — JsonItemRepository, JsonAnatomyRepository, JsonParser (hand-rolled JSON parser)
```

### Data Flow (one roll)

```
Player chooses stat → host reads InterestMeter.GetState() for adv/disadv
  → RollEngine.Resolve(stat, attacker, defender, traps, level, dice, hasAdv, hasDisadv)
  → RollResult { Tier, MissMargin, ActivatedTrap, IsSuccess, ... }
  → host applies InterestMeter.Apply(delta) based on result
  → host calls PromptBuilder.BuildSystemPrompt(...) with updated fragments + traps
  → LLM generates NPC reply
```

### Key Design Patterns
- **Stateless engine**: `RollEngine` is a static class. All mutable state (traps, interest) is owned by the caller.
- **Interface-driven injection**: Dice, failure pools, trap registries, item/anatomy repos are all interfaces — Unity provides ScriptableObject impls, standalone uses JSON repos.
- **Fragment assembly**: Character identity is built by summing stat modifiers and concatenating text fragments from items + anatomy tiers → `FragmentCollection` → `PromptBuilder`.
- **No external dependencies**: Custom `JsonParser` avoids NuGet dependency for Unity compat.

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
| §5 Success scale | **NOT YET IMPLEMENTED** — Beat DC by 1–4→+1, 5–9→+2, 10+→+3, Nat20→+4 | — | No code exists. See note below. |
| §6 Interest range | 0–25 | `Conversation/InterestMeter.cs` | `InterestMeter.Max = 25`, `InterestMeter.Min = 0` |
| §6 Starting interest | 10 | `Conversation/InterestMeter.cs` | `InterestMeter.StartingValue = 10` |
| §6 Interest states | Unmatched(0), Bored(1–4), Interested(5–15), VeryIntoIt(16–20), AlmostThere(21–24), DateSecured(25) | `Conversation/InterestMeter.cs` | **NOT YET IMPLEMENTED** — needs `GetState()`, `InterestState` enum |
| §6 Advantage from interest | VeryIntoIt/AlmostThere → advantage; Bored → disadvantage | `Conversation/InterestMeter.cs` | **NOT YET IMPLEMENTED** — needs `GrantsAdvantage`/`GrantsDisadvantage` |
| §10 XP thresholds | L1=0, L2=50, L3=150, L4=300, L5=500, L6=750, L7=1100, L8=1500, L9=2000, L10=2750, L11=3500 | `Progression/LevelTable.cs` | `XpThresholds` array |
| §10 Level bonuses | L1–2=+0, L3–4=+1, L5–6=+2, L7–8=+3, L9–10=+4, L11=+5 | `Progression/LevelTable.cs` | `LevelBonuses` array, `GetBonus()` |
| §10 Build points | L1=0(12 at creation), L2–3=2, L4=2, L5–6=3, L7=3, L8=4, L9=4, L10=5, L11=0(prestige) | `Progression/LevelTable.cs` | `BuildPointsGranted` array, `CreationBudget = 12` |
| §10 Item slots | L1–2=2, L3–4=3, L5–6=4, L7–8=5, L9–11=6 | `Progression/LevelTable.cs` | `ItemSlots` array |
| §10 Stat caps | Creation cap = 4, Base cap = 6 | `Progression/LevelTable.cs` | `CreationStatCap = 4`, `BaseStatCap = 6` |
| §8 Shadow pairs | Charm↔Madness, Rizz↔Horniness, Honesty↔Denial, Chaos↔Fixation, Wit↔Dread, SA↔Overthinking | `Stats/StatBlock.cs` | `StatBlock.ShadowPairs` |
| §8 Shadow penalty | -1 per 3 shadow points | `Stats/StatBlock.cs` | `GetEffective()` — `shadowVal / 3` |
| §7 Trap effects | Disadvantage, StatPenalty, OpponentDCIncrease | `Traps/TrapDefinition.cs` | `TrapEffect` enum |

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

### Known Gaps (as of Sprint 5)

| Gap | Rules Section | Status |
|-----|--------------|--------|
| Success scale (interest delta from successful rolls) | §5 | No code — `RollResult` has `MissMargin` but no `SuccessMargin` or interest delta |
| Interest state enum + GetState() | §6 | Planned: Issue #6 |
| Advantage/disadvantage from interest | §6 | Planned: Issue #6 |

---

## Component Boundaries

### Stats (`Pinder.Core.Stats`)
- **Owns**: Stat types, shadow stat types, stat block with effective modifier calculation, defence table, DC calculation
- **Public API**: `StatType` enum, `ShadowStatType` enum, `StatBlock` class
- **Does NOT own**: Roll resolution, interest tracking, character assembly

### Rolls (`Pinder.Core.Rolls`)
- **Owns**: d20 roll resolution, failure tier determination, advantage/disadvantage logic, trap activation during rolls
- **Public API**: `RollEngine.Resolve()`, `RollResult`, `FailureTier`
- **Does NOT own**: Interest delta computation (not yet implemented), stat storage, trap definitions

### Traps (`Pinder.Core.Traps`)
- **Owns**: Trap data model, active trap tracking, turn countdown, trap clearing
- **Public API**: `TrapDefinition`, `TrapState`, `ActiveTrap`, `TrapEffect`
- **Does NOT own**: Trap activation logic (that's in RollEngine), trap content (loaded from JSON)

### Conversation (`Pinder.Core.Conversation`)
- **Owns**: Interest meter (value tracking, clamping), timing profile (reply delay computation)
- **Public API**: `InterestMeter`, `TimingProfile`
- **Does NOT own**: What happens at interest boundaries (that's the host), roll resolution

### Progression (`Pinder.Core.Progression`)
- **Owns**: XP→level resolution, level bonus, build points, item slot counts, failure pool tier
- **Public API**: `LevelTable` (static), `FailurePoolTier`
- **Does NOT own**: XP tracking (that's the host), character creation validation

### Characters (`Pinder.Core.Characters`)
- **Owns**: Item/anatomy data models, fragment assembly pipeline, archetype ranking
- **Public API**: `CharacterAssembler`, `FragmentCollection`, `ItemDefinition`, `AnatomyTierDefinition`, `TimingModifier`
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
- **Public API**: `IDiceRoller`, `IFailurePool`, `ITrapRegistry`, `IItemRepository`, `IAnatomyRepository`
- **Does NOT own**: Any implementation
