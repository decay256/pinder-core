# Rules DSL

## Overview
The Rules DSL is a toolchain for extracting structured YAML rules from Pinder design markdown files and regenerating markdown from those rules. It enables round-trip processing of game design documents while preserving document structure, block ordering, and table formatting.

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

- **`rules/tools/test_issue443_roundtrip.py`** — Round-trip fidelity tests verifying paragraph order preservation, table column width preservation, full-document round-trip with < 50 diff lines, and no information loss.

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

## Architecture Notes
- The extract → generate pipeline is designed for **round-trip fidelity**: `extract` then `generate` should produce output closely matching the original markdown (< 50 diff lines per design doc).
- Blocks are stored as an **ordered list** (not grouped by type) to preserve the interleaving of paragraphs, tables, and other block types in the original document.
- Table separator cells (`sep_cells`) are stored verbatim so that column widths and alignment markers (`:---`, `:---:`, `---:`) survive the round-trip.
- Design docs live in `/root/.openclaw/agents-extra/pinder/design/{systems,settings}/`.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #443 | Initial creation — fixed round-trip diffs for paragraph reordering and table formatting; added `test_issue443_roundtrip.py` with tests for paragraph order preservation (AC1), table column width preservation (AC2), full doc round-trip < 50 lines (AC3/AC4), and no information loss (AC5). |
