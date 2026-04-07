# Contract: Issue #648 — Extract Archetypes into YAML

## Component
`rules/extracted/rules-v3-enriched.yaml` (Data Configuration)

## Description
The 20 archetypes currently documented only in `design/settings/archetypes.md` must be extracted into the structured YAML file `rules-v3-enriched.yaml` so they can be processed by the rules engine and accuracy checkers.

## Interface Changes
**File**: `rules/extracted/rules-v3-enriched.yaml`
**Format**:
Append a new section (or insert appropriately) containing 20 array items. Each item must match the structure:
```yaml
- id: archetype.<name>
  section: §3
  type: archetype_definition
  title: <Title>
  tier: <Integer 1-3>
  level_range: [<Min>, <Max>]
  stats:
    high: [<StatType>, ...]
    low: [<StatType>, ...]
  shadows:
    high: [<ShadowStatType>, ...]
  behavior: |
    <Multi-line string behavior description>
  interference:
    count_1_2: <String>
    count_3_5: <String>
    count_6_plus: <String>
```

**Constraints**:
- Valid YAML syntax.
- Accurate transcription from `/root/.openclaw/agents-extra/pinder/design/settings/archetypes.md`.
- Ensure round-trip validation scripts (if any) succeed.
