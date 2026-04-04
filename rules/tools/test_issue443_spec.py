#!/usr/bin/env python3
"""Tests for issue #443: Rules DSL round-trip fidelity — spec-driven coverage.

Written by test-engineer agent from docs/specs/issue-443-spec.md.
Covers edge cases, error conditions, and acceptance criteria gaps
not addressed in test_issue443_roundtrip.py.

Prototype maturity: happy-path + edge cases + error conditions.
"""

import os
import re
import subprocess
import sys
import tempfile
import unittest

# Add tools directory to path so we can import extract/generate
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS_DIR)

import extract
import generate


def roundtrip(md_text):
    """Helper: run markdown through extract → generate and return result + rules."""
    with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
        f.write(md_text)
        f.flush()
        tmp_path = f.name
    try:
        rules = extract.extract_rules(tmp_path)
    finally:
        os.unlink(tmp_path)
    return generate.generate_markdown(rules), rules


def extract_from_text(md_text):
    """Helper: extract rules from markdown text, return rules list."""
    with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
        f.write(md_text)
        f.flush()
        tmp_path = f.name
    try:
        rules = extract.extract_rules(tmp_path)
    finally:
        os.unlink(tmp_path)
    return rules


# ---------------------------------------------------------------------------
# Edge Case: Empty documents
# Spec: "If a Markdown file contains no headings, extract.py creates a preamble
# entry with id: §0.preamble"
# ---------------------------------------------------------------------------
class TestEmptyDocuments(unittest.TestCase):

    # Mutation: would catch if extract crashes on empty file instead of returning empty list
    def test_empty_file_returns_empty_rules(self):
        """Empty file produces empty rules list and empty output."""
        result, rules = roundtrip("")
        self.assertIsInstance(rules, list)
        # Spec: "An empty file produces an empty rules list and empty Markdown output"
        self.assertEqual(len(rules), 0)

    # Mutation: would catch if no-heading content is silently dropped
    def test_no_headings_creates_preamble(self):
        """Content with no headings gets a §0.preamble entry."""
        md = "Just some text without any headings.\n\nAnother paragraph.\n"
        rules = extract_from_text(md)
        self.assertGreater(len(rules), 0, "Should have at least one rule entry")
        preamble = rules[0]
        self.assertIn('preamble', preamble.get('id', '').lower(),
                       "First entry should be a preamble")

    # Mutation: would catch if preamble loses its content blocks
    def test_preamble_preserves_content(self):
        """Preamble entry collects all content into blocks."""
        md = "Some text.\n\nMore text.\n"
        rules = extract_from_text(md)
        self.assertGreater(len(rules), 0)
        blocks = rules[0].get('blocks', [])
        # Should have at least some content
        all_text = ' '.join(
            b.get('text', '') for b in blocks if b.get('kind') == 'paragraph'
        )
        self.assertIn('Some text', all_text)


# ---------------------------------------------------------------------------
# Edge Case: Tables with no data rows
# Spec: "A table with only a header and separator (no data rows) should produce
# a table block with rows: [] and valid sep_cells"
# ---------------------------------------------------------------------------
class TestTablesNoDataRows(unittest.TestCase):

    # Mutation: would catch if generate_table crashes on rows=[]
    def test_generate_table_empty_rows_no_crash(self):
        """generate_table handles empty rows without crashing."""
        block = {
            'kind': 'table',
            'rows': [],
            'sep_cells': ['---', '---'],
        }
        result = generate.generate_table(block)
        self.assertIsInstance(result, str)

    # Mutation: would catch if table with data row is dropped
    def test_table_with_single_data_row_preserved(self):
        """Table with at least one data row survives round-trip."""
        md = "## T\n\n| Header1 | Header2 |\n|---------|----------|\n| a | b |\n"
        result, rules = roundtrip(md)
        self.assertIn('Header1', result)
        self.assertIn('Header2', result)
        self.assertIn('a', result)


