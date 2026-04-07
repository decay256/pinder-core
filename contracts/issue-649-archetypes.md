# Contract: Issue #649 & #668 — Active Archetype Selection

## Component: `Pinder.Core.Characters`

### 1. `ArchetypeTier`
New Enum representing mutually exclusive level tiers.
```csharp
public enum ArchetypeTier { Tier1 = 1, Tier2 = 2, Tier3 = 3, Tier4 = 4 }
```

### 2. `ArchetypeDefinition`
- Remove `MinLevel` and `MaxLevel`.
- Add `ArchetypeTier Tier { get; }`.
- Add `string BehaviorInstruction { get; }`.

### 3. `ArchetypeCatalog`
- Update hardcoded archetypes to map to a single `Tier` instead of level ranges, and include the behavioral instructions from `rules-v3-enriched.yaml`.
  - Tier 1: Level 1-3
  - Tier 2: Level 4-6
  - Tier 3: Level 7-9
  - Tier 4: Level 10+

### 4. `ActiveArchetypeInfo`
New DTO class.
```csharp
public sealed class ActiveArchetypeInfo {
    public string Name { get; }
    public int Count { get; }
    public string BehaviorInstruction { get; }
    public ArchetypeTier Tier { get; }
}
```

### 5. `CharacterAssembler`
- Calculate character's Tier from `characterLevel`.
- Filter available archetypes to match the calculated Tier.
- Pick the one with the highest count (fallback to highest count overall if none match).
- Expose `ActiveArchetypeInfo? ActiveArchetype` on `FragmentCollection` and `CharacterProfile`.

## Component: `Pinder.Core.Prompts.PromptBuilder`
- Replace the current list of all archetypes in `BuildSystemPrompt`.
- Inject:
  ```
  ACTIVE ARCHETYPE: {Name} ({InterferenceString})
  {BehaviorInstruction}
  ```
- Interference String Logic:
  - Count 1-2: "slight tendency"
  - Count 3-5: "clear pattern"
  - Count 6+: "dominant"

## Component: `session-runner/Program.cs`
- Include the Active Archetype name in the character table output.
