# Unity ↔ Core Sync Architecture

**Status:** SHIPPED — covers pinder-core#1175 (anatomy) + pinder-core#1176 (items). Engine half of pinder-web#949.

---

## 1. Overview

```
Unity (p-game)                     EigenCore                       pinder-core
──────────────────────────────     ─────────────────────────       ──────────────────────────────────────────
Character Sculpt UI                                                 data/items/starter-items.json
  ↓                                                                 data/anatomy/anatomy-parameters.json
CharacterData.cs (raw values)       game_sessions table              (admin-authored gameplay modifiers)
  ↓                                 (jsonb payload)                  ↓
CharacterDataNormalizer             ↑                                JsonItemRepository
  ↓                                 EigencoreCharacterStore           JsonAnatomyRepository
dict<string,float>                  .Publish / .Fetch                 ↓
(normalised [0..1])                 ↑                                CharacterAssembler
  ↓                                 ↓                                  ↓
CharacterProfileBridge ─────────────────────────────────────────→  FragmentCollection
  ↓                                                                   ↓
CharacterDefinition.json                                             CharacterProfile
(items[], anatomy{})                                                   ↓
  ↓                                                                  PromptBuilder
CharacterDefinitionLoader ──────────────────────────────────────→  LLM system prompt
```

---

## 2. Full Pipeline Step-by-Step

### Step 1: Game Start / Character Creation

1. Player opens the Unity client (`Diego_Quarantine/p-game`).
2. Player sculpts avatar using the body sliders (trunk, glans, scrotum, age, expression, skin channels) — these drive `CharacterData.cs` float fields.
3. Player equips items (accessories, outfits, hair, arms, tattoos, stickers) — these are stored as ids in `CharacterData.equipedAccessories[]`, `CharacterData.hairStyleIndex`, `CharacterData.tattooId`, `CharacterData.stickerIds[]`.

### Step 2: EigenCore Store

After character creation (or any update):

1. Unity's `CharacterProfileBridge` (in `p-game`) serializes `CharacterData` → a `CharacterDefinition` JSON payload.
2. The payload is uploaded via `EigencoreCharacterStore.PublishAsync()` to the EigenCore service (Postgres `game_sessions` + `user_sessions` tables on the staging host).
3. The `items[]` array in the payload contains raw Unity ids (e.g. `["head_tophat", "vest1", "classic2"]`) verbatim.
4. The `anatomy{}` block contains the normalised float values produced by `CharacterDataNormalizer.Normalize(CharacterDataDto)`.

> **Bridge note:** The current `CharacterProfileBridge` in Unity live code calculates stats from `personalityTags` and accessory count — this is the old pipeline. The bridge should be updated to route equipped item ids and normalized anatomy through the core DLL. If the bridge lives in Unity, it is a Unity-side JIRA follow-up; the core DLL ships the normalizer and assembler so Unity can call them directly.

### Step 3: EigenCore Retrieve

At session start:

1. pinder-web fetches the character payload from EigenCore via HTTP.
2. The JSON payload (with `items[]` + `anatomy{}`) is passed to `CharacterDefinitionLoader.Load()`.

### Step 4: pinder-core Assembly (CharacterDefinitionLoader)

```
CharacterDefinitionLoader.Load(path, itemRepo, anatomyRepo)
  → CharacterDefinitionLoader.Parse(json, itemRepo, anatomyRepo)
    → CharacterDefinition (items[] + anatomy{} + allocation)
    → CharacterAssembler.Assemble(
          equippedItemIds,   // from CharacterDefinition.Items
          anatomyValues,     // from CharacterDefinition.Anatomy
          playerBaseStats,   // from CharacterDefinition.Allocation.Spent
          shadowStats)       // from CharacterDefinition.Allocation.Shadows
    → FragmentCollection
  → CharacterProfile (AssembledSystemPrompt + stats + timing + TextingStyleFragment)
```

### Step 5: Item Resolution (CharacterAssembler)

For each id in `equippedItemIds`:

1. `IItemRepository.GetItem(id)` looks up the Unity id in `starter-items.json`.
2. **Known id:** `ItemDefinition` with core-authored modifiers (stat_modifiers, fragments, priority, conflict_tags) is returned and queued.
3. **Unknown id:** id has no core definition → zero modifiers, id collected in `FragmentCollection.UnknownItemIds` for admin authoring. Player flow continues normally.