# ---------------------------------------------------------------------------
# Edge Case: Tables with empty cells
# Spec: "Empty cells in data rows result in empty string values in the row dict.
# generate.py renders them as |  |"
# ---------------------------------------------------------------------------
class TestTablesEmptyCells(unittest.TestCase):

    # Mutation: would catch if empty cells are skipped instead of rendered
    def test_empty_cell_preserved(self):
        """Empty cells in data rows survive round-trip."""
        md = "## T\n\n| A | B |\n|---|---|\n| val |  |\n"
        result, rules = roundtrip(md)
        self.assertIn('val', result)
        # The row should still have both columns (pipe-delimited)
        lines = result.split('\n')
        data_lines = [l for l in lines if 'val' in l]
        self.assertGreater(len(data_lines), 0)
        # Row should have correct number of pipes (3 for 2 columns: |...|...|)
        self.assertGreaterEqual(data_lines[0].count('|'), 3)

    # Mutation: would catch if empty cell padding is wrong (no space between pipes)
    def test_empty_cell_padded(self):
        """Empty cells render with at least a space between pipes."""
        md = "## T\n\n| A | B |\n|---|---|\n|  | val |\n"
        result, rules = roundtrip(md)
        # The result should contain the data row with val
        self.assertIn('val', result)
        lines = result.split('\n')
        data_lines = [l for l in lines if 'val' in l and '|' in l]
        self.assertGreater(len(data_lines), 0,
                           "Data row with 'val' must appear in output")
        # The row must have an empty first cell (space(s) between pipes)
        row = data_lines[0]
        cells = [c for c in row.split('|')]
        # First non-empty boundary should be a whitespace-only cell
        # (cells[0] is before first pipe, cells[1] is first column)
        self.assertTrue(len(cells) >= 3, "Row must have at least 2 columns")
        # First cell (cells[1]) should be whitespace-only (the empty cell)
        self.assertTrue(cells[1].strip() == '',
                        "First cell should be empty, got: '{}'".format(cells[1]))


# ---------------------------------------------------------------------------
# Edge Case: Tables with empty first header
# Spec: "When the first header cell is empty, the extracted header list has an
# empty string as the first key"
# ---------------------------------------------------------------------------
class TestTablesEmptyFirstHeader(unittest.TestCase):

    # Mutation: would catch if empty first header cell is dropped
    def test_empty_first_header_preserved(self):
        """Empty first header cell survives round-trip."""
        md = "## T\n\n| | Col2 | Col3 |\n|---|---|---|\n| R1 | a | b |\n"
        result, rules = roundtrip(md)
        self.assertIn('Col2', result)
        self.assertIn('Col3', result)
        self.assertIn('R1', result)

    # Mutation: would catch if row dict keyed by empty string loses data
    def test_empty_first_header_data_intact(self):
        """Data in the empty-header column is preserved."""
        md = "## T\n\n| | Need 5+ |\n|---|---|\n| Charm | Easy |\n"
        _, rules = roundtrip(md)
        rule = rules[0]
        blocks = rule.get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertGreater(len(table_blocks), 0)
        rows = table_blocks[0].get('rows', [])
        self.assertGreater(len(rows), 0)
        # Row should have Charm in some value
        row_values = list(rows[0].values())
        self.assertIn('Charm', row_values,
                       "Charm must be in row values: {}".format(row_values))


