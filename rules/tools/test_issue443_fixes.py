#!/usr/bin/env python3
"""Tests for issue #443 specific fixes: table detection, empty cells, blank lines."""

import os
import sys
import tempfile
import unittest

# Add tools dir to path
sys.path.insert(0, os.path.dirname(__file__))

from extract import extract_rules
from generate import generate_markdown, generate_table


class TestTableDetectionFix(unittest.TestCase):
    """Lines with | that don't start with | should NOT be treated as tables."""

    def _roundtrip(self, md):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md)
            f.flush()
            rules = extract_rules(f.name)
        os.unlink(f.name)
        return generate_markdown(rules)

    def test_pipe_in_bold_text_not_table(self):
        """Lines like **Stats:** A | B | C should be paragraphs, not tables."""
        md = (
            "## Test Section\n"
            "\n"
            "**Stats:** High Rizz | Low Self-Awareness | **Shadow:** High Horniness\n"
            "**Level range:** 1-5\n"
            "\n"
            "Description paragraph here.\n"
        )
        result = self._roundtrip(md)
        # The two lines should be in a single paragraph block, not split
        self.assertIn("High Rizz | Low Self-Awareness", result)
        self.assertIn("**Level range:** 1-5", result)
        # They should be consecutive (no blank line between)
        lines = result.split('\n')
        for i, line in enumerate(lines):
            if 'High Rizz' in line:
                self.assertIn('Level range', lines[i + 1],
                              "Stats and Level range lines should be consecutive")
                break

    def test_actual_table_still_detected(self):
        """Lines starting with | should still be detected as tables."""
        md = (
            "## Test Section\n"
            "\n"
            "| Header1 | Header2 |\n"
            "|---------|--------|\n"
            "| Cell1 | Cell2 |\n"
        )
        result = self._roundtrip(md)
        self.assertIn("| Header1 | Header2 |", result)
        self.assertIn("| Cell1 | Cell2 |", result)


class TestEmptyCellFormatting(unittest.TestCase):
    """Empty table cells should render as | | not |  |."""

    def test_empty_cell_compact(self):
        block = {
            'rows': [
                {'Col1': '+6 total', 'Col2': 'DC 11'},
                {'Col1': '', 'Col2': 'Need 5+'},
            ],
        }
        result = generate_table(block)
        lines = result.split('\n')
        # Line 0=header, 1=sep, 2=first data, 3=second data (with empty cell)
        data_line = lines[3]
        # Empty cell should produce | | (single space) not |  | (double space)
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
        # Padded empty cell should be single space then padding
        self.assertIn('|', result)


class TestBlankLinePreservation(unittest.TestCase):
    """Multiple consecutive blank lines should be preserved."""

    def _roundtrip(self, md):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md)
            f.flush()
            rules = extract_rules(f.name)
        os.unlink(f.name)
        return generate_markdown(rules)

    def test_double_blank_preserved(self):
        md = (
            "## Section\n"
            "\n"
            "Paragraph one.\n"
            "\n"
            "\n"
            "Paragraph two.\n"
        )
        result = self._roundtrip(md)
        # Should have double blank between paragraphs
        self.assertIn("Paragraph one.\n\n\n", result)

    def test_triple_blank_preserved(self):
        md = (
            "## Section\n"
            "\n"
            "Paragraph one.\n"
            "\n"
            "\n"
            "\n"
            "Paragraph two.\n"
        )
        result = self._roundtrip(md)
        # Should have triple blank between paragraphs
        self.assertIn("Paragraph one.\n\n\n\n", result)


class TestTrailingNewline(unittest.TestCase):
    """Generated markdown should not have extra trailing newlines."""

    def _roundtrip(self, md):
        with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
            f.write(md)
            f.flush()
            rules = extract_rules(f.name)
        os.unlink(f.name)
        return generate_markdown(rules)

    def test_no_trailing_blank_lines(self):
        md = (
            "## Section\n"
            "\n"
            "Final paragraph.\n"
        )
        result = self._roundtrip(md)
        # Should end with content, no trailing blank lines
        self.assertTrue(result.endswith("Final paragraph."),
                        f"Result should end with content, got: {repr(result[-30:])}")


class TestRoundtripDiffCounts(unittest.TestCase):
    """Verify all 9 docs have < 50 diff lines (AC4)."""

    DESIGN_DIR = "/root/.openclaw/agents-extra/pinder/design"

    def test_all_docs_under_50(self):
        import subprocess
        docs = []
        for subdir in ['systems', 'settings']:
            d = os.path.join(self.DESIGN_DIR, subdir)
            if os.path.isdir(d):
                for f in sorted(os.listdir(d)):
                    if f.endswith('.md'):
                        docs.append(os.path.join(d, f))
        
        self.assertTrue(len(docs) > 0, "No docs found")
        
        for doc in docs:
            name = os.path.splitext(os.path.basename(doc))[0]
            rules = extract_rules(doc)
            result = generate_markdown(rules)
            
            with tempfile.NamedTemporaryFile(mode='w', suffix='.md', delete=False) as f:
                f.write(result + '\n')  # print() adds newline
                f.flush()
                diff = subprocess.run(
                    ['diff', doc, f.name],
                    capture_output=True, text=True
                )
                os.unlink(f.name)
            
            diff_lines = len(diff.stdout.strip().split('\n')) if diff.stdout.strip() else 0
            self.assertLess(diff_lines, 50,
                           f"{name}: {diff_lines} diff lines (must be < 50)")


if __name__ == '__main__':
    unittest.main()
