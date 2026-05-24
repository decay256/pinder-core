#!/usr/bin/env python3
import os
import re
import subprocess
import sys
import tempfile
import unittest
from collections import Counter
from typing import Any, Dict, List, Optional, Set

import pytest
import yaml

from _test_shared import *

class TestTableFormatting(unittest.TestCase):
    def test_separator_width_preserved(self):
        md = "## Table\n\n| Name | Value |\n|----------|-------------|\n| foo | bar |\n"
        result, _ = _roundtrip(md)
        self.assertIn('|----------|-------------|', result)

    def test_alignment_markers_preserved(self):
        md = "## Aligned\n\n| Left | Center | Right |\n|:---|:---:|---:|\n| a | b | c |\n"
        result, _ = _roundtrip(md)
        self.assertIn('|:---|:---:|---:|', result)

    def test_padded_table_cells_preserved(self):
        md = ("## Padded\n\n"
              "| Name      | Value                |\n"
              "| --------- | -------------------- |\n"
              "| foo       | bar                  |\n")
        result, _ = _roundtrip(md)
        self.assertIn(' --------- ', result)
        self.assertIn(' -------------------- ', result)
        self.assertIn(' foo       ', result)

    def test_compact_table_not_padded(self):
        md = "## Compact\n\n| A | B |\n|---|---|\n| 1 | 2 |\n"
        result, _ = _roundtrip(md)
        self.assertIn('| 1 | 2 |', result)

class TestTableDetectionFix(unittest.TestCase):
    def test_pipe_in_bold_text_not_table(self):
        md = ("## Test Section\n\n"
              "**Stats:** High Rizz | Low Self-Awareness | **Shadow:** High Horniness\n"
              "**Level range:** 1-5\n\nDescription paragraph here.\n")
        result = _roundtrip(md)[0]
        self.assertIn("High Rizz | Low Self-Awareness", result)
        self.assertIn("**Level range:** 1-5", result)
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if 'High Rizz' in line:
                self.assertIn('Level range', lines[i + 1])
                break

    def test_actual_table_still_detected(self):
        md = ("## Test Section\n\n"
              "| Header1 | Header2 |\n|---------|--------|\n| Cell1 | Cell2 |\n")
        result = _roundtrip(md)[0]
        self.assertIn("| Header1 | Header2 |", result)
        self.assertIn("| Cell1 | Cell2 |", result)

class TestEmptyCellFormatting(unittest.TestCase):
    def test_empty_cell_compact(self):
        block = {
            'rows': [
                {'Col1': '+6 total', 'Col2': 'DC 11'},
                {'Col1': '', 'Col2': 'Need 5+'},
            ],
        }
        result = generate_table(block)
        lines = result.split('\n')
        data_line = lines[3]
        self.assertTrue(data_line.startswith('| |'),
                        f"Empty cell should start with '| |', got: {data_line}")

    def test_empty_cell_padded(self):
        block = {
            'rows': [
                {'Col1': 'Header', 'Col2': 'H2'},
                {'Col1': '', 'Col2': 'data'},
            ],
            'sep_cells': [' ---------- ', ' ---- '],
        }
        result = generate_table(block)
        self.assertIn('|', result)

class TestTableColumnWidths(unittest.TestCase):
    def test_wide_separator_preserved(self):
        md = ("## Table\n\n"
              "| Name          | Description         |\n"
              "|---------------|---------------------|\n"
              "| foo           | bar                 |\n")
        result, _ = _roundtrip(md)
        self.assertIn('|---------------|---------------------|', result)

    def test_left_alignment_preserved(self):
        md = "## T\n\n| H |\n|:---|\n| v |\n"
        result, _ = _roundtrip(md)
        self.assertIn(':---', result)

    def test_center_alignment_preserved(self):
        md = "## T\n\n| H |\n|:---:|\n| v |\n"
        result, _ = _roundtrip(md)
        self.assertIn(':---:', result)

    def test_right_alignment_preserved(self):
        md = "## T\n\n| H |\n|---:|\n| v |\n"
        result, _ = _roundtrip(md)
        self.assertIn('---:', result)

    def test_wide_aligned_separator(self):
        md = "## T\n\n| Header |\n|:---------------:|\n| val |\n"
        result, _ = _roundtrip(md)
        self.assertIn(':---------------:', result)

    def test_compact_separator_stays_compact(self):
        md = "## T\n\n| A | B |\n|---|---|\n| 1 | 2 |\n"
        result, _ = _roundtrip(md)
        self.assertIn('|---|---|', result)

    def test_padded_cell_content_matches_width(self):
        md = ("## T\n\n"
              "| Name      | Value |\n"
              "| --------- | ----- |\n"
              "| foo       | bar   |\n")
        result, _ = _roundtrip(md)
        self.assertIn(' --------- ', result)
        self.assertIn(' ----- ', result)

    def test_three_column_different_widths(self):
        md = ("## T\n\n"
              "| Short | Medium Column | Very Long Column Name |\n"
              "|-------|---------------|-----------------------|\n"
              "| a     | b             | c                     |\n")
        result, _ = _roundtrip(md)
        self.assertIn('|-------|---------------|-----------------------|', result)

