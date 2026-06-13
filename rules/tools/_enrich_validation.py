#!/usr/bin/env python3
# AUTO-GENERATED
import copy
import os
import re
from typing import Any, Dict, List, Tuple, Optional, Set
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers, load_yaml, save_yaml

def validate_vocabulary(entries: List[Dict[str, Any]], filename: str) -> List[str]:
    """Validate that all condition/outcome keys are from known vocabulary."""
    CONDITION_KEYS = {
        'miss_range', 'beat_range', 'interest_range', 'need_range', 'level_range',
        'timing_range', 'natural_roll', 'miss_minimum', 'streak', 'streak_minimum',
        'action', 'stat', 'failed_stat', 'shadow', 'threshold', 'conversation_start',
        'formula', 'shadow_points_per_penalty', 'dc', 'time_of_day', 'energy_below',
        'trap_active', 'combo_sequence', 'callback_distance', 'datee_behaviour',
        'datee_trait', 'active_conversations_range', 'cross_chat_event',
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
        # Cross-chat shadow keys
        'shadow_dread', 'shadow_madness', 'shadow_overthinking',
        # Archetype keys
        'high_stats', 'key_stats', 'key_shadow',
        # Extensibility keys
        'mod_path',
        # Additional keys
        'datee_action', 'actions', 'tier_stat_range',
    }

    issues = []
    for e in entries:
        cond = e.get('condition', {})
        out = e.get('outcome', {})
        if isinstance(cond, dict):
            for k in cond:
                if k not in CONDITION_KEYS:
                    issues.append(f"{filename}: {e.get('id', '?')}: unknown condition key '{k}'")
        if isinstance(out, dict):
            for k in out:
                if k not in OUTCOME_KEYS:
                    issues.append(f"{filename}: {e.get('id', '?')}: unknown outcome key '{k}'")
    return issues