After resolving, **conflict/priority resolution** runs:
- Items sharing a `conflict_tag` compete. Higher-priority item wins; tie → earlier equip order wins.
- Loser's fragments are suppressed; loser's stat_modifiers still apply.

### Step 6: Anatomy Resolution (CharacterAssembler)

For each `(paramId, normalised_value)` in `anatomyValues`:

1. `IAnatomyRepository.GetParameter(paramId)` looks up the param in `anatomy-parameters.json`.
2. `AnatomyParameterDefinition.ResolveBand(value)` finds the matching [lower, upper) band.
3. The band's fragment suite is applied (same fields as items).

### Step 7: Prompt Assembly

`PromptBuilder` takes the `FragmentCollection` and assembles:
- **PERSONALITY** section: all personality fragments (items + anatomy bands).
- **BACKSTORY** section: all backstory fragments.
- **TEXTING STYLE** section: aggregated via `TextingStyleAggregator` (slot→syntax axis + anatomy→tone axis) with conflict resolution from `texting-style-conflicts.yaml`.
- **ARCHETYPES** section: ranked by occurrence vote; filtered to eligible level range.

---

## 3. Authority Map

```
Unity (p-game, GitLab)
  SSOT for:
    - Item existence / ids / slots
    - Attachment transforms (graphics — excluded from core)
    - streamerMode / isStreamerSafe (geometry — excluded from core)
    - Raw CharacterData field values

pinder-core (GitHub decay256/pinder-core)
  SSOT for:
    - Item gameplay meaning (stat_modifiers, fragments, priority, conflict_tags, item_type)
    - Anatomy band gameplay fragments
    - Conflict/priority resolution logic
    - Stat accumulation + shadow stats
    - Texting-style aggregation rules (SlotToSyntaxAxis, tone groups)
    - Archetype selection
    - CharacterDataNormalizer (normalization rules)

pinder-web (GitHub decay256/pinder-web)
  SSOT for:
    - Admin editor: authors item gameplay modifiers, anatomy band fragments
    - EigenCore REST API (character store)
    - Deployment pipeline
    - pinder-core as a git submodule (staged separately)
```

---

## 4. DLL Consumption in Unity

The pinder-core DLL (`Pinder.Core.dll` + supporting DLLs) is embedded in the Unity client as a native plugin:

1. See `docs/unity-integration.md` for the exact pin commit and integration steps.
2. The DLL is pinned to a commit on `pinder-core/main` — **not** latest main, a pinned commit.
3. Unity calls `CharacterDefinitionLoader.Load()` or the assembler APIs directly.
4. To update the DLL: bump the pin in the Unity project manifest, rebuild.

---

## 5. Admin Edit Flow

How a pinder-web admin authoring item gameplay modifiers reaches production:

```
pinder-web admin editor
  → HTTP PUT /api/admin/items/{id}
  → pinder-web backend commits changes to pinder-core/main (via PR or direct push to staging-edits)
  → pinder-core/main updated
  → pinder-web staging deploy: git submodule update → rebuild game-api-staging Docker image
  → game-api-staging container picks up new starter-items.json
```

> The admin editor flow for items is the engine half of pinder-web#949. pinder-web#949 ships the UI/backend write path.

---

## 6. Supersession of Unity PromptLookupTable

Before pinder-core, Unity's `PromptLookupTable` (a scriptable object / data asset) was the SSOT for character personality text. This has been **superseded** by pinder-core:

| Before | After |
|--------|-------|
| Unity `PromptLookupTable` → personality text | pinder-core `starter-items.json` + `anatomy-parameters.json` → personality/backstory/texting fragments |
| Unity `personalityTags` → character style | Core item/anatomy modifiers → assembled prompt |
| Static text per character archetype | Dynamic, item + anatomy + archetype driven assembly |

Unity JIRA follow-up: remove the `PromptLookupTable` asset from `p-game` once the DLL-based pipeline is fully live.

---

## 7. CharacterDataNormalizer

`CharacterDataNormalizer.Normalize(CharacterDataDto)` is the bridge between raw Unity values and the normalised [0..1] representation that the anatomy repository works with.

**Input:** `CharacterDataDto` — mirrors Unity's `CharacterData.cs` field names + ranges.
**Output:** `IReadOnlyDictionary<string, float>` — all anatomy parameter ids mapped to [0..1] values.