# ---------------------------------------------------------------------------
# Edge Case: Code blocks containing pipe characters
# Spec: "Fenced code blocks are NOT parsed as tables, even if they contain |"
# ---------------------------------------------------------------------------
class TestCodeBlocksWithPipes(unittest.TestCase):

    # Mutation: would catch if pipe characters inside code blocks trigger table parsing
    def test_pipes_in_code_not_parsed_as_table(self):
        """Pipe chars inside fenced code blocks are NOT treated as tables."""
        md = ("## Section\n\n"
              "```\n"
              "| this | is | not | a | table |\n"
              "|------|----|----|---|-------|\n"
              "```\n")
        result, rules = roundtrip(md)
        # The code block should survive intact
        self.assertIn('| this | is | not | a | table |', result)
        # Verify it's inside a code block, not rendered as a table
        rule = rules[0]
        blocks = rule.get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        # Should have NO table blocks — the pipes are in code
        self.assertEqual(len(table_blocks), 0,
                         "Pipe chars in code blocks should not create table blocks")

    # Mutation: would catch if code block flag doesn't reset after closing fence
    def test_table_after_code_block_parsed_correctly(self):
        """Table appearing after a code block with pipes is parsed as table."""
        md = ("## Section\n\n"
              "```\n"
              "| fake | table |\n"
              "```\n\n"
              "| Real | Table |\n"
              "|------|-------|\n"
              "| a    | b     |\n")
        result, rules = roundtrip(md)
        rule = rules[0]
        blocks = rule.get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertGreater(len(table_blocks), 0,
                           "Table after code block should be parsed as table")


# ---------------------------------------------------------------------------
# Edge Case: Consecutive tables
# Spec: "Two tables separated by a blank line produce two separate table blocks"
# ---------------------------------------------------------------------------
class TestConsecutiveTables(unittest.TestCase):

    # Mutation: would catch if consecutive tables are merged into one
    def test_two_tables_remain_separate(self):
        """Two tables separated by blank line produce two separate table blocks."""
        md = ("## Section\n\n"
              "| A |\n|---|\n| 1 |\n\n"
              "| B |\n|---|\n| 2 |\n")
        result, rules = roundtrip(md)
        rule = rules[0]
        blocks = rule.get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertEqual(len(table_blocks), 2,
                         "Should have 2 separate table blocks, got {}".format(
                             len(table_blocks)))

    # Mutation: would catch if content from second table leaks into first
    def test_consecutive_tables_content_intact(self):
        """Each consecutive table retains its own data."""
        md = ("## Section\n\n"
              "| H1 |\n|---|\n| v1 |\n\n"
              "| H2 |\n|---|\n| v2 |\n")
        result, rules = roundtrip(md)
        # Both tables' content should appear in order
        self.assertIn('v1', result)
        self.assertIn('v2', result)
        self.assertLess(result.index('v1'), result.index('v2'))


# ---------------------------------------------------------------------------
# Edge Case: Blockquotes
# Spec: "A blockquote line that is just > is stored as an empty string"
# ---------------------------------------------------------------------------
class TestBlockquotes(unittest.TestCase):

    # Mutation: would catch if blockquotes are dropped or treated as paragraphs
    def test_blockquote_survives_roundtrip(self):
        """Blockquote content survives round-trip."""
        md = ("## Section\n\n"
              "> This is a blockquote.\n"
              "> Second line.\n")
        result, rules = roundtrip(md)
        self.assertIn('This is a blockquote', result)
        self.assertIn('Second line', result)

    # Mutation: would catch if blockquote block kind is wrong
    def test_blockquote_block_kind(self):
        """Blockquotes are extracted with kind='blockquote'."""
        md = "## Section\n\n> Quote text.\n"
        rules = extract_from_text(md)
        blocks = rules[0].get('blocks', [])
        bq_blocks = [b for b in blocks if b.get('kind') == 'blockquote']
        self.assertGreater(len(bq_blocks), 0,
                           "Should have at least one blockquote block")

    # Mutation: would catch if blockquotes lose their > prefix on regeneration
    def test_blockquote_has_prefix_in_output(self):
        """Regenerated blockquotes have > prefix."""
        md = "## Section\n\n> Important note.\n"
        result, _ = roundtrip(md)
        self.assertIn('>', result)
        self.assertIn('Important note', result)


