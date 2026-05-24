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

class TestSemanticRoundTrip(unittest.TestCase):
    """Deterministic semantic round-trip test — no LLM required."""

    def test_no_content_lost_in_round_trip(self):
        """YAML→MD→YAML round-trip produces semantically equivalent content.

        This test is deterministic and requires no LLM or API key.
        It verifies that no actual rule content (text, values, numbers) is lost
        during the round-trip, even if formatting changes.
        """
        yaml_path = os.path.join(EXTRACTED_DIR, 'rules-v3-enriched.yaml')
        if not os.path.exists(yaml_path):
            self.skipTest("rules-v3-enriched.yaml not found")

        # Load original YAML
        with open(yaml_path, 'r', encoding='utf-8') as f:
            original_rules = yaml.safe_load(f)
        self.assertIsInstance(original_rules, list)
        self.assertGreater(len(original_rules), 0)

        # Round-trip: YAML → MD → YAML
        md_text = yaml_to_md(yaml_path)
        roundtripped_rules = md_to_rules(md_text)

        # Build lookup by title for matching (ids may differ slightly)
        orig_by_title = {}
        for r in original_rules:
            title = r.get('title', '')
            if title:
                orig_by_title[title] = r

        rt_by_title = {}
        for r in roundtripped_rules:
            title = r.get('title', '')
            if title:
                rt_by_title[title] = r

        all_losses = []

        for title, orig_rule in orig_by_title.items():
            if title not in rt_by_title:
                all_losses.append(f"MISSING rule '{title}'")
                continue

            rt_rule = rt_by_title[title]

            # Compare blocks (the primary content carrier)
            orig_blocks = orig_rule.get('blocks', [])
            rt_blocks = rt_rule.get('blocks', [])

            if orig_blocks and rt_blocks:
                losses = _find_semantic_losses(
                    orig_blocks, rt_blocks, path=f"[{title}].blocks"
                )
                if not losses:
                    continue

                # Build full text of all round-tripped blocks to check
                # for content that was restructured (e.g. consecutive
                # blockquotes merged into one) but not actually lost.
                rt_all_text = ' '.join(
                    _normalize_value(b.get('text', ''))
                    for b in rt_blocks if isinstance(b, dict)
                )

                for loss in losses:
                    # For MISSING list items, check if content appears
                    # elsewhere in the round-tripped rule
                    if 'MISSING list item' in loss:
                        m = re.search(r'\[(\d+)\]', loss.rsplit('.blocks', 1)[-1])
                        if m:
                            idx = int(m.group(1))
                            if idx < len(orig_blocks):
                                orig_text = _normalize_value(
                                    orig_blocks[idx].get('text', '')
                                )
                                if orig_text and orig_text in rt_all_text:
                                    continue  # content present, just restructured
                    # For VALUE CHANGED on .text, check if the original
                    # content appears in the round-tripped text
                    if 'VALUE CHANGED' in loss and '.text' in loss:
                        # Extract the original value snippet
                        m = re.search(r"'(.+?)'", loss)
                        if m:
                            orig_snippet = m.group(1)
                            if orig_snippet in rt_all_text:
                                continue  # content present, just rearranged
                    all_losses.append(loss)

        # Filter acceptable differences
        real_losses = [l for l in all_losses if not _is_acceptable_loss(l)]

        if real_losses:
            summary = "\n".join(real_losses[:20])
            if len(real_losses) > 20:
                summary += f"\n... and {len(real_losses) - 20} more"
            self.fail(
                f"{len(real_losses)} semantic content losses found:\n{summary}"
            )

# ###########################################################################
# Entry point
# ###########################################################################

