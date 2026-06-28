# Unity ↔ Core Item & Anatomy Contract

**Status:** SHIPPED in core — issue #1176 (items) + issue #1175 (anatomy). Both cover the wire contract between Unity (`Diego_Quarantine/p-game`) and pinder-core.
> **Note:** The Unity runtime (`p-game` @ `c0d45c5`) is currently pinned/pending and not fully compatible with current core main without updating its vendored DLL/data/bridge. Current Unity `starter-items.json` still uses old fields like `tier` which are omitted in the v2 schema described below.

---

## 1. Authority Split

| Domain | Unity SSOT | pinder-core SSOT |
|--------|-----------|------------------|
| Item existence / ids | ✅ | — |
| Item slot assignment | ✅ (slot enum) | — |
| Attachment transforms (scale/offset/rotation/opacity/tiling) | ✅ (graphics-only) | ❌ excluded |
| `streamerMode` / `isStreamerSafe` | ✅ (geometry swap only) | ❌ excluded |
| Unity `personalityTags` | ✅ (placeholder only; all `elegant,fancy` / `submissive,cosplay`) | ❌ IGNORED |
| Unity `PromptLookupTable` | legacy | ❌ SUPERSEDED by core |
| Item gameplay meaning (stat mods, fragments, priority, conflict_tags, item_type) | — | ✅ |
| Anatomy parameter existence / range / normalisation | ✅ | — |
| Anatomy band gameplay fragments | — | ✅ |

---

## 2. Item Schema (pinder-core v2, issue #1176)

`data/items/starter-items.json` — JSON array of item objects.

```json
{
  "id": "head_tophat",
  "display_name": "Top Hat",
  "slot": "Head",
  "item_type": "accessory",
  "priority": 100,
  "conflict_tags": [],
  "stat_modifiers": { "charm": 1, "rizz": 1 },
  "personality_fragment": "...",
  "backstory_fragment": "...",
  "texting_style_fragment": "...",
  "archetype_tendencies": ["The Peacock"],
  "response_timing_modifier": {
    "base_delay_delta_minutes": -2,
    "delay_variance_multiplier": 0.8,
    "dry_spell_probability_delta": 0.0,
    "read_receipt": "neutral"
  }
}
```

### 2.1 Fields

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `id` | string | ✅ | Unity-verbatim id (e.g. `head_tophat`, `vest1`, `classic2`) |
| `display_name` | string | optional | Fallback = `id` |
| `slot` | string | ✅ | See §3 Slot Vocabulary |
| `item_type` | string | ✅ | See §4 Item Types |
| `priority` | int | optional | Default 100. Higher = wins conflict. |
| `conflict_tags` | string[] | optional | See §5 Conflict Resolution |
| `stat_modifiers` | object | optional | Keys: `charm` `rizz` `honesty` `chaos` `wit` `self_awareness` |
| `personality_fragment` | string | optional | Appended to PERSONALITY section |
| `backstory_fragment` | string | optional | Appended to BACKSTORY section |
| `texting_style_fragment` | string | optional | SYNTAX/TONE block (see §6) |
| `archetype_tendencies` | string[] | optional | Votes toward archetype ranking |
| `response_timing_modifier` | object | optional | See §7 |

**Excluded fields (not in pinder-core):** `scale`, `offset`, `rotation`, `opacity`, `tiling`, `personalityTags`, `isStreamerSafe`, `streamerMode`, `occupiedSlots`.

### 2.2 Stat Modifier Vocabulary

The `stat_modifiers` object keys map to `StatType` enum values:

| JSON key | C# enum |
|----------|---------|
| `charm` | `StatType.Charm` |
| `rizz` | `StatType.Rizz` |
| `honesty` | `StatType.Honesty` |
| `chaos` | `StatType.Chaos` |
| `wit` | `StatType.Wit` |
| `self_awareness` | `StatType.SelfAwareness` |

---