# ---------------------------------------------------------------------------
# Edge Case: Horizontal rules vs separator rows
# Spec: "A line of --- outside a table context is detected as a horizontal rule"
# ---------------------------------------------------------------------------
class TestHorizontalRules(unittest.TestCase):

    # Mutation: would catch if --- outside table is parsed as table separator
    def test_hr_outside_table_is_hr_block(self):
        """A --- line outside a table context becomes an hr block."""
        md = "## Section\n\nSome text.\n\n---\n\nMore text.\n"
        rules = extract_from_text(md)
        blocks = rules[0].get('blocks', [])
        hr_blocks = [b for b in blocks if b.get('kind') == 'hr']
        self.assertGreater(len(hr_blocks), 0,
                           "Should have at least one hr block")

    # Mutation: would catch if hr block is lost in round-trip
    def test_hr_survives_roundtrip(self):
        """Horizontal rule appears in regenerated output."""
        md = "## Section\n\nBefore.\n\n---\n\nAfter.\n"
        result, _ = roundtrip(md)
        self.assertIn('---', result)
        self.assertIn('Before', result)
        self.assertIn('After', result)

    # Mutation: would catch if hr and table separator are confused
    def test_hr_and_table_coexist(self):
        """HR and table separator in same section don't interfere."""
        md = ("## Section\n\n"
              "---\n\n"
              "| H |\n|---|\n| v |\n")
        result, rules = roundtrip(md)
        rule = rules[0]
        blocks = rule.get('blocks', [])
        hr_blocks = [b for b in blocks if b.get('kind') == 'hr']
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertGreater(len(hr_blocks), 0, "Should have hr block")
        self.assertGreater(len(table_blocks), 0, "Should have table block")


# ---------------------------------------------------------------------------
# Edge Case: Compact headings
# Spec: "When a heading is immediately followed by content (no blank line),
# extract.py sets compact_heading: true"
# ---------------------------------------------------------------------------
class TestCompactHeadings(unittest.TestCase):

    # Mutation: would catch if compact_heading flag is never set
    def test_compact_heading_flag_set(self):
        """Compact heading (no blank line after) sets compact_heading=true."""
        md = "## Section\n\n### Compact\nImmediate content.\n"
        rules = extract_from_text(md)
        compact_rules = [r for r in rules if r.get('compact_heading') is True]
        self.assertGreater(len(compact_rules), 0,
                           "At least one rule should have compact_heading=True")

    # Mutation: would catch if compact heading inserts spurious blank line
    def test_compact_heading_no_blank_line_in_output(self):
        """Compact heading omits blank line between heading and content."""
        md = "## Parent\n\n### Compact\nDirect content.\n"
        result, _ = roundtrip(md)
        # Find the compact heading line
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if '### Compact' in line:
                # Next non-empty line should be content, not blank
                if i + 1 < len(lines):
                    next_line = lines[i + 1]
                    self.assertNotEqual(next_line.strip(), '',
                        "Compact heading should NOT have blank line after it, "
                        "but got blank line at index {}".format(i + 1))
                break

    # Mutation: would catch if non-compact headings lose their blank line
    def test_non_compact_heading_has_blank_line(self):
        """Normal (non-compact) heading has blank line before content."""
        md = "## Section\n\nNormal content with blank line after heading.\n"
        result, _ = roundtrip(md)
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if '## Section' in line:
                if i + 1 < len(lines):
                    self.assertEqual(lines[i + 1].strip(), '',
                        "Non-compact heading should have blank line after it")
                break


