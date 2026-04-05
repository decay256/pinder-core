# Spec: Issue #444 — Rules DSL: Enrich All 9 YAML Files with Explicit Condition/Outcome Fields

**Module**: docs/modules/rules-dsl.md (create new)

---

## Overview

Currently only `rules-v3-enriched.yaml` (63 entries) has machine-readable `condition`/`outcome` fields. The other 8 extracted YAML documents (`risk-reward-and-hidden-depth`, `async-time`, `traps`, `archetypes`, `character-construction`, `items-pool`, `anatomy-parameters`, `extensibility`) contain only prose descriptions with no structured fields. This issue adds explicit `condition`/`outcome` dictionaries to every entry across all 9 documents that contains numeric thresholds, ranges, or named mechanical effects — enabling the Pinder.Rules engine (#446) to evaluate rules programmatically.

This is Python-only work. No C# code changes. The output is 8 new `*-enriched.yaml` files plus the already-existing `rules-v3-enriched.yaml`.

---

## File Inventory

The 9 YAML files live in `rules/extracted/` (in the external `pinder` repo, copied into `pinder-core` for this sprint). Current entry counts (as of extraction):

| File | Entries | Has condition/outcome? |
|------|---------|----------------------|
| `rules-v3-enriched.yaml` | 63 | ✅ Yes (all 63 enriched) |
| `risk-reward-and-hidden-depth.yaml` | 16 | ❌ No |
| `async-time.yaml` | 17 | ❌ No |
| `traps.yaml` | 12 | ❌ No |
| `archetypes.yaml` | 29 | ❌ No |
| `character-construction.yaml` | 30 | ❌ No |
| `items-pool.yaml` | 63 | ❌ No |
| `anatomy-parameters.yaml` | 48 | ❌ No |
| `extensibility.yaml` | 10 | ❌ No |

**Total**: 288 entries across 9 files. Not all entries will gain enrichment — entries with types `template`, `narrative`, or `definition` that contain no numeric values remain prose-only.

---

## Enrichment Process

For each of the 8 un-enriched YAML files, produce a new file named `<basename>-enriched.yaml` containing the same entries with `condition` and/or `outcome` dictionaries added to entries that contain specific numeric thresholds, ranges, or named mechanical effects.

### Priority Order

Process files in this order (per the issue):
1. `risk-reward-and-hidden-depth` — combos, momentum, tells, callbacks (many numeric values)
2. `async-time` — timing multipliers, energy costs, delay formulas
3. `traps` — already has JSON equivalent; enrichment should match traps.json schema
4. `archetypes` — ghost probabilities, test trigger frequencies (mostly qualitative)
5. `character-construction` — stat formulas, build point tables, fragment assembly rules
6. `items-pool` — item stat modifiers, slot counts, tier bonuses
7. `anatomy-parameters` — anatomy tier stat values, size modifiers
8. `extensibility` — custom content rules, schema extension points

### What Gets Enriched

An entry qualifies for enrichment if its `description`, `table_rows`, or `code_examples` contain any of:
- Numeric thresholds (e.g., "DC 12", "+2 bonus", "−3 interest")
- Numeric ranges (e.g., "6–9", "1-2 turns", "15–60 minutes")
- Named mechanical effects (e.g., "advantage", "disadvantage", "ghost trigger")
- Named stat references (e.g., "Charm", "Rizz", "Self-Awareness")
- Percentage values (e.g., "25% chance", "95%")
- Duration values (e.g., "1 turn", "2 turns", "24 hours")

### What Does NOT Get Enriched

Entries that are purely prose, UI mockups, design philosophy, or flavor text with no mechanical values remain unchanged. The existing fields (`id`, `section`, `title`, `type`, `description`, `table_rows`, `code_examples`, `designer_notes`, `flavor`, `heading_level`) are preserved exactly as-is.

---

## Condition/Outcome Field Schema

The enriched fields follow the same vocabulary established in `rules-v3-enriched.yaml`. Condition and outcome are both `Dictionary<string, object>` where keys are drawn from a controlled vocabulary.

### Condition Key Vocabulary

These keys describe **when** a rule triggers:

| Key | Type | Description | Example |
|-----|------|-------------|---------|
| `miss_range` | `[int, int]` | Roll missed DC by this range | `[1, 2]` |
| `beat_range` | `[int, int]` | Roll beat DC by this range | `[1, 4]` |
| `interest_range` | `[int, int]` | Interest meter in this range | `[5, 9]` |
| `need_range` | `[int, int]` | Need-to-hit value in this range | `[6, 10]` |
| `level_range` | `[int, int]` | Character level in this range | `[3, 4]` |
| `timing_range` | `[int, int]` | Response delay in minutes | `[15, 60]` |
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
| `callback_distance` | `int` | Turns since topic introduced | `4` |

All conditions in a single entry's `condition` dict use AND logic — all must be true for the rule to match.

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
| `shadow_effect` | `object` | Complex shadow effect | `{shadow: "Dread", delta: 2}` |
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
| `stat_modifiers` | `dict<string, int>` | Flat stat→modifier map | `{"charm": 1, "rizz": -1}` |
| `quality_boost` | `string` | LLM prompt quality instruction | `"improved"` |

New keys may be introduced for document-specific mechanics (e.g., anatomy tiers, item slots) as long as they follow the pattern of `snake_case` names with primitive values (`int`, `float`, `string`, `bool`, `[int, int]`) or flat `string→int` dicts for stat modifier maps (`stat_modifiers`).

---

## Input/Output Examples

### Example 1: Risk Tiers (risk-reward-and-hidden-depth)

**Before** (current `risk-reward-and-hidden-depth.yaml`):
```yaml
- id: §2.risk-tiers
  section: §2
  title: Risk Tiers
  type: table
  description: >-
    Every option shows its risk tier based on how hard the roll is.
    Risk and reward are always visible — the player chooses their gamble.
  table_rows:
    - DC vs Your Modifier: "Need 5 or less"
      Risk Tier: Safe
      Interest Bonus on Success: "+0 bonus"
      XP Multiplier: "1x"
      Display: "🟢 Safe"
    - DC vs Your Modifier: "Need 6–10"
      Risk Tier: Medium
      Interest Bonus on Success: "+0 bonus"
      XP Multiplier: "1.5x"
      Display: "🟡 Medium"
    - DC vs Your Modifier: "Need 11–15"
      Risk Tier: Hard
      Interest Bonus on Success: "+1 bonus"
      XP Multiplier: "2x"
      Display: "🟠 Hard"
    - DC vs Your Modifier: "Need 16+"
      Risk Tier: Bold
      Interest Bonus on Success: "+2 bonus"
      XP Multiplier: "3x"
      Display: "🔴 Bold"
```

**After** (enriched — note: original entry is split into 4 entries per tier, OR a single entry retains the table with per-row condition/outcome):

**Approach A — Per-row enrichment on table entries** (preferred for tables):
```yaml
- id: §2.risk-tiers
  section: §2
  title: Risk Tiers
  type: table
  description: >-
    Every option shows its risk tier based on how hard the roll is.
    Risk and reward are always visible — the player chooses their gamble.
  table_rows:
    - DC vs Your Modifier: "Need 5 or less"
      Risk Tier: Safe
      Interest Bonus on Success: "+0 bonus"
      XP Multiplier: "1x"
      Display: "🟢 Safe"
      condition:
        need_range: [1, 5]
      outcome:
        risk_tier: "Safe"
        interest_bonus: 0
        xp_multiplier: 1.0
    - DC vs Your Modifier: "Need 6–10"
      Risk Tier: Medium
      Interest Bonus on Success: "+0 bonus"
      XP Multiplier: "1.5x"
      Display: "🟡 Medium"
      condition:
        need_range: [6, 10]
      outcome:
        risk_tier: "Medium"
        interest_bonus: 0
        xp_multiplier: 1.5
    - DC vs Your Modifier: "Need 11–15"
      Risk Tier: Hard
      Interest Bonus on Success: "+1 bonus"
      XP Multiplier: "2x"
      Display: "🟠 Hard"
      condition:
        need_range: [11, 15]
      outcome:
        risk_tier: "Hard"
        interest_bonus: 1
        xp_multiplier: 2.0
    - DC vs Your Modifier: "Need 16+"
      Risk Tier: Bold
      Interest Bonus on Success: "+2 bonus"
      XP Multiplier: "3x"
      Display: "🔴 Bold"
      condition:
        need_range: [16, 99]
      outcome:
        risk_tier: "Bold"
        interest_bonus: 2
        xp_multiplier: 3.0
```

**Approach B — Separate entries** (used in rules-v3-enriched.yaml for failure tiers):

The implementer should use **Approach B** (separate entries) as the primary approach for consistency with `rules-v3-enriched.yaml`, which splits failure tiers, success scale, and interest states into separate entries with unique `id` slugs. Tables with multiple numeric rows should be exploded into separate entries, each with its own `condition`/`outcome`. The original table entry may be preserved as-is (with type `table`) if it serves as a summary.

### Example 2: Callback Bonus (risk-reward-and-hidden-depth)

**Before**:
```yaml
- id: §4.callback-distances
  section: §4
  title: Callback Distances
  type: table
  table_rows:
    - When topic was introduced: "2 turns ago"
      Hidden bonus: "+1 to roll"
    - When topic was introduced: "4+ turns ago"
      Hidden bonus: "+2 to roll"
    - When topic was introduced: "Opener callback (very first message)"
      Hidden bonus: "+3 to roll"
```

**After** (enriched — 3 new entries):
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

- id: §4.callback-bonus.4-plus
  section: §4
  title: Callback Bonus — 4+ Turns
  type: roll_modifier
  description: "Referencing a topic from 4+ turns ago grants +2 to roll."
  condition:
    callback_distance: 4
  outcome:
    roll_bonus: 2

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

### Example 3: Trap Entry (traps)

**Before**:
```yaml
- id: §2.the-cringe
  section: §2
  title: The Cringe
  description: >-
    Triggered by: Charm miss by 6+
    Duration: 1 turn
    Mechanical effect: Disadvantage on all Charm rolls
    ...
  type: table
  table_rows:
    - Condition: On success
      Taint behaviour: One slightly forced qualifier or emoji
    - Condition: On failure
      Taint behaviour: Try-hard energy overtakes the message
```

**After** (enriched):
```yaml
- id: §2.the-cringe
  section: §2
  title: The Cringe
  description: >-
    Triggered by: Charm miss by 6+
    Duration: 1 turn
    Mechanical effect: Disadvantage on all Charm rolls
    ...
  type: trap_activation
  condition:
    failed_stat: "Charm"
    miss_minimum: 6
  outcome:
    trap_name: "the-cringe"
    duration_turns: 1
    effect: "disadvantage"
    stat: "Charm"
  table_rows:
    - Condition: On success
      Taint behaviour: One slightly forced qualifier or emoji
    - Condition: On failure
      Taint behaviour: Try-hard energy overtakes the message
```

### Example 4: Timing Delay (async-time)

**Before** (from delay penalty table):
```yaml
- id: §4.delay-penalties
  section: §4
  title: Delay Penalties
  type: table
  table_rows:
    - Delay: "<1 minute"
      Penalty: "0"
    - Delay: "1–15 minutes"
      Penalty: "0"
    - Delay: "15–60 minutes"
      Penalty: "-1 (if interest ≥ 16)"
    - Delay: "1–6 hours"
      Penalty: "-2"
```

**After** (enriched — separate entries):
```yaml
- id: §4.delay-penalty.under-1m
  section: §4
  title: Delay Penalty — Under 1 Minute
  type: interest_change
  description: "No penalty for responding in under 1 minute."
  condition:
    timing_range: [0, 1]
  outcome:
    delay_penalty: 0

- id: §4.delay-penalty.1-15m
  section: §4
  title: Delay Penalty — 1-15 Minutes
  type: interest_change
  description: "No penalty for responding in 1-15 minutes."
  condition:
    timing_range: [1, 15]
  outcome:
    delay_penalty: 0

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

- id: §4.delay-penalty.1-6h
  section: §4
  title: Delay Penalty — 1-6 Hours
  type: interest_change
  description: "−2 interest penalty for 1-6 hour delay."
  condition:
    timing_range: [60, 360]
  outcome:
    delay_penalty: -2
```

### Example 5: Entry That Does NOT Get Enriched

```yaml
- id: §0.riskreward-hidden-depth
  section: §0
  title: Risk/Reward & Hidden Depth
  description: >-
    Five layered mechanics that reward attentive players without punishing
    casual ones. Casual players see funny dialogue and roll dice. Strategic
    players are playing 4D chess with conversation mechanics.
  type: interest_change
  heading_level: 1
```

This entry has no numeric thresholds, no ranges, and no named mechanical effects. It remains unchanged.

---

## Acceptance Criteria

### AC1: All 9 docs have enriched YAML files

- `rules-v3-enriched.yaml` already exists with 63 enriched entries — this file is preserved as-is (do not re-enrich).
- 8 new files are produced, each named `<basename>-enriched.yaml` alongside the original:
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

The implementation must produce a summary report (printed to stdout or committed as a summary file) listing:
- File name
- Total entries in file
- Number of entries enriched (have at least one of `condition` or `outcome`)
- Number of entries not enriched (prose-only, no mechanical values)

Example output format:
```
Enrichment Summary
==================
rules-v3-enriched.yaml:                    63 entries, 63 enriched
risk-reward-and-hidden-depth-enriched.yaml: 24 entries, 18 enriched
async-time-enriched.yaml:                   22 entries, 14 enriched
traps-enriched.yaml:                        18 entries, 12 enriched
archetypes-enriched.yaml:                   32 entries, 10 enriched
character-construction-enriched.yaml:       35 entries, 20 enriched
items-pool-enriched.yaml:                   70 entries, 45 enriched
anatomy-parameters-enriched.yaml:           55 entries, 38 enriched
extensibility-enriched.yaml:                12 entries,  4 enriched
==================
Total: 331 entries, 224 enriched
```

(Numbers above are illustrative — actual counts depend on how many entries have numeric values and how many table rows are exploded into separate entries.)

### AC3: Accuracy check run on all enriched entries

An accuracy check script (`accuracy_check.py` or equivalent) must be run on every enriched YAML file. The script validates that:
- Every `condition` and `outcome` value is consistent with the `description` and/or `table_rows` prose
- Numeric values in structured fields match the numeric values in the prose
- No fabricated values (values that appear in `condition`/`outcome` but not in the original prose)
- All condition keys are from the controlled vocabulary (see Condition Key Vocabulary above)
- All outcome keys are from the controlled vocabulary (see Outcome Key Vocabulary above)

### AC4: 0 INACCURATE findings

The accuracy check must produce 0 INACCURATE findings across all 9 enriched files. An INACCURATE finding is:
- A numeric value in `condition` or `outcome` that contradicts the prose (e.g., prose says "+2 bonus" but outcome says `roll_bonus: 3`)
- A condition/outcome field that has no basis in the entry's prose or table data
- A missing enrichment where the entry clearly contains numeric mechanical values

### AC5: Total enriched entries across all docs reported

The final summary must include a total count of enriched entries across all 9 documents. This number represents the total rules available for the Pinder.Rules engine to consume.

---

## Edge Cases

### Entries with table_rows but no numeric values
Some table entries contain purely qualitative data (e.g., the "Depth Gradient" table listing player types). These should NOT receive condition/outcome fields.

### Entries where numeric values are in code_examples only
If a numeric value appears only in a UI mockup code block (e.g., "DC 7" in a sample screen), it should be enriched only if the mockup illustrates a specific rule. Generic UI mockups should not be enriched.

### Entries with multiple independent rules in one description
If an entry's description contains multiple distinct mechanical rules (e.g., "Duration: 1 turn" AND "Disadvantage on all Charm rolls"), both should be captured in the single `condition`/`outcome` pair for that entry. If the rules are truly independent (different triggers), consider splitting into multiple entries with unique `id` slugs.

### Table entries with mixed enrichable/non-enrichable rows
If a table has some rows with numeric values and some without, the enrichable rows should be extracted into separate entries. The original table entry can be preserved as a summary.

### Entries that reference other sections
Cross-references like "Roll mechanics → [[rules-v3]]" should be captured in `related_rules` (an existing schema field), not in condition/outcome.

### Empty description entries
Some entries have empty `description` fields but contain `table_rows` with numeric data. These should still be enriched based on the table content.

### Duplicate IDs after splitting
When a table is split into per-row entries, each new entry needs a unique `id`. Convention: `§N.parent-slug.qualifier` (e.g., `§2.risk-tiers.safe`, `§2.risk-tiers.medium`).

---

## Error Conditions

### Invalid YAML output
If the enrichment process produces invalid YAML (syntax errors, unclosed quotes, bad indentation), the accuracy check must catch it. The file must be valid YAML parseable by PyYAML.

### Unknown condition/outcome keys
If an enrichment introduces a key not in the vocabulary tables above, it must be documented in the summary as a new key with a clear description. New keys are acceptable — the vocabulary is extensible — but undocumented keys are an error.

### Value type mismatches
All range values must be `[int, int]` lists (not strings). All single values must match their declared type. A string where an int is expected (e.g., `roll_bonus: "+2"` instead of `roll_bonus: 2`) is an accuracy failure.

### Entries that lose original fields
Enrichment must be **additive only**. No original field (`id`, `section`, `title`, `type`, `description`, `table_rows`, `code_examples`, `designer_notes`, `flavor`, `heading_level`, `unstructured_prose`, `related_rules`, `examples`) may be removed or modified. Only `condition` and `outcome` are added (or `type` may be updated if the original type was incorrect for the mechanical content).

---

## Dependencies

- **Issue #443 (Round-trip diff fixes)**: The enrichment process operates on extracted YAML files. #443 fixes block-ordering and table-separator preservation in the extraction tooling. The enrichment should use the corrected YAML output from #443 as input. If #443 is not yet merged, enrichment can proceed on the existing YAML files — the enrichment fields are additive and independent of block ordering.
- **Python 3 + PyYAML**: Required for the enrichment script and accuracy check.
- **Existing `rules-v3-enriched.yaml`**: Used as the reference for enrichment vocabulary and style. The implementer should study its patterns before enriching other files.
- **`rules/schema.yaml`**: Defines the base YAML entry schema. Enriched files extend this schema with `condition`/`outcome` fields.
- **`data/traps/traps.json`**: The traps YAML enrichment should match the mechanical values in this JSON file (duration, stat, trigger conditions).
- **No C# dependencies**: This is purely Python/YAML work. No Pinder.Core compilation needed.
- **Consumers**: Issue #446 (Pinder.Rules engine) will load the enriched YAML files. Issue #445 (test stubs) may generate additional stubs from the new enriched files.
