# Data Architecture & Extensibility Model

Pinder.Core utilizes a **two-tier data model** that strictly separates engine-enforced mechanics (Rules DSL) from runtime creative direction and content configuration. This separation ensures that balancing adjustments can be made via YAML data while preserving the core stateless C# engine integrity.

## 1. Two-tier Data Model

### Tier 1 — Rules DSL (engine-enforced mechanics)
- **Source**: `design/systems/*.md`, `design/settings/archetypes.md` (in future)
- **Derived**: `rules/extracted/rules-v3-enriched.yaml`
- **C# use**: `Pinder.Rules.RuleBook`, loaded via `RulesLoader`
- **Purpose**: Defines DCs, interest deltas, XP tables, shadow thresholds, and roll outcomes. These rules govern the mechanical progression and bounds of the game state.
- **Tooling**:
  - `rules/tools/extract.py` (MD → YAML)
  - `rules/tools/generate.py` (YAML → MD)
  - `rules/tools/accuracy_check.py` (validates sync between docs and generated data)

### Tier 2 — Configuration Data (runtime-loaded)
- **Source**: `data/*.yaml`, `data/**/*.json` (these **are** the source of truth)
- **C# use**: Loaded directly at runtime via Repositories (`JsonItemRepository`, `JsonAnatomyRepository`), `GameDefinition.LoadFrom`, etc.
- **Purpose**: Drives LLM creative direction, item pools, archetype definitions, and trap definitions. This tier contains the content payload evaluated within the mechanics constraints.
- **Tooling**:
  - `rules/tools/generate_game_definition.py` (YAML → MD for readability)

## 2. Configuration Data Files

The `data/` directory organizes the runtime-loaded configuration tier:

```
data/
  game-definition.yaml       — singleton LLM creative brief (non-extensible)
  traps/
    traps.json               — trap definitions (add new traps here)
    trap-schema.json         — schema for validation
  items/
    starter-items.json       — item pool (players add new items here)
  archetypes/                — (future) archetype definitions
  anatomy/                   — (future) anatomy parameter definitions
```

## 3. Extensibility Model

The system supports two kinds of runtime configurations with distinct handling patterns:

### Extensible Collections (Items, Archetypes, Anatomy Fragments)
- **Schema Validation**: The schema defines the structure, not the content.
- **Content Updates**: New entries are added directly to the data files without requiring any code changes.
- **Runtime Integrity**: C# loaders (`JsonItemRepository`, `JsonAnatomyRepository`, etc.) validate the loaded data against the defined schema at load time.
- **Documentation Sync**: Generator scripts automatically regenerate design/ docs from the full updated data set.

### Singleton Config (`game-definition.yaml`)
- **Immutability**: Non-extensible entity; edited directly to tweak overall creative direction.
- **Code Dependency**: Structure changes require corresponding code changes (e.g., adding a new property to the `GameDefinition` class).
- **Readability**: An MD view is automatically generated for easier human readability and diffing.

## 4. What the LLM Receives

The LLM is strictly guided by the composed data configuration at runtime:
- **System Prompt**: The `SessionSystemPromptBuilder` dynamically assembles the final system prompt by combining the `GameDefinition` and the individual `CharacterProfile`.
- **Game Definition**: Loaded directly from `game-definition.yaml` at startup to define the overarching world rules and creative frame.
- **Character Profile**: Assembled dynamically from character items and anatomy data via the `CharacterAssembler`.
- **Data Repositories**: Items and anatomy are loaded directly from the `data/` directories via the `Json*Repository` classes.

## 5. How to Extend

Follow these rules when introducing new content:
- **New Item**: Append the new entry to `data/items/starter-items.json` and validate against its schema.
- **New Archetype**: Add to `data/archetypes/*.yaml` (once archetype data definitions are implemented).
- **New Trap**: Append the new entry to `data/traps/traps.json`.
- **New Game Direction**: Edit `data/game-definition.yaml` directly, and regenerate the corresponding MD file.
