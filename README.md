# Pinder.Core

Pure C# RPG engine for [Pinder](https://github.com/decay256/pinder) — the comedy dating RPG.

Targets **netstandard2.0** with **C# 8.0**. No external dependencies. Drop the `src/Pinder.Core/` folder into any Unity project or reference as a standard .NET library.

## What's in here

| Namespace | What it does |
|---|---|
| `Pinder.Core.Stats` | `StatType`, `ShadowStatType`, `StatBlock` — stat pairs, shadow penalties, DC calculation |
| `Pinder.Core.Rolls` | `RollEngine`, `RollResult`, `FailureTier` — d20 resolution, fail tiers, advantage/disadvantage |
| `Pinder.Core.Traps` | `TrapDefinition`, `TrapState`, `ActiveTrap` — trap activation, carry-forward, expiry |
| `Pinder.Core.Progression` | `LevelTable` — XP thresholds, level bonus, failure pool tier |
| `Pinder.Core.Interfaces` | `IDiceRoller`, `IFailurePool`, `ITrapRegistry` — swap in Unity ScriptableObjects or JSON providers |

## Roll formula

```
d20 + statModifier + levelBonus >= DC
DC = 13 + opponent's defending stat modifier
```

Fail tiers (miss margin = DC − roll):

| Miss by | Tier |
|---|---|
| Nat 1 | Legendary |
| 1–2 | Fumble |
| 3–5 | Misfire |
| 6–9 | Trope Trap |
| 10+ | Catastrophe |

## Unity integration

1. Copy `src/Pinder.Core/` into `Assets/Plugins/Pinder.Core/`
2. Create implementations of `IDiceRoller`, `IFailurePool`, `ITrapRegistry` using ScriptableObjects
3. Use `RollEngine.Resolve(...)` to get a `RollResult`, pass `ActivatedTrap.LlmInstruction` to your EigenCore backend

## Running tests

```bash
dotnet test
```