## 3. Slot Vocabulary

Unity slot enum: `0=Head 1=Face 2=Body 3=Waist 4=Special`.

### Accessory / Outfit Slots (Unity enum strings verbatim)

| Unity Slot String | Unity Int | Used For |
|-------------------|-----------|----------|
| `Head` | 0 | Hats, crowns, wigs, horn accessories |
| `Face` | 1 | Glasses, monocle, nose rings, eye patches |
| `Body` | 2 | Outfits / vests |
| `Waist` | 3 | *(no items in current Unity build)* |
| `Special` | 4 | Shoes / footwear |

### LookCatalog Slots

| Slot String | Used For |
|-------------|----------|
| `Hair` | LookCatalog hair styles (hair1-5) |
| `Arms` | LookCatalog arm styles (arms0-6) |

### TatooCatalog Slots

| Slot String | Used For |
|-------------|----------|
| `Tattoo` | Classic tattoos (`classic2..35`), flower tattoos (`flowers1..9`) |
| `Sticker` | Sticker slots (same id pool as TatooCatalog; see §4) |

### Slot → TextingStyle Axis Mapping (`SlotToSyntaxAxis`)

| Slot | Syntax Axis |
|------|-------------|
| `Special` (Unity) / `shoes` (legacy) | emoji |
| `Head` (Unity) / `hat` (legacy) | shorthand |
| `Body` (Unity) / `shirt` (legacy) | grammar |
| `Hair` (Unity) / `trousers` (legacy) | structure |
| `Arms` (Unity) / `frame` (legacy) | length |
| `Face` (Unity) / `accessory` (legacy) | tics |
| `Waist` | *(no mapping — currently empty in Unity)* |
| `Tattoo`, `Sticker` | *(no syntax axis — contribute to personality channel only)* |

Legacy slot names (`shoes`, `hat`, `shirt`, `trousers`, `frame`, `accessory`) are kept for backward compatibility.

---

## 4. Item Types

| `item_type` | Description |
|-------------|-------------|
| `accessory` | MainAccessoryCatalog item (Head/Face/Special slot) |
| `outfit` | MainAccessoryCatalog outfit (Body slot); `vest1..11` + `outfit_maid` |
| `hair` | LookCatalog hair style |
| `arms` | LookCatalog arm style |
| `tattoo` | TatooCatalog tattoo (can be equipped in tattoo slot) |
| `sticker` | TatooCatalog id used as a sticker (same id pool; distinguished by item_type, not id) |

**Sticker vs Tattoo:** Unity's `CharacterData.stickerIds[3]` references the same `TatooCatalog` id pool as `tattooId`. The distinction is item_type in core — a `classic10` equipped as a tattoo has `item_type=tattoo`; if referenced via a sticker slot it has `item_type=sticker`. Core ships only `tattoo`-type entries; the sticker semantic is an equip-context distinction at the Unity layer and does NOT require separate JSON records.

---

## 5. Conflict / Priority Resolution

When two equipped items share a `conflict_tag`, the higher-priority item wins.

**Tie-break rule:** Earlier equip order wins (lower index in the `items` array wins when priorities are equal).

**Fragment suppression:** The lower-priority conflicting item's `personality_fragment`, `backstory_fragment`, `texting_style_fragment`, and `archetype_tendencies` are **dropped**. Stat modifiers (`stat_modifiers`) always apply regardless of conflict outcome.

**Implementation:** `CharacterAssembler.Assemble()` — see `src/Pinder.Core/Characters/CharacterAssembler.cs`.

**Example:** `face_glases1` and `face_monocle` both have `conflict_tags: ["face_eyewear"]`. Equipping both → the one listed first in `items` wins (both have priority 100); the other's fragments are suppressed.

---

## 6. Texting-Style Fragment Format

For items that contribute a syntax axis, the `texting_style_fragment` uses the SYNTAX/TONE block format:

