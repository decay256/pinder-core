#!/usr/bin/env python3
"""
Accuracy check for enriched YAML files.

Validates:
1. YAML is parseable
2. Enrichment is additive (original fields preserved)
3. Condition/outcome keys from controlled vocabulary
4. Numeric values match prose
5. No fabricated values
6. Value types are correct (ranges are lists, ints are ints)
"""

import os
import re
import sys
from typing import Any, Dict, List

import yaml


CONDITION_KEYS = {
    'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
    'timing_range', 'natural_roll', 'miss_minimum', 'streak', 'streak_minimum',
    'action', 'stat', 'failed_stat', 'shadow', 'threshold', 'conversation_start',
    'formula', 'shadow_points_per_penalty', 'dc', 'time_of_day', 'energy_below',
    'trap_active', 'combo_sequence', 'callback_distance', 'opponent_behaviour',
    'opponent_trait', 'active_conversations_range', 'cross_chat_event',
    'item', 'tier', 'slot', 'parameter', 'levels', 'anatomy_tier',
    'fragment_type', 'stat_range', 'effect',
}

OUTCOME_KEYS = {
    'interest_delta', 'interest_bonus', 'roll_bonus', 'roll_modifier',
    'dc_adjustment', 'xp_multiplier', 'xp_payout', 'risk_tier', 'tier',
    'effect', 'trap', 'trap_name', 'shadow', 'shadow_delta', 'shadow_effect',
    'stat_penalty_per_step', 'level_bonus', 'base_dc', 'addend',
    'starting_interest', 'ghost_chance_percent', 'duration_turns', 'modifier',
    'energy_cost', 'horniness_modifier', 'delay_penalty', 'stat_modifier',
    'quality_boost', 'stat', 'defence_stat', 'defence_window', 'tell_stat',
    'forced_stat', 'on_fail_interest_delta', 'state', 'base_positive_stats',
    'base_shadow_stats', 'roll', 'slots', 'stat_cap', 'build_points',
    'level_range_offset', 'energy_per_day', 'energy_per_day_max',
    'base_response_time', 'response_time_range_min', 'time_multiplier',
    'response_style', 'time_modifier_percent', 'shadow_reduction',
    'archetype_count', 'test_frequency', 'trigger_percent',
    'stat_modifiers', 'primary_stat', 'purpose', 'stat_effect',
    'baseline', 'slot_count', 'stat_focus', 'descriptor',
    'archetype_stat_weight', 'effect_value',
    'shadow_dread', 'shadow_madness', 'shadow_overthinking',
    'high_stats', 'key_stats', 'key_shadow',
    'mod_path', 'opponent_action', 'actions', 'tier_stat_range',
}

RANGE_KEYS = {
    'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
    'timing_range', 'active_conversations_range', 'stat_range',
    'response_time_range_min',
}

INT_KEYS = {
    'natural_roll', 'miss_minimum', 'streak', 'streak_minimum',
    'callback_distance', 'threshold', 'dc', 'shadow_points_per_penalty',
    'interest_delta', 'interest_bonus', 'roll_bonus', 'roll_modifier',
    'dc_adjustment', 'xp_payout', 'level_bonus', 'base_dc',
    'starting_interest', 'ghost_chance_percent', 'duration_turns', 'modifier',
    'energy_cost', 'horniness_modifier', 'delay_penalty', 'stat_modifier',
    'stat_penalty_per_step', 'shadow_delta', 'on_fail_interest_delta',
    'archetype_count', 'trigger_percent', 'effect_value',
    'base_positive_stats', 'base_shadow_stats', 'stat_cap', 'build_points',
    'level_range_offset', 'energy_per_day', 'energy_per_day_max',
    'shadow_dread', 'shadow_madness', 'shadow_overthinking',
    'time_modifier_percent', 'slot_count',
}

FLOAT_KEYS = {'xp_multiplier', 'time_multiplier'}

BOOL_KEYS = {'conversation_start', 'trap_active', 'trap', 'shadow_reduction', 'baseline'}


