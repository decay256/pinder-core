# Spec: Issue #444 — Rules DSL: Enrich All 9 YAML Files with Explicit Condition/Outcome Fields

**Module**: docs/modules/rules-dsl.md (create new)

---

## Overview

Currently only `rules-v3-enriched.yaml` has machine-readable `condition`/`outcome` fields on its entries. The other 8 extracted YAML documents (`risk-reward-and-hidden-depth`, `async-time`, `traps`, `archetypes`, `character-construction`, `items-pool`, `anatomy-parameters`, `extensibility`) contain only prose descriptions with no structured fields. This issue adds explicit `condition`/`outcome` dictionaries to every entry across all 9 documents that contains numeric thresholds, ranges, or named mechanical effects — enabling the Pinder.Rules engine (#446) to evaluate rules programmatically.

This is Python-only work. No C# code changes. The output is 9 `*-enriched.yaml` files in `rules/extracted/`.

---

## File Inventory

The 9 YAML files live in `rules/extracted/`. Entry counts and enrichment status before this work:

| File | Entries | Has condition/outcome? |
|------|---------|----------------------|
| `rules-v3.yaml` | 157 | ✅ Yes (100 enriched in rules-v3-enriched.yaml) |
| `risk-reward-and-hidden-depth.yaml` | 51 | ❌ No |
| `async-time.yaml` | 54 | ❌ No |
| `traps.yaml` | 12 | ❌ No |
| `archetypes.yaml` | 49 | ❌ No |
| `character-construction.yaml` | 46 | ❌ No |
| `items-pool.yaml` | 96 | ❌ No |
| `anatomy-parameters.yaml` | 57 | ❌ No |
| `extensibility.yaml` | 10 | ❌ No |

**Total**: 532 entries across 9 files. Not all entries will gain enrichment — entries that contain no numeric thresholds, ranges, or named mechanical effects remain prose-only.

---

## Enrichment Process

### Tool

`rules/tools/enrich.py` — a Python 3 CLI script that reads each extracted YAML file and writes a corresponding `*-enriched.yaml` file. Per-file enrichment functions handle document-specific patterns.

### Function Signatures

```python
def enrich_rules_v3(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_risk_reward(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_async_time(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_traps(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_archetypes(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_character_construction(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_items_pool(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_anatomy_parameters(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]
def enrich_extensibility(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]

def count_enriched(entries: List[Dict[str, Any]]) -> Tuple[int, int]
    # Returns (total_entries, enriched_count)

def validate_vocabulary(entries: List[Dict[str, Any]], filename: str) -> List[str]
    # Returns list of validation error messages (empty = valid)

def load_yaml(path: str) -> List[Dict[str, Any]]
def save_yaml(path: str, data: List[Dict[str, Any]]) -> None

def parse_stat_modifiers(text: str) -> Dict[str, int]
    # Parses "Charm +1, Rizz -1" → {"charm": 1, "rizz": -1}

def main() -> None
    # CLI entry point: reads all 9 YAML files, enriches, writes output, prints summary
```

### Priority Order

Process files in this order (per the issue):
1. `risk-reward-and-hidden-depth` — combos, momentum, tells, callbacks (many numeric values)
2. `async-time` — timing multipliers, energy costs, delay formulas
3. `traps` — already has JSON equivalent; enrichment should match `data/traps/traps.json` schema
4. `archetypes` — ghost probabilities, test trigger frequencies (mostly qualitative)
5. `character-construction` — stat formulas, build point tables, fragment assembly rules
6. `items-pool` — item stat modifiers, slot counts, tier bonuses
7. `anatomy-parameters` — anatomy tier stat values, size modifiers
8. `extensibility` — custom content rules, schema extension points

### What Gets Enriched

An entry qualifies for enrichment if its `description`, `blocks` (paragraphs, tables, code), or legacy `table_rows` contain any of:
- Numeric thresholds (e.g., "DC 12", "+2 bonus", "−3 interest")
- Numeric ranges (e.g., "6–9", "1-2 turns", "15–60 minutes")
- Named mechanical effects (e.g., "advantage", "disadvantage", "ghost trigger")
- Named stat references (e.g., "Charm", "Rizz", "Self-Awareness")
- Percentage values (e.g., "25% chance", "95%")
- Duration values (e.g., "1 turn", "2 turns", "24 hours")

### What Does NOT Get Enriched

Entries that are purely prose, UI mockups, design philosophy, or flavor text with no mechanical values remain unchanged. All existing fields (`id`, `section`, `title`, `type`, `description`, `blocks`, `heading_level`, etc.) are preserved exactly as-is. Enrichment is **additive only** — only `condition` and `outcome` are added. The `type` field may be updated if the original type was incorrect for the mechanical content.

---

## Condition/Outcome Field Schema

The enriched fields follow the vocabulary established in `rules-v3-enriched.yaml`. Condition and outcome are both `Dict[str, Any]` where keys are drawn from a controlled vocabulary and values are primitives (`str`, `int`, `float`, `bool`) or lists of primitives — no nested dicts.

### Condition Key Vocabulary

These keys describe **when** a rule triggers. All conditions in a single entry's `condition` dict use AND logic — all must be true for the rule to match.

| Key | Type | Description | Example |
|-----|------|-------------|---------|
| `miss_range` | `[int, int]` | Roll missed DC by this range | `[1, 2]` |
| `beat_range` | `[int, int]` | Roll beat DC by this range | `[1, 4]` |
| `interest_range` | `[int, int]` | Interest meter in this range | `[5, 9]` |
| `need_range` | `[int, int]` | Need-to-hit value in this range | `[6, 10]` |
| `level_range` | `[int, int]` | Character level in this range | `[3, 4]` |
| `timing_range` | `[int, int]` | Response delay in minutes | `[15, 60]` |
| `stat_range` | `[int, int]` | Stat modifier value range | `[-2, 0]` |
| `active_conversations_range` | `[int, int]` | Number of active conversations | `[1, 3]` |
| `natural_roll` | `int` | Specific natural d20 result | `20` |
| `miss_minimum` | `int` | Miss DC by at least this much | `6` |
| `streak` | `int` | Exact win streak count | `3` |
| `streak_minimum` | `int` | Win streak of at least N | `5` |
| `action` | `string` | Turn action type | `"Read"` |
| `stat` | `string` | Stat type involved | `"Charm"` |
| `failed_stat` | `string` | Which stat was rolled on failure | `"Rizz"` |
| `shadow` | `string` | Shadow stat name | `"Fixation"` |
| `threshold` | `int` | Shadow or other threshold value | `6` |
| `conversation_start` | `bool` | Is this the first turn? | `true` |
| `formula` | `string` | Named formula identifier | `"defense_dc"` |
| `shadow_points_per_penalty` | `int` | Shadow penalty step size | `3` |
| `dc` | `int` | Fixed DC value | `12` |
| `time_of_day` | `string` | Time period | `"LateNight"` |
| `energy_below` | `int` | Energy threshold | `3` |
| `trap_active` | `bool` | Whether a trap is currently active | `true` |
| `combo_sequence` | `[string, string]` | Stat sequence for combo | `["Wit", "Charm"]` |
| `callback_distance` | `int` | Turns since topic introduced (0 = opener callback) | `4` |
| `opponent_behaviour` | `string` | Opponent behavioral tag | `"flirty"` |
| `opponent_trait` | `string` | Opponent trait identifier | `"shy"` |
| `cross_chat_event` | `string` | Cross-chat event type | `"ghost"` |
| `item` | `string` | Item identifier | `"fedora"` |
| `tier` | `string` or `int` | Item/anatomy tier | `"Tier 2"` |
| `slot` | `string` | Equipment slot | `"hat"` |
| `parameter` | `string` | Anatomy parameter name | `"girth"` |
| `levels` | `[int, int]` | Level range for archetype | `[1, 4]` |
| `anatomy_tier` | `int` | Anatomy tier level | `2` |
| `fragment_type` | `string` | Fragment type identifier | `"personality"` |
| `effect` | `string` | Named effect condition | `"trap_cleared"` |

### Outcome Key Vocabulary

These keys describe **what happens** when a rule fires:

| Key | Type | Description | Example |
|-----|------|-------------|---------|
| `interest_delta` | `int` | Change to interest meter | `-1` |
| `interest_bonus` | `int` | Bonus interest on success | `+1` |
| `roll_bonus` | `int` | Hidden bonus to roll | `+2` |
| `roll_modifier` | `int` | Modifier to roll | `-2` |
| `dc_adjustment` | `int` | DC modifier | `-2` |
| `xp_multiplier` | `float` | XP multiplier | `1.5` |
| `xp_payout` | `int` | Fixed XP award | `50` |
| `risk_tier` | `string` | Risk tier name | `"Bold"` |
| `tier` | `string` | Failure/success tier name | `"Fumble"` |
| `effect` | `string` | Named effect | `"advantage"` |
| `trap` | `bool` | Whether a trap activates | `true` |
| `trap_name` | `string` | Specific trap id | `"the-cringe"` |
| `shadow` | `string` | Shadow stat affected | `"Fixation"` |
| `shadow_delta` | `int` | Shadow stat change | `+1` |
| `shadow_effect` | `string` | Shadow effect description | `"disadvantage at tier 2"` |
| `stat_penalty_per_step` | `int` | Stat penalty amount | `-1` |
| `level_bonus` | `int` | Level-derived roll bonus | `+3` |
| `base_dc` | `int` | Base DC value in formula | `13` |
| `addend` | `string` | Formula addend | `"opponent_stat_modifier"` |
| `starting_interest` | `int` | Override starting interest | `5` |
| `ghost_chance_percent` | `int` | Ghosting probability | `25` |
| `duration_turns` | `int` | Duration of effect | `2` |
| `on_fail_interest_delta` | `int` | Interest change on failure | `-1` |
| `modifier` | `int` | Generic modifier value | `-2` |
| `energy_cost` | `int` | Energy consumed | `1` |
| `horniness_modifier` | `int` | Horniness modifier | `+3` |
| `delay_penalty` | `int` | Interest penalty for delay | `-2` |
| `stat_modifier` | `int` | Stat modifier value | `+2` |
| `stat_modifiers` | `dict` | Stat modifier map | `{"charm": 1, "rizz": -1}` |
| `quality_boost` | `string` | LLM prompt quality instruction | `"improved"` |
| `state` | `string` | InterestState name | `"Bored"` |
| `defence_stat` | `string` | Defence pairing stat | `"Self-Awareness"` |
| `forced_stat` | `string` | Forced stat for option | `"Rizz"` |
| `shadow_reduction` | `int` | Shadow stat decrease | `1` |
| `stat` | `string` | Stat name in outcome | `"Charm"` |
| `defence_window` | `string` | Defence window type | `"cracked"` |
| `tell_stat` | `string` | Tell stat identifier | `"Honesty"` |
| `primary_stat` | `string` | Primary stat for archetype | `"Wit"` |
| `baseline` | `string` | Baseline descriptor | `"average"` |
| `slot_count` | `int` | Number of slots | `3` |
| `build_points` | `int` | Build point allocation | `8` |
| `stat_cap` | `int` | Maximum stat value | `5` |
| `slots` | `int` | Slot count | `4` |
| `high_stats` | `list[str]` | High stats for archetype | `["Charm", "Rizz"]` |
| `key_stats` | `list[str]` | Key stats | `["Wit"]` |
| `key_shadow` | `string` | Key shadow stat | `"Overthinking"` |
| `archetype_count` | `int` | Number of archetypes | `20` |
| `test_frequency` | `string` | How often tested | `"every 3 turns"` |
| `trigger_percent` | `int` | Trigger probability | `25` |
| `purpose` | `string` | Descriptive purpose | `"reveal interest"` |
| `descriptor` | `string` | Text descriptor | `"compact"` |
| `mod_path` | `string` | Mod extension path | `"data/items/"` |

New keys may be introduced for document-specific mechanics as long as they follow `snake_case` naming with primitive values. New keys must be added to `validate_vocabulary()`.

---

## Input/Output Examples

### Example 1: Risk Tier Entry (risk-reward-and-hidden-depth)

**Input**: Entry with need-range thresholds for risk tiers

**Enriched output** (separate entries per tier):
```yaml
- id: §2.risk-tier.safe
  section: §2
  title: Risk Tier — Safe
  type: roll_modifier
  description: "Need Need 5 or less: Safe. +0 bonus. 1x."
  condition:
    need_range: [1, 5]
  outcome:
    risk_tier: "Safe"
    interest_bonus: 0
    xp_multiplier: 1.0

- id: §2.risk-tier.bold
  section: §2
  title: Risk Tier — Bold
  type: roll_modifier
  description: "Need 16+: Bold. +2 bonus. 3x."
  condition:
    need_range: [16, 99]
  outcome:
    risk_tier: "Bold"
    interest_bonus: 2
    xp_multiplier: 3.0
```

### Example 2: Callback Bonus (risk-reward-and-hidden-depth)

**Enriched output** (per distance):
```yaml
- id: §4.callback-bonus.2-turns
  section: §4
  title: Callback Bonus — 2 Turns
  type: roll_modifier
  description: "Referencing a topic from 2 turns ago grants +1 to roll."
  condition:
    callback_distance: 2
  outcome:
    roll_bonus: 1

- id: §4.callback-bonus.opener
  section: §4
  title: Callback Bonus — Opener
  type: roll_modifier
  description: "Referencing the opener grants +3 to roll."
  condition:
    callback_distance: 0
  outcome:
    roll_bonus: 3
```

Note: `callback_distance: 0` means opener callback (the very first message reference). Distances 2 and 4+ are for turn-based callbacks.

### Example 3: Trap Entry (traps)

**Enriched output**:
```yaml
- id: §2.the-cringe
  section: §2
  title: The Cringe
  type: trap_activation
  description: "Triggered by: Charm miss by 6+..."
  condition:
    failed_stat: "Charm"
    miss_minimum: 6
  outcome:
    trap_name: "the-cringe"
    duration_turns: 1
    effect: "disadvantage"
    stat: "Charm"
```

### Example 4: Timing Delay (async-time)

**Enriched output** (per time bracket):
```yaml
- id: §4.delay-penalty.15-60m
  section: §4
  title: Delay Penalty — 15-60 Minutes
  type: interest_change
  description: "−1 interest penalty if interest is 16 or higher."
  condition:
    timing_range: [15, 60]
    interest_range: [16, 25]
  outcome:
    delay_penalty: -1
```

### Example 5: Entry That Does NOT Get Enriched

```yaml
- id: §0.riskreward-hidden-depth
  section: §0
  title: Risk/Reward & Hidden Depth
  type: interest_change
  description: >-
    Five layered mechanics that reward attentive players without punishing
    casual ones.
  heading_level: 1
```

This entry has no numeric thresholds, no ranges, and no named mechanical effects. It remains unchanged — no `condition` or `outcome` fields added.

### Example 6: Archetype Entry with Stat Modifiers

**Enriched output**:
```yaml
- id: §2.the-smooth-operator
  section: §2
  title: The Smooth Operator
  type: archetype
  description: "Primary: Charm +3, Rizz +2. Shadow: Denial."
  condition:
    levels: [1, 4]
  outcome:
    stat_modifiers:
      charm: 3
      rizz: 2
    primary_stat: "Charm"
    key_shadow: "Denial"
```

---

## Acceptance Criteria

### AC1: All 9 docs have enriched YAML files

- 9 enriched files are produced in `rules/extracted/`, each named `<basename>-enriched.yaml`:
  - `rules-v3-enriched.yaml`
  - `risk-reward-and-hidden-depth-enriched.yaml`
  - `async-time-enriched.yaml`
  - `traps-enriched.yaml`
  - `archetypes-enriched.yaml`
  - `character-construction-enriched.yaml`
  - `items-pool-enriched.yaml`
  - `anatomy-parameters-enriched.yaml`
  - `extensibility-enriched.yaml`
- Each enriched file contains all entries from the original file. Entries that qualify for enrichment gain `condition` and/or `outcome` fields. Entries that don't qualify are passed through unchanged.

### AC2: Enriched entries per doc reported

The implementation must produce a summary report (to stdout and/or committed as `rules/extracted/enrichment-summary.txt`) listing per file:
- File name
- Total entries in file
- Number of entries enriched (have at least one of `condition` or `outcome`)

**Expected output** (actual counts from implementation):
```
Enrichment Summary
============================================================
  rules-v3-enriched.yaml                             157 entries, 100 enriched
  risk-reward-and-hidden-depth-enriched.yaml          51 entries,  39 enriched
  async-time-enriched.yaml                            54 entries,  38 enriched
  traps-enriched.yaml                                 12 entries,   7 enriched
  archetypes-enriched.yaml                            49 entries,  41 enriched
  character-construction-enriched.yaml                46 entries,  18 enriched
  items-pool-enriched.yaml                            96 entries,  66 enriched
  anatomy-parameters-enriched.yaml                    57 entries,  41 enriched
  extensibility-enriched.yaml                         10 entries,   1 enriched
============================================================
  Total: 532 entries, 351 enriched
```

### AC3: Accuracy check run on all enriched entries

An accuracy check script (`rules/tools/accuracy_check.py`) must be run on every enriched YAML file. The script validates that:
- Every `condition` and `outcome` key is from the controlled vocabulary (defined in `validate_vocabulary()`)
- Numeric values in structured fields are consistent with the prose in `description` or `blocks`
- No fabricated values (values that appear in `condition`/`outcome` but not in the original prose)
- All range values are `[int/float, int/float]` lists (not strings)
- All single values match their expected type (int, float, string, bool)

### AC4: 0 INACCURATE findings

The accuracy check must produce 0 INACCURATE findings across all 9 enriched files. An INACCURATE finding is:
- A numeric value in `condition` or `outcome` that contradicts the prose (e.g., prose says "+2 bonus" but outcome says `roll_bonus: 3`)
- A condition/outcome field that has no basis in the entry's prose, table data, or block content
- A missing enrichment where the entry clearly contains numeric mechanical values (flagged as MISSING)

### AC5: Total enriched entries across all docs reported

The final summary includes a total count: **532 entries, 351 enriched** across all 9 documents.

---

## Edge Cases

### Entries with blocks/tables but no numeric values
Some entries contain purely qualitative tables (e.g., depth gradient listing player types). These must NOT receive condition/outcome fields.

### Entries where numeric values are in code blocks only
If a numeric value appears only in a UI mockup code block (e.g., "DC 7" in a sample screen), it should be enriched only if the mockup illustrates a specific, unique rule. Generic UI mockups should not be enriched.

### Entries with multiple independent rules in one description
If an entry's description contains multiple distinct mechanical rules (e.g., "Duration: 1 turn" AND "Disadvantage on all Charm rolls"), both should be captured in the single `condition`/`outcome` pair for that entry.

### Empty description entries
Some entries have empty `description` fields but contain `blocks` with table data or paragraphs with numeric values. These should still be enriched based on the block content.

### Duplicate IDs after splitting table rows
When a table is split into per-row entries, each new entry needs a unique `id`. Convention: `§N.parent-slug.qualifier` (e.g., `§2.risk-tier.safe`, `§2.risk-tier.medium`).

### Unicode en-dashes and minus signs
Prose uses both `–` (en-dash, U+2013) and `−` (minus, U+2212) for ranges and negative values. The `parse_stat_modifiers()` helper handles both. Enrichment must normalize these to integer values in condition/outcome fields.

### `rules-v3-enriched.yaml` already enriched
The existing enriched file for rules-v3 is re-processed by `enrich_rules_v3()` — it must produce identical output. Do not skip it.

### `extensibility.yaml` has very few enrichable entries
Only ~1 entry has numeric/mechanical content. Most entries are about modding philosophy. This is expected — the file produces the lowest enrichment count.

---

## Error Conditions

### Invalid YAML output
If the enrichment process produces invalid YAML (syntax errors, unclosed quotes, bad indentation), the accuracy check catches it. All output files must be parseable by PyYAML.

### Unknown condition/outcome keys
If an enrichment introduces a key not in `validate_vocabulary()`, the validation function returns an error. New keys are acceptable if intentionally added, but they must be registered in the vocabulary sets.

### Value type mismatches
Range values must be `[int, int]` or `[float, float]` lists (not strings). Single values must match their declared type. A string where an int is expected (e.g., `roll_bonus: "+2"` instead of `roll_bonus: 2`) is an accuracy failure.

### Enrichment must be additive only
No original field (`id`, `section`, `title`, `type`, `description`, `blocks`, `heading_level`, etc.) may be removed. Only `condition` and `outcome` fields are added. The `type` field may be updated if the original was incorrect for the mechanical content.

### File I/O errors
If a source YAML file is missing or malformed, `load_yaml()` will raise a Python exception. The script should fail fast with a clear error message identifying which file failed to load.

---

## Dependencies

- **Issue #443 (Round-trip diff fixes)**: The enrichment operates on extracted YAML files whose format is stabilized by #443. Must be merged first so that extracted YAML has correct block ordering.
- **Python 3 + PyYAML**: Required runtime. Already in use across all pipeline tools.
- **Existing `rules-v3-enriched.yaml`**: Used as the reference for enrichment vocabulary and style. The implementer should study its patterns before enriching other files.
- **`data/traps/traps.json`**: The traps YAML enrichment should match the mechanical values in this JSON file (duration, stat, trigger conditions).
- **No C# dependencies**: This is purely Python/YAML work. No Pinder.Core compilation needed.
- **Consumers**: Issue #446 (Pinder.Rules engine) loads enriched YAML files via `RuleBook`. Issue #445 (test stubs) generates C# stubs from `rules-v3-enriched.yaml`.
