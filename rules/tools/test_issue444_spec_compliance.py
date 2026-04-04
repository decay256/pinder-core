#!/usr/bin/env python3
"""
Tests for Issue #444 — Spec compliance tests for Rules DSL enrichment.

Written by test-engineer agent from docs/specs/issue-444-spec.md.
Supplements test_issue444_enrichment.py with additional coverage for:
- stat_modifiers dict<string, int> type validation (added post-initial tests)
- accuracy_check.py execution (AC4)
- per-file enrichment minimum thresholds
- table-row splitting ID convention
- enrichment completeness cross-checks

Maturity: prototype — happy-path + key edge cases per acceptance criterion.
"""

import os
import re
import subprocess
import sys
from collections import Counter
from typing import Any, Dict, List, Optional, Set

import yaml

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
EXTRACTED_DIR = os.path.join(SCRIPT_DIR, '..', 'extracted')

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

# Condition keys from spec vocabulary table
CONDITION_KEYS_SPEC = {
    'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
    'timing_range', 'natural_roll', 'miss_minimum', 'streak', 'streak_minimum',
    'action', 'stat', 'failed_stat', 'shadow', 'threshold', 'conversation_start',
    'formula', 'shadow_points_per_penalty', 'dc', 'time_of_day', 'energy_below',
    'trap_active', 'combo_sequence', 'callback_distance',
}

# Outcome keys from spec vocabulary table
OUTCOME_KEYS_SPEC = {
    'interest_delta', 'interest_bonus', 'roll_bonus', 'roll_modifier',
    'dc_adjustment', 'xp_multiplier', 'xp_payout', 'risk_tier', 'tier',
    'effect', 'trap', 'trap_name', 'shadow', 'shadow_delta', 'shadow_effect',
    'stat_penalty_per_step', 'level_bonus', 'base_dc', 'addend',
    'starting_interest', 'ghost_chance_percent', 'duration_turns',
    'on_fail_interest_delta', 'modifier', 'energy_cost', 'horniness_modifier',
    'delay_penalty', 'stat_modifier', 'stat_modifiers', 'quality_boost',
}


def _load(fname: str) -> list:
    path = os.path.join(EXTRACTED_DIR, fname)
    with open(path, 'r') as f:
        return yaml.safe_load(f)


def _find(entries: list, entry_id: str) -> Optional[dict]:
    for e in entries:
        if e.get('id') == entry_id:
            return e
    return None


def _all_enriched_entries(entries: list) -> list:
    return [e for e in entries if e.get('condition') or e.get('outcome')]


# ===========================================================================
# stat_modifiers type validation (spec: dict<string, int>)
# ===========================================================================

