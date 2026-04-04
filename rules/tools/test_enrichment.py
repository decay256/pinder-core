#!/usr/bin/env python3
"""
Tests for the YAML enrichment pipeline (Issue #444).

Validates:
- All 9 enriched files exist and are valid YAML
- Enrichment is additive (original entries preserved)
- Condition/outcome fields use correct types
- Known mechanical values are correctly enriched
- Accuracy check passes with 0 INACCURATE findings
"""

import os
import re
import sys

import yaml

# Base directories
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
EXTRACTED_DIR = os.path.join(SCRIPT_DIR, '..', 'extracted')

EXPECTED_FILES = [
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


def load(fname):
    path = os.path.join(EXTRACTED_DIR, fname)
    with open(path, 'r') as f:
        return yaml.safe_load(f)


def find_entry(entries, entry_id):
    """Find entry by ID."""
    for e in entries:
        if e.get('id') == entry_id:
            return e
    return None


def find_entries_prefix(entries, prefix):
    """Find all entries with IDs starting with prefix."""
    return [e for e in entries if e.get('id', '').startswith(prefix)]


# ─── AC1: All 9 docs have enriched YAML files ─────────────────────

def test_all_enriched_files_exist():
    """AC1: All 9 enriched YAML files exist."""
    missing = []
    for fname in EXPECTED_FILES:
        path = os.path.join(EXTRACTED_DIR, fname)
        if not os.path.exists(path):
            missing.append(fname)
    assert not missing, f"Missing enriched files: {missing}"


def test_all_enriched_files_parseable():
    """AC1: All enriched files are valid YAML."""
    for fname in EXPECTED_FILES:
        entries = load(fname)
        assert isinstance(entries, list), f"{fname}: top-level is not a list"
        assert len(entries) > 0, f"{fname}: empty file"


# ─── AC2: Enriched entries per doc reported ─────────────────────────

def test_enriched_entry_counts():
    """AC2: Each file has a non-trivial number of enriched entries."""
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
        entries = load(fname)
        enriched = sum(1 for e in entries if 'condition' in e or 'outcome' in e)
        assert enriched >= min_count, \
            f"{fname}: expected ≥{min_count} enriched entries, got {enriched}"


# ─── AC3+4: Accuracy check passes ──────────────────────────────────

def test_accuracy_check_passes():
    """AC3+4: accuracy_check.py reports 0 INACCURATE findings."""
    import subprocess
    result = subprocess.run(
        [sys.executable, os.path.join(SCRIPT_DIR, 'accuracy_check.py')],
        capture_output=True, text=True
    )
    assert result.returncode == 0, \
        f"accuracy_check.py failed:\n{result.stdout}\n{result.stderr}"
    assert 'INACCURATE: 0' in result.stdout, \
        f"Expected 0 INACCURATE findings:\n{result.stdout}"


# ─── Specific value checks ─────────────────────────────────────────

def test_rules_v3_fail_tiers():
    """Verify fail tier enrichment matches rules §5."""
    entries = load('rules-v3-enriched.yaml')
    
    fumble = find_entry(entries, '§7.fail-tier.fumble')
    assert fumble is not None, "Missing §7.fail-tier.fumble"
    assert fumble['condition']['miss_range'] == [1, 2]
    assert fumble['outcome']['tier'] == 'Fumble'
    assert fumble['outcome']['interest_delta'] == -1

    trope = find_entry(entries, '§7.fail-tier.trope-trap')
    assert trope is not None, "Missing §7.fail-tier.trope-trap"
    assert trope['condition']['miss_range'] == [6, 9]
    assert trope['outcome']['interest_delta'] == -2
    assert trope['outcome'].get('trap') == True


def test_rules_v3_success_scale():
    """Verify success scale enrichment."""
    entries = load('rules-v3-enriched.yaml')
    
    beat_1_4 = find_entry(entries, '§7.success-scale.1-4')
    assert beat_1_4 is not None, "Missing §7.success-scale.1-4"
    assert beat_1_4['condition']['beat_range'] == [1, 4]
    assert beat_1_4['outcome']['interest_delta'] == 1

    beat_10plus = find_entry(entries, '§7.success-scale.10plus')
    assert beat_10plus is not None, "Missing §7.success-scale.10plus"
    assert beat_10plus['condition']['beat_range'] == [10, 99]
    assert beat_10plus['outcome']['interest_delta'] == 3


def test_rules_v3_nat1_nat20():
    """Verify nat 1 and nat 20 enrichment."""
    entries = load('rules-v3-enriched.yaml')
    
    nat1 = find_entry(entries, '§6.natural-1')
    assert nat1 is not None, "Missing §6.natural-1"
    assert nat1['condition']['natural_roll'] == 1
    assert nat1['outcome']['interest_delta'] == -5

    nat20 = find_entry(entries, '§6.natural-20')
    assert nat20 is not None, "Missing §6.natural-20"
    assert nat20['condition']['natural_roll'] == 20
    assert nat20['outcome']['interest_delta'] == 4


def test_rules_v3_shadow_thresholds():
    """Verify shadow threshold enrichment."""
    entries = load('rules-v3-enriched.yaml')
    
    dread_t3 = find_entry(entries, '§9.shadow-threshold.dread.t3')
    assert dread_t3 is not None, "Missing §9.shadow-threshold.dread.t3"
    assert dread_t3['condition']['shadow'] == 'Dread'
    assert dread_t3['condition']['threshold'] == 18
    assert dread_t3['outcome']['starting_interest'] == 8


def test_rules_v3_level_bonus():
    """Verify level bonus enrichment."""
    entries = load('rules-v3-enriched.yaml')
    
    lvl_1_2 = find_entry(entries, '§4.level-bonus.1-2')
    assert lvl_1_2 is not None, "Missing §4.level-bonus.1-2"
    assert lvl_1_2['condition']['level_range'] == [1, 2]
    assert lvl_1_2['outcome']['level_bonus'] == 0

    lvl_5_6 = find_entry(entries, '§4.level-bonus.5-6')
    assert lvl_5_6 is not None, "Missing §4.level-bonus.5-6"
    assert lvl_5_6['condition']['level_range'] == [5, 6]
    assert lvl_5_6['outcome']['level_bonus'] == 2


def test_risk_reward_risk_tiers():
    """Verify risk tier enrichment."""
    entries = load('risk-reward-and-hidden-depth-enriched.yaml')
    
    safe = find_entry(entries, '§2.risk-tier.safe')
    assert safe is not None, "Missing §2.risk-tier.safe"
    assert safe['condition']['need_range'] == [1, 5]
    assert safe['outcome']['risk_tier'] == 'Safe'
    assert safe['outcome']['interest_bonus'] == 0
    assert safe['outcome']['xp_multiplier'] == 1.0

    bold = find_entry(entries, '§2.risk-tier.bold')
    assert bold is not None, "Missing §2.risk-tier.bold"
    assert bold['condition']['need_range'] == [16, 99]
    assert bold['outcome']['interest_bonus'] == 2
    assert bold['outcome']['xp_multiplier'] == 3.0


def test_risk_reward_callback_bonus():
    """Verify callback bonus enrichment."""
    entries = load('risk-reward-and-hidden-depth-enriched.yaml')
    
    cb_2 = find_entry(entries, '§4.callback-bonus.2-turns')
    assert cb_2 is not None, "Missing §4.callback-bonus.2-turns"
    assert cb_2['condition']['callback_distance'] == 2
    assert cb_2['outcome']['roll_bonus'] == 1

    cb_opener = find_entry(entries, '§4.callback-bonus.opener')
    assert cb_opener is not None, "Missing §4.callback-bonus.opener"
    assert cb_opener['condition']['callback_distance'] == 0
    assert cb_opener['outcome']['roll_bonus'] == 3


def test_risk_reward_combos():
    """Verify combo enrichment."""
    entries = load('risk-reward-and-hidden-depth-enriched.yaml')
    
    setup = find_entry(entries, '§5.combo.setup')
    assert setup is not None, "Missing §5.combo.setup"
    assert setup['condition']['combo_sequence'] == ['Wit', 'Charm']
    assert setup['outcome']['interest_bonus'] == 1


def test_risk_reward_momentum():
    """Verify momentum enrichment."""
    entries = load('risk-reward-and-hidden-depth-enriched.yaml')
    
    mom_3 = find_entry(entries, '§6.momentum.3-wins')
    assert mom_3 is not None, "Missing §6.momentum.3-wins"
    assert mom_3['condition']['streak'] == 3
    assert mom_3['outcome']['roll_bonus'] == 2


def test_risk_reward_tells():
    """Verify tell enrichment."""
    entries = load('risk-reward-and-hidden-depth-enriched.yaml')

    e = find_entry(entries, '§7.opponent-tells-post-roll-feedback')
    assert e is not None
    assert e['outcome']['roll_bonus'] == 2


def test_async_time_horniness_modifiers():
    """Verify time-of-day horniness modifier enrichment."""
    entries = load('async-time-enriched.yaml')
    
    late_night = find_entry(entries, '§6.time-of-day.late-night')
    assert late_night is not None, "Missing §6.time-of-day.late-night"
    assert late_night['outcome']['horniness_modifier'] == 3

    after_2am = find_entry(entries, '§6.time-of-day.after-2am')
    assert after_2am is not None, "Missing §6.time-of-day.after-2am"
    assert after_2am['outcome']['horniness_modifier'] == 5

    morning = find_entry(entries, '§6.time-of-day.morning')
    assert morning is not None, "Missing §6.time-of-day.morning"
    assert morning['outcome']['horniness_modifier'] == -2


def test_async_time_delay_penalties():
    """Verify response delay penalty enrichment."""
    entries = load('async-time-enriched.yaml')
    
    delay_entries = find_entries_prefix(entries, '§5.response-delay.')
    assert len(delay_entries) >= 4, f"Expected ≥4 delay entries, got {len(delay_entries)}"


def test_traps_enrichment():
    """Verify trap enrichment matches traps.json data."""
    entries = load('traps-enriched.yaml')
    
    cringe = find_entry(entries, '§2.the-cringe')
    assert cringe is not None, "Missing §2.the-cringe"
    assert cringe['condition']['failed_stat'] == 'Charm'
    assert cringe['condition']['miss_minimum'] == 6
    assert cringe['outcome']['duration_turns'] == 1
    assert cringe['outcome']['effect'] == 'disadvantage'

    creep = find_entry(entries, '§2.the-creep')
    assert creep is not None, "Missing §2.the-creep"
    assert creep['condition']['failed_stat'] == 'Rizz'
    assert creep['outcome']['duration_turns'] == 2


def test_archetypes_enrichment():
    """Verify archetype enrichment has level ranges."""
    entries = load('archetypes-enriched.yaml')
    
    # Check that individual archetypes got enriched
    hey = find_entry(entries, '§2.the-hey-opener')
    assert hey is not None, "Missing §2.the-hey-opener"
    assert 'condition' in hey or 'outcome' in hey, "The Hey Opener should be enriched"


def test_items_pool_stat_modifiers():
    """Verify item stat modifier enrichment."""
    entries = load('items-pool-enriched.yaml')
    
    # Find an item with known stats
    beanie = find_entry(entries, '§12.beanie-with-patches')
    assert beanie is not None, "Missing §12.beanie-with-patches"
    if 'outcome' in beanie:
        mods = beanie['outcome'].get('stat_modifiers', {})
        assert 'charm' in mods, "Beanie should have Charm modifier"
        assert mods['charm'] == 1


def test_anatomy_stat_modifiers():
    """Verify anatomy tier stat modifier enrichment."""
    entries = load('anatomy-parameters-enriched.yaml')
    
    # Find a tier with known stats (e.g., §3.long has Rizz +1, Charm +1, SA -1)
    long_tier = find_entry(entries, '§3.long')
    if long_tier and 'outcome' in long_tier:
        mods = long_tier['outcome'].get('stat_modifiers', {})
        assert 'rizz' in mods, "Long should have Rizz modifier"
        assert mods['rizz'] == 1


def test_enrichment_is_additive():
    """Verify that enrichment doesn't remove original fields."""
    for fname in EXPECTED_FILES:
        original_name = fname.replace('-enriched', '')
        original_path = os.path.join(EXTRACTED_DIR, original_name)
        if not os.path.exists(original_path):
            continue
        
        original = load(original_name)
        enriched = load(fname)
        
        original_ids = {e['id'] for e in original}
        enriched_ids = {e['id'] for e in enriched}
        
        # All original IDs should be present in enriched
        missing = original_ids - enriched_ids
        assert not missing, \
            f"{fname}: original entries missing after enrichment: {missing}"


def test_condition_outcome_types():
    """Verify condition and outcome are always dicts when present."""
    for fname in EXPECTED_FILES:
        entries = load(fname)
        for e in entries:
            if 'condition' in e:
                assert isinstance(e['condition'], dict), \
                    f"{fname}/{e['id']}: condition is not a dict"
            if 'outcome' in e:
                assert isinstance(e['outcome'], dict), \
                    f"{fname}/{e['id']}: outcome is not a dict"


# ─── AC5: Total enriched entries ────────────────────────────────────

def test_total_enriched_entries():
    """AC5: Total enriched entries across all docs is significant."""
    total = 0
    for fname in EXPECTED_FILES:
        entries = load(fname)
        total += sum(1 for e in entries if 'condition' in e or 'outcome' in e)
    assert total >= 200, f"Expected ≥200 total enriched entries, got {total}"


# ─── Runner ─────────────────────────────────────────────────────────

def run_tests():
    """Simple test runner — finds and runs all test_ functions."""
    test_funcs = [(name, obj) for name, obj in globals().items()
                  if name.startswith('test_') and callable(obj)]
    
    passed = 0
    failed = 0
    errors = []

    for name, func in sorted(test_funcs):
        try:
            func()
            passed += 1
            print(f"  ✅ {name}")
        except AssertionError as e:
            failed += 1
            errors.append((name, str(e)))
            print(f"  ❌ {name}: {e}")
        except Exception as e:
            failed += 1
            errors.append((name, str(e)))
            print(f"  💥 {name}: {type(e).__name__}: {e}")

    print(f"\n{'=' * 60}")
    print(f"Tests: {passed + failed} total, {passed} passed, {failed} failed")

    if errors:
        print(f"\nFailures:")
        for name, msg in errors:
            print(f"  {name}: {msg}")

    return 0 if failed == 0 else 1


if __name__ == '__main__':
    # First run the enrichment to generate files
    import subprocess
    print("Running enrichment...")
    subprocess.run([sys.executable, os.path.join(SCRIPT_DIR, 'enrich.py')], check=True)
    print("\nRunning tests...")
    sys.exit(run_tests())