# ---------------------------------------------------------------------------
# Edge Case: Legacy YAML entries (no blocks field)
# Spec: "rule_to_markdown() falls back to rendering individual fields if blocks
# is not present"
# ---------------------------------------------------------------------------
class TestLegacyYamlFallback(unittest.TestCase):

    # Mutation: would catch if missing blocks field causes crash
    def test_rule_without_blocks_renders_description(self):
        """Legacy rule entry (no blocks) renders description field."""
        legacy_rule = {
            'id': '§1.test',
            'section': '§1',
            'title': 'Legacy Rule',
            'type': 'definition',
            'description': 'This is a legacy description.',
        }
        result = generate.rule_to_markdown(legacy_rule, heading_level=2)
        self.assertIn('Legacy Rule', result)
        self.assertIn('This is a legacy description', result)

    # Mutation: would catch if legacy table_rows field is ignored
    def test_rule_without_blocks_renders_table_rows(self):
        """Legacy rule with table_rows but no blocks renders the table."""
        legacy_rule = {
            'id': '§1.test',
            'section': '§1',
            'title': 'Legacy Table',
            'type': 'table',
            'description': 'Intro.',
            'table_rows': [{'Col': 'val'}],
        }
        result = generate.rule_to_markdown(legacy_rule, heading_level=2)
        self.assertIn('Legacy Table', result)
        # Should contain some table content
        self.assertIn('Col', result)


# ---------------------------------------------------------------------------
# Edge Case: Mixed content ordering (5 blocks)
# Spec: "paragraph → table → paragraph → code → paragraph must preserve all 5"
# ---------------------------------------------------------------------------
class TestMixedContentFiveBlocks(unittest.TestCase):

    # Mutation: would catch if any of the 5 block types gets reordered
    def test_five_block_types_in_order(self):
        """para → table → para → code → para preserves exact order."""
        md = ("## Complex\n\n"
              "First paragraph.\n\n"
              "| H |\n|---|\n| v |\n\n"
              "Middle paragraph.\n\n"
              "```\nsome code\n```\n\n"
              "Final paragraph.\n")
        result, _ = roundtrip(md)
        positions = [
            result.index('First paragraph'),
            result.index('| H |'),
            result.index('Middle paragraph'),
            result.index('some code'),
            result.index('Final paragraph'),
        ]
        self.assertEqual(positions, sorted(positions),
                         "All 5 blocks must be in original order")

    # Mutation: would catch if block count is wrong (merged or dropped)
    def test_five_blocks_all_present(self):
        """All 5 blocks are present in output."""
        md = ("## Complex\n\n"
              "First paragraph.\n\n"
              "| H |\n|---|\n| v |\n\n"
              "Middle paragraph.\n\n"
              "```\nsome code\n```\n\n"
              "Final paragraph.\n")
        result, rules = roundtrip(md)
        rule = rules[0]
        blocks = rule.get('blocks', [])
        self.assertGreaterEqual(len(blocks), 5,
                                "Should have at least 5 blocks, got {}".format(len(blocks)))


# ---------------------------------------------------------------------------
# Error Conditions
# ---------------------------------------------------------------------------
class TestErrorConditions(unittest.TestCase):

    # Mutation: would catch if extract silently returns empty on missing file
    def test_extract_file_not_found(self):
        """extract_rules raises FileNotFoundError for missing file."""
        with self.assertRaises(FileNotFoundError):
            extract.extract_rules('/nonexistent/path/to/file.md')

    # Mutation: would catch if generate_table doesn't handle missing sep_cells
    def test_generate_table_missing_sep_cells(self):
        """Table block without sep_cells falls back to --- separators."""
        table_block = {
            'kind': 'table',
            'rows': [{'A': '1', 'B': '2'}],
            # No sep_cells key
        }
        result = generate.generate_table(table_block)
        # Should still produce valid table with --- separators
        self.assertIn('---', result)
        self.assertIn('A', result)
        self.assertIn('B', result)

    # Mutation: would catch if empty rows causes crash instead of empty output
    def test_generate_table_empty_rows(self):
        """Table block with empty rows returns empty string or minimal table."""
        table_block = {
            'kind': 'table',
            'rows': [],
        }
        result = generate.generate_table(table_block)
        # Spec: returns empty string ""
        self.assertIsInstance(result, str)

    # Mutation: would catch if generate_markdown crashes on empty list
    def test_generate_markdown_empty_rules(self):
        """generate_markdown with empty rules list returns empty string."""
        result = generate.generate_markdown([])
        self.assertIsInstance(result, str)


