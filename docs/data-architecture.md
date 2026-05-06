# Data Architecture & Extensibility Model

## Overview
Pinder.Core utilizes a **two-tier data model** that strictly separates engine-enforced mechanics (Rules DSL) from runtime creative direction and content configuration. This separation ensures that balancing adjustments can be made via YAML data while preserving the core stateless C# engine integrity.

## Key Components
- `Pinder.Rules.RuleBook` — Engine-enforced mechanics (Tier 1).
- `JsonItemRepository` — Runtime-loaded item configurations.
- `GameDefinition.LoadFrom` — Loads the singleton creative brief.
- `SessionSystemPromptBuilder` — Assembles final system prompt using `GameDefinition` and `CharacterProfile`.
- `CharacterAssembler` — Assembles character profiles dynamically.
- `data/game-definition.yaml` — Singleton LLM creative brief.
- `data/traps/traps.json` — Trap configurations.
- `data/items/starter-items.json` — Starting item pool.
- `rules/extracted/rules-v3-enriched.yaml` — Derived mechanics rules.

## API / Public Interface
N/A - This module primarily defines data structure and architectural constraints.

## Architecture Notes

### 1. Two-tier Data Model

#### Tier 1 — Rules DSL (engine-enforced mechanics)
- **Source**: `design/systems/*.md`, `design/settings/archetypes.md` (in future)
- **Derived**: `rules/extracted/rules-v3-enriched.yaml`
- **C# use**: `Pinder.Rules.RuleBook`, loaded via `RulesLoader`
- **Purpose**: Defines DCs, interest deltas, XP tables, shadow thresholds, and roll outcomes. These rules govern the mechanical progression and bounds of the game state.
- **Tooling**:
  - `rules/tools/extract.py` (MD → YAML)
  - `rules/tools/generate.py` (YAML → MD)
  - `rules/tools/accuracy_check.py` (validates sync between docs and generated data)

#### Tier 2 — Configuration Data (runtime-loaded)
- **Source**: `data/*.yaml`, `data/**/*.json` (these **are** the source of truth)
- **C# use**: Loaded directly at runtime via Repositories (`JsonItemRepository`, `JsonAnatomyRepository`), `GameDefinition.LoadFrom`, etc.
- **Purpose**: Drives LLM creative direction, item pools, archetype definitions, and trap definitions. This tier contains the content payload evaluated within the mechanics constraints.
- **Tooling**:
  - `rules/tools/generate_game_definition.py` (YAML → MD for readability)

### 2. Configuration Data Files

The `data/` directory organizes the runtime-loaded configuration tier. Every file below is loaded by an engine repository or a singleton loader; editing them is the supported way to retune content without recompiling.

```
data/
  game-definition.yaml         — singleton LLM creative brief (non-extensible structure)
  delivery-instructions.yaml   — per-stat × per-outcome rewrite prompts + horniness + shadow corruption
  characters/
    <slug>.json                — one file per character (items, anatomy, build_points, shadows)
  items/
    starter-items.json         — item pool (extensible by appending entries)
  anatomy/
    anatomy-parameters.json    — anatomy parameters × tiers (extensible by appending parameters and/or tiers)
  traps/
    traps.json                 — trap definitions (one per StatType)
    trap-schema.json           — JSON Schema for validation
  timing/
    response-profiles.json     — opponent reply timing profiles
```

#### File-by-file map