| Parameter Group | Raw Unity Range | Normalisation |
|-----------------|-----------------|---------------|
| Trunk (except curvature) | 0–100 float | `/100` |
| `trunkCurvature` | −100..100 float (bipolar) | `(x+100)/200` |
| Glans | 0–100 float | `/100` |
| Scrotum | 0–100 float | `/100` |
| Age params | 0–100 float | `/100` |
| Expression | 0–100 float | `/100` |
| `skinColor` (RGB) | each channel [0..1] | RGB→HSV → `skinHue/skinSat/skinVal` |
| `freckles`, `blemishes`, `veins` | 0–100 float | `/100` |
| `isCircumcised` | bool | `false→0.0`, `true→1.0` |

All values are clamped to [0..1] after normalisation.

---

## 8. Key Classes

| Class | Location | Responsibility |
|-------|----------|----------------|
| `CharacterAssembler` | `src/Pinder.Core/Characters/CharacterAssembler.cs` | Full assembly pipeline: items + anatomy → FragmentCollection |
| `ItemDefinition` | `src/Pinder.Core/Characters/ItemDefinition.cs` | Single item; carries gameplay modifiers + conflict/priority |
| `JsonItemRepository` | `src/Pinder.Core/Data/JsonItemRepository.cs` | Parses `starter-items.json`; GetItem(id) |
| `JsonAnatomyRepository` | `src/Pinder.Core/Data/JsonAnatomyRepository.cs` | Parses `anatomy-parameters.json`; GetParameter(id) |
| `AnatomyParameterDefinition` | `src/Pinder.Core/Characters/AnatomyParameterDefinition.cs` | Scalar param + 6 bands; ResolveBand(value) |
| `AnatomyBandDefinition` | `src/Pinder.Core/Characters/AnatomyBandDefinition.cs` | One band [lower, upper) + fragment suite |
| `CharacterDataNormalizer` | `src/Pinder.Core/Characters/CharacterDataDto.cs` | Raw Unity → normalised [0..1] |
| `CharacterDataDto` | `src/Pinder.Core/Characters/CharacterDataDto.cs` | Wire DTO mirroring Unity CharacterData fields |
| `CharacterDefinitionLoader` | `src/Pinder.SessionSetup/CharacterDefinitionLoader.cs` | Load+parse character JSON → CharacterProfile |
| `CharacterDefinitionWriter` | `src/Pinder.Core/Characters/CharacterDefinitionWriter.cs` | Serialize CharacterDefinition → canonical JSON (round-trip stable) |
| `TextingStyleAggregator` | `src/Pinder.Core/Prompts/TextingStyleAggregator.cs` | Slot→axis + tone-group voting; conflict resolution |
| `FragmentCollection` | `src/Pinder.Core/Characters/FragmentCollection.cs` | Output of assembly: all fragments, stats, timing, unknownItemIds |
| `PromptBuilder` | `src/Pinder.Core/Prompts/PromptBuilder.cs` | Assembles LLM system prompt from FragmentCollection |
| `EigencoreCharacterStore` | `src/Pinder.RemoteAssets/EigencoreCharacterStore.cs` | HTTP client for EigenCore character asset API |

---

## 9. Unknown-ID Surfacing

When a Unity id has no core definition at assembly time:
1. `CharacterAssembler` records the id in a `unknownIds` list.
2. The final `FragmentCollection.UnknownItemIds` exposes the list (read-only).
3. The player flow continues normally with zero modifiers for that item.
4. Admin tooling / logging can inspect `UnknownItemIds` to know what needs to be authored.

---

## 10. Issue Reference

| Issue | Change |
|-------|--------|
| pinder-core#1175 | Anatomy: scalar-band rebuild (24 Unity params, 6 bands, normalized [0..1]) |
| pinder-core#1176 | Items: real Unity ids, new schema (item_type/priority/conflict_tags), conflict/priority resolution, unknown-id safety |
| pinder-web#949 | Items: admin editor + backend write path for item gameplay modifiers |
| pinder-web#947 | Anatomy: admin editor + backend write path for anatomy band fragments |
| pinder-core#836 | Texting style: slot→axis aggregation rule |
| pinder-core#907 | Texting style: conflict matrix |
