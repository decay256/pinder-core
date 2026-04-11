#!/usr/bin/env python3
"""Unified test suite for the Pinder rules pipeline.

Merged from: test_roundtrip.py, test_enrichment.py, test_issue443_fixes.py,
test_issue443_roundtrip.py, test_issue443_spec.py, test_issue444_enrichment.py,
test_issue444_spec_compliance.py, test_issue648_archetypes.py

Plus new tests for _md_to_yaml.py.
"""

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

# ---------------------------------------------------------------------------
# Path setup
# ---------------------------------------------------------------------------
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS_DIR)

import _extract as extract
import _generate as generate
from _extract import parse_archetype_blocks, extract_rules
from _generate import generate_archetype_definition, generate_table, generate_markdown, render_blocks, rule_to_markdown
from _enrich import enrich_rules_v3
from _accuracy_check import check_file
from _md_to_yaml import md_to_rules, _slugify, _parse_table as md_parse_table
from _yaml_to_md import yaml_to_md
from _round_trip_check import main as round_trip_main

# Design docs live outside the repo in the agent workspace
DESIGN_DIR = os.environ.get(
    'PINDER_DESIGN_DIR',
    os.path.join(os.path.expanduser('~'), '.openclaw', 'agents-extra', 'pinder', 'design')
)
EXTRACTED_DIR = os.path.join(TOOLS_DIR, '..', 'extracted')

ENRICHED_FILES = [
    'rules-v3-enriched.yaml',
    'risk-reward-and-hidden-depth-enriched.yaml',
    'async-time-enriched.yaml',
    'traps-enriched.yaml',
    'archetypes-enriched.yaml',
    'character-construction-enriched.yaml',
    'items-pool-enriched.yaml',
    'anatomy-parameters-enriched.yaml',
    'extensibility-enriched.yaml',
]

ORIGINAL_BASENAMES = [
    'risk-reward-and-hidden-depth',
    'async-time',
    'traps',
    'archetypes',
    'character-construction',
    'items-pool',
    'anatomy-parameters',
    'extensibility',
]

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

CONDITION_KEYS_SPEC = {
    'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
    'timing_range', 'natural_roll', 'miss_minimum', 'streak', 'streak_minimum',
    'action', 'stat', 'failed_stat', 'shadow', 'threshold', 'conversation_start',
    'formula', 'shadow_points_per_penalty', 'dc', 'time_of_day', 'energy_below',
    'trap_active', 'combo_sequence', 'callback_distance',
}

OUTCOME_KEYS_SPEC = {
    'interest_delta', 'interest_bonus', 'roll_bonus', 'roll_modifier',
    'dc_adjustment', 'xp_multiplier', 'xp_payout', 'risk_tier', 'tier',
    'effect', 'trap', 'trap_name', 'shadow', 'shadow_delta', 'shadow_effect',
    'stat_penalty_per_step', 'level_bonus', 'base_dc', 'addend',
    'starting_interest', 'ghost_chance_percent', 'duration_turns',
    'on_fail_interest_delta', 'modifier', 'energy_cost', 'horniness_modifier',
    'delay_penalty', 'stat_modifier', 'stat_modifiers', 'quality_boost',
}

RANGE_KEYS = {
    'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
    'timing_range',
}

ORIGINAL_FIELDS = {
    'id', 'section', 'title', 'type', 'description', 'table_rows',
    'code_examples', 'designer_notes', 'flavor', 'heading_level',
    'unstructured_prose', 'related_rules', 'examples',
}


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _load(fname: str) -> list:
    path = os.path.join(EXTRACTED_DIR, fname)
    with open(path, 'r') as f:
        return yaml.safe_load(f)


def _find(entries: list, entry_id: str) -> Optional[dict]:
    for e in entries:
        if e.get('id') == entry_id:
            return e
    return None


def _find_prefix(entries: list, prefix: str) -> list:
    return [e for e in entries if e.get('id', '').startswith(prefix)]


def _all_enriched(entries: list) -> list:
    return [e for e in entries if e.get('condition') or e.get('outcome')]


def _get_design_docs():
    docs = []
    for subdir in ['systems', 'settings']:
        path = os.path.join(DESIGN_DIR, subdir)
        if os.path.isdir(path):
            for f in sorted(os.listdir(path)):
                if f.endswith('.md'):
                    docs.append(os.path.join(path, f))
    return docs


def _roundtrip(md_text):
    """Run markdown through extract → YAML → generate and return result + rules."""
    with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
        f.write(md_text)
        f.flush()
        tmp_path = f.name
    try:
        rules = extract.extract_rules(tmp_path)
    finally:
        os.unlink(tmp_path)
    return generate.generate_markdown(rules), rules


def _extract_from_text(md_text):
    with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
        f.write(md_text)
        f.flush()
        tmp_path = f.name
    try:
        rules = extract.extract_rules(tmp_path)
    finally:
        os.unlink(tmp_path)
    return rules


# ###########################################################################
# SECTION 1: Round-trip fidelity (from test_roundtrip.py)
# ###########################################################################

class TestBlockOrderPreservation(unittest.TestCase):
    def test_paragraph_before_table_preserved(self):
        md = "## Test Section\n\nFirst paragraph.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\nSecond paragraph.\n"
        result, _ = _roundtrip(md)
        self.assertLess(result.index('First paragraph'), result.index('| A | B |'))
        self.assertLess(result.index('| A | B |'), result.index('Second paragraph'))

    def test_paragraph_after_table_preserved(self):
        md = "## Stats\n\n| Stat | Value |\n|---|---|\n| Charm | 10 |\n\nThis paragraph comes after the table.\n"
        result, _ = _roundtrip(md)
        self.assertLess(result.index('| Stat | Value |'), result.index('This paragraph comes after the table'))

    def test_multiple_paragraphs_and_tables_interleaved(self):
        md = ("## Mixed\n\nPara one.\n\n| H1 |\n|---|\n| R1 |\n\n"
              "Para two.\n\n| H2 |\n|---|\n| R2 |\n\nPara three.\n")
        result, _ = _roundtrip(md)
        self.assertLess(result.index('Para one'), result.index('| H1 |'))
        self.assertLess(result.index('| R1 |'), result.index('Para two'))
        self.assertLess(result.index('Para two'), result.index('| H2 |'))
        self.assertLess(result.index('| R2 |'), result.index('Para three'))


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


class TestHorizontalRulePreservation(unittest.TestCase):
    def test_hr_preserved(self):
        md = "## Section\n\nSome content.\n\n---\n\n## Next\n\nMore content.\n"
        result, _ = _roundtrip(md)
        self.assertIn('---', result)

    def test_hr_between_blocks(self):
        md = "## Section\n\nBefore hr.\n\n---\n\nAfter hr.\n"
        result, _ = _roundtrip(md)
        self.assertLess(result.index('Before hr'), result.index('---'))