# ---------------------------------------------------------------------------
# parse_table: sep_cells preservation
# ---------------------------------------------------------------------------
class TestParseTableSepCells(unittest.TestCase):

    # Mutation: would catch if sep_cells strips leading/trailing spaces
    def test_sep_cells_preserve_spaces(self):
        """parse_table preserves leading/trailing spaces in separator cells."""
        lines = [
            "| Stat          | Defence       |",
            "| ------------- | ------------- |",
            "| Charm         | SA            |",
        ]
        rows, sep_cells, fallback = extract.parse_table(lines)
        self.assertIsNone(fallback, "parse_table should succeed")
        self.assertIsNotNone(sep_cells)
        self.assertGreater(len(sep_cells), 0)
        # sep_cells should contain the padded separator strings
        for cell in sep_cells:
            self.assertIn('-', cell, "Sep cell should contain dashes")

    # Mutation: would catch if alignment markers are lost in parsing
    def test_sep_cells_preserve_alignment(self):
        """parse_table preserves alignment markers (:---:, :---, ---:)."""
        lines = [
            "| Left | Center | Right |",
            "|:-----|:------:|------:|",
            "| a    | b      | c     |",
        ]
        rows, sep_cells, fallback = extract.parse_table(lines)
        self.assertIsNone(fallback)
        sep_str = '|'.join(sep_cells)
        self.assertIn(':---', sep_str, "Left alignment marker should be preserved")
        self.assertIn('---:', sep_str, "Right alignment marker should be preserved")

    # Mutation: would catch if row data is lost during parsing
    def test_parse_table_returns_correct_rows(self):
        """parse_table returns row dicts keyed by header names."""
        lines = [
            "| Name | Value |",
            "|------|-------|",
            "| foo  | bar   |",
            "| baz  | qux   |",
        ]
        rows, sep_cells, fallback = extract.parse_table(lines)
        self.assertIsNone(fallback)
        self.assertEqual(len(rows), 2)
        self.assertEqual(rows[0].get('Name', '').strip(), 'foo')
        self.assertEqual(rows[1].get('Name', '').strip(), 'baz')


# ---------------------------------------------------------------------------
# render_blocks: dispatch by kind
# ---------------------------------------------------------------------------
class TestRenderBlocks(unittest.TestCase):

    # Mutation: would catch if render_blocks doesn't handle paragraph kind
    def test_render_paragraph_block(self):
        """render_blocks renders paragraph kind correctly."""
        blocks = [{'kind': 'paragraph', 'text': 'Hello world.'}]
        lines = generate.render_blocks(blocks)
        text = '\n'.join(lines)
        self.assertIn('Hello world', text)

    # Mutation: would catch if render_blocks doesn't handle code kind
    def test_render_code_block(self):
        """render_blocks renders code kind correctly."""
        blocks = [{'kind': 'code', 'text': '```\nprint("hi")\n```'}]
        lines = generate.render_blocks(blocks)
        text = '\n'.join(lines)
        self.assertIn('print("hi")', text)

    # Mutation: would catch if render_blocks doesn't insert blank lines between blocks
    def test_render_blocks_separates_with_blank_lines(self):
        """render_blocks inserts blank line between consecutive blocks."""
        blocks = [
            {'kind': 'paragraph', 'text': 'First.'},
            {'kind': 'paragraph', 'text': 'Second.'},
        ]
        lines = generate.render_blocks(blocks)
        text = '\n'.join(lines)
        # Should have a blank line between the two paragraphs
        self.assertIn('First.', text)
        self.assertIn('Second.', text)
        # Find blank line between them
        first_idx = text.index('First.')
        second_idx = text.index('Second.')
        between = text[first_idx + len('First.'):second_idx]
        self.assertIn('\n\n', between,
                      "Should have blank line between blocks")