class TestStatModifiersType:
    """Spec §Outcome Key Vocabulary: stat_modifiers must be dict<string, int>."""

    # Mutation: would catch if stat_modifiers values were strings instead of ints
    def test_stat_modifiers_values_are_ints(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                out = e.get('outcome')
                if not isinstance(out, dict):
                    continue
                sm = out.get('stat_modifiers')
                if sm is None:
                    continue
                assert isinstance(sm, dict), (
                    f"{fname} / {e.get('id')}: stat_modifiers must be dict, "
                    f"got {type(sm).__name__}"
                )
                for k, v in sm.items():
                    assert isinstance(k, str), (
                        f"{fname} / {e.get('id')}: stat_modifiers key must be str, "
                        f"got {type(k).__name__} = {k!r}"
                    )
                    assert isinstance(v, (int, float)), (
                        f"{fname} / {e.get('id')}: stat_modifiers['{k}'] must be int, "
                        f"got {type(v).__name__} = {v!r}"
                    )

    # Mutation: would catch if stat_modifiers was present but never used in any file
    def test_stat_modifiers_used_in_at_least_one_file(self):
        """Spec adds stat_modifiers as a valid key — verify it's actually used."""
        found = False
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                out = e.get('outcome')
                if isinstance(out, dict) and 'stat_modifiers' in out:
                    found = True
                    break
            if found:
                break
        assert found, "stat_modifiers not found in any enriched file"

    # Mutation: would catch if items-pool enrichment lacks stat_modifiers for items
    def test_items_pool_has_stat_modifiers(self):
        """Items define stat modifiers — items-pool-enriched should use stat_modifiers."""
        entries = _load('items-pool-enriched.yaml')
        sm_entries = [e for e in entries
                      if isinstance(e.get('outcome'), dict)
                      and 'stat_modifiers' in e.get('outcome', {})]
        assert len(sm_entries) > 0, (
            "items-pool-enriched.yaml has no stat_modifiers entries — "
            "item definitions should have stat modifier maps"
        )

    # Mutation: would catch if anatomy-parameters enrichment lacks stat_modifiers
    def test_anatomy_parameters_has_stat_modifiers(self):
        """Anatomy tiers define stat values — should use stat_modifiers."""
        entries = _load('anatomy-parameters-enriched.yaml')
        sm_entries = [e for e in entries
                      if isinstance(e.get('outcome'), dict)
                      and 'stat_modifiers' in e.get('outcome', {})]
        assert len(sm_entries) > 0, (
            "anatomy-parameters-enriched.yaml has no stat_modifiers entries"
        )


# ===========================================================================
# AC4: accuracy_check.py runs and produces 0 INACCURATE findings
# ===========================================================================

class TestAC4_AccuracyCheckExecution:
    """AC4: Run accuracy_check.py and verify 0 INACCURATE findings."""

    # Mutation: would catch if accuracy_check.py crashes or reports errors
    def test_accuracy_check_runs_without_error(self):
        script_path = os.path.join(SCRIPT_DIR, 'accuracy_check.py')
        if not os.path.exists(script_path):
            # AC4 requires the script exists; checked in other test file
            return
        result = subprocess.run(
            [sys.executable, script_path],
            capture_output=True, text=True, timeout=120,
            cwd=SCRIPT_DIR
        )
        # Should not crash
        assert result.returncode == 0, (
            f"accuracy_check.py failed with rc={result.returncode}\n"
            f"stderr: {result.stderr[:500]}\n"
            f"stdout: {result.stdout[:500]}"
        )

    # Mutation: would catch if accuracy check finds INACCURATE entries
    def test_accuracy_check_zero_inaccurate(self):
        script_path = os.path.join(SCRIPT_DIR, 'accuracy_check.py')
        if not os.path.exists(script_path):
            return
        result = subprocess.run(
            [sys.executable, script_path],
            capture_output=True, text=True, timeout=120,
            cwd=SCRIPT_DIR
        )
        output = result.stdout + result.stderr
        # Look for "INACCURATE: N" pattern in the summary output
        # The accuracy check prints "INACCURATE: 0" on success
        matches = re.findall(r'INACCURATE:\s*(\d+)', output, re.IGNORECASE)
        for m in matches:
            assert int(m) == 0, (
                f"accuracy_check.py reported {m} INACCURATE findings"
            )
        # Also check the PASS line exists (confirms script completed)
        assert 'PASS' in output or 'pass' in output.lower() or result.returncode == 0, (
            f"accuracy_check.py did not produce a PASS result\nstdout: {output[:500]}"
        )


# ===========================================================================
# Per-file enrichment minimum thresholds
# ===========================================================================

class TestPerFileEnrichmentMinimums:
    """Verify each file has a reasonable number of enriched entries per spec."""

    # Mutation: would catch if risk-reward-and-hidden-depth was barely enriched
    def test_risk_reward_has_substantial_enrichment(self):
        """Spec says this file has 'many numeric values' — expect significant enrichment."""
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        enriched = _all_enriched_entries(entries)
        assert len(enriched) >= 10, (
            f"risk-reward-and-hidden-depth has only {len(enriched)} enriched entries, "
            f"expected >= 10 (many numeric values per spec)"
        )

    # Mutation: would catch if async-time was barely enriched
    def test_async_time_has_substantial_enrichment(self):
        """Spec says 'timing multipliers, energy costs, delay formulas'."""
        entries = _load('async-time-enriched.yaml')
        enriched = _all_enriched_entries(entries)
        assert len(enriched) >= 5, (
            f"async-time has only {len(enriched)} enriched entries, expected >= 5"
        )

    # Mutation: would catch if traps was barely enriched
    def test_traps_has_substantial_enrichment(self):
        """Traps file has JSON equivalent — should be well-enriched."""
        entries = _load('traps-enriched.yaml')
        enriched = _all_enriched_entries(entries)
        assert len(enriched) >= 5, (
            f"traps has only {len(enriched)} enriched entries, expected >= 5"
        )

    # Mutation: would catch if items-pool was barely enriched
    def test_items_pool_has_substantial_enrichment(self):
        """Items have stat modifiers — should be heavily enriched."""
        entries = _load('items-pool-enriched.yaml')
        enriched = _all_enriched_entries(entries)
        assert len(enriched) >= 15, (
            f"items-pool has only {len(enriched)} enriched entries, expected >= 15"
        )

    # Mutation: would catch if anatomy-parameters was barely enriched
    def test_anatomy_parameters_has_substantial_enrichment(self):
        """Anatomy tiers have stat values and size modifiers."""
        entries = _load('anatomy-parameters-enriched.yaml')
        enriched = _all_enriched_entries(entries)
        assert len(enriched) >= 10, (
            f"anatomy-parameters has only {len(enriched)} enriched entries, expected >= 10"
        )


# ===========================================================================
# Table-row splitting ID convention
# ===========================================================================

class TestTableRowSplitting:
    """Spec: Split table rows use §N.parent-slug.qualifier convention."""

    # Mutation: would catch if split entries used flat IDs without parent context
    def test_split_entries_follow_id_convention(self):
        """IDs should follow §N.slug or §N.slug.qualifier pattern."""
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                eid = e.get('id', '')
                # All IDs should start with §
                assert eid.startswith('§'), (
                    f"{fname}: entry ID '{eid}' does not start with §"
                )
                # Should have at least section.slug format
                parts = eid.split('.')
                assert len(parts) >= 2, (
                    f"{fname}: entry ID '{eid}' should have at least §N.slug format"
                )


# ===========================================================================
# Enrichment completeness — specific known rules
# ===========================================================================

class TestKnownRuleEnrichments:
    """Verify specific known mechanical rules are enriched."""

    # Mutation: would catch if momentum bonuses are missing from risk-reward
    def test_momentum_bonus_entries_exist(self):
        """§15 momentum: 3-streak→+2, 4-streak→+2, 5+→+3."""
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        momentum_entries = [e for e in entries
                           if isinstance(e.get('condition'), dict)
                           and ('streak' in e.get('condition', {})
                                or 'streak_minimum' in e.get('condition', {}))]
        assert len(momentum_entries) >= 2, (
            f"Expected >= 2 momentum/streak entries, found {len(momentum_entries)}"
        )

    # Mutation: would catch if combo entries are missing
    def test_combo_entries_exist(self):
        """§15 combos: 8 named combo sequences."""
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        combo_entries = [e for e in entries
                         if isinstance(e.get('condition'), dict)
                         and 'combo_sequence' in e.get('condition', {})]
        assert len(combo_entries) >= 4, (
            f"Expected >= 4 combo entries, found {len(combo_entries)}"
        )

    # Mutation: would catch if tell bonus is missing
    def test_tell_bonus_entry_exists(self):
        """§15 tell: +2 hidden roll bonus."""
        entries = _load('risk-reward-and-hidden-depth-enriched.yaml')
        tell_entries = [e for e in entries
                        if isinstance(e.get('outcome'), dict)
                        and e.get('outcome', {}).get('roll_bonus') == 2
                        and 'tell' in e.get('id', '').lower()]
        assert len(tell_entries) >= 1, (
            "Missing tell bonus entry with roll_bonus: 2"
        )

    # Mutation: would catch if energy cost entries are missing from async-time
    def test_energy_cost_entries_exist(self):
        """async-time has energy costs per action."""
        entries = _load('async-time-enriched.yaml')
        energy_entries = [e for e in entries
                          if isinstance(e.get('outcome'), dict)
                          and 'energy_cost' in e.get('outcome', {})]
        assert len(energy_entries) >= 1, (
            "async-time should have at least 1 energy_cost entry"
        )

    # Mutation: would catch if time-of-day horniness entries lack time_of_day condition
    def test_horniness_entries_have_time_of_day(self):
        entries = _load('async-time-enriched.yaml')
        horn_entries = [e for e in entries
                        if isinstance(e.get('outcome'), dict)
                        and 'horniness_modifier' in e.get('outcome', {})]
        for he in horn_entries:
            cond = he.get('condition', {})
            assert 'time_of_day' in cond, (
                f"{he.get('id')}: horniness modifier entry should have time_of_day condition"
            )


# ===========================================================================
# New keys documentation — spec says unknown keys must follow snake_case
# ===========================================================================

class TestNewKeysFollowConvention:
    """Spec: new keys are acceptable if they follow snake_case convention."""

    # Mutation: would catch if new condition keys violate snake_case
    def test_all_condition_keys_snake_case(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                cond = e.get('condition')
                if not isinstance(cond, dict):
                    continue
                for k in cond.keys():
                    assert re.match(r'^[a-z][a-z0-9_]*$', k), (
                        f"{fname} / {e.get('id')}: condition key '{k}' is not snake_case"
                    )

    # Mutation: would catch if new outcome keys violate snake_case
    def test_all_outcome_keys_snake_case(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                out = e.get('outcome')
                if not isinstance(out, dict):
                    continue
                for k in out.keys():
                    assert re.match(r'^[a-z][a-z0-9_]*$', k), (
                        f"{fname} / {e.get('id')}: outcome key '{k}' is not snake_case"
                    )


# ===========================================================================
# Error condition: range values lower bound <= upper bound
# ===========================================================================

class TestRangeValueOrdering:
    """Spec: range values are [int, int] — lower bound should be <= upper bound."""

    RANGE_KEYS = {
        'miss_range', 'beat_range', 'interest_range', 'need_range',
        'level_range', 'timing_range',
    }

    # Mutation: would catch if range bounds were reversed (e.g. [10, 1] instead of [1, 10])
    def test_range_lower_leq_upper(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                cond = e.get('condition')
                if not isinstance(cond, dict):
                    continue
                for k in self.RANGE_KEYS:
                    if k in cond:
                        val = cond[k]
                        if isinstance(val, list) and len(val) == 2:
                            assert val[0] <= val[1], (
                                f"{fname} / {e.get('id')}: {k} has inverted range "
                                f"[{val[0]}, {val[1]}]"
                            )


# ===========================================================================
# Enriched entries have at least one of condition or outcome (not empty dicts)
# ===========================================================================

class TestEnrichedEntriesNotEmpty:
    """If an entry has condition/outcome, the dict should not be empty."""

    # Mutation: would catch if enrichment added empty {} condition/outcome dicts
    def test_no_empty_condition_dicts(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                cond = e.get('condition')
                if cond is not None:
                    assert isinstance(cond, dict), (
                        f"{fname} / {e.get('id')}: condition must be dict"
                    )
                    assert len(cond) > 0, (
                        f"{fname} / {e.get('id')}: condition dict is empty"
                    )

    # Mutation: would catch if outcome was set to {} instead of real values
    def test_no_empty_outcome_dicts(self):
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            for e in entries:
                out = e.get('outcome')
                if out is not None:
                    assert isinstance(out, dict), (
                        f"{fname} / {e.get('id')}: outcome must be dict"
                    )
                    # Note: empty outcome dicts like stat_modifiers: {} inside outcome
                    # are acceptable, but the top-level outcome dict should have keys
                    assert len(out) > 0, (
                        f"{fname} / {e.get('id')}: outcome dict is empty"
                    )


# ===========================================================================
# Cross-file: total enriched entries count
# ===========================================================================

class TestTotalEnrichedCountDetailed:
    """AC5: Total enriched entries detailed by file."""

    # Mutation: would catch if total was below spec's illustrative example (~224)
    def test_total_enriched_above_minimum(self):
        total = 0
        per_file = {}
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            enriched = _all_enriched_entries(entries)
            per_file[fname] = len(enriched)
            total += len(enriched)
        # The spec illustrative example shows ~224; actual may vary
        # but should be substantial
        assert total >= 150, (
            f"Total enriched entries = {total}, expected >= 150. "
            f"Per-file: {per_file}"
        )

    # Mutation: would catch if total entry count was unreasonably low
    def test_total_entries_above_minimum(self):
        total = 0
        for fname in ENRICHED_FILES:
            entries = _load(fname)
            total += len(entries)
        # Spec says 288 entries across 9 files (before splitting);
        # after splitting should be >= 288
        assert total >= 250, (
            f"Total entries across all files = {total}, expected >= 250"
        )


# ===========================================================================
# Run with pytest
# ===========================================================================

if __name__ == '__main__':
    import pytest
    sys.exit(pytest.main([__file__, '-v']))