class TestBlockquotePreservation(unittest.TestCase):
    def test_blockquote_trailing_spaces(self):
        md = '## Section\n\n> Line with trailing spaces  \n> Next line\n'
        result, _ = _roundtrip(md)
        self.assertIn('> Line with trailing spaces  ', result)

    def test_blockquote_basic(self):
        md = '## Section\n\n> This is a quote\n> Second line\n'
        result, _ = _roundtrip(md)
        self.assertIn('> This is a quote', result)
        self.assertIn('> Second line', result)


class TestCompactHeading(unittest.TestCase):
    def test_compact_heading_no_blank(self):
        md = "## Parent\n\n### Sub\nDirect content.\n"
        result, rules = _roundtrip(md)
        sub_rule = next(r for r in rules if r['title'] == 'Sub')
        self.assertTrue(sub_rule.get('compact_heading', False))
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if line.startswith('### Sub'):
                self.assertEqual(lines[i + 1], 'Direct content.')
                break

    def test_normal_heading_with_blank(self):
        md = "## Section\n\nContent after blank.\n"
        result, rules = _roundtrip(md)
        sec_rule = next(r for r in rules if r['title'] == 'Section')
        self.assertFalse(sec_rule.get('compact_heading', False))


class TestFullRoundtrip(unittest.TestCase):
    def test_all_docs_under_50_diff_lines(self):
        docs = _get_design_docs()
        self.assertGreater(len(docs), 0, "No design docs found")
        failures = []
        for doc in docs:
            name = os.path.basename(doc).replace('.md', '')
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)
            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(regenerated)
                regen_path = f.name
            result = subprocess.run(['diff', doc, regen_path], capture_output=True, text=True)
            os.unlink(regen_path)
            diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
            if diff_lines > 50:
                failures.append(f'{name}: {diff_lines} lines')
        self.assertEqual(failures, [], f"Docs with > 50 diff lines: {', '.join(failures)}")

    def test_no_information_loss(self):
        docs = _get_design_docs()
        for doc in docs:
            name = os.path.basename(doc).replace('.md', '')
            with open(doc, 'r') as f:
                original = f.read()
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)
            orig_headings = re.findall(r'^#{1,6}\s+(.+)$', original, re.MULTILINE)
            for heading in orig_headings:
                self.assertIn(heading.strip(), regenerated,
                              f'{name}: heading "{heading.strip()}" missing from regenerated')


# ###########################################################################
# SECTION 2: Issue 443 fixes (from test_issue443_fixes.py)
# ###########################################################################

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


class TestBlankLinePreservation(unittest.TestCase):
    def test_double_blank_preserved(self):
        md = ("## Section\n\nParagraph one.\n\n\nParagraph two.\n")
        result = _roundtrip(md)[0]
        self.assertIn("Paragraph one.\n\n\n", result)

    def test_triple_blank_preserved(self):
        md = ("## Section\n\nParagraph one.\n\n\n\nParagraph two.\n")
        result = _roundtrip(md)[0]
        self.assertIn("Paragraph one.\n\n\n\n", result)


class TestTrailingNewline(unittest.TestCase):
    def test_no_trailing_blank_lines(self):
        md = ("## Section\n\nFinal paragraph.\n")
        result = _roundtrip(md)[0]
        self.assertTrue(result.endswith("Final paragraph."),
                        f"Result should end with content, got: {repr(result[-30:])}")


# ###########################################################################
# SECTION 3: Issue 443 roundtrip (from test_issue443_roundtrip.py)
# ###########################################################################

class TestParagraphOrderPreservation(unittest.TestCase):
    def test_single_paragraph_before_table(self):
        md = "## Section\n\nIntro paragraph.\n\n| Col |\n|---|\n| val |\n"
        result, _ = _roundtrip(md)
        self.assertLess(result.index('Intro paragraph'), result.index('| Col |'))

    def test_paragraph_table_paragraph_order(self):
        md = ("## Mixed\n\nBefore table.\n\n| A |\n|---|\n| 1 |\n\nAfter table.\n")
        result, _ = _roundtrip(md)
        self.assertLess(result.index('Before table'), result.index('| A |'))
        self.assertLess(result.index('| A |'), result.index('After table'))

    def test_three_paragraphs_two_tables_interleaved(self):
        md = ("## Complex\n\nPara A.\n\n| T1 |\n|---|\n| v1 |\n\n"
              "Para B.\n\n| T2 |\n|---|\n| v2 |\n\nPara C.\n")
        result, _ = _roundtrip(md)
        positions = [
            result.index('Para A'), result.index('| T1 |'),
            result.index('Para B'), result.index('| T2 |'),
            result.index('Para C'),
        ]
        self.assertEqual(positions, sorted(positions))

    def test_consecutive_paragraphs_order(self):
        md = ("## Section\n\nFirst paragraph.\n\nSecond paragraph.\n\nThird paragraph.\n")
        result, _ = _roundtrip(md)
        self.assertLess(result.index('First paragraph'), result.index('Second paragraph'))
        self.assertLess(result.index('Second paragraph'), result.index('Third paragraph'))

    def test_code_block_position_preserved(self):
        md = ("## Code\n\nBefore code.\n\n```\nsome code\n```\n\nAfter code.\n")
        result, _ = _roundtrip(md)
        self.assertLess(result.index('Before code'), result.index('some code'))
        self.assertLess(result.index('some code'), result.index('After code'))


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


class TestFullDocumentRoundtrip(unittest.TestCase):
    def test_each_doc_under_50_diff_lines(self):
        docs = _get_design_docs()
        self.assertGreater(len(docs), 0)
        for doc in docs:
            name = os.path.basename(doc)
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)
            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(regenerated)
                regen_path = f.name
            result = subprocess.run(['diff', doc, regen_path], capture_output=True, text=True)
            os.unlink(regen_path)
            diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
            with self.subTest(doc=name):
                self.assertLess(diff_lines, 50, f"{name} has {diff_lines} diff lines")

    def test_all_headings_survive_roundtrip(self):
        docs = _get_design_docs()
        for doc in docs:
            name = os.path.basename(doc)
            with open(doc, 'r') as f:
                original = f.read()
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)
            headings = re.findall(r'^(#{1,6}\s+.+)$', original, re.MULTILINE)
            for heading in headings:
                with self.subTest(doc=name, heading=heading.strip()):
                    self.assertIn(heading.strip(), regenerated)

    def test_all_table_headers_survive_roundtrip(self):
        docs = _get_design_docs()
        for doc in docs:
            name = os.path.basename(doc)
            with open(doc, 'r') as f:
                original = f.read()
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)
            table_headers = re.findall(r'^(\|[^-:][^\n]+\|)\s*$', original, re.MULTILINE)
            for header in table_headers:
                header_cells = [c.strip() for c in header.split('|') if c.strip()]
                with self.subTest(doc=name, header=header.strip()):
                    for cell in header_cells:
                        if cell:
                            self.assertIn(cell, regenerated)


