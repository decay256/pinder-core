#!/usr/bin/env python3
"""Tests for extract.py and generate.py round-trip fidelity.

Prototype maturity: happy-path tests covering the main fix categories
(paragraph ordering, table formatting, block preservation).
"""

import os
import sys
import subprocess
import tempfile
import unittest

# Add tools directory to path
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS_DIR)

import extract
import generate
import yaml


class TestBlockOrderPreservation(unittest.TestCase):
    """Verify that blocks are extracted and regenerated in document order."""

    def _roundtrip(self, md_text):
        """Run markdown through extract → YAML → generate and return result."""
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md_text)
            f.flush()
            rules = extract.extract_rules(f.name)
        os.unlink(f.name)
        return generate.generate_markdown(rules), rules

    def test_paragraph_before_table_preserved(self):
        """Paragraphs that appear before a table stay before it."""
        md = "## Test Section\n\nFirst paragraph.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\nSecond paragraph.\n"
        result, _ = self._roundtrip(md)
        # In the result, "First paragraph" should appear before the table
        first_idx = result.index('First paragraph')
        table_idx = result.index('| A | B |')
        second_idx = result.index('Second paragraph')
        self.assertLess(first_idx, table_idx)
        self.assertLess(table_idx, second_idx)

    def test_paragraph_after_table_preserved(self):
        """Paragraphs that appear after a table stay after it."""
        md = "## Stats\n\n| Stat | Value |\n|---|---|\n| Charm | 10 |\n\nThis paragraph comes after the table.\n"
        result, _ = self._roundtrip(md)
        table_idx = result.index('| Stat | Value |')
        para_idx = result.index('This paragraph comes after the table')
        self.assertLess(table_idx, para_idx)

    def test_multiple_paragraphs_and_tables_interleaved(self):
        """Interleaved paragraphs and tables maintain order."""
        md = ("## Mixed\n\nPara one.\n\n| H1 |\n|---|\n| R1 |\n\n"
              "Para two.\n\n| H2 |\n|---|\n| R2 |\n\nPara three.\n")
        result, _ = self._roundtrip(md)
        self.assertLess(result.index('Para one'), result.index('| H1 |'))
        self.assertLess(result.index('| R1 |'), result.index('Para two'))
        self.assertLess(result.index('Para two'), result.index('| H2 |'))
        self.assertLess(result.index('| R2 |'), result.index('Para three'))


class TestTableFormatting(unittest.TestCase):
    """Verify table column widths and alignment markers are preserved."""

    def _roundtrip(self, md_text):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md_text)
            f.flush()
            rules = extract.extract_rules(f.name)
        os.unlink(f.name)
        return generate.generate_markdown(rules), rules

    def test_separator_width_preserved(self):
        """Column separator dashes match original width."""
        md = "## Table\n\n| Name | Value |\n|----------|-------------|\n| foo | bar |\n"
        result, _ = self._roundtrip(md)
        self.assertIn('|----------|-------------|', result)

    def test_alignment_markers_preserved(self):
        """Alignment markers like :---: are preserved in separator."""
        md = "## Aligned\n\n| Left | Center | Right |\n|:---|:---:|---:|\n| a | b | c |\n"
        result, _ = self._roundtrip(md)
        self.assertIn('|:---|:---:|---:|', result)

    def test_padded_table_cells_preserved(self):
        """Tables with space-padded separators get padded cells."""
        md = ("## Padded\n\n"
              "| Name      | Value                |\n"
              "| --------- | -------------------- |\n"
              "| foo       | bar                  |\n")
        result, rules = self._roundtrip(md)
        # The separator should be preserved
        self.assertIn(' --------- ', result)
        self.assertIn(' -------------------- ', result)
        # Data cells should be padded
        self.assertIn(' foo       ', result)

    def test_compact_table_not_padded(self):
        """Tables with compact separators (---) stay compact."""
        md = "## Compact\n\n| A | B |\n|---|---|\n| 1 | 2 |\n"
        result, _ = self._roundtrip(md)
        self.assertIn('| 1 | 2 |', result)


class TestHorizontalRulePreservation(unittest.TestCase):
    """Verify horizontal rules (---) survive round-trip."""

    def _roundtrip(self, md_text):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md_text)
            f.flush()
            rules = extract.extract_rules(f.name)
        os.unlink(f.name)
        return generate.generate_markdown(rules), rules

    def test_hr_preserved(self):
        """Horizontal rules appear in output."""
        md = "## Section\n\nSome content.\n\n---\n\n## Next\n\nMore content.\n"
        result, _ = self._roundtrip(md)
        self.assertIn('---', result)

    def test_hr_between_blocks(self):
        """HR between paragraphs maintains position."""
        md = "## Section\n\nBefore hr.\n\n---\n\nAfter hr.\n"
        result, _ = self._roundtrip(md)
        before_idx = result.index('Before hr')
        hr_idx = result.index('---')
        # "After hr" may or may not be in same section, but HR should be between
        self.assertLess(before_idx, hr_idx)