| File | Loader | What it controls |
|---|---|---|
| `game-definition.yaml` | `GameDefinition.LoadFrom` (singleton) | `vision`, `world_description`, `texting_psychology`, `player_role_description`, `max_turns`, `max_dialogue_options`, time-of-day horniness bands. Becomes the top of every system prompt. |
| `delivery-instructions.yaml` | `StatDeliveryInstructions.LoadFrom` (singleton) | Two top-level sections: `delivery_instructions.{stat}.{outcome}` (per-stat × 11 outcomes from `clean` to `nat1`, plus `horniness_overlay` per tier) and `shadow_corruption.{shadow}.{tier}` (corruption text for Madness, Despair, Dread, Denial, Fixation, Overthinking). This is the prompt library for *how a delivered message gets rewritten* based on roll outcome and shadow state. |
| `characters/<slug>.json` | `Pinder.SessionSetup.CharacterDefinitionLoader` | Per-character: `name`, `gender_identity`, `bio`, `level`, `items[]` (item ids), `anatomy{}` (parameterId → tierId), `build_points{}`, `shadows{}`. |
| `items/starter-items.json` | `JsonItemRepository` | Item pool. Each item carries `stat_modifiers`, `personality_fragment`, `backstory_fragment`, `texting_style_fragment`, `archetype_tendencies`, `response_timing_modifier`, plus UI flavor (`flavor.shop_description`, `display_name`, `slot`, `tier`). |
| `anatomy/anatomy-parameters.json` | `JsonAnatomyRepository` | Anatomy parameters × tiers. Each tier carries the same fragment/modifier shape as items. The number and names of parameters are fully data-driven — see "Anatomy parameter extensibility" below. |
| `traps/traps.json` | `JsonTrapRepository` (`ITrapRegistry`) | Trap definitions (one per stat). Fields: `stat`, `effect`, `effect_value`, `duration_turns`, `llm_instruction` (the trap overlay prompt used on persistence turns), `clear_method`, `nat1_bonus`. |
| `traps/trap-schema.json` | (validation only) | JSON Schema documenting trap structure. |
| `timing/response-profiles.json` | response-timing layer | Base profiles for opponent reply timing; combined with item/anatomy `response_timing_modifier` deltas. |

All character / item / anatomy fields above are concatenated by `CharacterAssembler` into a `FragmentCollection`, which `PromptBuilder` then renders into the `TEXTING STYLE`, `PERSONALITY`, `BACKSTORY`, `ARCHETYPES`, `EFFECTIVE STATS` blocks of the per-character system prompt.

#### Quick reference — "I want to change X, where do I edit?"

| Want to change | File | Field |
|----------------|------|-------|
| A character's items / stats / level | `data/characters/<slug>.json` | top-level fields |
| Add a new item | `data/items/starter-items.json` | append entry |
| Item personality / texting style | `data/items/starter-items.json` | item's `personality_fragment` / `texting_style_fragment` |
| What an anatomy tier feels like | `data/anatomy/anatomy-parameters.json` | tier's fragments + `stat_modifiers` |
| Add an anatomy parameter or tier | `data/anatomy/anatomy-parameters.json` | append parameter or tier (no code change — see below) |
| What a trap's corruption *feels like* | `data/traps/traps.json` | trap's `llm_instruction` |
| Trap stat / duration / effect | `data/traps/traps.json` | other trap fields |
| Per-stat × per-outcome rewrite prompt | `data/delivery-instructions.yaml` | `delivery_instructions.<stat>.<outcome>` |
| Horniness overlay text per fail tier | `data/delivery-instructions.yaml` | `delivery_instructions.horniness_overlay.{fumble\|misfire\|trope_trap\|catastrophe}` |
| Shadow corruption text (Madness etc.) | `data/delivery-instructions.yaml` | `shadow_corruption.<shadow>.<tier>` |
| Top-level vision / world / psychology blocks | `data/game-definition.yaml` | top-level keys |
| Mechanical rule numbers (DC, deltas, scales) | `rules/extracted/rules-v3-enriched.yaml` | rule entry by `id` |

### 3. Extensibility Model

The system supports two kinds of runtime configurations with distinct handling patterns:

#### Extensible Collections (Items, Archetypes, Anatomy Fragments)
- **Schema Validation**: The schema defines the structure, not the content.
- **Content Updates**: New entries are added directly to the data files without requiring any code changes.
- **Runtime Integrity**: C# loaders (`JsonItemRepository`, `JsonAnatomyRepository`, etc.) validate the loaded data against the defined schema at load time.
- **Documentation Sync**: Generator scripts automatically regenerate design/ docs from the full updated data set.

#### Singleton Config (`game-definition.yaml`)
- **Immutability**: Non-extensible entity; edited directly to tweak overall creative direction.
- **Code Dependency**: Structure changes require corresponding code changes (e.g., adding a new property to the `GameDefinition` class).
- **Readability**: An MD view is automatically generated for easier human readability and diffing.

### 4. What the LLM Receives

