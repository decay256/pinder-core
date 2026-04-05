# Characters

## Overview
The `Pinder.Core.Characters` namespace handles character assembly — combining equipped items, anatomy selections, stats, and archetype tendencies into a complete character profile. It includes an archetype catalog with level-range eligibility so that dominant archetype selection respects the character's current level tier.

## Key Components

| File | Description |
|------|-------------|
| `src/Pinder.Core/Characters/CharacterAssembler.cs` | Core assembly logic: resolves items/anatomy, collects fragments, ranks archetypes (with optional level filtering) |
| `src/Pinder.Core/Characters/ArchetypeCatalog.cs` | Static catalog of 20 archetype definitions with level ranges; provides `GetByName`, `All`, and `IsEligibleAtLevel` |
| `src/Pinder.Core/Characters/ArchetypeDefinition.cs` | Immutable data class: archetype name + min/max level range |
| `src/Pinder.Core/Characters/CharacterProfile.cs` | Assembled character profile with system prompt, stats, and archetype data |
| `src/Pinder.Core/Characters/FragmentCollection.cs` | Collection of personality/backstory/texting-style fragments and ranked archetypes |
| `src/Pinder.Core/Characters/ItemDefinition.cs` | Item data: slot, rarity, stat mods, personality fragments, archetype tendencies, timing modifier |
| `src/Pinder.Core/Characters/AnatomyParameterDefinition.cs` | Anatomy parameter definition (e.g. "length") |
| `src/Pinder.Core/Characters/AnatomyTierDefinition.cs` | Anatomy tier within a parameter (contributes fragments and archetype tendencies) |
| `src/Pinder.Core/Characters/TimingModifier.cs` | Timing/pacing modifier from items |
| `tests/Pinder.Core.Tests/ArchetypeLevelFilterTests.cs` | Tests for archetype catalog lookups, eligibility checks, and level-filtered assembly |

## API / Public Interface

### ArchetypeDefinition

```csharp
public sealed class ArchetypeDefinition
{
    public string Name { get; }
    public int MinLevel { get; }   // inclusive
    public int MaxLevel { get; }   // inclusive
    public ArchetypeDefinition(string name, int minLevel, int maxLevel);
    public bool IsEligibleAtLevel(int characterLevel);
}
```

### ArchetypeCatalog

```csharp
public static class ArchetypeCatalog
{
    public static ArchetypeDefinition? GetByName(string name);          // case-insensitive
    public static IReadOnlyCollection<ArchetypeDefinition> All { get; } // 20 archetypes
    public static bool IsEligibleAtLevel(string archetypeName, int characterLevel);
    // Unknown archetypes always return true (no filtering applied)
}
```

### CharacterAssembler.Assemble

```csharp
public FragmentCollection Assemble(
    IEnumerable<string> equippedItemIds,
    IReadOnlyDictionary<string, string> anatomySelections,
    IReadOnlyDictionary<StatType, int> playerBaseStats,
    IReadOnlyDictionary<ShadowStatType, int> shadowStats,
    int characterLevel = 0);  // 0 = no level filtering (backward-compatible)
```

## Architecture Notes

- **Archetype level filtering**: When `characterLevel > 0`, `CharacterAssembler.Assemble` filters the counted archetypes through `ArchetypeCatalog.IsEligibleAtLevel` before ranking. If filtering eliminates all archetypes, it falls back to the unfiltered list so the character always has archetype data.
- **Unknown archetypes**: Archetypes not present in the catalog are never filtered out — they are always considered eligible. This ensures forward compatibility with custom/new archetypes.
- **Catalog data**: The 20 archetypes and their level ranges are hardcoded in `ArchetypeCatalog`, sourced from `rules/extracted/archetypes-enriched.yaml §3`. Level ranges span from 1–3 (e.g. "The Hey Opener") up to 1–10 (e.g. "The Ghost") and 5–11 (e.g. "The Sniper").
- **Backward compatibility**: The `characterLevel` parameter defaults to `0`, preserving existing behavior for callers that don't pass a level.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-05 | #540 | Initial creation — added `ArchetypeDefinition`, `ArchetypeCatalog` (20 archetypes with level ranges), and level-range filtering in `CharacterAssembler.Assemble` so dominant archetype respects character's level tier |