```
SYNTAX:
- emoji: <rule>
- shorthand: <rule>
- grammar: <rule>
- structure: <rule>
- length: <rule>
- tics: <rule>
TONE:
- stance (<qualifier>): <rule>
- register (<qualifier>): <rule>
- pacing (<qualifier>): <rule>
```

The aggregator (`TextingStyleAggregator`) reads only the axis owned by the item's slot (see §3). Other axes in the SYNTAX block are ignored when only that slot's axis is needed.

---

## 7. Response Timing Modifier

```json
"response_timing_modifier": {
  "base_delay_delta_minutes": 0,
  "delay_variance_multiplier": 1.0,
  "dry_spell_probability_delta": 0.0,
  "read_receipt": "neutral"
}
```

| Field | Type | Effect |
|-------|------|--------|
| `base_delay_delta_minutes` | int | Additive delta to base reply delay (minutes) |
| `delay_variance_multiplier` | float | Multiplicative; default 1.0 |
| `dry_spell_probability_delta` | float | Additive delta to dry-spell probability [0..1] |
| `read_receipt` | string | `"neutral"` \| `"shows"` \| `"hides"` — last non-neutral wins |

---

## 8. Unknown-ID Safety

An equipped Unity item id with no core definition resolves to **zero modifiers** (no stat mods, no fragments). The id is collected in `FragmentCollection.UnknownItemIds` for admin authoring. The player flow never hard-fails.

See: `CharacterAssembler` resolvedItems loop; `FragmentCollection.UnknownItemIds`.

---

## 9. Anatomy Contract (issue #1175)

### 9.1 Parameter Set (~24 scalars)

All parameters mirror Unity's `CharacterData.cs` field names verbatim:

| Group | Parameters |
|-------|-----------|
| Trunk | `trunkLengthBase`, `trunkLengthMid`, `trunkLengthTip`, `trunkGirth`, `trunkCurvature` (bipolar) |
| Glans | `glansScale`, `glansWidth` |
| Scrotum | `scrotumScale`, `leftTesticleScale`, `rightTesticleScale`, `scrotumDrop` |
| Age | `prepucius`, `arrugatis`, `gravitatis`, `venicus` |
| Expression | `sad`, `happy`, `serius` |
| Skin | `skinHue`, `skinSat`, `skinVal`, `freckles`, `blemishes`, `veins`, `isCircumcised` |

Grooming fields (`hasHair`, `hairLength`, `hair`, `hairColor`) are **cosmetic-only** and excluded from anatomy parameters.

### 9.2 Normalisation Rules

| Source type | Rule |
|-------------|------|
| Unity float 0–100 | `/ 100` → [0..1] |
| `trunkCurvature` (bipolar −100..100) | `(x + 100) / 200` → [0..1] |
| `skinColor` (Unity RGB, each channel [0..1]) | RGB → HSV → `skinHue`/`skinSat`/`skinVal` (each [0..1]) |
| `isCircumcised` (bool) | `false → 0.0`, `true → 1.0` |

Normalisation is implemented in `CharacterDataNormalizer.Normalize(CharacterDataDto)`.

### 9.3 Band System (6-band scalar)

Fixed standard thresholds: `[0.00, 0.05, 0.20, 0.50, 0.70, 0.95, 1.00]` → 6 bands.

| Band | Range (half-open) | Description |
|------|-------------------|-------------|
| 0 | [0.00, 0.05) | Minimal / absent |
| 1 | [0.05, 0.20) | Low |
| 2 | [0.20, 0.50) | Below-average |
| 3 | [0.50, 0.70) | Average / mid |
| 4 | [0.70, 0.95) | Above-average |
| 5 | [0.95, 1.00] | Maximal (last band inclusive) |

**Special cases:**
- `isCircumcised` (bool): 2 bands — `[0.0, 0.5)` = uncircumcised, `[0.5, 1.0]` = circumcised.
- `trunkCurvature` (bipolar): same 6-band thresholds applied to normalised [0..1] value; the midpoint band (band 3) represents neutral/straight.