class TestEmptyDocuments(unittest.TestCase):
    def test_empty_file_returns_empty_rules(self):
        result, rules = _roundtrip("")
        self.assertIsInstance(rules, list)
        self.assertEqual(len(rules), 0)

    def test_no_headings_creates_preamble(self):
        md = "Just some text without any headings.\n\nAnother paragraph.\n"
        rules = _extract_from_text(md)
        self.assertGreater(len(rules), 0)
        self.assertIn('preamble', rules[0].get('id', '').lower())

    def test_preamble_preserves_content(self):
        md = "Some text.\n\nMore text.\n"
        rules = _extract_from_text(md)
        self.assertGreater(len(rules), 0)
        blocks = rules[0].get('blocks', [])
        all_text = ' '.join(b.get('text', '') for b in blocks if b.get('kind') == 'paragraph')
        self.assertIn('Some text', all_text)

class TestTablesNoDataRows(unittest.TestCase):
    def test_generate_table_empty_rows_no_crash(self):
        block = {'kind': 'table', 'rows': [], 'sep_cells': ['---', '---']}
        result = generate_table(block)
        self.assertIsInstance(result, str)

    def test_table_with_single_data_row_preserved(self):
        md = "## T\n\n| Header1 | Header2 |\n|---------|----------|\n| a | b |\n"
        result, _ = _roundtrip(md)
        self.assertIn('Header1', result)
        self.assertIn('a', result)

class TestTablesEmptyCells(unittest.TestCase):
    def test_empty_cell_preserved(self):
        md = "## T\n\n| A | B |\n|---|---|\n| val |  |\n"
        result, _ = _roundtrip(md)
        self.assertIn('val', result)
        lines = result.split('\n')
        data_lines = [l for l in lines if 'val' in l]
        self.assertGreater(len(data_lines), 0)
        self.assertGreaterEqual(data_lines[0].count('|'), 3)

    def test_empty_cell_padded(self):
        md = "## T\n\n| A | B |\n|---|---|\n|  | val |\n"
        result, _ = _roundtrip(md)
        self.assertIn('val', result)
        lines = result.split('\n')
        data_lines = [l for l in lines if 'val' in l and '|' in l]
        self.assertGreater(len(data_lines), 0)
        row = data_lines[0]
        cells = [c for c in row.split('|')]
        self.assertTrue(len(cells) >= 3)
        self.assertTrue(cells[1].strip() == '')

class TestTablesEmptyFirstHeader(unittest.TestCase):
    def test_empty_first_header_preserved(self):
        md = "## T\n\n| | Col2 | Col3 |\n|---|---|---|\n| R1 | a | b |\n"
        result, _ = _roundtrip(md)
        self.assertIn('Col2', result)
        self.assertIn('R1', result)

    def test_empty_first_header_data_intact(self):
        md = "## T\n\n| | Need 5+ |\n|---|---|\n| Charm | Easy |\n"
        _, rules = _roundtrip(md)
        blocks = rules[0].get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertGreater(len(table_blocks), 0)
        rows = table_blocks[0].get('rows', [])
        self.assertGreater(len(rows), 0)
        self.assertIn('Charm', list(rows[0].values()))

class TestCodeBlocksWithPipes(unittest.TestCase):
    def test_pipes_in_code_not_parsed_as_table(self):
        md = ("## Section\n\n```\n| this | is | not | a | table |\n|------|----|----|---|-------|\n```\n")
        result, rules = _roundtrip(md)
        self.assertIn('| this | is | not | a | table |', result)
        blocks = rules[0].get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertEqual(len(table_blocks), 0)

    def test_table_after_code_block_parsed_correctly(self):
        md = ("## Section\n\n```\n| fake | table |\n```\n\n"
              "| Real | Table |\n|------|-------|\n| a    | b     |\n")
        result, rules = _roundtrip(md)
        blocks = rules[0].get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertGreater(len(table_blocks), 0)

class TestConsecutiveTables(unittest.TestCase):
    def test_two_tables_remain_separate(self):
        md = ("## Section\n\n| A |\n|---|\n| 1 |\n\n| B |\n|---|\n| 2 |\n")
        _, rules = _roundtrip(md)
        blocks = rules[0].get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertEqual(len(table_blocks), 2)

    def test_consecutive_tables_content_intact(self):
        md = ("## Section\n\n| H1 |\n|---|\n| v1 |\n\n| H2 |\n|---|\n| v2 |\n")
        result, _ = _roundtrip(md)
        self.assertIn('v1', result)
        self.assertIn('v2', result)
        self.assertLess(result.index('v1'), result.index('v2'))

