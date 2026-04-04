# Rules DSL

## Overview
The Rules DSL is a toolchain for extracting structured YAML rules from Pinder design markdown files, regenerating markdown from those rules, and enriching extracted YAML entries with machine-readable `condition`/`outcome` fields. It enables round-trip processing of game design documents while preserving document structure, and provides structured rule data for the Pinder.Rules engine.

## Key Components

- **`rules/tools/extract.py`** — Parses a markdown design doc into an ordered list of structured YAML rules. Preserves block ordering (paragraphs, tables, code blocks, blockquotes, flavor text, horizontal rules) and table column widths/alignment.
  - `slugify(text)` — Converts heading text to a URL-friendly slug.
  - `guess_type(title, blocks)` — Infers rule type from content.
  - `parse_table(lines)` — Parses markdown table lines into structured data, capturing separator cells for width/alignment preservation.
  - `extract_rules(filepath)` — Main entry point: reads a markdown file and returns a list of rule dicts with ordered `blocks` lists.
  - `main()` — CLI entry point.

- **`rules/tools/generate.py`** — Regenerates markdown from structured YAML rules, reproducing original document order and table formatting.
  - `generate_table(table_block)` — Renders a table block back to markdown, faithfully reproducing separator rows (alignment markers, column widths, cell padding).
  - `render_blocks(blocks)` — Renders an ordered list of blocks (paragraphs, tables, code, etc.) to markdown.
  - `rule_to_markdown(rule, heading_level=2)` — Converts a single rule dict to markdown with appropriate heading level.
  - `generate_markdown(rules)` — Main entry point: converts a full list of rules back to markdown.
  - `main()` — CLI entry point.

- **`rules/tools/enrich.py`** — Enrichment script that adds structured `condition`/`outcome` dictionaries to YAML entries containing numeric thresholds, ranges, or named mechanical effects. Produces `*-enriched.yaml` files in `rules/extracted/`.
  - `load_yaml(path)` — Loads a YAML file and returns a list of entry dicts.
  - `save_yaml(path, data)` — Writes a list of entry dicts to a YAML file.
  - `parse_stat_modifiers(text)` — Parses stat modifier strings (e.g., "Charm +1, Rizz −2") into a dict.
  - `enrich_rules_v3(entries)` — Enricher for `rules-v3.yaml`.
  - `enrich_risk_reward(entries)` — Enricher for `risk-reward-and-hidden-depth.yaml`.
  - `enrich_async_time(entries)` — Enricher for `async-time.yaml`.
  - `enrich_traps(entries)` — Enricher for `traps.yaml`.
  - `enrich_archetypes(entries)` — Enricher for `archetypes.yaml`.
  - `enrich_character_construction(entries)` — Enricher for `character-construction.yaml`.
  - `enrich_items_pool(entries)` — Enricher for `items-pool.yaml`.
  - `enrich_anatomy_parameters(entries)` — Enricher for `anatomy-parameters.yaml`.
  - `enrich_extensibility(entries)` — Enricher for `extensibility.yaml`.
  - `count_enriched(entries)` — Returns `(total, enriched)` counts for a list of entries.
  - `validate_vocabulary(entries, filename)` — Validates condition/outcome keys against the controlled vocabulary.
  - `main()` — CLI entry point. Processes all 9 files and prints an enrichment summary.

- **`rules/tools/accuracy_check.py`** — Validates enriched YAML files for correctness: parseable YAML, additive-only enrichment, controlled vocabulary compliance, numeric value consistency with prose, correct value types (ranges as lists, ints as ints).
  - `check_file(filepath)` — Checks a single enriched YAML file and returns a list of findings.
  - `main()` — CLI entry point.

- **`rules/extracted/enrichment-summary.txt`** — Generated summary report with per-file enrichment counts.

- **`rules/extracted/*-enriched.yaml`** — 9 enriched YAML files (see File Inventory below).

- **`rules/tools/test_issue443_roundtrip.py`** — Round-trip fidelity tests verifying paragraph order preservation, table column width preservation, full-document round-trip with < 50 diff lines, and no information loss.

- **`rules/tools/test_enrichment.py`** — Tests for the YAML enrichment pipeline: validates all 9 enriched files exist and are valid YAML, enrichment is additive, condition/outcome types are correct, known mechanical values are correctly enriched, and accuracy check passes with 0 INACCURATE findings.

- **`rules/tools/test_issue444_enrichment.py`** — 35 tests covering all acceptance criteria for issue #444: file existence, enrichment counts, vocabulary compliance, numeric accuracy, type correctness, and summary report validation.

- **`rules/tools/test_roundtrip.py`** — Additional round-trip tests.

- **`rules/tools/roundtrip_test.sh`** — Shell-based round-trip test script.

## API / Public Interface

### extract.py
```python
def extract_rules(filepath: str) -> list[dict]
```
Returns a list of rule dicts. Each rule contains:
- `title` — Section heading text
- `slug` — URL-friendly slug
- `type` — Inferred rule type
- `blocks` — Ordered list of block dicts, each with a `kind` field (`"paragraph"`, `"table"`, `"code"`, `"blockquote"`, `"flavor"`, `"hr"`)