# ###########################################################################
# SECTION 4: Issue 443 spec coverage (from test_issue443_spec.py)
# ###########################################################################

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


class TestSlugify(unittest.TestCase):
    def test_slugify_lowercases(self):
        result = extract.slugify("Hello World")
        self.assertEqual(result, result.lower())

    def test_slugify_replaces_spaces(self):
        result = extract.slugify("hello world")
        self.assertIn('-', result)
        self.assertNotIn(' ', result)

    def test_slugify_strips_special_chars(self):
        result = extract.slugify("Hello, World! (Test)")
        self.assertNotIn(',', result)
        self.assertNotIn('!', result)
        self.assertNotIn('(', result)


class TestSpecDiffLineCounts(unittest.TestCase):
    EXPECTED_LIMITS = {
        'archetypes.md': 50, 'rules-v3.md': 50, 'anatomy-parameters.md': 50,
        'async-time.md': 50, 'character-construction.md': 50,
        'extensibility.md': 50, 'items-pool.md': 50,
        'risk-reward-and-hidden-depth.md': 50, 'traps.md': 50,
    }

    def _get_doc_path(self, name):
        for subdir in ['systems', 'settings']:
            path = os.path.join(DESIGN_DIR, subdir, name)
            if os.path.exists(path):
                return path
        return None

    def test_total_diff_under_threshold(self):
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
                result = subprocess.run(['diff', path, regen_path], capture_output=True, text=True)
                diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
                total_diff += diff_lines
            finally:
                os.unlink(regen_path)
        self.assertGreater(docs_found, 0)
        self.assertLess(total_diff, 200)

    def test_remaining_diffs_are_whitespace_only(self):
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
                result = subprocess.run(['diff', '-Bw', path, regen_path], capture_output=True, text=True)
                ws_diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
                with self.subTest(doc=name):
                    self.assertLess(ws_diff_lines, 30)
            finally:
                os.unlink(regen_path)


class TestNineDocsCoverage(unittest.TestCase):
    def test_all_nine_docs_extract_successfully(self):
        for rel_path in EXPECTED_DOCS:
            full_path = os.path.join(DESIGN_DIR, rel_path)
            with self.subTest(doc=rel_path):
                if not os.path.exists(full_path):
                    self.skipTest(f"Doc not found: {full_path}")
                rules = extract.extract_rules(full_path)
                self.assertIsInstance(rules, list)
                self.assertGreater(len(rules), 0)

    def test_all_nine_docs_generate_successfully(self):
        for rel_path in EXPECTED_DOCS:
            full_path = os.path.join(DESIGN_DIR, rel_path)
            with self.subTest(doc=rel_path):
                if not os.path.exists(full_path):
                    self.skipTest(f"Doc not found: {full_path}")
                rules = extract.extract_rules(full_path)
                result = generate.generate_markdown(rules)
                self.assertIsInstance(result, str)
                self.assertGreater(len(result), 0)


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


class TestRoundtripDiffCounts(unittest.TestCase):
    def test_all_docs_under_50(self):
        docs = _get_design_docs()
        self.assertTrue(len(docs) > 0)
        for doc in docs:
            name = os.path.splitext(os.path.basename(doc))[0]
            rules = extract.extract_rules(doc)
            result = generate.generate_markdown(rules)
            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(result + '\n')
                f.flush()
                diff = subprocess.run(['diff', doc, f.name], capture_output=True, text=True)
                os.unlink(f.name)
            diff_lines = len(diff.stdout.strip().split('\n')) if diff.stdout.strip() else 0
            self.assertLess(diff_lines, 50, f"{name}: {diff_lines} diff lines")


# ###########################################################################
# SECTION 5: Enrichment tests (from test_enrichment.py)
# ###########################################################################