class TestBlockquotePreservation(unittest.TestCase):
    """Verify blockquotes preserve trailing whitespace."""

    def _roundtrip(self, md_text):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md_text)
            f.flush()
            rules = extract.extract_rules(f.name)
        os.unlink(f.name)
        return generate.generate_markdown(rules), rules

    def test_blockquote_trailing_spaces(self):
        """Trailing spaces (markdown line breaks) in blockquotes are preserved."""
        md = '## Section\n\n> Line with trailing spaces  \n> Next line\n'
        result, _ = self._roundtrip(md)
        self.assertIn('> Line with trailing spaces  ', result)

    def test_blockquote_basic(self):
        """Basic blockquote round-trips."""
        md = '## Section\n\n> This is a quote\n> Second line\n'
        result, _ = self._roundtrip(md)
        self.assertIn('> This is a quote', result)
        self.assertIn('> Second line', result)


class TestCompactHeading(unittest.TestCase):
    """Verify compact headings (no blank after) are preserved."""

    def _roundtrip(self, md_text):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md_text)
            f.flush()
            rules = extract.extract_rules(f.name)
        os.unlink(f.name)
        return generate.generate_markdown(rules), rules

    def test_compact_heading_no_blank(self):
        """Heading immediately followed by content (no blank line)."""
        md = "## Parent\n\n### Sub\nDirect content.\n"
        result, rules = self._roundtrip(md)
        # Find the sub rule
        sub_rule = next(r for r in rules if r['title'] == 'Sub')
        self.assertTrue(sub_rule.get('compact_heading', False))
        # In output, heading should be directly followed by content
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if line.startswith('### Sub'):
                # Next non-empty line should be content, not blank
                self.assertEqual(lines[i + 1], 'Direct content.')
                break

    def test_normal_heading_with_blank(self):
        """Heading with blank line after retains it."""
        md = "## Section\n\nContent after blank.\n"
        result, rules = self._roundtrip(md)
        sec_rule = next(r for r in rules if r['title'] == 'Section')
        self.assertFalse(sec_rule.get('compact_heading', False))


class TestFullRoundtrip(unittest.TestCase):
    """Integration test: round-trip all 9 design docs and verify < 50 diff lines."""

    DESIGN_DIR = '/root/.openclaw/agents-extra/pinder/design'

    def _get_docs(self):
        """Find all design markdown files."""
        docs = []
        for subdir in ['systems', 'settings']:
            path = os.path.join(self.DESIGN_DIR, subdir)
            if os.path.isdir(path):
                for f in sorted(os.listdir(path)):
                    if f.endswith('.md'):
                        docs.append(os.path.join(path, f))
        return docs

    def test_all_docs_under_50_diff_lines(self):
        """Every design doc round-trips with < 50 diff lines."""
        docs = self._get_docs()
        self.assertGreater(len(docs), 0, "No design docs found")

        failures = []
        for doc in docs:
            name = os.path.basename(doc).replace('.md', '')
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)

            # Write to temp file and diff
            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(regenerated)
                regen_path = f.name

            result = subprocess.run(
                ['diff', doc, regen_path],
                capture_output=True, text=True
            )
            os.unlink(regen_path)

            diff_lines = len(result.stdout.strip().split('\n')) if result.stdout.strip() else 0
            if diff_lines > 50:
                failures.append('{}: {} lines'.format(name, diff_lines))

        self.assertEqual(failures, [],
                         "Docs with > 50 diff lines: {}".format(', '.join(failures)))

    def test_no_information_loss(self):
        """All original headings appear in regenerated output."""
        docs = self._get_docs()
        for doc in docs:
            name = os.path.basename(doc).replace('.md', '')
            with open(doc, 'r') as f:
                original = f.read()
            rules = extract.extract_rules(doc)
            regenerated = generate.generate_markdown(rules)

            # Extract all headings from original
            import re
            orig_headings = re.findall(r'^#{1,6}\s+(.+)$', original, re.MULTILINE)
            for heading in orig_headings:
                self.assertIn(heading.strip(), regenerated,
                              '{}: heading "{}" missing from regenerated'.format(name, heading.strip()))


if __name__ == '__main__':
    unittest.main()