class TestBlockquotes(unittest.TestCase):
    def test_blockquote_survives_roundtrip(self):
        md = "## Section\n\n> This is a blockquote.\n> Second line.\n"
        result, _ = _roundtrip(md)
        self.assertIn('This is a blockquote', result)
        self.assertIn('Second line', result)

    def test_blockquote_block_kind(self):
        md = "## Section\n\n> Quote text.\n"
        rules = _extract_from_text(md)
        blocks = rules[0].get('blocks', [])
        bq_blocks = [b for b in blocks if b.get('kind') == 'blockquote']
        self.assertGreater(len(bq_blocks), 0)

    def test_blockquote_has_prefix_in_output(self):
        md = "## Section\n\n> Important note.\n"
        result, _ = _roundtrip(md)
        self.assertIn('>', result)
        self.assertIn('Important note', result)

class TestHorizontalRules(unittest.TestCase):
    def test_hr_outside_table_is_hr_block(self):
        md = "## Section\n\nSome text.\n\n---\n\nMore text.\n"
        rules = _extract_from_text(md)
        blocks = rules[0].get('blocks', [])
        hr_blocks = [b for b in blocks if b.get('kind') == 'hr']
        self.assertGreater(len(hr_blocks), 0)

    def test_hr_survives_roundtrip(self):
        md = "## Section\n\nBefore.\n\n---\n\nAfter.\n"
        result, _ = _roundtrip(md)
        self.assertIn('---', result)

    def test_hr_and_table_coexist(self):
        md = "## Section\n\n---\n\n| H |\n|---|\n| v |\n"
        _, rules = _roundtrip(md)
        blocks = rules[0].get('blocks', [])
        hr_blocks = [b for b in blocks if b.get('kind') == 'hr']
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertGreater(len(hr_blocks), 0)
        self.assertGreater(len(table_blocks), 0)

class TestCompactHeadings(unittest.TestCase):
    def test_compact_heading_flag_set(self):
        md = "## Section\n\n### Compact\nImmediate content.\n"
        rules = _extract_from_text(md)
        compact_rules = [r for r in rules if r.get('compact_heading') is True]
        self.assertGreater(len(compact_rules), 0)

    def test_compact_heading_no_blank_line_in_output(self):
        md = "## Parent\n\n### Compact\nDirect content.\n"
        result, _ = _roundtrip(md)
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if '### Compact' in line:
                if i + 1 < len(lines):
                    self.assertNotEqual(lines[i + 1].strip(), '')
                break

    def test_non_compact_heading_has_blank_line(self):
        md = "## Section\n\nNormal content with blank line after heading.\n"
        result, _ = _roundtrip(md)
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if '## Section' in line:
                if i + 1 < len(lines):
                    self.assertEqual(lines[i + 1].strip(), '')
                break

class TestLegacyYamlFallback(unittest.TestCase):
    def test_rule_without_blocks_renders_description(self):
        legacy_rule = {
            'id': '§1.test', 'section': '§1', 'title': 'Legacy Rule',
            'type': 'definition', 'description': 'This is a legacy description.',
        }
        result = rule_to_markdown(legacy_rule, heading_level=2)
        self.assertIn('Legacy Rule', result)
        self.assertIn('This is a legacy description', result)

    def test_rule_without_blocks_renders_table_rows(self):
        legacy_rule = {
            'id': '§1.test', 'section': '§1', 'title': 'Legacy Table',
            'type': 'table', 'description': 'Intro.', 'table_rows': [{'Col': 'val'}],
        }
        result = rule_to_markdown(legacy_rule, heading_level=2)
        self.assertIn('Legacy Table', result)
        self.assertIn('Col', result)

class TestMixedContentFiveBlocks(unittest.TestCase):
    def test_five_block_types_in_order(self):
        md = ("## Complex\n\nFirst paragraph.\n\n| H |\n|---|\n| v |\n\n"
              "Middle paragraph.\n\n```\nsome code\n```\n\nFinal paragraph.\n")
        result, _ = _roundtrip(md)
        positions = [
            result.index('First paragraph'), result.index('| H |'),
            result.index('Middle paragraph'), result.index('some code'),
            result.index('Final paragraph'),
        ]
        self.assertEqual(positions, sorted(positions))

    def test_five_blocks_all_present(self):
        md = ("## Complex\n\nFirst paragraph.\n\n| H |\n|---|\n| v |\n\n"
              "Middle paragraph.\n\n```\nsome code\n```\n\nFinal paragraph.\n")
        _, rules = _roundtrip(md)
        blocks = rules[0].get('blocks', [])
        self.assertGreaterEqual(len(blocks), 5)

