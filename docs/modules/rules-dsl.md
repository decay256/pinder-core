# Rules DSL

## Overview
The Rules DSL module manages the extraction, enrichment, validation, and generation of the system's rule definitions. It provides a bidirectional pipeline between user-friendly authoritative Markdown source files and the structured YAML formats that the Rule Engine consumes.

## Key Components
* `rules/tools/extract.py`: Parses authoritative Markdown files into structured rule objects and `archetype_definition` dictionaries, tracking current contextual tiers.
* `rules/tools/generate.py`: Renders structured dictionaries (like `archetype_definition`) back into standard authoritative Markdown templates without data loss.
* `rules/tools/enrich.py`: Reads extracted YAML definitions (e.g., `rules/extracted/archetypes.yaml`), applies transformations, normalizes IDs, and assembles the enriched `rules-v3-enriched.yaml` payload.
* `rules/tools/accuracy_check.py`: Enforces data integrity by validating the structure and required keys of extracted definitions.

## API / Public Interface

**`rules/tools/extract.py`**
* `def parse_archetype_blocks(title: str, blocks: list, current_tier: int) -> dict:`
  Parses raw blocks from an archetype heading into a structured `archetype_definition` dict.

**`rules/tools/generate.py`**
* `def generate_archetype_definition(rule: dict, heading_level: int = 4) -> str:`
  Renders a structured `archetype_definition` dict back into the authoritative Markdown format.

**`rules/tools/enrich.py`**
* `def enrich_rules_v3(entries: list) -> list:`
  Processes base entries and incorporates externally extracted definitions into the enriched payload.

**`rules/tools/accuracy_check.py`**
* `def check_file(filepath: str) -> list:`
  Validates a YAML file's entries for required keys and accurate structure.

## Architecture Notes
The Rules DSL pipeline ensures that `archetypes.md` remains the authoritative source while making its contained intelligence visible and structured for the Rule Engine. By supporting bidirectional round-tripping, authors can freely edit text while automated pipelines can serialize, enrich, and validate the definitions. Edge cases such as missing stats/shadows and open-ended level ranges are safely mitigated.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-07 | #648 | Initial creation — Added extraction, generation, enrichment, and accuracy validation for `archetype_definition` from `archetypes.md`. |
