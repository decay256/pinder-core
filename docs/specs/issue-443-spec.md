# Spec: Issue #443 — Rules DSL: Fix Remaining Round-Trip Diffs

**Module**: `docs/modules/rules-dsl.md`

## Overview

The round-trip conversion pipeline (Markdown → YAML → Markdown) for Pinder design documents currently produces ~1251 diff lines against the originals. The two root causes are (1) paragraph/block reordering — `extract.py` groups content by type instead of preserving document order — and (2) table formatting — `generate.py` normalizes column widths instead of reproducing originals. This issue fixes both so that every document round-trips with fewer than 50 diff lines (whitespace-only differences acceptable).

## Function Signatures

All functions are in Python 3. Types are described informally (Python is dynamically typed; no type stubs exist in this codebase).

### `extract.py`

```python
def extract_rules(filepath: str) -> list[dict]
```

**Returns** a list of rule entry dicts. Each entry contains:

| Field | Type | Description |
|-------|------|-------------|
| `id` | `str` | Rule identifier, e.g. `§5.failure-tiers` |
| `section` | `str` | Section prefix, e.g. `§5` |
| `title` | `str` | Heading text |
| `type` | `str` | Guessed type: `table`, `template`, `definition`, etc. |
| `description` | `str` | Text of the first paragraph block (backward compat) |
| `blocks` | `list[dict]` | **Ordered** list of content blocks |
| `heading_level` | `int` (optional) | Markdown heading level (1–6) |
| `compact_heading` | `bool` (optional) | `True` if original had zero blank lines after heading |

Each block in the `blocks` list is a dict with a `kind` field:

| `kind` | Additional Fields | Description |
|--------|-------------------|-------------|
| `paragraph` | `text: str` | One or more lines of prose or list items |
| `table` | `rows: list[dict]`, `sep_cells: list[str]` | Parsed table with raw separator cells |
| `code` | `text: str` | Fenced code block including opening/closing fences |
| `blockquote` | `text: str` | Lines after `>` marker, joined by `\n` |
| `flavor` | `text: str` | Italic-only lines (stripped of `*` or `_` markers) |
| `hr` | *(none)* | Horizontal rule (`---` or `***`) |

**Key invariant**: Blocks appear in `blocks` in the same order as they appear in the source Markdown. Paragraphs, tables, code blocks, blockquotes, flavor text, and horizontal rules are interleaved exactly as in the original document.

```python
def parse_table(lines: list[str]) -> tuple[list[dict], list[str], str | None]
```

**Returns** `(rows, sep_cells, raw_fallback)`:

- `rows` — list of dicts keyed by header names, one dict per data row
- `sep_cells` — list of raw separator cell strings (between pipes), preserving alignment markers (`:---`, `:---:`, etc.) and exact character widths
- `raw_fallback` — raw text if parsing fails; `None` on success

```python
def slugify(text: str) -> str
```

Converts heading text to a URL-friendly slug. Lowercases, strips non-word characters, replaces spaces/underscores with hyphens.

```python
def guess_type(title: str, blocks: list[dict]) -> str
```

Infers rule type from title keywords and block content. Returns one of: `table`, `template`, `interest_change`, `shadow_growth`, `roll_modifier`, `trap_activation`, `state_change`, `definition`, `narrative`.

### `generate.py`

```python
def generate_markdown(rules: list[dict]) -> str
```

**Returns** a complete Markdown document string reconstructed from the list of rule entries. Iterates through each rule, determining heading level from `heading_level` field (or inferring from `section`/`id`), and renders blocks in their stored order.

```python
def rule_to_markdown(rule: dict, heading_level: int = 2) -> str
```

**Returns** a Markdown string for a single rule entry. Renders the heading, then iterates through `blocks` in order. Falls back to legacy individual fields (`description`, `table_rows`, `code_examples`, etc.) if `blocks` is absent.

```python
def generate_table(table_block: dict) -> str
```

**Returns** a Markdown table string from a block dict. Uses `sep_cells` to reproduce the original separator row exactly. When separator cells start with a space (indicating padded formatting), header and data cells are right-padded to match stored column widths.

```python
def render_blocks(blocks: list[dict]) -> list[str]
```

**Returns** a list of Markdown lines (including trailing blank lines between blocks). Dispatches each block by `kind`.

## Input/Output Examples

### Example 1: Paragraph ordering preservation

**Source Markdown:**
```markdown
## 5. Failure Tiers

When a roll fails, the margin determines severity.

| Tier | Range |
|------|-------|
| Fumble | 1-2 |

This is important context that follows the table.
```

**Extracted YAML (blocks field):**
```yaml
blocks:
  - kind: paragraph
    text: "When a roll fails, the margin determines severity."
  - kind: table
    rows:
      - {Tier: Fumble, Range: "1-2"}
    sep_cells: ["------", "-------"]
  - kind: paragraph
    text: "This is important context that follows the table."
```

**Regenerated Markdown** reproduces paragraph→table→paragraph order exactly.

