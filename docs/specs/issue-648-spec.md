# Specification: Issue #648 - Extract archetype definitions into YAML

**Module**: `docs/modules/rules-dsl.md`

## Overview
Currently, `design/settings/archetypes.md` is extracted as generic paragraphs, meaning its 20 archetypes are invisible to the Rule Engine. This specification details how to update the Rules DSL toolchain to extract these archetypes into structured `archetype_definition` entries containing `tier`, `level_range`, `stats`, `shadows`, `behavior`, and `interference` mappings. To satisfy rule engine consumption requirements, these entries will be appended to `rules-v3-enriched.yaml`, while ensuring `archetypes.md` remains the authoritative markdown source and supports bidirectional round-tripping.

## Function Signatures

**`rules/tools/extract.py`**
- `def parse_archetype_blocks(title: str, blocks: list, current_tier: int) -> dict:`
  Parses raw blocks from an archetype heading into a structured `archetype_definition` dict.

**`rules/tools/generate.py`**
- `def generate_archetype_definition(rule: dict, heading_level: int = 4) -> str:`
  Renders a structured `archetype_definition` dict back into the authoritative Markdown format.

**`rules/tools/enrich.py`**
- Modified `enrich_rules_v3(entries: list) -> list`:
  Reads `rules/extracted/archetypes.yaml`, filters for `type: archetype_definition`, and appends them to the `rules-v3-enriched.yaml` payload.

**`rules/tools/accuracy_check.py`**
- Modified `check_file(filepath: str) -> list`:
  Adds top-level validation for `type: archetype_definition` to enforce required keys.

## Input/Output Examples

**Input (Markdown in `archetypes.md`)**:
```markdown
### Tier 3 — Mid Game (Levels 3–9)

#### The Peacock
**Stats:** High Charm, Rizz | Low Honesty, Self-Awareness | **Shadow:** High Dread, Denial
**Level range:** 3–8

By message 3, mention one achievement or capability as context — not bragging, accounting.
Frame it as information. The mention should feel natural, like it came up because it's relevant.

**Interference:**
* count_1_2: slight — the character occasionally drops a credential or achievement reference
* count_3_5: moderate — achievement mentions are consistent and recognizable
* count_6_plus: strong — every option has some form of social proof woven in
```

**Output (YAML injected into `rules-v3-enriched.yaml`)**:
```yaml
- id: archetype.peacock
  section: §3
  type: archetype_definition
  title: The Peacock
  tier: 3
  level_range: [3, 8]
  stats:
    high: [Charm, Rizz]
    low: [Honesty, Self-Awareness]
  shadows:
    high: [Dread, Denial]
  behavior: |
    By message 3, mention one achievement or capability as context — not bragging, accounting.
    Frame it as information. The mention should feel natural, like it came up because it's relevant.
  interference:
    count_1_2: slight — the character occasionally drops a credential or achievement reference
    count_3_5: moderate — achievement mentions are consistent and recognizable
    count_6_plus: strong — every option has some form of social proof woven in
```

## Acceptance Criteria

### 1. Extraction Pipeline Updates (`extract.py`)
- Extract.py tracks the current tier when it encounters a tier heading (e.g., `### Tier 3...`).
- When encountering a level 4 heading in `archetypes.md` (e.g., `#### The Peacock`), it infers the rule type as `archetype_definition`.
- Parses the subsequent paragraphs to extract `stats` (high/low arrays), `shadows` (high/low arrays), `level_range` (e.g., `[3, 8]`), `behavior` text, and the `interference` bullet list.
- If `interference` is missing in the markdown, it populates an empty mapping to support future authoring.

### 2. Generation Pipeline Updates (`generate.py`)
- Safely formats `archetype_definition` back into the standard `archetypes.md` template without data loss.
- Includes the `**Interference:**` block rendering when interference data is present.
- Bidirectional round-trip must succeed (YAML to MD, MD to YAML) with <= 50 diff lines.

### 3. V3 Enrichment Injection (`enrich.py`)
- `enrich_rules_v3()` opens `rules/extracted/archetypes.yaml` (from the local extraction pass).
- It extracts all 20 `archetype_definition` entries, sets their `section` to `§3`, normalizes their IDs to match the required `archetype.name` format, and appends them to the `rules-v3` payload.

### 4. Accuracy Checker Updates (`accuracy_check.py`)
- Recognizes `archetype_definition` as a valid type.
- Asserts that all `archetype_definition` entries have the top-level keys: `tier` (int), `level_range` (list of 2 ints), `behavior` (string), and `interference` (dict).
- Fails the build if any of the 20 archetypes are missing these fields.

## Edge Cases
- **Missing Data**: Some archetypes might not have a `Shadow` or `Stats` defined (e.g., "—"). The extractor must parse these as empty arrays `[]` rather than throwing errors.
- **Open-Ended Ranges**: Level ranges like `6+` or `8+` should be parsed safely (e.g., `[6, 99]`).
- **Punctuation Nuances**: Paragraph blocks might be joined with newlines; `behavior` must preserve the exact block text, retaining Markdown semantics.

## Error Conditions
- If `archetypes.yaml` cannot be located by `enrich.py` during injection, it logs a critical error or raises `FileNotFoundError` (preventing silent omissions).
- If `accuracy_check.py` finds an archetype with missing or malformed `interference` structure, it logs `INACCURATE` with the specific entry ID and field name.

## Dependencies
- `rules/tools/extract.py`
- `rules/tools/generate.py`
- `rules/tools/enrich.py`
- `rules/tools/accuracy_check.py`