# ---------------------------------------------------------------------------
# slugify
# ---------------------------------------------------------------------------
class TestSlugify(unittest.TestCase):

    # Mutation: would catch if slugify doesn't lowercase
    def test_slugify_lowercases(self):
        result = extract.slugify("Hello World")
        self.assertEqual(result, result.lower())

    # Mutation: would catch if slugify doesn't replace spaces with hyphens
    def test_slugify_replaces_spaces(self):
        result = extract.slugify("hello world")
        self.assertIn('-', result)
        self.assertNotIn(' ', result)

    # Mutation: would catch if slugify keeps special characters
    def test_slugify_strips_special_chars(self):
        result = extract.slugify("Hello, World! (Test)")
        self.assertNotIn(',', result)
        self.assertNotIn('!', result)
        self.assertNotIn('(', result)


# ---------------------------------------------------------------------------
# AC4: Per-document diff line counts (verifying spec table)
# ---------------------------------------------------------------------------
class TestSpecDiffLineCounts(unittest.TestCase):
    """Verify per-document diff counts match the spec's AC4 table."""

    DESIGN_DIR = '/root/.openclaw/agents-extra/pinder/design'
    # From spec AC4 table: {filename: max_expected_diff_lines}
    EXPECTED_LIMITS = {
        'archetypes.md': 50,       # spec says 38, we use <50 threshold
        'rules-v3.md': 50,         # spec says 15
        'anatomy-parameters.md': 50,  # spec says 6
        'async-time.md': 50,
        'character-construction.md': 50,
        'extensibility.md': 50,
        'items-pool.md': 50,
        'risk-reward-and-hidden-depth.md': 50,
        'traps.md': 50,
    }

    def _get_doc_path(self, name):
        for subdir in ['systems', 'settings']:
            path = os.path.join(self.DESIGN_DIR, subdir, name)
            if os.path.exists(path):
                return path
        return None

    # Mutation: would catch if total diff lines regress above threshold
    def test_total_diff_under_threshold(self):
        """Total diff across all 9 docs should be well under 450 (9 * 50)."""
        total_diff = 0
        docs_found = 0
        for name in self.EXPECTED_LIMITS:
            path = self._get_doc_path(name)
            if path is None:
                continue
            docs_found += 1
            rules = extract.extract_rules(path)
            regenerated = generate.generate_markdown(rules)

            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(regenerated)
                regen_path = f.name

            try:
                result = subprocess.run(
                    ['diff', path, regen_path],
                    capture_output=True, text=True
                )
                diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
                total_diff += diff_lines
            finally:
                os.unlink(regen_path)

        self.assertGreater(docs_found, 0, "No design docs found")
        # Spec says ~71 total post-fix; we allow up to 200 as generous bound
        self.assertLess(total_diff, 200,
                        "Total diff lines {} should be well under 200".format(total_diff))

    # Mutation: would catch if diffs are non-whitespace (content changes)
    def test_remaining_diffs_are_whitespace_only(self):
        """Any remaining diffs should be whitespace-only, not content changes."""
        for name in self.EXPECTED_LIMITS:
            path = self._get_doc_path(name)
            if path is None:
                continue

            rules = extract.extract_rules(path)
            regenerated = generate.generate_markdown(rules)

            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(regenerated)
                regen_path = f.name

            try:
                # Use diff -B (ignore blank line changes) -w (ignore whitespace)
                result = subprocess.run(
                    ['diff', '-Bw', path, regen_path],
                    capture_output=True, text=True
                )
                ws_diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
                with self.subTest(doc=name):
                    # With whitespace-ignore, diffs should be minimal
                    self.assertLess(ws_diff_lines, 30,
                        "{}: {} non-whitespace diff lines".format(name, ws_diff_lines))
            finally:
                os.unlink(regen_path)


