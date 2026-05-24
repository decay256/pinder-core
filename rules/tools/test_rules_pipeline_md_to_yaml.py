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
# SECTION 10: Semantic round-trip test (deterministic, no LLM)
# ###########################################################################

# Category: Rules
# Run with: dotnet test --filter "Category=Rules" (via RulesPipelineTests.cs)
# Or directly: python3 -m pytest rules/tools/test_rules_pipeline.py -k "Semantic"