### Example 2: Table column width preservation

**Source Markdown:**
```markdown
| Stat          | Defence       | Base DC |
| ------------- | ------------- | ------- |
| Charm         | SA            | 13      |
```

**Extracted YAML (sep_cells):**
```yaml
sep_cells:
  - " ------------- "
  - " ------------- "
  - " ------- "
```

The leading space in `" ------------- "` signals padded formatting. `generate_table()` pads all header and data cells to match the stored widths (15, 15, 9 characters respectively).

**Regenerated Markdown:**
```markdown
| Stat          | Defence       | Base DC |
| ------------- | ------------- | ------- |
| Charm         | SA            | 13      |
```

### Example 3: Empty first header cell

**Source Markdown:**
```markdown
| | Need 5+ (80%) | Need 8+ (65%) |
|---|---|---|
| Charm | Easy | Medium |
```

**Extracted YAML:**
```yaml
rows:
  - {"": Charm, "Need 5+ (80%)": Easy, "Need 8+ (65%)": Medium}
sep_cells: ["---", "---", "---"]
```

**Regenerated Markdown** must preserve the empty first header: `| | Need 5+ (80%) | ...` — not `|  | Need 5+ (80%) | ...` (note: single space vs. double space between pipes is a known remaining diff at ≤50 lines tolerance).

### Example 4: Compact heading (no blank line after)

**Source Markdown:**
```markdown
### Stat Breakdowns
Charm is the primary social stat.
```

**Extracted YAML:**
```yaml
compact_heading: true
```

**Regenerated Markdown** omits the blank line between heading and first content block.

## Acceptance Criteria

### AC1: Paragraph reordering fixed

**Requirement**: Extracted blocks are stored as an ordered list in document order, and `generate.py` renders them in that stored order.

**Specification**: `extract.py` must parse content blocks (paragraphs, tables, code blocks, blockquotes, flavor text, horizontal rules) and append them to the rule's `blocks` list in the exact order they appear in the source document. It must NOT group blocks by type (e.g., all paragraphs first, then all tables). `generate.py` must iterate `blocks` sequentially and render each block, inserting a blank line after each.

**Verification**: For all 9 design documents, paragraphs in the regenerated Markdown must appear in the same order relative to tables and other content as in the original. No paragraph reordering diffs should appear.

### AC2: Table column widths preserved

**Requirement**: `extract.py` stores raw separator cells in `sep_cells`, and `generate.py` reproduces them.

**Specification**: `parse_table()` must split the separator row by `|` and store each cell string exactly as-is (including leading/trailing spaces, alignment markers like `:---:`, and padding dashes). `generate_table()` must reconstruct the separator row by joining `sep_cells` with `|` delimiters. When `sep_cells` entries begin with a space, header and data cells must be padded to the same widths as their corresponding separator cells.

**Verification**: Table separator rows in regenerated Markdown must match originals. The `rules-v3.diff` should shrink from 15 lines to ≤15 lines (remaining diffs are the empty-header-cell space issue, which is within tolerance).

### AC3: Re-run roundtrip test on all 9 docs

**Requirement**: `roundtrip_test.sh` must be run against all 9 design documents.

**Specification**: The script processes all `.md` files in `$DESIGN_DIR/systems/` and `$DESIGN_DIR/settings/`, running `extract.py` then `generate.py` for each, and computing `diff` line counts. The 9 documents are:

1. `rules-v3.md` (systems/)
2. `risk-reward-and-hidden-depth.md` (systems/)
3. `async-time.md` (systems/)
4. `archetypes.md` (settings/)
5. `character-construction.md` (settings/)
6. `anatomy-parameters.md` (settings/)
7. `items-pool.md` (settings/)
8. `traps.md` (settings/)
9. `extensibility.md` (settings/)

### AC4: All diffs < 50 lines per doc

**Requirement**: Every document's round-trip diff must be under 50 lines.

**Specification**: After running `roundtrip_test.sh`, each `.diff` file in `rules/diffs/` must contain fewer than 50 lines. Whitespace-only diffs (blank line insertions/removals, trailing space differences) are acceptable. Non-whitespace content diffs are NOT acceptable.

**Current state (post-implementation)**:

| Document | Diff Lines | Nature of Remaining Diffs |
|----------|-----------|--------------------------|
| `archetypes` | 38 | Blank line insertions after compact headings |
| `rules-v3` | 15 | Table cell padding (empty first header cell space) |
| `anatomy-parameters` | 6 | Trailing space + blank line |
| `async-time` | 2 | Trailing newline |
| `character-construction` | 2 | Trailing newline |
| `extensibility` | 2 | Trailing newline |
| `items-pool` | 2 | Trailing newline |
| `risk-reward-and-hidden-depth` | 2 | Trailing newline |
| `traps` | 2 | Trailing newline |

All are within the <50 threshold and are whitespace-only.

### AC5: Before/after line count report

**Requirement**: A report showing diff line counts before and after the fix.