The LLM is strictly guided by the composed data configuration at runtime:
- **System Prompt**: The `SessionSystemPromptBuilder` dynamically assembles the final system prompt by combining the `GameDefinition` and the individual `CharacterProfile`.
- **Game Definition**: Loaded directly from `game-definition.yaml` at startup to define the overarching world rules and creative frame.
- **Character Profile**: Assembled dynamically from character items and anatomy data via the `CharacterAssembler`.
- **Data Repositories**: Items and anatomy are loaded directly from the `data/` directories via the `Json*Repository` classes.

### 5. How to Extend

Follow these rules when introducing new content:
- **New Item**: Append the new entry to `data/items/starter-items.json` and validate against its schema.
- **New Archetype**: Add to `data/archetypes/*.yaml` (once archetype data definitions are implemented).
- **New Trap**: Append the new entry to `data/traps/traps.json`.
- **New Game Direction**: Edit `data/game-definition.yaml` directly, and regenerate the corresponding MD file.

### 6. Anatomy parameter extensibility

The number and names of anatomy parameters are **fully data-driven**. There is no enum, no hardcoded list, and no per-parameter C# class — `JsonAnatomyRepository` reads whatever parameters are present in `anatomy-parameters.json` and exposes them via `IAnatomyRepository.GetAll()` / `GetParameter(id)`.

Characters reference parameters by string id in their `anatomy` block:

```json
"anatomy": {
  "length": "medium",
  "girth": "slim",
  ...
}
```

`CharacterAssembler.Assemble` takes an `IReadOnlyDictionary<string, string>` of `parameterId → tierId`. Unknown parameter ids are silently skipped — the engine does not require any specific parameter to exist. Removing a parameter from the JSON simply means no character can reference it; existing characters that still reference the removed id silently lose those fragments and modifiers (no error).

#### Adding a parameter

1. Append a new object to `data/anatomy/anatomy-parameters.json`:
   ```json
   {
     "id": "voice_pitch",
     "name": "Voice Pitch",
     "tiers": [
       {
         "id": "low",
         "name": "Low",
         "stat_modifiers": { "charm": 1, "self_awareness": -1 },
         "personality_fragment": "...",
         "backstory_fragment": "...",
         "texting_style_fragment": "...",
         "archetype_tendencies": ["The Philosopher"],
         "response_timing_modifier": {
           "base_delay_delta_minutes": 0,
           "delay_variance_multiplier": 1.0,
           "dry_spell_probability_delta": 0.0,
           "read_receipt": "neutral"
         }
       },
       { "id": "high", ... },
       { "id": "crackling", ... }
     ]
   }
   ```
2. Optionally update characters that should pick a tier on this parameter (`data/characters/<slug>.json` → add `"voice_pitch": "low"` under `anatomy`).
3. If the host (Unity, web) renders an anatomy picker, expose the new parameter in its UI — see [HOSTING.md](HOSTING.md) for the recommended pattern.

#### Adding or removing tiers on an existing parameter

1. Edit the parameter's `tiers[]` array in `anatomy-parameters.json`.
2. Tiers must have unique `id` within the parameter.
3. **Visual-only tiers** (e.g. `skin_tone`) are recognised when the tier object has `visual_description` and no `personality_fragment` — these contribute zero stat modifiers and zero fragments. Use this for purely cosmetic dimensions.

#### What is hardcoded

Very little. The only places anatomy is referenced by *type* in C# are:

- `JsonAnatomyRepository` — schema-aware loader.
- `CharacterAssembler` — calls `repo.GetParameter(id).GetTier(tierId)` for each `(parameterId, tierId)` pair on the character.
- `Pinder.GameApi` (the web tier) — may have UI-side schemas describing pickers; if you change parameter shape, audit the web tier.

The **stats** (Charm, Rizz, Honesty, Chaos, Wit, Self-Awareness) and **shadows** (Madness, Despair, Denial, Fixation, Dread, Overthinking) are enums (`StatType`, `ShadowStatType`) and are not data-driven. Anatomy parameters refer to those stats by string in `stat_modifiers`, parsed against the enum at load time.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-07 | #655 | Initial creation — documented two-tier data model and extensibility approach |
| 2026-04-30 | (this update) | Filled in the full configuration map (delivery-instructions, characters, anatomy, timing). Added per-file loader table, quick-reference, and the anatomy parameter extensibility section. Cross-linked to HOSTING.md for engine-host integration. |