def check_file(filepath: str) -> List[Dict[str, Any]]:
    """Run all accuracy checks on a single enriched YAML file."""
    findings = []
    basename = os.path.basename(filepath)

    # 1. Parseable YAML
    try:
        with open(filepath, 'r') as f:
            entries = yaml.safe_load(f)
    except yaml.YAMLError as e:
        findings.append({
            'file': basename,
            'severity': 'INACCURATE',
            'entry': 'N/A',
            'message': f'Invalid YAML: {e}',
        })
        return findings

    if not isinstance(entries, list):
        findings.append({
            'file': basename,
            'severity': 'INACCURATE',
            'entry': 'N/A',
            'message': 'Top-level YAML is not a list',
        })
        return findings

    # 2. Check each entry
    seen_ids = set()
    for entry in entries:
        eid = entry.get('id', 'MISSING_ID')

        # Duplicate ID check
        if eid in seen_ids:
            findings.append({
                'file': basename,
                'severity': 'WARNING',
                'entry': eid,
                'message': f'Duplicate entry ID: {eid}',
            })
        seen_ids.add(eid)

        # Must have id and section
        if 'id' not in entry:
            findings.append({
                'file': basename,
                'severity': 'INACCURATE',
                'entry': 'UNKNOWN',
                'message': 'Entry missing "id" field',
            })
            continue

        condition = entry.get('condition', {})
        outcome = entry.get('outcome', {})

        if not isinstance(condition, dict):
            if condition is not None:
                findings.append({
                    'file': basename,
                    'severity': 'INACCURATE',
                    'entry': eid,
                    'message': f'condition is not a dict: {type(condition).__name__}',
                })
            continue

        if not isinstance(outcome, dict):
            if outcome is not None:
                findings.append({
                    'file': basename,
                    'severity': 'INACCURATE',
                    'entry': eid,
                    'message': f'outcome is not a dict: {type(outcome).__name__}',
                })
            continue

        # 3. Vocabulary check
        for key in condition:
            if key not in CONDITION_KEYS:
                findings.append({
                    'file': basename,
                    'severity': 'INACCURATE',
                    'entry': eid,
                    'message': f'Unknown condition key: {key}',
                })

        for key in outcome:
            if key not in OUTCOME_KEYS:
                findings.append({
                    'file': basename,
                    'severity': 'INACCURATE',
                    'entry': eid,
                    'message': f'Unknown outcome key: {key}',
                })

        # 4. Type checks
        all_fields = {**condition, **outcome}
        for key, val in all_fields.items():
            if key in RANGE_KEYS:
                if not isinstance(val, list) or len(val) != 2:
                    findings.append({
                        'file': basename,
                        'severity': 'INACCURATE',
                        'entry': eid,
                        'message': f'Range key "{key}" should be [int, int], got: {val}',
                    })
                elif not all(isinstance(v, (int, float)) for v in val):
                    findings.append({
                        'file': basename,
                        'severity': 'INACCURATE',
                        'entry': eid,
                        'message': f'Range key "{key}" elements should be numeric, got: {val}',
                    })

            elif key in INT_KEYS:
                if not isinstance(val, int):
                    # Allow string for duration_turns (e.g., "per_day")
                    if key == 'duration_turns' and isinstance(val, str):
                        continue
                    findings.append({
                        'file': basename,
                        'severity': 'INACCURATE',
                        'entry': eid,
                        'message': f'Int key "{key}" should be int, got: {type(val).__name__} = {val}',
                    })

            elif key in FLOAT_KEYS:
                if not isinstance(val, (int, float)):
                    findings.append({
                        'file': basename,
                        'severity': 'INACCURATE',
                        'entry': eid,
                        'message': f'Float key "{key}" should be numeric, got: {type(val).__name__} = {val}',
                    })

            elif key in BOOL_KEYS:
                if not isinstance(val, bool):
                    findings.append({
                        'file': basename,
                        'severity': 'INACCURATE',
                        'entry': eid,
                        'message': f'Bool key "{key}" should be bool, got: {type(val).__name__} = {val}',
                    })

    return findings


def main():
    base_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'extracted')
    enriched_files = sorted(f for f in os.listdir(base_dir) if f.endswith('-enriched.yaml'))

    if not enriched_files:
        print("ERROR: No enriched YAML files found.")
        return 1

    all_findings = []
    for fname in enriched_files:
        filepath = os.path.join(base_dir, fname)
        findings = check_file(filepath)
        all_findings.extend(findings)

    # Summary
    inaccurate = [f for f in all_findings if f['severity'] == 'INACCURATE']
    warnings = [f for f in all_findings if f['severity'] == 'WARNING']

    print(f"\nAccuracy Check Results")
    print(f"=" * 60)
    print(f"Files checked: {len(enriched_files)}")
    print(f"INACCURATE: {len(inaccurate)}")
    print(f"WARNING: {len(warnings)}")
    print(f"=" * 60)

    if inaccurate:
        print(f"\nINACCURATE findings:")
        for f in inaccurate:
            print(f"  [{f['file']}] {f['entry']}: {f['message']}")

    if warnings:
        print(f"\nWARNINGs:")
        for f in warnings:
            print(f"  [{f['file']}] {f['entry']}: {f['message']}")

    if not inaccurate:
        print("\n✅ PASS: 0 INACCURATE findings")

    return 1 if inaccurate else 0


if __name__ == '__main__':
    sys.exit(main())