class TestErrorConditions(unittest.TestCase):
    def test_extract_file_not_found(self):
        with self.assertRaises(FileNotFoundError):
            extract.extract_rules('/nonexistent/path/to/file.md')

    def test_generate_table_missing_sep_cells(self):
        block = {'kind': 'table', 'rows': [{'A': '1', 'B': '2'}]}
        result = generate_table(block)
        self.assertIn('---', result)
        self.assertIn('A', result)

    def test_generate_table_empty_rows(self):
        block = {'kind': 'table', 'rows': []}
        result = generate_table(block)
        self.assertIsInstance(result, str)

    def test_generate_markdown_empty_rules(self):
        result = generate_markdown([])
        self.assertIsInstance(result, str)

class TestParseTableSepCells(unittest.TestCase):
    def test_sep_cells_preserve_spaces(self):
        lines = [
            "| Stat          | Defence       |",
            "| ------------- | ------------- |",
            "| Charm         | SA            |",
        ]
        rows, sep_cells, fallback = extract.parse_table(lines)
        self.assertIsNone(fallback)
        self.assertIsNotNone(sep_cells)
        for cell in sep_cells:
            self.assertIn('-', cell)

    def test_sep_cells_preserve_alignment(self):
        lines = [
            "| Left | Center | Right |",
            "|:-----|:------:|------:|",
            "| a    | b      | c     |",
        ]
        rows, sep_cells, fallback = extract.parse_table(lines)
        self.assertIsNone(fallback)
        sep_str = '|'.join(sep_cells)
        self.assertIn(':---', sep_str)
        self.assertIn('---:', sep_str)

    def test_parse_table_returns_correct_rows(self):
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

class TestRenderBlocks(unittest.TestCase):
    def test_render_paragraph_block(self):
        blocks = [{'kind': 'paragraph', 'text': 'Hello world.'}]
        lines = render_blocks(blocks)
        self.assertIn('Hello world', '\n'.join(lines))

    def test_render_code_block(self):
        blocks = [{'kind': 'code', 'text': '```\nprint("hi")\n```'}]
        lines = render_blocks(blocks)
        self.assertIn('print("hi")', '\n'.join(lines))

    def test_render_blocks_separates_with_blank_lines(self):
        blocks = [
            {'kind': 'paragraph', 'text': 'First.'},
            {'kind': 'paragraph', 'text': 'Second.'},
        ]
        text = '\n'.join(render_blocks(blocks))
        self.assertIn('First.', text)
        self.assertIn('Second.', text)
        between = text[text.index('First.') + len('First.'):text.index('Second.')]
        self.assertIn('\n\n', between)

class TestFlavorBlocks(unittest.TestCase):
    def test_italic_lines_detected_as_flavor(self):
        md = "## Section\n\n*This is flavor text.*\n\nNormal paragraph.\n"
        rules = _extract_from_text(md)
        blocks = rules[0].get('blocks', [])
        all_text = ' '.join(b.get('text', '') for b in blocks)
        self.assertIn('flavor text', all_text.lower())

class TestGenerateTableFormatting(unittest.TestCase):
    def test_custom_sep_cells_used(self):
        block = {'kind': 'table', 'rows': [{'Header': 'val'}], 'sep_cells': ['---------------']}
        result = generate_table(block)
        self.assertIn('---------------', result)

    def test_output_has_pipe_delimiters(self):
        block = {'kind': 'table', 'rows': [{'A': '1', 'B': '2'}], 'sep_cells': ['---', '---']}
        result = generate_table(block)
        self.assertIn('|', result)
        lines = [l for l in result.split('\n') if l.strip()]
        self.assertGreaterEqual(len(lines), 3)

class TestExtractBlockOrdering(unittest.TestCase):
    def test_blocks_are_ordered_list(self):
        md = "## Section\n\nParagraph one.\n\n| T |\n|---|\n| v |\n\nParagraph two.\n"
        _, rules = _roundtrip(md)
        rule = next(r for r in rules if r.get('title') == 'Section')
        blocks = rule.get('blocks', [])
        self.assertIsInstance(blocks, list)
        self.assertGreaterEqual(len(blocks), 3)

    def test_blocks_have_kind_field(self):
        md = "## Section\n\nSome text.\n\n| H |\n|---|\n| V |\n"
        _, rules = _roundtrip(md)
        rule = next(r for r in rules if r.get('title') == 'Section')
        for block in rule.get('blocks', []):
            self.assertIn('kind', block)

    def test_table_block_contains_rows(self):
        md = "## Section\n\n| Name | Val |\n|---|---|\n| a | b |\n"
        _, rules = _roundtrip(md)
        rule = next(r for r in rules if r.get('title') == 'Section')
        table_blocks = [b for b in rule.get('blocks', []) if b.get('kind') == 'table']
        self.assertGreater(len(table_blocks), 0)