Each band may carry any or all of: `personality_fragment`, `backstory_fragment`, `texting_style_fragment`, `archetype_tendencies`, `response_timing_modifier`, `stat_modifiers`. All fields optional; an empty band contributes nothing.

### 9.4 Anatomy JSON Schema

`data/anatomy/anatomy-parameters.json`:

```json
[
  {
    "id": "trunkLengthBase",
    "name": "Trunk Length Base",
    "bands": [
      { "lower": 0.00, "upper": 0.05, "personality_fragment": "..." },
      { "lower": 0.05, "upper": 0.20 },
      ...
    ]
  }
]
```

---

## 10. Real Unity Inventory (verified p-game tip c0d45c5)

### MainAccessoryCatalog — Accessories (33 items)

`head_tophat`, `face_monocle`, `head_cheff`, `head_crown`, `head_hair`, `head_hat`, `head_hat_2`, `head_hat_3`, `head_hat_4`, `head_hat_5`, `head_hat_6`, `head_hat_8`, `head_hat_9`, `head_hat_10`, `head_horns`, `face_eyes1`, `face_eyes2`, `face_glases1`, `face_mouth1`, `face_tongue1`, `face_nariz1`, `face_pirate1`, `head_antenas`, `head_horns2`, `head_pirate`, `head_wiz`, `special_shoe1`, `special_shoe2`, `special_shoe3`, `special_shoe4`, `special_shoe5`, `special_shoe6`, `special_shoe7`

### MainAccessoryCatalog — Outfits (11 items, Body slot)

`outfit_maid`, `vest1`, `vest2`, `vest3`, `vest4`, `vest5`, `vest7`, `vest8`, `vest9`, `vest10`, `vest11`

> **Note:** `vest6` is ABSENT in Unity — do NOT create a `vest6` entry. See Unity follow-up.

### LookCatalog
 
 Hair: `hair1`, `hair2`, `hair3`, `hair4`, `hair5`
 Arms: `arms0`, `arms1`, `arms2`, `arms3`, `arms4`, `arms5`, `arms6`

### TatooCatalog (34 classic + 9 flowers = 43 items)

`classic2..classic35` (with some gaps), `flowers1..flowers9`

---

## 11. Explicit Exclusions

The following Unity fields/concepts are **excluded from pinder-core** and must never appear in the item schema or assembly pipeline:

| Excluded | Reason |
|----------|--------|
| Attachment transforms (scale/offset/rotation/opacity/tiling) | Unity graphics — not gameplay |
| `streamerMode` / `isStreamerSafe` | Geometry swap (cucumber geometry) — Unity-only |
| Unity `personalityTags` | Placeholder data (`elegant,fancy`/`submissive,cosplay`) — superseded by core |
| Unity `PromptLookupTable` | Superseded by pinder-core gameplay modifiers |
| `occupiedSlots` field on outfits | Dead field in Unity — not used by core |
| Grooming fields (`hasHair`, `hairLength`, `hair`, `hairColor`) | Cosmetic-only |

---

## 12. Unity Follow-ups (file AFTER core/web/docs done)

- **Placeholder `personalityTags`:** Replace Unity placeholder tags with actual descriptors once pinder-core modifiers are shipped.
- **`arms3`/`arms4` ids:** Both map to 'T Rex' — Unity should resolve to a single id. Core carries both in the interim.
- **Retire `PromptLookupTable`:** Remove the legacy table from Unity once pinder-core DLL is live.
- **Empty `Waist` / `Body` slots:** No items currently use `Waist` (slot 3) or non-outfit `Body` items. Confirm intent.
- **Dead `occupiedSlots` field on outfits:** Can be removed from Unity's `AccessoryData` struct.
- **`vest6` gap:** `vest6` is absent from Unity's catalog. If intentional, no action needed. If a bug, re-add.
