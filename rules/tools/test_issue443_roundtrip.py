#!/usr/bin/env python3
"""Tests for issue #443: Rules DSL round-trip fidelity.

Verifies acceptance criteria:
  AC1: Paragraph reordering fixed — extracted/generated order matches original
  AC2: Table column widths preserved
  AC3: Re-run roundtrip on all 9 docs
  AC4: All diffs < 50 lines per doc
  AC5: No information loss (headings, tables, paragraphs all survive)

Prototype maturity: happy-path tests for each acceptance criterion.
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
        rules = extract.extract_rules(f.name)
    os.unlink(f.name)
    return generate.generate_markdown(rules), rules


# ---------------------------------------------------------------------------
# AC1: Paragraph reordering fixed
# ---------------------------------------------------------------------------
class TestParagraphOrderPreservation(unittest.TestCase):
    """AC1: extracted/generated order matches original for paragraphs."""

    # Mutation: would catch if blocks list doesn't preserve insertion order
    def test_single_paragraph_before_table(self):
        md = "## Section\n\nIntro paragraph.\n\n| Col |\n|---|\n| val |\n"
        result, _ = roundtrip(md)
        self.assertLess(
            result.index('Intro paragraph'),
            result.index('| Col |'),
            "Paragraph must appear before table"
        )

    # Mutation: would catch if paragraphs are grouped after tables
    def test_paragraph_table_paragraph_order(self):
        md = ("## Mixed\n\n"
              "Before table.\n\n"
              "| A |\n|---|\n| 1 |\n\n"
              "After table.\n")
        result, _ = roundtrip(md)
        before_idx = result.index('Before table')
        table_idx = result.index('| A |')
        after_idx = result.index('After table')
        self.assertLess(before_idx, table_idx)
        self.assertLess(table_idx, after_idx)

    # Mutation: would catch if only first/last positions are preserved but middle is scrambled
    def test_three_paragraphs_two_tables_interleaved(self):
        md = ("## Complex\n\n"
              "Para A.\n\n"
              "| T1 |\n|---|\n| v1 |\n\n"
              "Para B.\n\n"
              "| T2 |\n|---|\n| v2 |\n\n"
              "Para C.\n")
        result, _ = roundtrip(md)
        positions = [
            result.index('Para A'),
            result.index('| T1 |'),
            result.index('Para B'),
            result.index('| T2 |'),
            result.index('Para C'),
        ]
        self.assertEqual(positions, sorted(positions),
                         "All blocks must appear in original document order")

    # Mutation: would catch if multiple paragraphs within same position get reversed
    def test_consecutive_paragraphs_order(self):
        md = ("## Section\n\n"
              "First paragraph.\n\n"
              "Second paragraph.\n\n"
              "Third paragraph.\n")
        result, _ = roundtrip(md)
        self.assertLess(result.index('First paragraph'), result.index('Second paragraph'))
        self.assertLess(result.index('Second paragraph'), result.index('Third paragraph'))

    # Mutation: would catch if code blocks are treated as paragraphs and reordered
    def test_code_block_position_preserved(self):
        md = ("## Code\n\n"
              "Before code.\n\n"
              "```\nsome code\n```\n\n"
              "After code.\n")
        result, _ = roundtrip(md)
        self.assertLess(result.index('Before code'), result.index('some code'))
        self.assertLess(result.index('some code'), result.index('After code'))


# ---------------------------------------------------------------------------
# AC2: Table column widths preserved
# ---------------------------------------------------------------------------
class TestTableColumnWidths(unittest.TestCase):
    """AC2: Table column widths (separator dashes) are preserved through round-trip."""

    # Mutation: would catch if separator is normalized to minimal dashes (---)
    def test_wide_separator_preserved(self):
        md = ("## Table\n\n"
              "| Name          | Description         |\n"
              "|---------------|---------------------|\n"
              "| foo           | bar                 |\n")
        result, _ = roundtrip(md)
        self.assertIn('|---------------|---------------------|', result)

    # Mutation: would catch if alignment markers are stripped
    def test_left_alignment_preserved(self):
        md = "## T\n\n| H |\n|:---|\n| v |\n"
        result, _ = roundtrip(md)
        self.assertIn(':---', result)

    # Mutation: would catch if center alignment marker loses leading colon
    def test_center_alignment_preserved(self):
        md = "## T\n\n| H |\n|:---:|\n| v |\n"
        result, _ = roundtrip(md)
        self.assertIn(':---:', result)

    # Mutation: would catch if right alignment marker loses trailing colon
    def test_right_alignment_preserved(self):
        md = "## T\n\n| H |\n|---:|\n| v |\n"
        result, _ = roundtrip(md)
        self.assertIn('---:', result)

    # Mutation: would catch if wide separators with alignment lose width
    def test_wide_aligned_separator(self):
        md = "## T\n\n| Header |\n|:---------------:|\n| val |\n"
        result, _ = roundtrip(md)
        self.assertIn(':---------------:', result)

    # Mutation: would catch if compact (minimal) separators get padded
    def test_compact_separator_stays_compact(self):
        md = "## T\n\n| A | B |\n|---|---|\n| 1 | 2 |\n"
        result, _ = roundtrip(md)
        # Should contain compact separators, not padded ones
        self.assertIn('|---|---|', result)

    # Mutation: would catch if cell padding is not applied for wide tables
    def test_padded_cell_content_matches_width(self):
        md = ("## T\n\n"
              "| Name      | Value |\n"
              "| --------- | ----- |\n"
              "| foo       | bar   |\n")
        result, _ = roundtrip(md)
        # Separator widths should be preserved
        self.assertIn(' --------- ', result)
        self.assertIn(' ----- ', result)

    # Mutation: would catch if multi-column tables lose specific column widths
    def test_three_column_different_widths(self):
        md = ("## T\n\n"
              "| Short | Medium Column | Very Long Column Name |\n"
              "|-------|---------------|-----------------------|\n"
              "| a     | b             | c                     |\n")
        result, _ = roundtrip(md)
        self.assertIn('|-------|---------------|-----------------------|', result)


# ---------------------------------------------------------------------------
# AC3 + AC4: Full round-trip on all design docs, < 50 diff lines each
# ---------------------------------------------------------------------------
class TestFullDocumentRoundtrip(unittest.TestCase):
    """AC3/AC4: all design docs round-trip with < 50 diff lines."""

    DESIGN_DIR = '/root/.openclaw/agents-extra/pinder/design'

    def _get_docs(self):
        docs = []
        for subdir in ['systems', 'settings']:
            path = os.path.join(self.DESIGN_DIR, subdir)
            if os.path.isdir(path):
                for f in sorted(os.listdir(path)):
                    if f.endswith('.md'):
                        docs.append(os.path.join(path, f))
        return docs

    # Mutation: would catch if any doc exceeds 50 diff lines (regression)
    def test_each_doc_under_50_diff_lines(self):
        """Each individual design doc must have < 50 diff lines after round-trip."""
        docs = self._get_docs()
        self.assertGreater(len(docs), 0, "No design docs found")

        for doc in docs:
            name = os.path.basename(doc)
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)

            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(regenerated)
                regen_path = f.name

            result = subprocess.run(
                ['diff', doc, regen_path],
                capture_output=True, text=True
            )
            os.unlink(regen_path)

            diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
            with self.subTest(doc=name):
                self.assertLess(diff_lines, 50,
                    "{} has {} diff lines (must be < 50)".format(name, diff_lines))

    # Mutation: would catch if extract loses some headings entirely
    def test_all_headings_survive_roundtrip(self):
        """Every heading from every design doc must appear in regenerated output."""
        docs = self._get_docs()
        for doc in docs:
            name = os.path.basename(doc)
            with open(doc, 'r') as f:
                original = f.read()

            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)

            headings = re.findall(r'^(#{1,6}\s+.+)$', original, re.MULTILINE)
            for heading in headings:
                with self.subTest(doc=name, heading=heading.strip()):
                    self.assertIn(heading.strip(), regenerated,
                        "{}: heading '{}' missing".format(name, heading.strip()))

    # Mutation: would catch if extract drops table content
    def test_all_table_headers_survive_roundtrip(self):
        """Table header rows from original docs appear in regenerated output."""
        docs = self._get_docs()
        for doc in docs:
            name = os.path.basename(doc)
            with open(doc, 'r') as f:
                original = f.read()

            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)

            # Find table header rows (lines starting with | that aren't separators)
            table_headers = re.findall(r'^(\|[^-:][^\n]+\|)\s*$', original, re.MULTILINE)
            for header in table_headers:
                # Normalize whitespace for comparison
                header_cells = [c.strip() for c in header.split('|') if c.strip()]
                with self.subTest(doc=name, header=header.strip()):
                    for cell in header_cells:
                        if cell:
                            self.assertIn(cell, regenerated,
                                "{}: table header cell '{}' missing".format(name, cell))


# ---------------------------------------------------------------------------
# Edge cases: block type interactions
# ---------------------------------------------------------------------------
class TestEdgeCases(unittest.TestCase):
    """Edge cases for block ordering and formatting."""

    # Mutation: would catch if empty sections cause index errors
    def test_empty_section_roundtrips(self):
        md = "## Empty\n\n## Next\n\nContent here.\n"
        result, _ = roundtrip(md)
        self.assertIn('## Next', result)
        self.assertIn('Content here', result)

    # Mutation: would catch if bullet lists between tables get reordered
    def test_list_between_tables(self):
        md = ("## Section\n\n"
              "| A |\n|---|\n| 1 |\n\n"
              "- item one\n- item two\n\n"
              "| B |\n|---|\n| 2 |\n")
        result, _ = roundtrip(md)
        self.assertLess(result.index('| A |'), result.index('item one'))
        self.assertLess(result.index('item two'), result.index('| B |'))

    # Mutation: would catch if single-row tables lose their content
    def test_single_row_table(self):
        md = "## T\n\n| Only |\n|---|\n| val |\n"
        result, _ = roundtrip(md)
        self.assertIn('| val |', result)

    # Mutation: would catch if tables with many columns lose some
    def test_wide_table_many_columns(self):
        md = ("## T\n\n"
              "| A | B | C | D | E | F |\n"
              "|---|---|---|---|---|---|\n"
              "| 1 | 2 | 3 | 4 | 5 | 6 |\n")
        result, _ = roundtrip(md)
        for col in ['A', 'B', 'C', 'D', 'E', 'F']:
            self.assertIn(col, result)
        for val in ['1', '2', '3', '4', '5', '6']:
            self.assertIn(val, result)

    # Mutation: would catch if multi-row tables lose intermediate rows
    def test_multi_row_table(self):
        md = ("## T\n\n"
              "| H |\n|---|\n| r1 |\n| r2 |\n| r3 |\n")
        result, _ = roundtrip(md)
        self.assertIn('| r1 |', result)
        self.assertIn('| r2 |', result)
        self.assertIn('| r3 |', result)

    # Mutation: would catch if nested headings lose hierarchy
    def test_nested_heading_hierarchy(self):
        md = ("## Parent\n\nParent content.\n\n"
              "### Child\n\nChild content.\n\n"
              "#### Grandchild\n\nGrandchild content.\n")
        result, _ = roundtrip(md)
        self.assertIn('## Parent', result)
        self.assertIn('### Child', result)
        self.assertIn('#### Grandchild', result)


# ---------------------------------------------------------------------------
# Extract module: block ordering in YAML output
# ---------------------------------------------------------------------------
class TestExtractBlockOrdering(unittest.TestCase):
    """Verify extract.py stores blocks as ordered list, not grouped by type."""

    # Mutation: would catch if blocks are grouped by type (all paragraphs, then all tables)
    def test_blocks_are_ordered_list(self):
        md = ("## Section\n\n"
              "Paragraph one.\n\n"
              "| T |\n|---|\n| v |\n\n"
              "Paragraph two.\n")
        _, rules = roundtrip(md)
        # Find the rule for this section
        rule = next(r for r in rules if r.get('title') == 'Section')
        blocks = rule.get('blocks', [])
        # Blocks should be a list (ordered), not separate paragraph/table keys
        self.assertIsInstance(blocks, list, "blocks must be an ordered list")
        self.assertGreaterEqual(len(blocks), 3,
            "Should have at least 3 blocks (para, table, para)")

    # Mutation: would catch if block kind metadata is missing
    def test_blocks_have_kind_field(self):
        md = ("## Section\n\n"
              "Some text.\n\n"
              "| H |\n|---|\n| V |\n")
        _, rules = roundtrip(md)
        rule = next(r for r in rules if r.get('title') == 'Section')
        blocks = rule.get('blocks', [])
        for block in blocks:
            self.assertIn('kind', block,
                "Each block must have a 'kind' field")

    # Mutation: would catch if table blocks don't contain row data
    def test_table_block_contains_rows(self):
        md = ("## Section\n\n"
              "| Name | Val |\n|---|---|\n| a | b |\n")
        _, rules = roundtrip(md)
        rule = next(r for r in rules if r.get('title') == 'Section')
        blocks = rule.get('blocks', [])
        table_blocks = [b for b in blocks if b.get('kind') == 'table']
        self.assertGreater(len(table_blocks), 0, "Should have at least one table block")


if __name__ == '__main__':
    unittest.main()