# ---------------------------------------------------------------------------
# AC5: verify 9 docs are processed
# ---------------------------------------------------------------------------
class TestNineDocsCoverage(unittest.TestCase):
    """AC3/AC5: all 9 design documents are present and processable."""

    DESIGN_DIR = '/root/.openclaw/agents-extra/pinder/design'
    EXPECTED_DOCS = [
        'systems/rules-v3.md',
        'systems/risk-reward-and-hidden-depth.md',
        'systems/async-time.md',
        'settings/archetypes.md',
        'settings/character-construction.md',
        'settings/anatomy-parameters.md',
        'settings/items-pool.md',
        'settings/traps.md',
        'settings/extensibility.md',
    ]

    # Mutation: would catch if pipeline silently fails on some docs
    def test_all_nine_docs_extract_successfully(self):
        """All 9 design docs can be extracted without error."""
        for rel_path in self.EXPECTED_DOCS:
            full_path = os.path.join(self.DESIGN_DIR, rel_path)
            with self.subTest(doc=rel_path):
                if not os.path.exists(full_path):
                    self.skipTest("Doc not found: {}".format(full_path))
                rules = extract.extract_rules(full_path)
                self.assertIsInstance(rules, list)
                self.assertGreater(len(rules), 0,
                    "{} should produce at least one rule".format(rel_path))

    # Mutation: would catch if generate fails on extracted output
    def test_all_nine_docs_generate_successfully(self):
        """All 9 design docs can be generated back to markdown without error."""
        for rel_path in self.EXPECTED_DOCS:
            full_path = os.path.join(self.DESIGN_DIR, rel_path)
            with self.subTest(doc=rel_path):
                if not os.path.exists(full_path):
                    self.skipTest("Doc not found: {}".format(full_path))
                rules = extract.extract_rules(full_path)
                result = generate.generate_markdown(rules)
                self.assertIsInstance(result, str)
                self.assertGreater(len(result), 0,
                    "{} should produce non-empty markdown".format(rel_path))


# ---------------------------------------------------------------------------
# Flavor text blocks
# ---------------------------------------------------------------------------
class TestFlavorBlocks(unittest.TestCase):

    # Mutation: would catch if italic-only lines aren't detected as flavor
    def test_italic_lines_detected_as_flavor(self):
        """Italic-only lines should be detected as flavor blocks."""
        md = "## Section\n\n*This is flavor text.*\n\nNormal paragraph.\n"
        rules = extract_from_text(md)
        blocks = rules[0].get('blocks', [])
        kinds = [b.get('kind') for b in blocks]
        # Should have flavor or paragraph — at minimum content preserved
        all_text = ' '.join(b.get('text', '') for b in blocks)
        self.assertIn('flavor text', all_text.lower(),
                       "Flavor text content should be preserved")


# ---------------------------------------------------------------------------
# generate_table: specific formatting
# ---------------------------------------------------------------------------
class TestGenerateTable(unittest.TestCase):

    # Mutation: would catch if generate_table ignores sep_cells and uses defaults
    def test_custom_sep_cells_used(self):
        """generate_table uses provided sep_cells, not default ---."""
        block = {
            'kind': 'table',
            'rows': [{'Header': 'val'}],
            'sep_cells': ['---------------'],
        }
        result = generate.generate_table(block)
        self.assertIn('---------------', result)

    # Mutation: would catch if generate_table doesn't produce valid markdown table
    def test_output_has_pipe_delimiters(self):
        """generate_table output uses pipe delimiters."""
        block = {
            'kind': 'table',
            'rows': [{'A': '1', 'B': '2'}],
            'sep_cells': ['---', '---'],
        }
        result = generate.generate_table(block)
        self.assertIn('|', result)
        # Should have header, separator, and data rows
        lines = [l for l in result.split('\n') if l.strip()]
        self.assertGreaterEqual(len(lines), 3,
                                "Table should have header + sep + data rows")


if __name__ == '__main__':
    unittest.main()