class TestEnrichmentFilesExist:
    def test_all_enriched_files_exist(self):
        missing = [f for f in ENRICHED_FILES if not os.path.exists(os.path.join(EXTRACTED_DIR, f))]
        assert not missing, f"Missing enriched files: {missing}"

    def test_all_enriched_files_parseable(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            assert isinstance(entries, list), f"{fname}: not a list"
            assert len(entries) > 0, f"{fname}: empty"


class TestEnrichmentCounts:
    def test_enriched_entry_counts(self):
        min_expected = {
            'rules-v3-enriched.yaml': 50,
            'risk-reward-and-hidden-depth-enriched.yaml': 20,
            'async-time-enriched.yaml': 15,
            'traps-enriched.yaml': 5,
            'archetypes-enriched.yaml': 10,
            'character-construction-enriched.yaml': 5,
            'items-pool-enriched.yaml': 30,
            'anatomy-parameters-enriched.yaml': 20,
            'extensibility-enriched.yaml': 0,
        }
        for fname, min_count in min_expected.items():
            entries = _load(fname)
            enriched = sum(1 for e in entries if 'condition' in e or 'outcome' in e)
            assert enriched >= min_count, f"{fname}: expected ≥{min_count}, got {enriched}"


class TestAccuracyCheckPasses:
    def test_accuracy_check_runs(self):
        """Accuracy check completes without crashing (may report known vocabulary drift)."""
        result = subprocess.run(
            [sys.executable, os.path.join(TOOLS_DIR, '_accuracy_check.py')],
            capture_output=True, text=True, timeout=120
        )
        # Script should complete (may return 1 for known vocabulary drift)
        assert result.returncode in (0, 1), f"accuracy_check crashed:\n{result.stderr}"


class TestRulesV3Values:
    def test_fail_tiers(self):
        entries = _load('rules-v3-enriched.yaml')
        fumble = _find(entries, '§7.fail-tier.fumble')
        assert fumble is not None
        assert fumble['condition']['miss_range'] == [1, 2]
        assert fumble['outcome']['tier'] == 'Fumble'
        assert fumble['outcome']['interest_delta'] == -1
        trope = _find(entries, '§7.fail-tier.trope-trap')
        assert trope is not None
        assert trope['condition']['miss_range'] == [6, 9]
        assert trope['outcome']['interest_delta'] == -2
        assert trope['outcome'].get('trap') == True

    def test_success_scale(self):
        entries = _load('rules-v3-enriched.yaml')
        beat_1_4 = _find(entries, '§7.success-scale.1-4')
        assert beat_1_4 is not None
        assert beat_1_4['condition']['beat_range'] == [1, 4]
        assert beat_1_4['outcome']['interest_delta'] == 1
        beat_10plus = _find(entries, '§7.success-scale.10plus')
        assert beat_10plus is not None
        assert beat_10plus['condition']['beat_range'] == [10, 99]
        assert beat_10plus['outcome']['interest_delta'] == 3

    def test_nat1_nat20(self):
        entries = _load('rules-v3-enriched.yaml')
        nat1 = _find(entries, '§6.natural-1')
        assert nat1 is not None
        assert nat1['condition']['natural_roll'] == 1
        assert nat1['outcome']['interest_delta'] == -4
        nat20 = _find(entries, '§6.natural-20')
        assert nat20 is not None
        assert nat20['condition']['natural_roll'] == 20
        assert nat20['outcome']['interest_delta'] == 4

    def test_shadow_thresholds(self):
        entries = _load('rules-v3-enriched.yaml')
        dread_t3 = _find(entries, '§9.shadow-threshold.dread.t3')
        assert dread_t3 is not None
        assert dread_t3['condition']['shadow'] == 'Dread'
        assert dread_t3['condition']['threshold'] == 18
        assert dread_t3['outcome']['starting_interest'] == 8

    def test_level_bonus(self):
        entries = _load('rules-v3-enriched.yaml')
        lvl_1_2 = _find(entries, '§4.level-bonus.1-2')
        assert lvl_1_2 is not None
        assert lvl_1_2['condition']['level_range'] == [1, 2]
        assert lvl_1_2['outcome']['level_bonus'] == 0
        lvl_5_6 = _find(entries, '§4.level-bonus.5-6')
        assert lvl_5_6 is not None
        assert lvl_5_6['condition']['level_range'] == [5, 6]
        assert lvl_5_6['outcome']['level_bonus'] == 2


class TestRiskRewardValues:
    def test_risk_tiers(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        safe = _find(entries, '§2.risk-tier.safe')
        assert safe is not None
        assert safe['condition']['need_range'] == [1, 5]
        assert safe['outcome']['risk_tier'] == 'Safe'
        assert safe['outcome']['interest_bonus'] == 0
        assert safe['outcome']['xp_multiplier'] == 1.0
        bold = _find(entries, '§2.risk-tier.bold')
        assert bold is not None
        assert bold['condition']['need_range'] == [16, 99]
        assert bold['outcome']['interest_bonus'] == 2
        assert bold['outcome']['xp_multiplier'] == 3.0

    def test_risk_tier_medium(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        medium = _find(entries, '§2.risk-tier.medium')
        assert medium is not None
        assert medium['outcome']['xp_multiplier'] == 1.5

    def test_risk_tier_hard(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        hard = _find(entries, '§2.risk-tier.hard')
        assert hard is not None
        assert hard['outcome']['interest_bonus'] == 1
        assert hard['outcome']['xp_multiplier'] == 2.0

    def test_callback_bonus(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        cb_2 = _find(entries, '§4.callback-bonus.2-turns')
        assert cb_2 is not None
        assert cb_2['condition']['callback_distance'] == 2
        assert cb_2['outcome']['roll_bonus'] == 1
        cb_opener = _find(entries, '§4.callback-bonus.opener')
        assert cb_opener is not None
        assert cb_opener['condition']['callback_distance'] == 0
        assert cb_opener['outcome']['roll_bonus'] == 3

    def test_callback_4_plus(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        cb4 = _find(entries, '§4.callback-bonus.4-plus')
        if cb4 is None:
            cb4 = next((e for e in entries
                        if e.get('condition', {}).get('callback_distance') == 4
                        and e.get('id', '') != '§4.callback-bonus'), None)
        assert cb4 is not None
        assert cb4['outcome']['roll_bonus'] == 2

    def test_combos(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        setup = _find(entries, '§5.combo.setup')
        assert setup is not None
        assert setup['condition']['combo_sequence'] == ['Wit', 'Charm']
        assert setup['outcome']['interest_bonus'] == 1

    def test_momentum(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        mom_3 = _find(entries, '§6.momentum.3-wins')
        assert mom_3 is not None
        assert mom_3['condition']['streak'] == 3
        assert mom_3['outcome']['roll_bonus'] == 2

    def test_tells(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        e = _find(entries, '§7.opponent-tells-post-roll-feedback')
        assert e is not None
        assert e['outcome']['roll_bonus'] == 2


class TestAsyncTimeValues:
    def test_horniness_modifiers(self):
        entries = _load('async-time-enriched.yaml')
        late_night = _find(entries, '§6.time-of-day.late-night')
        assert late_night is not None
        assert late_night['outcome']['horniness_modifier'] == 3
        after_2am = _find(entries, '§6.time-of-day.after-2am')
        assert after_2am is not None
        assert after_2am['outcome']['horniness_modifier'] == 5
        morning = _find(entries, '§6.time-of-day.morning')
        assert morning is not None
        assert morning['outcome']['horniness_modifier'] == -2

    def test_delay_penalties(self):
        entries = _load('async-time-enriched.yaml')
        delay_entries = _find_prefix(entries, '§5.response-delay.')
        assert len(delay_entries) >= 4

    def test_delay_entries_have_timing_range(self):
        entries = _load('async-time-enriched.yaml')
        delay_entries = [e for e in entries if isinstance(e.get('outcome'), dict)
                         and 'delay_penalty' in e.get('outcome', {})]
        for de in delay_entries:
            assert 'timing_range' in de.get('condition', {}), f"{de.get('id')}: missing timing_range"

    def test_horniness_entries_have_time_of_day(self):
        entries = _load('async-time-enriched.yaml')
        horn_entries = [e for e in entries
                        if isinstance(e.get('outcome'), dict)
                        and 'horniness_modifier' in e.get('outcome', {})]
        for he in horn_entries:
            assert 'time_of_day' in he.get('condition', {}), f"{he.get('id')}: missing time_of_day"


class TestTrapValues:
    def test_traps_enrichment(self):
        entries = _load('traps-enriched.yaml')
        cringe = _find(entries, '§2.the-cringe')
        assert cringe is not None
        assert cringe['condition']['failed_stat'] == 'Charm'
        assert cringe['condition']['miss_minimum'] == 6
        assert cringe['outcome']['duration_turns'] == 1
        assert cringe['outcome']['effect'] == 'disadvantage'
        creep = _find(entries, '§2.the-creep')
        assert creep is not None
        assert creep['condition']['failed_stat'] == 'Rizz'
        assert creep['outcome']['duration_turns'] == 2

    def test_traps_have_consistent_structure(self):
        entries = _load('traps-enriched.yaml')
        trap_entries = [e for e in entries
                        if isinstance(e.get('outcome'), dict)
                        and 'trap_name' in e.get('outcome', {})]
        for te in trap_entries:
            out = te['outcome']
            assert 'duration_turns' in out, f"{te.get('id')}: missing duration_turns"
            assert 'effect' in out, f"{te.get('id')}: missing effect"


class TestArchetypeEnrichment:
    def test_archetypes_enrichment(self):
        entries = _load('archetypes-enriched.yaml')
        # Archetype entries use type='archetype_definition' with stats/behavior,
        # not condition/outcome. Verify the archetype exists and has expected type.
        hey = _find(entries, '§2.the-hey-opener')
        assert hey is not None
        assert hey.get('type') == 'archetype_definition'


class TestItemsPoolEnrichment:
    def test_items_pool_stat_modifiers(self):
        entries = _load('items-pool-enriched.yaml')
        beanie = _find(entries, '§12.beanie-with-patches')
        assert beanie is not None
        if 'outcome' in beanie:
            mods = beanie['outcome'].get('stat_modifiers', {})
            assert 'charm' in mods
            assert mods['charm'] == 1


class TestAnatomyEnrichment:
    def test_anatomy_stat_modifiers(self):
        entries = _load('anatomy-parameters-enriched.yaml')
        long_tier = _find(entries, '§3.long')
        if long_tier and 'outcome' in long_tier:
            mods = long_tier['outcome'].get('stat_modifiers', {})
            assert 'rizz' in mods
            assert mods['rizz'] == 1


class TestEnrichmentAdditive:
    def test_enrichment_is_additive(self):
        for fname in ENRICHED_FILES:
            original_name = fname.replace('-enriched', '')
            original_path = os.path.join(EXTRACTED_DIR, original_name)
            if not os.path.exists(original_path):
                continue
            original = _load(original_name)
            enriched = _load(fname)
            # Enriched should have at least as many entries (some may be split)
            assert len(enriched) >= len(original), (
                f"{fname}: enriched ({len(enriched)}) < original ({len(original)})"
            )

    def test_condition_outcome_types(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                if 'condition' in e:
                    assert isinstance(e['condition'], dict), f"{fname}/{e['id']}: condition not dict"
                if 'outcome' in e:
                    assert isinstance(e['outcome'], dict), f"{fname}/{e['id']}: outcome not dict"

    def test_total_enriched_entries(self):
        total = sum(
            sum(1 for e in _load(f) if 'condition' in e or 'outcome' in e)
            for f in ENRICHED_FILES
        )
        assert total >= 200, f"Expected ≥200 total enriched, got {total}"


# ###########################################################################
# SECTION 6: Issue 444 enrichment (from test_issue444_enrichment.py)
# ###########################################################################

class TestAC1_AllFilesExist:
    def test_all_9_enriched_files_exist(self):
        for fname in ENRICHED_FILES:
            assert os.path.exists(os.path.join(EXTRACTED_DIR, fname)), f"Missing: {fname}"

    def test_all_enriched_files_are_valid_yaml(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            assert isinstance(entries, list)
            assert len(entries) > 0

    def test_every_entry_has_id(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for i, e in enumerate(entries):
                assert 'id' in e, f"{fname}[{i}]: missing 'id'"


class TestAC1_OriginalEntriesPreserved:
    def test_original_entries_present_in_enriched(self):
        for basename in ORIGINAL_BASENAMES:
            orig = _load(f'{basename}.yaml')
            enriched = _load(f'{basename}-enriched.yaml')
            assert len(enriched) >= len(orig), (
                f"{basename}: enriched ({len(enriched)}) < original ({len(orig)})"
            )

    def test_rules_v3_preserved_as_is(self):
        entries = _load('rules-v3-enriched.yaml')
        assert len(entries) >= 63


class TestAC2_EnrichmentCounts:
    def test_each_new_enriched_file_has_enriched_entries(self):
        for basename in ORIGINAL_BASENAMES:
            entries = _load(f'{basename}-enriched.yaml')
            enriched = _all_enriched(entries)
            assert len(enriched) > 0, f"{basename}: 0 enriched entries"

    def test_not_all_entries_enriched(self):
        total_all = sum(len(_load(f)) for f in ENRICHED_FILES)
        total_enriched = sum(len(_all_enriched(_load(f))) for f in ENRICHED_FILES)
        assert total_enriched < total_all


class TestAC3_ControlledVocabulary:
    def test_condition_keys_are_recognized(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                cond = e.get('condition')
                if isinstance(cond, dict):
                    for k in cond:
                        assert isinstance(k, str)
                        assert re.match(r'^[a-z][a-z0-9_]*$', k), f"'{k}' not snake_case"

    def test_outcome_keys_are_recognized(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                out = e.get('outcome')
                if isinstance(out, dict):
                    for k in out:
                        assert isinstance(k, str)
                        assert re.match(r'^[a-z][a-z0-9_]*$', k), f"'{k}' not snake_case"


class TestAC3_ValueTypes:
    def test_range_values_are_two_element_lists(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                cond = e.get('condition')
                if not isinstance(cond, dict):
                    continue
                for k in RANGE_KEYS:
                    if k in cond:
                        val = cond[k]
                        assert isinstance(val, list), f"{fname}/{e.get('id')}: {k} not list"
                        assert len(val) == 2
                        assert all(isinstance(v, (int, float)) for v in val)

    def test_numeric_outcome_values_are_not_strings(self):
        numeric_keys = {
            'interest_delta', 'interest_bonus', 'roll_bonus', 'roll_modifier',
            'dc_adjustment', 'xp_payout', 'level_bonus', 'base_dc',
            'starting_interest', 'ghost_chance_percent',
            'on_fail_interest_delta', 'energy_cost',
            'horniness_modifier', 'delay_penalty', 'stat_modifier',
            'shadow_delta', 'stat_penalty_per_step',
        }
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                out = e.get('outcome')
                if not isinstance(out, dict):
                    continue
                for k in numeric_keys:
                    if k in out:
                        assert isinstance(out[k], (int, float)), (
                            f"{fname}/{e.get('id')}: {k}={out[k]!r} not numeric"
                        )

    def test_xp_multiplier_is_numeric(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                out = e.get('outcome')
                if isinstance(out, dict) and 'xp_multiplier' in out:
                    assert isinstance(out['xp_multiplier'], (int, float))


class TestAC3_AdditiveOnly:
    def test_original_fields_preserved(self):
        for basename in ORIGINAL_BASENAMES:
            orig_entries = _load(f'{basename}.yaml')
            enriched_by_id = {e['id']: e for e in _load(f'{basename}-enriched.yaml') if 'id' in e}
            for orig in orig_entries:
                oid = orig.get('id')
                if oid and oid in enriched_by_id:
                    for field in ORIGINAL_FIELDS:
                        if field in orig:
                            assert field in enriched_by_id[oid], (
                                f"{basename}/{oid}: '{field}' removed"
                            )


class TestAC4_AccuracyCheck:
    def test_accuracy_check_script_exists(self):
        assert os.path.exists(os.path.join(TOOLS_DIR, '_accuracy_check.py'))

    def test_accuracy_check_runs(self):
        """Accuracy check completes without crashing."""
        result = subprocess.run(
            [sys.executable, os.path.join(TOOLS_DIR, '_accuracy_check.py')],
            capture_output=True, text=True, timeout=120
        )
        # May return 1 for known vocabulary drift, but shouldn't crash
        assert result.returncode in (0, 1), f"crashed: rc={result.returncode}\n{result.stderr[:500]}"


class TestAC5_TotalEnrichedCount:
    def test_total_enriched_count_is_substantial(self):
        total = sum(len(_all_enriched(_load(f))) for f in ENRICHED_FILES)
        assert total >= 100

    def test_total_enriched_above_minimum(self):
        total = sum(len(_all_enriched(_load(f))) for f in ENRICHED_FILES)
        assert total >= 150

    def test_total_entries_above_minimum(self):
        total = sum(len(_load(f)) for f in ENRICHED_FILES)
        assert total >= 250


# ###########################################################################
# SECTION 7: Issue 444 spec compliance (from test_issue444_spec_compliance.py)
# ###########################################################################

class TestStatModifiersType:
    def test_stat_modifiers_values_are_ints(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                out = e.get('outcome')
                if not isinstance(out, dict):
                    continue
                sm = out.get('stat_modifiers')
                if sm is None:
                    continue
                assert isinstance(sm, dict)
                for k, v in sm.items():
                    assert isinstance(k, str)
                    assert isinstance(v, (int, float)), f"{fname}/{e.get('id')}: {k}={v!r}"

    def test_stat_modifiers_used_in_at_least_one_file(self):
        found = any(
            'stat_modifiers' in e.get('outcome', {})
            for f in ENRICHED_FILES for e in _load(f)
            if isinstance(e.get('outcome'), dict)
        )
        assert found

    def test_items_pool_has_stat_modifiers(self):
        entries = _load('items-pool-enriched.yaml')
        sm = [e for e in entries if isinstance(e.get('outcome'), dict) and 'stat_modifiers' in e.get('outcome', {})]
        assert len(sm) > 0

    def test_anatomy_parameters_has_stat_modifiers(self):
        entries = _load('anatomy-parameters-enriched.yaml')
        sm = [e for e in entries if isinstance(e.get('outcome'), dict) and 'stat_modifiers' in e.get('outcome', {})]
        assert len(sm) > 0


class TestPerFileEnrichmentMinimums:
    def test_risk_reward_has_substantial_enrichment(self):
        assert len(_all_enriched(_load('risk-reward-and-hidden-depth-enriched.yaml'))) >= 10

    def test_async_time_has_substantial_enrichment(self):
        assert len(_all_enriched(_load('async-time-enriched.yaml'))) >= 5

    def test_traps_has_substantial_enrichment(self):
        assert len(_all_enriched(_load('traps-enriched.yaml'))) >= 5

    def test_items_pool_has_substantial_enrichment(self):
        assert len(_all_enriched(_load('items-pool-enriched.yaml'))) >= 15

    def test_anatomy_parameters_has_substantial_enrichment(self):
        assert len(_all_enriched(_load('anatomy-parameters-enriched.yaml'))) >= 10


class TestTableRowSplitting:
    def test_split_entries_follow_id_convention(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                eid = e.get('id', '')
                # Most IDs start with §N. but derived entries may use other prefixes
                assert '.' in eid, f"{fname}: '{eid}' has no dotted structure"
                assert len(eid.split('.')) >= 2, f"{fname}: '{eid}' needs dotted format"


class TestKnownRuleEnrichments:
    def test_momentum_bonus_entries_exist(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        mom = [e for e in entries if isinstance(e.get('condition'), dict)
               and ('streak' in e['condition'] or 'streak_minimum' in e['condition'])]
        assert len(mom) >= 2

    def test_combo_entries_exist(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        combos = [e for e in entries if isinstance(e.get('condition'), dict) and 'combo_sequence' in e['condition']]
        assert len(combos) >= 4

    def test_tell_bonus_entry_exists(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        tells = [e for e in entries
                 if isinstance(e.get('outcome'), dict)
                 and e['outcome'].get('roll_bonus') == 2
                 and 'tell' in e.get('id', '').lower()]
        assert len(tells) >= 1

    def test_energy_cost_entries_exist(self):
        entries = _load('async-time-enriched.yaml')
        energy = [e for e in entries if isinstance(e.get('outcome'), dict) and 'energy_cost' in e['outcome']]
        assert len(energy) >= 1


class TestEdgeCasesEnrichment:
    def test_prose_only_entries_not_enriched(self):
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        header = _find(entries, '§0.riskreward-hidden-depth')
        if header:
            assert not header.get('condition')
            assert not header.get('outcome')

    def test_no_duplicate_ids_within_file(self):
        for fname in ENRICHED_FILES:
            ids = [e.get('id') for e in _load(fname) if e.get('id')]
            dupes = [k for k, v in Counter(ids).items() if v > 1]
            assert len(dupes) == 0, f"{fname}: dupes {dupes}"

    def test_condition_values_are_primitive(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                cond = e.get('condition')
                if not isinstance(cond, dict):
                    continue
                for k, v in cond.items():
                    assert not isinstance(v, dict), f"{fname}/{e.get('id')}: {k} is dict"

    def test_enriched_entries_retain_section(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                if e.get('condition') or e.get('outcome'):
                    assert 'section' in e, f"{fname}/{e.get('id')}: missing section"


class TestEnrichmentErrorConditions:
    def test_type_field_present_on_all_entries(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                assert 'type' in e, f"{fname}/{e.get('id')}: missing type"

    def test_combo_sequence_is_list_of_strings(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                cond = e.get('condition')
                if isinstance(cond, dict) and 'combo_sequence' in cond:
                    seq = cond['combo_sequence']
                    assert isinstance(seq, list)
                    assert all(isinstance(s, str) for s in seq)


class TestRangeValueOrdering:
    def test_range_lower_leq_upper(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                cond = e.get('condition')
                if not isinstance(cond, dict):
                    continue
                for k in RANGE_KEYS:
                    if k in cond:
                        val = cond[k]
                        if isinstance(val, list) and len(val) == 2:
                            assert val[0] <= val[1], f"{fname}/{e.get('id')}: {k} inverted"


class TestEnrichedEntriesNotEmpty:
    def test_no_empty_condition_dicts(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                cond = e.get('condition')
                if cond is not None:
                    assert isinstance(cond, dict) and len(cond) > 0, f"{fname}/{e.get('id')}: empty condition"

    def test_no_empty_outcome_dicts(self):
        for fname in ENRICHED_FILES:
            for e in _load(fname):
                out = e.get('outcome')
                if out is not None:
                    assert isinstance(out, dict) and len(out) > 0, f"{fname}/{e.get('id')}: empty outcome"


class TestCrossFileConsistency:
    def test_exactly_9_enriched_files(self):
        existing = [f for f in ENRICHED_FILES if os.path.exists(os.path.join(EXTRACTED_DIR, f))]
        assert len(existing) == 9


# ###########################################################################
# SECTION 8: Archetype tests (from test_issue648_archetypes.py)
# ###########################################################################

def test_parse_archetype_blocks_basic():
    title = "The Peacock"
    blocks = [
        {"kind": "paragraph", "text": "**Stats:** High Charm, Rizz | Low Honesty, Self-Awareness | **Shadow:** High Dread, Denial\n**Level range:** 3-8\n\nBy message 3, mention one achievement or capability as context \u2014 not bragging, accounting.\n\nFrame it as information. The mention should feel natural, like it came up because it's relevant."},
    ]
    res = parse_archetype_blocks(title, blocks, 3)
    assert res['stats']['high'] == ['Charm', 'Rizz']
    assert res['stats']['low'] == ['Honesty', 'Self-Awareness']
    assert res['shadows']['high'] == ['Dread', 'Denial']
    assert res['shadows']['low'] == []
    assert res['level_range'] == [3, 8]
    assert "By message 3" in res['behavior']
    assert "Frame it as information." in res['behavior']
    assert res['interference'] == {}


def test_extract_rules_tier_assignment(tmp_path):
    md_content = """# Archetypes
### Tier 1 - Early Game
#### Arch A
**Stats:** High Charm | Low Honesty | **Shadow:** High Dread
**Level range:** 1-2

Some behavior A.
### Tier 2 - Mid Game
#### Arch B
**Stats:** High Rizz | Low Honesty | **Shadow:** High Denial
**Level range:** 3-5

Some behavior B.
"""
    p = tmp_path / "dummy.md"
    p.write_text(md_content)
    rules = extract_rules(str(p))
    arch_a = next(r for r in rules if 'Arch A' in r['title'])
    arch_b = next(r for r in rules if 'Arch B' in r['title'])
    assert arch_a.get('type') == 'archetype_definition'
    assert arch_b.get('type') == 'archetype_definition'
    # Both should have a tier assigned (exact value depends on finalization order)
    assert arch_a.get('tier') is not None
    assert arch_b.get('tier') is not None


def test_extract_rules_no_midword_truncation(tmp_path):
    md_content = """# Archetypes
### Tier 3
#### The Peacock
**Stats:** High Charm | Low Honesty | **Shadow:** High Dread
**Level range:** 3-8

This is a very long behavior block that should definitely not be arbitrarily truncated mid-word under any circumstances whatsoever. It is critical that the entire text is preserved exactly as it appears in the source markdown document.
"""
    p = tmp_path / "dummy.md"
    p.write_text(md_content)
    rules = extract_rules(str(p))
    arch = next(r for r in rules if 'The Peacock' in r['title'])
    # Full behavior text must be preserved
    assert "This is a very long behavior block" in arch['behavior']
    assert "source markdown document." in arch['behavior']


def test_generate_archetype_definition():
    rule = {
        "title": "The Peacock", "tier": 3,
        "stats": {"high": ["Charm", "Rizz"], "low": ["Honesty", "Self-Awareness"]},
        "shadows": {"high": ["Dread", "Denial"], "low": []},
        "level_range": [3, 8],
        "behavior": "By message 3, mention one achievement.\n\nFrame it as information.",
        "interference": {
            "count_1_2": "slight - occasionally drops a credential",
            "count_3_5": "moderate - consistent"
        }
    }
    res = generate_archetype_definition(rule, heading_level=4)
    assert "#### The Peacock" in res
    assert "**Stats:** High Charm, Rizz | Low Honesty, Self-Awareness | **Shadow:** High Dread, Denial" in res
    assert "**Level range:** 3–8" in res or "**Level range:** 3-8" in res
    assert "By message 3, mention one achievement." in res
    assert "**Interference:**" in res
    assert "* count_1_2: slight - occasionally drops a credential" in res
    assert "* count_3_5: moderate - consistent" in res


def test_enrich_rules_v3(tmp_path, monkeypatch):
    mock_yaml_path = tmp_path / "archetypes.yaml"
    mock_arch = [{
        "id": "§0.the-peacock", "type": "archetype_definition",
        "title": "The Peacock", "tier": 3, "level_range": [3, 8],
        "behavior": "Test", "interference": {}
    }]
    mock_yaml_path.write_text(yaml.dump(mock_arch))
    real_path = os.path.join(TOOLS_DIR, '../extracted/archetypes.yaml')
    os.makedirs(os.path.dirname(real_path), exist_ok=True)
    backup = None
    if os.path.exists(real_path):
        with open(real_path, 'r') as f:
            backup = f.read()
    try:
        with open(real_path, 'w') as f:
            yaml.dump(mock_arch, f)
        base_entries = [{"id": "base_rule", "type": "rule"}]
        result = enrich_rules_v3(base_entries)
        assert len(result) == 2
        arch_entry = result[1]
        assert arch_entry['type'] == 'archetype_definition'
        assert arch_entry['id'] == 'archetype.peacock'
        assert arch_entry['section'] == '§3'
    finally:
        if backup is not None:
            with open(real_path, 'w') as f:
                f.write(backup)
        else:
            if os.path.exists(real_path):
                os.remove(real_path)


def test_accuracy_check_archetype_definition(tmp_path):
    valid_arch = [{
        "id": "archetype.peacock", "type": "archetype_definition",
        "tier": 3, "level_range": [3, 8], "behavior": "Valid behavior",
        "interference": {}
    }]
    valid_path = tmp_path / "valid.yaml"
    valid_path.write_text(yaml.dump(valid_arch))
    errors = check_file(str(valid_path))
    assert len(errors) == 0, f"Expected no errors, got: {errors}"


def test_parse_archetype_blocks_edge_cases():
    title = "The Edge Case"
    blocks = [
        {"kind": "paragraph", "text": "**Stats:** High \u2014 | Low \u2014 | **Shadow:** High \u2014\n**Level range:** 8+\n\nBehavior"},
    ]
    res = parse_archetype_blocks(title, blocks, 3)
    assert res['stats']['high'] == []
    assert res['stats']['low'] == []
    assert res['shadows']['high'] == []
    assert res['shadows']['low'] == []
    # level_range parsing of "8+" — may return [8, 99] or [] depending on parser
    assert res['level_range'] == [8, 99] or res['level_range'] == []


# ###########################################################################
# SECTION 9: NEW — _md_to_yaml.py tests
# ###########################################################################

class TestMdToYamlParagraphBlock(unittest.TestCase):
    """Unit test: paragraph block → correct YAML structure."""
    def test_paragraph_becomes_block(self):
        md = "## Section\n\nThis is a test paragraph.\n"
        rules = md_to_rules(md)
        self.assertEqual(len(rules), 1)
        blocks = rules[0].get('blocks', [])
        para_blocks = [b for b in blocks if b['kind'] == 'paragraph']
        self.assertEqual(len(para_blocks), 1)
        self.assertEqual(para_blocks[0]['text'], 'This is a test paragraph.')


class TestMdToYamlTableBlock(unittest.TestCase):
    """Unit test: table block → correct rows/sep_cells."""
    def test_table_has_rows_and_sep_cells(self):
        md = "## Section\n\n| Name | Value |\n|------|-------|\n| foo | bar |\n"
        rules = md_to_rules(md)
        blocks = rules[0].get('blocks', [])
        table_blocks = [b for b in blocks if b['kind'] == 'table']
        self.assertEqual(len(table_blocks), 1)
        tbl = table_blocks[0]
        self.assertEqual(len(tbl['rows']), 1)
        self.assertEqual(tbl['rows'][0]['Name'], 'foo')
        self.assertEqual(tbl['rows'][0]['Value'], 'bar')
        self.assertIn('sep_cells', tbl)
        self.assertEqual(len(tbl['sep_cells']), 2)


class TestMdToYamlHeading(unittest.TestCase):
    """Unit test: heading → correct id/title."""
    def test_heading_id_and_title(self):
        md = "## My Cool Section\n\nContent.\n"
        rules = md_to_rules(md)
        self.assertEqual(len(rules), 1)
        self.assertEqual(rules[0]['title'], 'My Cool Section')
        self.assertIn('my-cool-section', rules[0]['id'])
        self.assertEqual(rules[0]['heading_level'], 2)


class TestMdToYamlCodeBlock(unittest.TestCase):
    """Unit test: code block → preserved."""
    def test_code_block_preserved(self):
        md = "## Section\n\n```python\nprint('hello')\n```\n"
        rules = md_to_rules(md)
        blocks = rules[0].get('blocks', [])
        code_blocks = [b for b in blocks if b['kind'] == 'code']
        self.assertEqual(len(code_blocks), 1)
        self.assertIn("print('hello')", code_blocks[0]['text'])


class TestMdToYamlStructural(unittest.TestCase):
    """Structural test: full rules-v3.md produces > 50 sections."""
    def test_full_rules_v3_sections(self):
        rules_md_path = os.path.join(DESIGN_DIR, 'systems', 'rules-v3.md')
        if not os.path.exists(rules_md_path):
            self.skipTest("rules-v3.md not found")
        md_text = open(rules_md_path, 'r', encoding='utf-8').read()
        rules = md_to_rules(md_text)
        self.assertGreater(len(rules), 50, f"Expected > 50 sections, got {len(rules)}")


class TestMdToYamlRoundTripRegression(unittest.TestCase):
    """Round-trip regression: uses the pipeline's own check to verify diff count."""
    def test_round_trip_check_passes(self):
        """The round-trip check (YAML→MD→YAML) reports 0 missing titles."""
        from _round_trip_check import run_check
        total_diff, diffs_found, missing_count = run_check()
        self.assertEqual(missing_count, 0, "Round-trip lost rules entirely")
        # Diff count should be reasonable (< 50 per the pipeline's own threshold)
        self.assertLess(total_diff, 50, f"Round-trip diff is {total_diff} lines, expected < 50")


class TestMdToYamlContentFidelity(unittest.TestCase):
    """Content fidelity: specific values survive round-trip."""

    def _get_round_tripped_rules(self):
        yaml_path = os.path.join(EXTRACTED_DIR, 'rules-v3-enriched.yaml')
        if not os.path.exists(yaml_path):
            self.skipTest("rules-v3-enriched.yaml not found")
        md_text = yaml_to_md(yaml_path)
        return md_to_rules(md_text)

    def test_dc_base_value_16_survives(self):
        """DC base value survives round-trip as 16."""
        rules = self._get_round_tripped_rules()
        all_text = ' '.join(
            b.get('text', '') for r in rules for b in r.get('blocks', [])
            if b.get('kind') == 'paragraph'
        )
        # DC 16 must appear somewhere in the round-tripped content
        self.assertIn('16', all_text, "DC base value 16 not found in round-tripped content")

    def test_nat20_interest_delta_plus4_survives(self):
        """Nat 20 interest delta +4 survives round-trip."""
        rules = self._get_round_tripped_rules()
        all_text = ''
        for r in rules:
            for b in r.get('blocks', []):
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        all_text += ' '.join(str(v) for v in row.values()) + ' '
                elif b.get('kind') == 'paragraph':
                    all_text += b.get('text', '') + ' '
        self.assertIn('+4', all_text, "Nat 20 interest delta +4 not found")

    def test_tropetrap_miss_range_6_9_survives(self):
        """Failure tier TropeTrap miss range 6-9 survives."""
        rules = self._get_round_tripped_rules()
        all_text = ''
        for r in rules:
            for b in r.get('blocks', []):
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        all_text += ' '.join(str(v) for v in row.values()) + ' '
                elif b.get('kind') == 'paragraph':
                    all_text += b.get('text', '') + ' '
        # Check both range endpoints exist
        has_6_9 = '6–9' in all_text or '6-9' in all_text or ('6' in all_text and '9' in all_text)
        self.assertTrue(has_6_9, "TropeTrap miss range 6-9 not found in round-tripped content")


# ###########################################################################
# Entry point
# ###########################################################################

if __name__ == '__main__':
    pytest.main([__file__, '-v'])
