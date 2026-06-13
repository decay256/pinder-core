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
        e = _find(entries, '§7.datee-tells-post-roll-feedback')
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