**Specification**: The report must show per-document diff line counts. Before: ~1251 total lines across all documents. After: <50 per document, ~71 total. The report can be output by `roundtrip_test.sh` or documented in the PR description.

## Edge Cases

### Empty documents
If a Markdown file contains no headings, `extract.py` creates a preamble entry with `id: §0.preamble` and collects all content into its `blocks` list. `generate.py` renders this as a level-1 heading. An empty file produces an empty rules list and empty Markdown output.

### Tables with no data rows
A table with only a header and separator (no data rows) should produce a `table` block with `rows: []` and valid `sep_cells`. `generate_table()` returns just the header and separator lines.

### Tables with empty cells
Empty cells in data rows result in empty string values in the row dict. `generate.py` renders them as `|  |` (space-pipe-space-pipe).

### Tables with empty first header
When the first header cell is empty (e.g., `| | Col2 | Col3 |`), the extracted header list has an empty string `""` as the first key. The regenerated table may produce `|  |` instead of `| |` — a 1-character whitespace difference that is within the acceptable tolerance.

### Mixed content ordering
A section containing `paragraph → table → paragraph → code → paragraph` must preserve all 5 blocks in that exact order. No reordering, no grouping.

### Code blocks containing pipe characters
Fenced code blocks (between `` ``` `` markers) are NOT parsed as tables, even if they contain `|` characters. The `in_code_block` flag takes priority over table detection.

### Consecutive tables
Two tables separated by a blank line produce two separate `table` blocks. The blank line between them is implicit (each block gets a trailing blank line in `render_blocks`).

### Blockquotes with empty lines
A blockquote line that is just `>` (no content after the marker) is stored as an empty string in the blockquote text. `generate.py` renders it back as `>`.

### Horizontal rules vs. separator rows
A line of `---` outside a table context is detected as a horizontal rule (`hr` block). Inside a table (after a header row), it is part of the separator row. The `in_table` state variable prevents misclassification.

### Compact headings (no blank line after)
When a heading is immediately followed by content (no intervening blank line), `extract.py` sets `compact_heading: true`. `generate.py` omits the blank line between heading and first block. If `compact_heading` is false or absent, a blank line is inserted.

### Legacy YAML entries (no `blocks` field)
`rule_to_markdown()` falls back to rendering individual fields (`description`, `table_rows`, `code_examples`, `designer_notes`, `examples`, `unstructured_prose`) if `blocks` is not present. This maintains backward compatibility with YAML files generated before the block-ordering fix.

## Error Conditions

### File not found
`extract_rules(filepath)` raises `FileNotFoundError` if the Markdown file does not exist. `generate.py` raises `FileNotFoundError` if the YAML file does not exist. No custom error handling — standard Python exceptions propagate.

### Malformed YAML
`yaml.safe_load()` in `generate.py` raises `yaml.YAMLError` on invalid YAML input. The `main()` function does not catch this — it propagates to the caller.

### Empty YAML
If `yaml.safe_load()` returns `None` or an empty list, `generate.py` prints `"No rules found."` to stderr and exits with code 1.

### Invalid table structure
If a table block has no `rows` or the rows list is empty, `generate_table()` returns an empty string `""`. No exception is raised.

### Missing `sep_cells`
If a table block lacks `sep_cells`, `generate_table()` falls back to `---` separators (3 dashes per column, no width preservation).

### Command-line usage errors
Both `extract.py` and `generate.py` print a usage message to stderr and exit with code 1 if no file argument is provided.

## Dependencies

### Python runtime
- **Python 3** (any 3.x version with `re`, `sys` built-ins)
- **PyYAML** (`yaml` module) — used for YAML serialization (`extract.py`) and deserialization (`generate.py`)

### Source documents
- Markdown design documents in the external `pinder` repo at `$DESIGN_DIR`:
  - `$DESIGN_DIR/systems/rules-v3.md`
  - `$DESIGN_DIR/systems/risk-reward-and-hidden-depth.md`
  - `$DESIGN_DIR/systems/async-time.md`
  - `$DESIGN_DIR/settings/archetypes.md`
  - `$DESIGN_DIR/settings/character-construction.md`
  - `$DESIGN_DIR/settings/anatomy-parameters.md`
  - `$DESIGN_DIR/settings/items-pool.md`
  - `$DESIGN_DIR/settings/traps.md`
  - `$DESIGN_DIR/settings/extensibility.md`
- Default `DESIGN_DIR` path: `/root/.openclaw/agents-extra/pinder/design`

### Downstream consumers
- `enrich.py` reads extracted YAML (must understand `blocks` format)
- `Pinder.Rules.RuleBook` loads enriched YAML (unaffected — enrichment fields are additive)
- `roundtrip_test.sh` validates round-trip fidelity
- `test_issue443_roundtrip.py` (25 unit tests) validates extraction and generation behavior

### No C# dependencies
This issue is entirely Python tooling. No changes to `Pinder.Core`, `Pinder.Rules`, or `Pinder.LlmAdapters`.