Table blocks include:
- `rows` — List of row dicts (header key → cell value)
- `sep_cells` — Original separator cells preserving width and alignment markers

### generate.py
```python
def generate_markdown(rules: list[dict]) -> str
def generate_table(table_block: dict) -> str
def render_blocks(blocks: list[dict]) -> str
def rule_to_markdown(rule: dict, heading_level: int = 2) -> str
```

### enrich.py
```python
def load_yaml(path: str) -> list[dict]
def save_yaml(path: str, data: list[dict]) -> None
def parse_stat_modifiers(text: str) -> dict[str, int]
def enrich_rules_v3(entries: list[dict]) -> list[dict]
def enrich_risk_reward(entries: list[dict]) -> list[dict]
def enrich_async_time(entries: list[dict]) -> list[dict]
def enrich_traps(entries: list[dict]) -> list[dict]
def enrich_archetypes(entries: list[dict]) -> list[dict]
def enrich_character_construction(entries: list[dict]) -> list[dict]
def enrich_items_pool(entries: list[dict]) -> list[dict]
def enrich_anatomy_parameters(entries: list[dict]) -> list[dict]
def enrich_extensibility(entries: list[dict]) -> list[dict]
def count_enriched(entries: list[dict]) -> tuple[int, int]
def validate_vocabulary(entries: list[dict], filename: str) -> list[str]
def main()
```

### accuracy_check.py
```python
def check_file(filepath: str) -> list[dict]
def main()
```

### Enriched Entry Schema

Enriched entries extend the base YAML entry schema with two optional dictionaries:
- `condition` — `dict[str, Any]` describing **when** a rule triggers (AND logic for multiple keys)
- `outcome` — `dict[str, Any]` describing **what happens** when a rule fires

Keys are drawn from a controlled vocabulary (see `accuracy_check.py` for the full set). Values are primitives: `int`, `float`, `string`, `bool`, `[int, int]` (ranges), or `dict` (for complex effects like `stat_modifiers`).

## Enriched File Inventory

| File | Entries | Enriched |
|------|---------|----------|
| `rules-v3-enriched.yaml` | 157 | 100 |
| `risk-reward-and-hidden-depth-enriched.yaml` | 51 | 39 |
| `async-time-enriched.yaml` | 54 | 38 |
| `traps-enriched.yaml` | 12 | 7 |
| `archetypes-enriched.yaml` | 49 | 41 |
| `character-construction-enriched.yaml` | 46 | 18 |
| `items-pool-enriched.yaml` | 96 | 66 |
| `anatomy-parameters-enriched.yaml` | 57 | 41 |
| `extensibility-enriched.yaml` | 10 | 1 |
| **Total** | **532** | **351** |

Note: Entry counts differ from the original un-enriched files because table rows are exploded into separate entries (Approach B from the spec).

## Architecture Notes
- The extract → generate pipeline is designed for **round-trip fidelity**: `extract` then `generate` should produce output closely matching the original markdown (< 50 diff lines per design doc).
- Blocks are stored as an **ordered list** (not grouped by type) to preserve the interleaving of paragraphs, tables, and other block types in the original document.
- Table separator cells (`sep_cells`) are stored verbatim so that column widths and alignment markers (`:---`, `:---:`, `---:`) survive the round-trip.
- Design docs live in `/root/.openclaw/agents-extra/pinder/design/{systems,settings}/`.
- Enrichment is **additive only** — original fields (`id`, `section`, `title`, `type`, `description`, `table_rows`, `blocks`, etc.) are never removed or modified. Only `condition` and `outcome` fields are added.
- Each of the 8 un-enriched YAML files has a dedicated enricher function that understands the file's domain-specific mechanics (e.g., anatomy tiers, item slots, trap triggers).
- Table entries with multiple numeric rows are exploded into separate entries with unique `id` slugs following the convention `§N.parent-slug.qualifier`.
- Entries without numeric thresholds, ranges, or named mechanical effects are passed through unchanged.
- The controlled vocabulary for condition/outcome keys is extensible — new keys follow `snake_case` naming with primitive value types. The `accuracy_check.py` script validates vocabulary compliance.
- The enriched YAML files are consumed by the Pinder.Rules engine (issue #446).

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #443 | Initial creation — fixed round-trip diffs for paragraph reordering and table formatting; added `test_issue443_roundtrip.py` with tests for paragraph order preservation (AC1), table column width preservation (AC2), full doc round-trip < 50 lines (AC3/AC4), and no information loss (AC5). |
| 2026-04-04 | #444 | Enriched all 9 YAML files with `condition`/`outcome` fields — added `enrich.py` (per-file enrichers), `accuracy_check.py` (validation), 8 new `*-enriched.yaml` files, enrichment summary (532 entries, 351 enriched), `test_enrichment.py` and `test_issue444_enrichment.py` (35 tests). |
