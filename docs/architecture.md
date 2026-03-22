# Pinder.Core — Architecture

## Overview

Pinder.Core is a **pure C# rules engine** (netstandard2.0, zero dependencies) for a comedy dating RPG. It is consumed by a Unity client and potentially standalone .NET hosts. The library is **stateless at the class level** — all mutable state (traps, interest, shadow stats) is owned by the caller and passed in.

### Module Boundaries

| Namespace | Responsibility | Key Types |
|---|---|---|
| `Stats` | Stat definitions, shadow penalties, defence DC calculation | `StatType`, `ShadowStatType`, `StatBlock` |
| `Rolls` | d20 resolution, failure tier classification | `RollEngine` (static), `RollResult`, `FailureTier` |
| `Traps` | Trap data model and active-trap tracking | `TrapDefinition`, `TrapState`, `ActiveTrap`, `TrapEffect` |
| `Progression` | XP/level tables, build points, item slots | `LevelTable` (static), `FailurePoolTier` |
| `Conversation` | Interest tracking, reply timing | `InterestMeter`, `TimingProfile` |
| `Characters` | Item/anatomy data models, character assembly pipeline | `CharacterAssembler`, `FragmentCollection`, `ItemDefinition`, `AnatomyParameterDefinition` |
| `Prompts` | LLM system prompt construction | `PromptBuilder` (static) |
| `Data` | JSON parsing, repository implementations | `JsonItemRepository`, `JsonAnatomyRepository`, `JsonParser` |
| `Interfaces` | Abstractions for external dependencies | `IDiceRoller`, `IFailurePool`, `ITrapRegistry`, `IItemRepository`, `IAnatomyRepository` |

### Data Flow

```
Player input → RollEngine.Resolve(stat, attacker, defender, traps, level, ...) → RollResult
                                                                                    ↓
                                                            InterestMeter.Apply(delta)  ← (success scale — NOT YET IMPLEMENTED)
                                                                                    ↓
                                                            PromptBuilder.BuildSystemPrompt(fragments, traps) → LLM prompt string
```

### Key Patterns
- **Immutable data models**: `StatBlock`, `RollResult`, `ItemDefinition`, `TrapDefinition` are read-only after construction
- **Mutable state containers**: `InterestMeter`, `TrapState` — caller owns lifecycle
- **Interface-based extensibility**: Unity swaps in ScriptableObject implementations of `IDiceRoller`, `ITrapRegistry`, `IFailurePool`
- **No external dependencies**: Custom `JsonParser` instead of Newtonsoft/System.Text.Json

### Constants That Are Rules-Derived

The following constants in code correspond to values in the design rules document. When rules change, these must be updated in lockstep:

| Rules Section | Code Location | Current Value | v3.4 Value |
|---|---|---|---|
| §3 Defence pairings | `StatBlock.DefenceTable` | Honesty→SA, Wit→Wit | Honesty→Chaos, Wit→Rizz |
| §3 Base DC | `StatBlock.GetDefenceDC()` | 10 | **13** |
| §4 Interest max | `InterestMeter.Max` | 20 | **25** |
| §5 Success scale | *(not implemented)* | — | +1/+2/+3/+4 per margin |
| §6 Interest states | *(not implemented)* | — | 6 states with adv/disadv |
| §6 Interest states (note) | *(not implemented)* | — | NO "Lukewarm" state — only 6 states per rules |

---

## Rules-to-Code Sync Process

### Source of Truth
- **Authoritative**: `design/systems/rules-v3.md` (in the parent pinder repo)
- **Content data**: `design/settings/` (items, anatomy, traps — JSON)
- **Implementation**: this repo (`pinder-core`)

### Sync Table

Every rules-derived constant MUST appear in the table above. When the rules change:

1. Update the sync table in this file
2. Update the C# constants
3. Update/add tests in `RulesConstantsTests.cs` that assert every value
4. Update README.md if affected (DC formula, defence pairings, interest range)

### Which Files Map to Which Rules Sections

| Rules Section | C# File(s) |
|---|---|
| §3 Stats & DC | `Stats/StatBlock.cs`, `Stats/StatType.cs` |
| §4 Interest meter | `Conversation/InterestMeter.cs` |
| §5 Success scale | `Rolls/RollResult.cs` (to be added: `SuccessMargin`, `InterestDelta`) |
| §6 Interest states | `Conversation/InterestMeter.cs` (to be added: `InterestState` enum, `GetState()`) |
| §7 Failure tiers | `Rolls/FailureTier.cs`, `Rolls/RollEngine.cs` |
| §8 Traps | `Traps/TrapDefinition.cs`, `Traps/TrapState.cs` |
| §10 Progression | `Progression/LevelTable.cs` |

---

## Component Dependency Graph

```
Interfaces ← (no deps)
Stats      ← (no deps)
Progression ← Rolls (FailureTier enum only, via FailurePoolTier)
Rolls      ← Stats, Progression, Traps, Interfaces
Traps      ← Stats
Conversation ← Interfaces
Characters ← Stats, Conversation, Interfaces
Prompts    ← Characters, Stats, Traps
Data       ← Characters, Interfaces, Stats
```

No circular dependencies. Each namespace can be understood in isolation given its dependency list.
