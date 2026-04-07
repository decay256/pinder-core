#!/usr/bin/env python3
"""
Enrichment script for Pinder rules YAML files.

Reads each extracted YAML file, adds structured condition/outcome fields
to entries that contain numeric thresholds, ranges, or named mechanical effects.
Produces *-enriched.yaml files in rules/extracted/.

Usage:
    python3 rules/tools/enrich.py
"""

import copy
import os
import re
import sys
from typing import Any, Dict, List, Optional, Tuple

import yaml


# ─── YAML helpers ────────────────────────────────────────────────────

def load_yaml(path: str) -> List[Dict[str, Any]]:
    with open(path, 'r') as f:
        return yaml.safe_load(f) or []


def save_yaml(path: str, data: List[Dict[str, Any]]) -> None:
    with open(path, 'w') as f:
        yaml.dump(data, f, default_flow_style=False, allow_unicode=True, width=200, sort_keys=False)


# ─── Stat parsing helpers ────────────────────────────────────────────

STAT_NAMES = {'Charm', 'Rizz', 'Honesty', 'Chaos', 'Wit', 'Self-Awareness'}
SHADOW_NAMES = {'Overthinking', 'Denial', 'Fixation', 'Dread', 'Madness', 'Horniness'}


def parse_stat_modifiers(text: str) -> Dict[str, int]:
    """Parse stat modifiers from text like 'Charm +1, Rizz -1'."""
    mods = {}
    # Match patterns like "Charm +1" or "Self-Awareness −2"
    for m in re.finditer(r'(Charm|Rizz|Honesty|Chaos|Wit|Self-Awareness)\s*([+\-−])\s*(\d+)', text):
        stat = m.group(1)
        sign = -1 if m.group(2) in ('-', '−') else 1
        val = int(m.group(3)) * sign
        mods[stat.lower().replace('-', '_')] = val
    return mods


# ─── File-specific enrichers ─────────────────────────────────────────

def enrich_rules_v3(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich rules-v3.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # §4 shadow penalty
        if eid == '§4.shadow-penalty':
            e['condition'] = {'shadow': 'any', 'shadow_points_per_penalty': 3}
            e['outcome'] = {'stat_penalty_per_step': -1}

        # §4 starting stats
        elif eid == '§4.starting-stats':
            e['condition'] = {'conversation_start': True}
            e['outcome'] = {'base_positive_stats': 8, 'base_shadow_stats': 0}

        # §5 DC examples
        elif eid == '§5.dc-examples':
            e['condition'] = {'formula': 'defense_dc'}
            e['outcome'] = {'base_dc': 13, 'addend': 'opponent_stat_modifier'}

        # §6 basic roll
        elif eid == '§6.basic-roll':
            e['condition'] = {'formula': 'attack_roll'}
            e['outcome'] = {'roll': 'd20', 'addend': 'stat_modifier + level_bonus'}

        # §6 nat 1 and nat 20
        elif eid == '§6.natural-1-and-natural-20':
            result.append(e)
            nat1 = {
                'id': '§6.natural-1',
                'section': '§6',
                'title': 'Natural 1 — Auto-fail',
                'type': 'roll_modifier',
                'description': 'Nat 1: Auto-fail regardless of modifier. Legendary disaster.',
                'condition': {'natural_roll': 1},
                'outcome': {'tier': 'Legendary', 'interest_delta': -5, 'xp_payout': 10, 'trap': True}
            }
            result.append(nat1)
            nat20 = {
                'id': '§6.natural-20',
                'section': '§6',
                'title': 'Natural 20 — Auto-success',
                'type': 'roll_modifier',
                'description': 'Nat 20: Auto-success regardless of DC. Crit. +4 Interest. Advantage on next roll.',
                'condition': {'natural_roll': 20},
                'outcome': {'interest_delta': 4, 'effect': 'advantage_next_roll', 'xp_payout': 25}
            }
            result.append(nat20)
            continue

        # §6 advantage/disadvantage
        elif eid == '§6.advantage-disadvantage':
            e['condition'] = {'effect': 'advantage_or_disadvantage'}
            e['outcome'] = {'roll': '2d20_take_best_or_worst'}

        # §7 fail severity scale
        elif eid == '§7.fail-severity-scale':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        miss = row.get('Miss DC by', '')
                        severity = row.get('Severity', '').strip('*').strip()
                        interest = row.get('Interest Δ', '')
                        if not severity:
                            continue
                        slug = severity.lower().replace(' ', '-')
                        cond = {}
                        m_range = re.search(r'(\d+)\s*[–-]\s*(\d+)', miss)
                        m_plus = re.search(r'(\d+)\+', miss)
                        m_nat = re.search(r'[Nn]at\s*(\d+)', miss)
                        if m_range:
                            cond['miss_range'] = [int(m_range.group(1)), int(m_range.group(2))]
                        elif m_plus:
                            cond['miss_minimum'] = int(m_plus.group(1))
                        elif m_nat:
                            cond['natural_roll'] = int(m_nat.group(1))
                        i_match = re.search(r'([+\-−])(\d+)', str(interest))
                        outcome = {'tier': severity}
                        if i_match:
                            sign = -1 if i_match.group(1) in ('-', '−') else 1
                            outcome['interest_delta'] = sign * int(i_match.group(2))
                        if 'trap' in str(interest).lower():
                            outcome['trap'] = True
                        sub = {
                            'id': f'§7.fail-tier.{slug}',
                            'section': '§7',
                            'title': f'Fail Tier — {severity}',
                            'type': 'interest_change',
                            'description': f'Miss by {miss}: {severity}. {interest}.',
                            'condition': cond,
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        # §7 on success (success scale)
        elif eid == '§7.on-success':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        beat = row.get('Beat DC by', '')
                        interest = row.get('Interest Δ', '')
                        if not beat:
                            continue
                        cond = {}
                        m_range = re.search(r'(\d+)\s*[–-]\s*(\d+)', beat)
                        m_plus = re.search(r'(\d+)\+', beat)
                        m_nat = re.search(r'[Nn]at\s*(\d+)', beat)
                        if m_range:
                            cond['beat_range'] = [int(m_range.group(1)), int(m_range.group(2))]
                        elif m_plus:
                            cond['beat_range'] = [int(m_plus.group(1)), 99]
                        elif m_nat:
                            cond['natural_roll'] = int(m_nat.group(1))
                        i_match = re.search(r'([+\-−])(\d+)', str(interest))
                        outcome = {}
                        if i_match:
                            sign = -1 if i_match.group(1) in ('-', '−') else 1
                            outcome['interest_delta'] = sign * int(i_match.group(2))
                        slug = beat.lower().replace('+', 'plus').replace(' ', '-').replace('–', '-')
                        sub = {
                            'id': f'§7.success-scale.{slug}',
                            'section': '§7',
                            'title': f'Success Scale — Beat by {beat}',
                            'type': 'interest_change',
                            'description': f'Beat DC by {beat}: {interest}.',
                            'condition': cond,
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        # §7 stat-specific trope traps
        elif eid == '§7.stat-specific-trope-traps-on-miss-by-6':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        stat = row.get('Failed Stat', '')
                        trap_name = row.get('Trap', '').strip('*').strip()
                        status = row.get('Status Effect', '')
                        if not stat:
                            continue
                        slug = stat.lower().replace('-', '_')
                        # Parse duration
                        dur_m = re.search(r'(\d+)\s*turn', status)
                        duration = int(dur_m.group(1)) if dur_m else 1
                        # Parse effect
                        effect = 'disadvantage' if 'disadvantage' in status.lower() else 'stat_penalty'
                        sub = {
                            'id': f'§7.trope-trap.{slug}',
                            'section': '§7',
                            'title': f'Trope Trap — {stat}',
                            'type': 'trap_activation',
                            'description': f'{stat} miss by 6+: {trap_name}. {status}.',
                            'condition': {'failed_stat': stat, 'miss_minimum': 6},
                            'outcome': {'trap_name': trap_name, 'duration_turns': duration, 'effect': effect, 'stat': stat},
                        }
                        result.append(sub)
            result.append(e)
            continue

        # §4 stat pairs
        elif eid == '§4.the-stat-pairs':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        positive = row.get('Positive', '').strip('*').strip()
                        shadow = row.get('Shadow', '').strip('*').strip()
                        if positive and shadow:
                            sub = {
                                'id': f'§4.stat-pair.{positive.lower()}',
                                'section': '§4',
                                'title': f'Stat Pair — {positive}/{shadow}',
                                'type': 'definition',
                                'description': f'{positive} is paired with shadow {shadow}.',
                                'condition': {'stat': positive},
                                'outcome': {'shadow': shadow},
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §4 level bonus
        elif eid == '§4.level-bonus':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        level = row.get('Level', '')
                        bonus = row.get('Bonus', '')
                        if not level:
                            continue
                        m = re.search(r'(\d+)\s*[–-]\s*(\d+)', str(level))
                        m_plus = re.search(r'(\d+)\+', str(level))
                        cond = {}
                        if m:
                            cond['level_range'] = [int(m.group(1)), int(m.group(2))]
                        elif m_plus:
                            cond['level_range'] = [int(m_plus.group(1)), 99]
                        b_match = re.search(r'([+\-−])(\d+)', str(bonus))
                        outcome = {}
                        if b_match:
                            sign = -1 if b_match.group(1) in ('-', '−') else 1
                            outcome['level_bonus'] = sign * int(b_match.group(2))
                        else:
                            outcome['level_bonus'] = 0
                        if cond:
                            slug = str(level).replace('–', '-').replace(' ', '')
                            sub = {
                                'id': f'§4.level-bonus.{slug}',
                                'section': '§4',
                                'title': f'Level Bonus — Level {level}',
                                'type': 'roll_modifier',
                                'description': f'Level {level}: {bonus} to all rolls.',
                                'condition': cond,
                                'outcome': outcome,
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §9 shadow growth tables (dread, madness, denial, fixation, overthinking)
        elif eid.startswith('§9.') and eid.endswith(('-wit', '-charm', '-honesty', '-chaos', '-self-awareness')):
            # Shadow growth event tables
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        event = row.get('Event', '')
                        # Find the delta column (e.g., 'Dread Δ', 'Madness Δ')
                        delta_col = None
                        delta_val = None
                        for k, v in row.items():
                            if 'Δ' in k or 'Delta' in k:
                                delta_col = k
                                delta_val = v
                                break
                        if not event or delta_val is None:
                            continue
                        d_match = re.search(r'([+\-−])(\d+)', str(delta_val))
                        if not d_match:
                            continue
                        sign = -1 if d_match.group(1) in ('-', '−') else 1
                        delta = sign * int(d_match.group(2))
                        shadow_name = delta_col.replace(' Δ', '').strip() if delta_col else ''
                        slug = event.lower().replace(' ', '-').replace('(', '').replace(')', '').replace('→', '')[:40]
                        sub = {
                            'id': f'§9.shadow-growth.{slug}',
                            'section': '§9',
                            'title': f'Shadow Growth — {event[:50]}',
                            'type': 'shadow_growth',
                            'description': f'{event}: {shadow_name} {delta_val}.',
                            'condition': {'action': event},
                            'outcome': {'shadow': shadow_name, 'shadow_delta': delta},
                        }
                        result.append(sub)
            result.append(e)
            continue

        # §9 shadow reduction
        elif eid == '§9.shadow-reduction':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        action = row.get('Action', '')
                        reduced = row.get('Shadow Reduced', '')
                        if not action:
                            continue
                        # Parse shadow name and delta from "Dread -1"
                        for shadow in SHADOW_NAMES:
                            if shadow in reduced:
                                d_match = re.search(r'([+\-−])(\d+)', reduced)
                                if d_match:
                                    sign = -1 if d_match.group(1) in ('-', '−') else 1
                                    delta = sign * int(d_match.group(2))
                                    slug = action.lower().replace(' ', '-')[:30]
                                    sub = {
                                        'id': f'§9.shadow-reduce.{slug}',
                                        'section': '§9',
                                        'title': f'Shadow Reduction — {action}',
                                        'type': 'shadow_growth',
                                        'description': f'{action}: {reduced}.',
                                        'condition': {'action': action},
                                        'outcome': {'shadow': shadow, 'shadow_delta': delta},
                                    }
                                    result.append(sub)
                                break
            result.append(e)
            continue

        # §9 shadow thresholds
        elif eid == '§9.shadow-thresholds':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        shadow = row.get('Shadow', '').strip('*').strip()
                        at6 = row.get('At 6', '')
                        at12 = row.get('At 12', '')
                        at18 = row.get('At 18+', '')
                        if not shadow:
                            continue
                        slug = shadow.lower()
                        for thresh, val, lvl in [(6, at6, 'T1'), (12, at12, 'T2'), (18, at18, 'T3')]:
                            outcome = {'effect': val}
                            if 'disadvantage' in val.lower():
                                outcome['effect'] = 'disadvantage'
                                # Try to find which stat
                                stat_m = re.search(r'(Charm|Wit|Honesty|Chaos|Rizz|Self-Awareness)', val)
                                if stat_m:
                                    outcome['stat'] = stat_m.group(1)
                            if 'starting interest' in val.lower():
                                si_m = re.search(r'(\d+)', val)
                                if si_m:
                                    outcome['starting_interest'] = int(si_m.group(1))
                            sub = {
                                'id': f'§9.shadow-threshold.{slug}.{lvl.lower()}',
                                'section': '§9',
                                'title': f'Shadow Threshold — {shadow} {lvl}',
                                'type': 'shadow_growth',
                                'description': f'{shadow} at {thresh}: {val}.',
                                'condition': {'shadow': shadow, 'threshold': thresh},
                                'outcome': outcome,
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §10 turn actions
        elif eid == '§10.your-turn':
            e['condition'] = {'action': 'player_turn'}
            e['outcome'] = {'actions': ['Speak', 'Read', 'Recover', 'Wait']}

        # §10 opponent turn
        elif eid == '§10.opponents-turn-llm-controlled':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        interest = row.get('Interest', '')
                        action = row.get('Action', row.get('Behaviour', ''))
                        if not interest:
                            continue
                        m = re.search(r'(\d+)\s*[–-]\s*(\d+)', str(interest))
                        if m:
                            sub = {
                                'id': f'§10.opponent-turn.interest-{m.group(1)}-{m.group(2)}',
                                'section': '§10',
                                'title': f'Opponent Turn — Interest {interest}',
                                'type': 'interest_change',
                                'description': f'At interest {interest}: {action}.',
                                'condition': {'interest_range': [int(m.group(1)), int(m.group(2))]},
                                'outcome': {'opponent_action': action},
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §11 item tiers
        elif eid == '§11.item-tiers':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        tier = row.get('Tier', '')
                        unlock = row.get('Unlock', '')
                        stat_range = row.get('Stat Range', '')
                        if not tier:
                            continue
                        slug = tier.lower()
                        cond = {}
                        lvl_m = re.search(r'(\d+)', unlock)
                        if lvl_m:
                            cond['level_range'] = [int(lvl_m.group(1)), 99]
                        outcome = {'tier': tier, 'tier_stat_range': stat_range}
                        sub = {
                            'id': f'§11.item-tier.{slug}',
                            'section': '§11',
                            'title': f'Item Tier — {tier}',
                            'type': 'definition',
                            'description': f'{tier}: Unlock at {unlock}, stats {stat_range}.',
                            'condition': cond,
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        # §6 interest meter table
        elif eid == '§6.6-the-interest-meter':
            # Explode the table rows into separate entries for each interest state
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        rng = row.get('Range', '')
                        state = row.get('State', '')
                        effect = row.get('Effect', '')
                        if not state:
                            continue
                        sub = {
                            'id': f'§6.interest-state.{state.lower().replace(" ", "-")}',
                            'section': '§6',
                            'title': f'Interest State — {state}',
                            'type': 'interest_change',
                            'description': f'{state} ({rng}): {effect}',
                        }
                        # Parse range
                        rng_match = re.search(r'(\d+)\s*[–-]\s*(\d+)', rng)
                        single = re.match(r'^(\d+)$', rng.strip())
                        if rng_match:
                            sub['condition'] = {'interest_range': [int(rng_match.group(1)), int(rng_match.group(2))]}
                        elif single:
                            val = int(single.group(1))
                            sub['condition'] = {'interest_range': [val, val]}
                        # Parse effects
                        outcome = {}
                        if 'advantage' in effect.lower():
                            outcome['effect'] = 'advantage'
                        if 'disadvantage' in effect.lower():
                            outcome['effect'] = 'disadvantage'
                        if 'ghost' in effect.lower():
                            m = re.search(r'(\d+)%', effect)
                            if m:
                                outcome['ghost_chance_percent'] = int(m.group(1))
                        if '+1' in effect:
                            outcome['interest_delta'] = 1
                        if outcome:
                            sub['outcome'] = outcome
                        else:
                            sub['outcome'] = {'state': state}
                        result.append(sub)
            result.append(e)
            continue

        # §9 horniness penalizes rizz
        elif eid == '§9.horniness-penalizes-rizz':
            e['condition'] = {'shadow': 'Horniness', 'conversation_start': True}
            e['outcome'] = {'roll': '1d10', 'shadow': 'Horniness'}

        # §11 item slots
        elif eid == '§11.item-slots':
            e['condition'] = {'formula': 'item_slots'}
            e['outcome'] = {'slots': ['Hat', 'Shirt', 'Trousers', 'Shoes', 'Accessory', 'Accessory', 'Frame']}

        # §12 spending build points
        elif eid == '§12.spending-build-points':
            e['condition'] = {'formula': 'build_points'}
            e['outcome'] = {'stat_cap': 6}

        # §12 character creation starting budget
        elif eid == '§12.character-creation-level-1-starting-budget':
            e['condition'] = {'level_range': [1, 1]}
            e['outcome'] = {'build_points': 12, 'stat_cap': 4}

        # §14 matchmaking
        elif eid == '§14.matchmaking':
            e['condition'] = {'formula': 'matchmaking'}
            e['outcome'] = {'level_range_offset': 2}

        # Entries with tables containing failure tiers, success scale, etc.
        elif eid == '§6.failure-tiers':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        miss = row.get('Miss by', row.get('Miss By', ''))
                        tier_name = row.get('Fail Tier', row.get('Tier', ''))
                        interest = row.get('Interest', row.get('Interest Change', ''))
                        if not tier_name:
                            continue
                        sub = {
                            'id': f'§6.failure-tier.{tier_name.lower().replace(" ", "-")}',
                            'section': '§6',
                            'title': f'Failure Tier — {tier_name}',
                            'type': 'interest_change',
                            'description': f'Miss by {miss}: {tier_name}, {interest}',
                        }
                        cond = {}
                        m_range = re.search(r'(\d+)\s*[–-]\s*(\d+)', miss)
                        m_plus = re.search(r'(\d+)\+', miss)
                        m_nat = re.search(r'[Nn]at(?:ural)?\s*(\d+)', miss)
                        if m_range:
                            cond['miss_range'] = [int(m_range.group(1)), int(m_range.group(2))]
                        elif m_plus:
                            cond['miss_minimum'] = int(m_plus.group(1))
                        elif m_nat:
                            cond['natural_roll'] = int(m_nat.group(1))
                        sub['condition'] = cond
                        # Parse interest delta
                        i_match = re.search(r'([+\-−])(\d+)', str(interest))
                        outcome = {'tier': tier_name}
                        if i_match:
                            sign = -1 if i_match.group(1) in ('-', '−') else 1
                            outcome['interest_delta'] = sign * int(i_match.group(2))
                        # Check for trap
                        if 'trap' in tier_name.lower() or 'trap' in str(interest).lower():
                            outcome['trap'] = True
                        sub['outcome'] = outcome
                        result.append(sub)
            result.append(e)
            continue

        # §6 success scale
        elif eid == '§6.success-scale':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        beat = row.get('Beat DC by', row.get('Beat DC By', ''))
                        interest = row.get('Interest', row.get('Interest Change', ''))
                        if not beat:
                            continue
                        sub = {
                            'id': f'§6.success-scale.beat-{beat.lower().replace("+", "plus").replace(" ", "-").replace("–","-")}',
                            'section': '§6',
                            'title': f'Success Scale — Beat by {beat}',
                            'type': 'interest_change',
                        }
                        cond = {}
                        m_range = re.search(r'(\d+)\s*[–-]\s*(\d+)', beat)
                        m_plus = re.search(r'(\d+)\+', beat)
                        m_nat = re.search(r'[Nn]at(?:ural)?\s*(\d+)', beat)
                        if m_range:
                            cond['beat_range'] = [int(m_range.group(1)), int(m_range.group(2))]
                        elif m_plus:
                            cond['beat_range'] = [int(m_plus.group(1)), 99]
                        elif m_nat:
                            cond['natural_roll'] = int(m_nat.group(1))
                        sub['condition'] = cond
                        i_match = re.search(r'([+\-−])(\d+)', str(interest))
                        outcome = {}
                        if i_match:
                            sign = -1 if i_match.group(1) in ('-', '−') else 1
                            outcome['interest_delta'] = sign * int(i_match.group(2))
                        sub['outcome'] = outcome
                        sub['description'] = f'Beat DC by {beat}: {interest}'
                        result.append(sub)
            result.append(e)
            continue

        # §5 level bonus table
        elif eid == '§5.level-bonus-vs-opponent-dc-the-spread':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        level = row.get('Level', '')
                        bonus = row.get('Level Bonus', row.get('Bonus', ''))
                        if not level or not bonus:
                            continue
                        level_str = str(level)
                        m_range = re.search(r'(\d+)\s*[–-]\s*(\d+)', level_str)
                        m_single = re.match(r'^(\d+)$', level_str.strip())
                        cond = {}
                        if m_range:
                            cond['level_range'] = [int(m_range.group(1)), int(m_range.group(2))]
                        elif m_single:
                            lv = int(m_single.group(1))
                            cond['level_range'] = [lv, lv]
                        b_match = re.search(r'([+\-−])(\d+)', str(bonus))
                        outcome = {}
                        if b_match:
                            sign = -1 if b_match.group(1) in ('-', '−') else 1
                            outcome['level_bonus'] = sign * int(b_match.group(2))
                        if cond and outcome:
                            sub = {
                                'id': f'§5.level-bonus.{level_str.strip().lower().replace("–","-").replace(" ","-")}',
                                'section': '§5',
                                'title': f'Level Bonus — Level {level_str.strip()}',
                                'type': 'roll_modifier',
                                'description': f'Level {level_str.strip()}: {bonus} level bonus.',
                                'condition': cond,
                                'outcome': outcome,
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §4 stat pairs (defence pairings)
        elif eid == '§4.the-stat-pairs':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        attack = row.get('Attack Stat', row.get('Stat', ''))
                        defence = row.get('Opposed by', row.get('Defence Stat', row.get('Defended By', '')))
                        if attack and defence:
                            sub = {
                                'id': f'§4.defence-pairing.{attack.lower().replace("-","_")}',
                                'section': '§4',
                                'title': f'Defence Pairing — {attack}',
                                'type': 'roll_modifier',
                                'description': f'{attack} is defended by {defence}.',
                                'condition': {'stat': attack},
                                'outcome': {'defence_stat': defence},
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §7 shadow growth triggers
        elif '§7' in eid or '§8' in eid or '§9' in eid or '§10' in eid:
            # Try to parse shadow growth from description
            if 'shadow' in desc.lower() or 'overthinking' in desc.lower() or 'denial' in desc.lower() or 'fixation' in desc.lower() or 'dread' in desc.lower() or 'madness' in desc.lower():
                for shadow in SHADOW_NAMES:
                    if shadow.lower() in desc.lower():
                        m = re.search(r'([+\-−])(\d+)\s*' + shadow, desc, re.IGNORECASE)
                        if m:
                            sign = -1 if m.group(1) in ('-', '−') else 1
                            if 'condition' not in e:
                                e['condition'] = {}
                            e['outcome'] = e.get('outcome', {})
                            e['outcome']['shadow'] = shadow
                            e['outcome']['shadow_delta'] = sign * int(m.group(2))

        # §10 XP table / level table
        elif eid in ('§10.xp-table', '§10.xp-sources', '§10.level-progression'):
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        # XP sources
                        source = row.get('Source', row.get('Event', ''))
                        xp = row.get('XP', row.get('XP Earned', ''))
                        if source and xp:
                            xp_match = re.search(r'(\d+)', str(xp))
                            if xp_match:
                                sub = {
                                    'id': f'§10.xp-source.{source.lower().replace(" ", "-")[:30]}',
                                    'section': '§10',
                                    'title': f'XP Source — {source}',
                                    'type': 'interest_change',
                                    'description': f'{source}: {xp} XP.',
                                    'condition': {'action': source},
                                    'outcome': {'xp_payout': int(xp_match.group(1))},
                                }
                                result.append(sub)
            result.append(e)
            continue

        result.append(e)

    # Inject archetypes into rules-v3
    archetypes_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'extracted', 'archetypes.yaml')
    if not os.path.exists(archetypes_path):
        raise FileNotFoundError(f"Missing {archetypes_path} required for rules-v3 injection")
    
    arch_entries = load_yaml(archetypes_path)
        
    for a in arch_entries:
        if a.get('type') == 'archetype_definition':
            a['section'] = '§3'
            slug = a.get('title', '').lower().replace(' ', '-').replace('the-', '')
            a['id'] = f"archetype.{slug}"
            result.append(a)

    return result



def enrich_risk_reward(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich risk-reward-and-hidden-depth.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        blocks = e.get('blocks', [])

        if eid == '§2.risk-tiers':
            # Explode table rows into separate entries
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        dc_text = row.get('DC vs Your Modifier', '')
                        tier_name = row.get('Risk Tier', '')
                        bonus_text = row.get('Interest Bonus on Success', '')
                        xp_text = row.get('XP Multiplier', '')
                        if not tier_name:
                            continue
                        cond = {}
                        m = re.search(r'(\d+)\s*or\s*less', dc_text)
                        if m:
                            cond['need_range'] = [1, int(m.group(1))]
                        m = re.search(r'(\d+)\s*[–-]\s*(\d+)', dc_text)
                        if m:
                            cond['need_range'] = [int(m.group(1)), int(m.group(2))]
                        m = re.search(r'(\d+)\+', dc_text)
                        if m:
                            cond['need_range'] = [int(m.group(1)), 99]
                        outcome = {'risk_tier': tier_name}
                        b_match = re.search(r'([+\-−])(\d+)', bonus_text)
                        if b_match:
                            sign = -1 if b_match.group(1) in ('-', '−') else 1
                            outcome['interest_bonus'] = sign * int(b_match.group(2))
                        else:
                            outcome['interest_bonus'] = 0
                        xp_match = re.search(r'([\d.]+)x', xp_text)
                        if xp_match:
                            outcome['xp_multiplier'] = float(xp_match.group(1))
                        sub = {
                            'id': f'§2.risk-tier.{tier_name.lower()}',
                            'section': '§2',
                            'title': f'Risk Tier — {tier_name}',
                            'type': 'roll_modifier',
                            'description': f'Need {dc_text}: {tier_name}. {bonus_text}. {xp_text}.',
                            'condition': cond,
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.weakness-triggers':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        behaviour = row.get('Opponent Behaviour', '')
                        defense = row.get('Defense Window', '')
                        reduction = row.get('DC Reduction', '')
                        if not behaviour:
                            continue
                        slug = behaviour.lower().replace(' ', '-').replace('/', '-')[:30]
                        r_match = re.search(r'([+\-−])(\d+)', reduction)
                        dc_adj = 0
                        if r_match:
                            sign = -1 if r_match.group(1) in ('-', '−') else 1
                            dc_adj = sign * int(r_match.group(2))
                        sub = {
                            'id': f'§3.weakness-trigger.{slug}',
                            'section': '§3',
                            'title': f'Weakness Trigger — {behaviour}',
                            'type': 'roll_modifier',
                            'description': f'When opponent {behaviour.lower()}: {defense} DC {reduction}.',
                            'condition': {'opponent_behaviour': behaviour},
                            'outcome': {'dc_adjustment': dc_adj, 'defence_window': defense},
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.opponent-weakness-window':
            e['condition'] = {'opponent_behaviour': 'crack'}
            e['outcome'] = {'dc_adjustment': -2}

        elif eid == '§4.callback-bonus':
            e['condition'] = {'callback_distance': 2}
            e['outcome'] = {'roll_bonus': 1}

        elif eid == '§4.callback-distances':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        when = row.get('When topic was introduced', '')
                        bonus = row.get('Hidden bonus', '')
                        if not when:
                            continue
                        b_match = re.search(r'([+\-−])(\d+)', bonus)
                        roll_bonus = 0
                        if b_match:
                            sign = -1 if b_match.group(1) in ('-', '−') else 1
                            roll_bonus = sign * int(b_match.group(2))
                        # Determine callback distance
                        dist_match = re.search(r'(\d+)', when)
                        dist = 0
                        if 'opener' in when.lower() or 'first' in when.lower():
                            dist = 0
                            slug = 'opener'
                        elif dist_match:
                            dist = int(dist_match.group(1))
                            slug = f'{dist}-turns'
                            if '+' in when:
                                slug = f'{dist}-plus'
                        else:
                            slug = when.lower().replace(' ', '-')[:20]
                        sub = {
                            'id': f'§4.callback-bonus.{slug}',
                            'section': '§4',
                            'title': f'Callback Bonus — {when}',
                            'type': 'roll_modifier',
                            'description': f'Referencing a topic from {when.lower()} grants {bonus}.',
                            'condition': {'callback_distance': dist},
                            'outcome': {'roll_bonus': roll_bonus},
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§5.stat-combo':
            pass  # Prose-only intro — no enrichment needed

        elif eid == '§5.combo-list':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        combo_name = row.get('Combo', '').strip('*').strip()
                        sequence = row.get('Sequence', '')
                        bonus = row.get('Bonus', '')
                        if not combo_name:
                            continue
                        slug = combo_name.lower().replace(' ', '-').replace('the-', '')
                        # Parse sequence
                        seq_parts = [s.strip() for s in sequence.split('→')]
                        # Parse bonus
                        outcome = {}
                        i_match = re.search(r'([+\-−])(\d+)', bonus)
                        if i_match:
                            sign = -1 if i_match.group(1) in ('-', '−') else 1
                            outcome['interest_bonus'] = sign * int(i_match.group(2))
                        if 'roll' in bonus.lower():
                            r_match = re.search(r'([+\-−])(\d+)\s*to\s*all\s*rolls', bonus)
                            if r_match:
                                s = -1 if r_match.group(1) in ('-', '−') else 1
                                outcome['roll_bonus'] = s * int(r_match.group(2))
                        sub = {
                            'id': f'§5.combo.{slug}',
                            'section': '§5',
                            'title': f'Combo — {combo_name}',
                            'type': 'roll_modifier',
                            'description': f'{combo_name}: {sequence} → {bonus}.',
                            'condition': {'combo_sequence': seq_parts},
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§6.momentum':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        streak = row.get('Streak', '')
                        effect = row.get('Effect', '')
                        if not streak:
                            continue
                        s_match = re.search(r'(\d+)', streak)
                        if not s_match:
                            continue
                        streak_val = int(s_match.group(1))
                        slug = streak.lower().replace(' ', '-').replace('+', 'plus')
                        cond = {}
                        if '+' in streak:
                            cond['streak_minimum'] = streak_val
                        else:
                            cond['streak'] = streak_val
                        outcome = {}
                        r_match = re.search(r'([+\-−])(\d+)\s*to\s*(?:next\s*)?(?:\d*\s*)?rolls?', effect)
                        if r_match:
                            sign = -1 if r_match.group(1) in ('-', '−') else 1
                            outcome['roll_bonus'] = sign * int(r_match.group(2))
                        i_match = re.search(r'([+\-−])(\d+)\s*Interest', effect)
                        if i_match:
                            sign = -1 if i_match.group(1) in ('-', '−') else 1
                            outcome['interest_delta'] = sign * int(i_match.group(2))
                        if 'nothing' in effect.lower():
                            outcome['effect'] = 'none'
                        if outcome:
                            sub = {
                                'id': f'§6.momentum.{slug}',
                                'section': '§6',
                                'title': f'Momentum — {streak}',
                                'type': 'roll_modifier',
                                'description': f'{streak}: {effect}.',
                                'condition': cond,
                                'outcome': outcome,
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§7.opponent-tells-post-roll-feedback':
            e['condition'] = {'action': 'tell_read'}
            e['outcome'] = {'roll_bonus': 2}

        elif eid == '§7.tell-categories':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        says = row.get('Opponent says/does', '')
                        tell_stat = row.get('Tell stat (+2)', '')
                        if not says:
                            continue
                        slug = says.lower().replace(' ', '-').replace('/', '-')[:30]
                        sub = {
                            'id': f'§7.tell.{slug}',
                            'section': '§7',
                            'title': f'Tell — {says}',
                            'type': 'roll_modifier',
                            'description': f'Opponent {says.lower()}: Tell stat {tell_stat} (+2).',
                            'condition': {'opponent_behaviour': says},
                            'outcome': {'roll_bonus': 2, 'tell_stat': tell_stat},
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§8.horniness-forced-options':
            e['condition'] = {'shadow': 'Horniness', 'threshold': 6}
            e['outcome'] = {'forced_stat': 'Rizz'}

        result.append(e)
    return result


def enrich_async_time(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich async-time.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        blocks = e.get('blocks', [])

        if eid == '§3.base-response-time-by-trait':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        trait = row.get('Opponent Trait', '')
                        resp_time = row.get('Base Response Time', '')
                        if not trait:
                            continue
                        slug = trait.lower().replace(' ', '-')[:30]
                        # Try to parse time range
                        m = re.search(r'(\d+)\s*[–-]\s*(\d+)\s*(min|sec|hour)', resp_time, re.IGNORECASE)
                        outcome = {'base_response_time': resp_time}
                        if m:
                            unit = m.group(3).lower()
                            multiplier = 1
                            if unit.startswith('hour'):
                                multiplier = 60
                            elif unit.startswith('sec'):
                                multiplier = 1.0/60
                            outcome['response_time_range_min'] = [round(int(m.group(1)) * multiplier, 1), round(int(m.group(2)) * multiplier, 1)]
                        sub = {
                            'id': f'§3.response-time.{slug}',
                            'section': '§3',
                            'title': f'Response Time — {trait}',
                            'type': 'interest_change',
                            'description': f'{trait}: {resp_time}.',
                            'condition': {'opponent_trait': trait},
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.interest-modifier':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        interest = row.get('Interest Level', '')
                        multiplier = row.get('Time Multiplier', '')
                        if not interest:
                            continue
                        # Parse interest range
                        m = re.search(r'(\d+)\s*[–-]\s*(\d+)', interest)
                        cond = {}
                        if m:
                            cond['interest_range'] = [int(m.group(1)), int(m.group(2))]
                        # Parse multiplier
                        xm = re.search(r'×([\d.]+)', multiplier)
                        outcome = {}
                        if xm:
                            outcome['time_multiplier'] = float(xm.group(1))
                        # Extract state name
                        state_m = re.search(r'\(([^)]+)\)', interest)
                        state_name = state_m.group(1) if state_m else interest
                        slug = state_name.lower().replace(' ', '-')
                        sub = {
                            'id': f'§3.interest-time.{slug}',
                            'section': '§3',
                            'title': f'Interest Time Modifier — {state_name}',
                            'type': 'interest_change',
                            'description': f'{interest}: {multiplier}.',
                            'condition': cond,
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.shadow-modifier':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        shadow = row.get('Shadow', '')
                        effect = row.get('Effect on replies', '')
                        if not shadow:
                            continue
                        slug = shadow.lower().replace(' ', '-')[:30]
                        outcome = {'response_style': effect}
                        m = re.search(r'([+\-−])(\d+)%', effect)
                        if m:
                            sign = -1 if m.group(1) in ('-', '−') else 1
                            outcome['time_modifier_percent'] = sign * int(m.group(2))
                        sub = {
                            'id': f'§3.shadow-response.{slug}',
                            'section': '§3',
                            'title': f'Shadow Response Modifier — {shadow}',
                            'type': 'interest_change',
                            'description': f'{shadow}: {effect}.',
                            'condition': {'shadow': shadow.replace('High ', '')},
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§5.your-response-delay':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        delay = row.get('Your response delay', '')
                        effect = row.get('Effect', '')
                        if not delay:
                            continue
                        slug = delay.lower().replace(' ', '-').replace('<', 'under-').replace('+', 'plus')[:30]
                        cond = {}
                        # Parse timing range in minutes
                        if '<' in delay and 'min' in delay:
                            m = re.search(r'(\d+)', delay)
                            if m:
                                cond['timing_range'] = [0, int(m.group(1))]
                        elif 'hour' in delay.lower():
                            m = re.search(r'(\d+)\s*[–-]\s*(\d+)', delay)
                            if m:
                                cond['timing_range'] = [int(m.group(1)) * 60, int(m.group(2)) * 60]
                            m2 = re.search(r'(\d+)\+\s*hour', delay, re.IGNORECASE)
                            if m2:
                                cond['timing_range'] = [int(m2.group(1)) * 60, 99999]
                        elif 'min' in delay.lower():
                            m = re.search(r'(\d+)\s*[–-]\s*(\d+)', delay)
                            if m:
                                cond['timing_range'] = [int(m.group(1)), int(m.group(2))]
                        # Parse interest delta
                        outcome = {}
                        i_match = re.search(r'([+\-−])(\d+)\s*Interest', effect)
                        if i_match:
                            sign = -1 if i_match.group(1) in ('-', '−') else 1
                            outcome['delay_penalty'] = sign * int(i_match.group(2))
                        else:
                            outcome['delay_penalty'] = 0
                        # Check for conditional interest range
                        cond_match = re.search(r'if\s*(?:they\s*were\s*)?at\s*(\d+)\+', effect)
                        if cond_match:
                            cond['interest_range'] = [int(cond_match.group(1)), 25]
                        sub = {
                            'id': f'§5.response-delay.{slug}',
                            'section': '§5',
                            'title': f'Response Delay — {delay}',
                            'type': 'interest_change',
                            'description': f'{delay}: {effect}.',
                            'condition': cond,
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§6.time-of-day-effects':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        time = row.get('Time', '')
                        modifier = row.get('Horniness Modifier', '')
                        if not time:
                            continue
                        slug = time.split('(')[0].strip().lower().replace(' ', '-')
                        # Parse modifier
                        m_match = re.search(r'([+\-−])(\d+)', modifier)
                        horn_mod = 0
                        if m_match:
                            sign = -1 if m_match.group(1) in ('-', '−') else 1
                            horn_mod = sign * int(m_match.group(2))
                        # Extract time name for condition
                        time_name_m = re.search(r'^([\w\s]+?)(?:\s*\()', time)
                        time_name = time_name_m.group(1).strip() if time_name_m else time.strip()
                        sub = {
                            'id': f'§6.time-of-day.{slug}',
                            'section': '§6',
                            'title': f'Time of Day — {time_name}',
                            'type': 'roll_modifier',
                            'description': f'{time}: Horniness {modifier}.',
                            'condition': {'time_of_day': time_name},
                            'outcome': {'horniness_modifier': horn_mod},
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§7.energy-system':
            e['condition'] = {'formula': 'energy'}
            e['outcome'] = {'energy_per_day': 15, 'energy_per_day_max': 20, 'energy_cost': 1}

        elif eid == '§7.juggling-penalty':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        active = row.get('Active conversations', '')
                        effect = row.get('Effect', '')
                        if not active:
                            continue
                        m = re.search(r'(\d+)', active)
                        if 'Normal' in effect:
                            sub = {
                                'id': '§7.juggling.normal',
                                'section': '§7',
                                'title': 'Juggling — Normal',
                                'type': 'interest_change',
                                'description': f'{active}: {effect}.',
                                'condition': {'active_conversations_range': [1, 3]},
                                'outcome': {'effect': 'none'},
                            }
                            result.append(sub)
                        elif '+' in active and m:
                            sub = {
                                'id': '§7.juggling.overload',
                                'section': '§7',
                                'title': 'Juggling — Overload',
                                'type': 'shadow_growth',
                                'description': f'{active}: {effect}.',
                                'condition': {'active_conversations_range': [int(m.group(1)), 99]},
                                'outcome': {'shadow': 'Overthinking', 'shadow_delta': 1, 'duration_turns': 'per_day'},
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§7.cross-chat-events':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        event = row.get('Event', '')
                        effect = row.get('Cross-Chat Effect', '')
                        if not event:
                            continue
                        slug = event.lower().replace(' ', '-').replace('/', '-')[:30]
                        outcome = {}
                        # Parse shadow deltas
                        for shadow in SHADOW_NAMES:
                            m = re.search(shadow + r'\s*([+\-−])(\d+)', effect)
                            if m:
                                sign = -1 if m.group(1) in ('-', '−') else 1
                                outcome[f'shadow_{shadow.lower()}'] = sign * int(m.group(2))
                        # Parse roll bonus
                        r_match = re.search(r'([+\-−])(\d+)\s*to\s*all\s*rolls', effect)
                        if r_match:
                            sign = -1 if r_match.group(1) in ('-', '−') else 1
                            outcome['roll_bonus'] = sign * int(r_match.group(2))
                        if not outcome:
                            outcome['effect'] = effect
                        sub = {
                            'id': f'§7.cross-chat.{slug}',
                            'section': '§7',
                            'title': f'Cross-Chat — {event}',
                            'type': 'shadow_growth',
                            'description': f'{event}: {effect}.',
                            'condition': {'cross_chat_event': event},
                            'outcome': outcome,
                        }
                        result.append(sub)
            result.append(e)
            continue

        elif eid == '§8.conversation-lifecycle':
            # Parse the outcomes from the code block
            for b in blocks:
                if b.get('kind') == 'code':
                    text = b.get('text', '')
                    # Date Secured
                    if 'Date Secured' in text:
                        result.append({
                            'id': '§8.outcome.date-secured',
                            'section': '§8',
                            'title': 'Outcome — Date Secured',
                            'type': 'interest_change',
                            'description': 'Interest reaches 20: Date Secured. XP payout, shadow reduction.',
                            'condition': {'interest_range': [20, 25]},
                            'outcome': {'xp_payout': 50, 'shadow_reduction': True},
                        })
                    if 'Unmatched' in text:
                        result.append({
                            'id': '§8.outcome.unmatched',
                            'section': '§8',
                            'title': 'Outcome — Unmatched',
                            'type': 'interest_change',
                            'description': 'Interest reaches 0: Unmatched. Dread +1.',
                            'condition': {'interest_range': [0, 0]},
                            'outcome': {'shadow': 'Dread', 'shadow_delta': 1},
                        })
                    if 'Ghosted' in text:
                        result.append({
                            'id': '§8.outcome.ghosted',
                            'section': '§8',
                            'title': 'Outcome — Ghosted',
                            'type': 'interest_change',
                            'description': '48h game silence: Ghosted. Dread +1.',
                            'condition': {'timing_range': [2880, 99999]},
                            'outcome': {'shadow': 'Dread', 'shadow_delta': 1},
                        })
                    if 'Fizzled' in text:
                        result.append({
                            'id': '§8.outcome.fizzled',
                            'section': '§8',
                            'title': 'Outcome — Fizzled',
                            'type': 'interest_change',
                            'description': 'Interest 5-9 with 24h silence: Fizzled. Archived, no penalty.',
                            'condition': {'interest_range': [5, 9], 'timing_range': [1440, 99999]},
                            'outcome': {'effect': 'archived'},
                        })
                    if 'Paused' in text:
                        result.append({
                            'id': '§8.outcome.paused',
                            'section': '§8',
                            'title': 'Outcome — Paused',
                            'type': 'interest_change',
                            'description': 'Paused: re-engage later. Interest decays -1/day of silence.',
                            'condition': {'action': 'pause'},
                            'outcome': {'interest_delta': -1, 'duration_turns': 'per_day'},
                        })
            result.append(e)
            continue

        result.append(e)
    return result


def enrich_traps(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich traps.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # Individual trap entries
        trap_map = {
            '§2.the-cringe': ('Charm', 6, 1, 'disadvantage', 'Charm', 0),
            '§2.the-creep': ('Rizz', 6, 2, 'stat_penalty', 'Rizz', -2),
            '§2.the-overshare': ('Honesty', 6, 1, 'opponent_dc_increase', 'Chaos', 2),
            '§2.the-unhinged': ('Chaos', 6, 1, 'disadvantage', 'Chaos', 0),
            '§2.the-pretentious': ('Wit', 6, 1, 'opponent_dc_increase', 'Rizz', 3),
            '§2.the-spiral': ('Self-Awareness', 6, 2, 'disadvantage', 'Self-Awareness', 0),
        }

        if eid in trap_map:
            stat, miss_min, duration, effect_type, effect_stat, effect_val = trap_map[eid]
            e['condition'] = {
                'failed_stat': stat,
                'miss_minimum': miss_min,
            }
            outcome = {
                'trap_name': eid.split('.')[-1],
                'duration_turns': duration,
                'effect': effect_type,
                'stat': effect_stat,
            }
            if effect_val != 0:
                outcome['effect_value'] = effect_val
            e['outcome'] = outcome

        elif eid == '§1.how-traps-work':
            e['condition'] = {'miss_range': [6, 9]}
            e['outcome'] = {'trap': True, 'duration_turns': 1}

        elif eid == '§3.trap-summary-table':
            # Already covered by individual trap entries, keep as-is
            pass

        elif eid == '§4.adding-custom-traps':
            # Template/instructions, no enrichment needed
            pass

        result.append(e)
    return result


def enrich_archetypes(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich archetypes.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # How archetypes work has some mechanical info
        if eid == '§1.how-archetypes-work':
            e['condition'] = {'formula': 'archetype_selection'}
            e['outcome'] = {'archetype_count': 2}

        # Archetype summary table — enrich individual rows
        elif eid == '§3.archetype-summary-table':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        name = row.get('Archetype', '')
                        stats = row.get('Key High Stats', '')
                        shadow = row.get('Key Shadow', '')
                        level_range = row.get('Level Range', '')
                        if not name:
                            continue
                        slug = name.lower().replace(' ', '-').replace('the-', '')
                        cond = {}
                        if level_range:
                            m = re.search(r'(\d+)\s*[–-]\s*(\d+)', level_range)
                            if m:
                                cond['level_range'] = [int(m.group(1)), int(m.group(2))]
                        outcome = {}
                        if stats and stats != '—':
                            outcome['key_stats'] = stats
                        if shadow:
                            outcome['key_shadow'] = shadow
                        if cond or outcome:
                            sub = {
                                'id': f'§3.archetype.{slug}',
                                'section': '§3',
                                'title': f'Archetype — {name}',
                                'type': 'definition',
                                'description': f'{name}: Stats={stats}, Shadow={shadow}, Levels={level_range}.',
                                'condition': cond,
                                'outcome': outcome,
                            }
                            result.append(sub)
            result.append(e)
            continue

        # Individual archetype entries — parse stats and level range from blocks
        elif '§2.' in eid and eid not in ('§2.archetype-reference', '§2.tier-1-low-level-levels-13',
                '§2.tier-2-early-game-levels-26', '§2.tier-3-mid-game-levels-39',
                '§2.tier-4-high-level-levels-511'):
            all_text = desc
            for b in blocks:
                if b.get('kind') == 'paragraph':
                    all_text += ' ' + b.get('text', '')

            cond = {}
            outcome = {}

            # Parse level range
            lr = re.search(r'\*\*Level\s*range:\*\*\s*(\d+)\s*[–-]\s*(\d+)', all_text)
            if lr:
                cond['level_range'] = [int(lr.group(1)), int(lr.group(2))]

            # Parse high stats
            high_m = re.search(r'\*\*Stats:\*\*\s*High\s+([\w,\s-]+?)(?:\s*\||\s*\*\*)', all_text)
            if high_m:
                stats = [s.strip() for s in high_m.group(1).split(',') if s.strip()]
                outcome['high_stats'] = stats

            # Parse shadow stats
            shadow_m = re.search(r'\*\*Shadow:\*\*\s*High\s+([\w,\s]+?)(?:\s*$|\s*\n|\s*\*\*)', all_text)
            if shadow_m:
                shadows = [s.strip() for s in shadow_m.group(1).split(',') if s.strip()]
                outcome['key_shadow'] = ', '.join(shadows)

            if cond:
                e['condition'] = cond
            if outcome:
                e['outcome'] = outcome

        result.append(e)
    return result


def enrich_character_construction(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich character-construction.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        if eid == '§2.slot-structure':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        slot = row.get('Slot', '')
                        occupancy = row.get('Occupancy', row.get('Count', ''))
                        effect_scope = row.get('Effect scope', row.get('Effect', ''))
                        if not slot:
                            continue
                        slug = slot.lower().replace(' ', '-').replace('×', 'x')[:30]
                        outcome = {}
                        c_match = re.search(r'(\d+)', str(occupancy))
                        if c_match:
                            outcome['slot_count'] = int(c_match.group(1))
                        if effect_scope:
                            outcome['stat_focus'] = effect_scope
                        if outcome:
                            sub = {
                                'id': f'§2.slot.{slug}',
                                'section': '§2',
                                'title': f'Slot — {slot}',
                                'type': 'definition',
                                'description': f'{slot}: occupancy={occupancy}, scope={effect_scope}.',
                                'condition': {'slot': slot},
                                'outcome': outcome,
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§2.4-archetype-tendencies':
            e['condition'] = {'formula': 'archetype_tendency'}
            e['outcome'] = {'archetype_stat_weight': 'dominant_stat'}

        elif eid == '§3.stat-descriptors-dynamic-scaled-to-effective-value':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        eff_mod = row.get('Effective modifier', '')
                        descriptor = row.get('Descriptor', '')
                        if not eff_mod:
                            continue
                        # Parse ranges like "+4 or higher", "+2 to +3", "0 to +1", etc.
                        m = re.search(r'([+\-−]?\d+)\s*(?:to|–|-)\s*([+\-−]?\d+)', str(eff_mod))
                        m_single = re.search(r'([+\-−]?\d+)\s*or\s*(higher|lower)', str(eff_mod))
                        cond = {}
                        if m:
                            lo = int(m.group(1).replace('+', ''))
                            hi = int(m.group(2).replace('+', ''))
                            cond['stat_range'] = [lo, hi]
                        elif m_single:
                            val = int(m_single.group(1).replace('+', ''))
                            if 'higher' in m_single.group(2):
                                cond['stat_range'] = [val, 99]
                            else:
                                cond['stat_range'] = [-99, val]
                        if cond:
                            slug = eff_mod.replace('+', 'p').replace('-', 'm').replace(' ', '').replace('−', 'm')[:20]
                            sub = {
                                'id': f'§3.stat-descriptor.{slug}',
                                'section': '§3',
                                'title': f'Stat Descriptor — {eff_mod}',
                                'type': 'definition',
                                'description': f'Effective modifier {eff_mod}: {descriptor}.',
                                'condition': cond,
                                'outcome': {'descriptor': descriptor.strip('"')},
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.35-opponent-message-generation':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        interest = row.get('Interest', '')
                        block_text = row.get('Block text', '')
                        if not interest:
                            continue
                        m = re.search(r'(\d+)\s*[–-]\s*(\d+)', str(interest))
                        if m:
                            slug = f'{m.group(1)}-{m.group(2)}'
                            sub = {
                                'id': f'§3.opponent-tone.{slug}',
                                'section': '§3',
                                'title': f'Opponent Tone — Interest {interest}',
                                'type': 'template',
                                'description': f'Interest {interest}: {block_text}',
                                'condition': {'interest_range': [int(m.group(1)), int(m.group(2))]},
                                'outcome': {'quality_boost': block_text.strip('"')[:80]},
                            }
                            result.append(sub)
            result.append(e)
            continue

        elif eid == '§3.dynamic-assembly':
            # Has references to fragment assembly with numeric thresholds
            all_text = desc
            for b in blocks:
                all_text += ' ' + str(b.get('text', ''))
            if any(c.isdigit() for c in all_text):
                e['condition'] = {'formula': 'dynamic_assembly'}
                e['outcome'] = {'effect': 'fragment_concatenation'}

        result.append(e)
    return result


def enrich_items_pool(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich items-pool.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # Item entries (§12.*) have stat modifiers
        if eid.startswith('§12.'):
            # Parse stat modifiers from description
            mods = parse_stat_modifiers(desc)
            if mods:
                # Parse tier
                tier_m = re.search(r'\*\*Tier:\*\*\s*(\w+)', desc)
                tier = tier_m.group(1) if tier_m else 'Unknown'
                e['condition'] = {'item': e.get('title', ''), 'tier': tier}
                e['outcome'] = {'stat_modifiers': mods}

        # Slot/placement tables
        elif eid == '§2.anatomical-placement':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        slot = row.get('Slot', '')
                        placement = row.get('Placement', row.get('Location', ''))
                        stat = row.get('Primary Stat', row.get('Stat Focus', ''))
                        if not slot:
                            continue
                        if stat:
                            slug = slot.lower().replace(' ', '-')[:30]
                            sub = {
                                'id': f'§2.placement.{slug}',
                                'section': '§2',
                                'title': f'Placement — {slot}',
                                'type': 'definition',
                                'description': f'{slot}: {placement}. Primary stat: {stat}.',
                                'condition': {'slot': slot},
                                'outcome': {'primary_stat': stat},
                            }
                            result.append(sub)
            result.append(e)
            continue

        # Fragment types
        elif eid == '§4.fragment-types':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        frag_type = row.get('Fragment', row.get('Type', ''))
                        purpose = row.get('Purpose', row.get('Description', ''))
                        if not frag_type:
                            continue
                        slug = frag_type.lower().replace(' ', '-')[:30]
                        sub = {
                            'id': f'§4.fragment.{slug}',
                            'section': '§4',
                            'title': f'Fragment Type — {frag_type}',
                            'type': 'definition',
                            'description': f'{frag_type}: {purpose}.',
                            'condition': {'fragment_type': frag_type},
                            'outcome': {'purpose': purpose},
                        }
                        result.append(sub)
            result.append(e)
            continue

        # Quick reference tables (hats, shirts, etc.)
        elif 'quick-reference' in eid:
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        item_name = row.get('Item', row.get('Name', ''))
                        tier = row.get('Tier', '')
                        stats = row.get('Stats', '')
                        if not item_name:
                            continue
                        mods = parse_stat_modifiers(str(stats))
                        if mods:
                            slug = item_name.lower().replace(' ', '-').replace("'", '').replace('"', '')[:30]
                            sub = {
                                'id': f'{eid}.{slug}',
                                'section': e.get('section', ''),
                                'title': f'{item_name}',
                                'type': 'definition',
                                'description': f'{item_name}: Tier {tier}, Stats: {stats}.',
                                'condition': {'item': item_name, 'tier': tier},
                                'outcome': {'stat_modifiers': mods},
                            }
                            result.append(sub)
            result.append(e)
            continue

        result.append(e)
    return result


def enrich_anatomy_parameters(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich anatomy-parameters.yaml entries."""
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')
        desc = str(e.get('description', ''))
        blocks = e.get('blocks', [])

        # §1 parameters overview table
        if eid == '§1.parameters':
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        param = row.get('Parameter', '')
                        levels = row.get('Levels', '')
                        stat_effect = row.get('Stat effect', '')
                        if not param:
                            continue
                        slug = param.lower().replace(' ', '-')
                        sub = {
                            'id': f'§1.param.{slug}',
                            'section': '§1',
                            'title': f'Parameter — {param}',
                            'type': 'definition',
                            'description': f'{param}: Levels = {levels}. Stat effect: {stat_effect}.',
                            'condition': {'parameter': param, 'levels': levels},
                            'outcome': {'stat_effect': stat_effect},
                        }
                        result.append(sub)
            result.append(e)
            continue

        # Individual anatomy tier entries (§3.short, §3.medium, etc.)
        # These have stat modifiers in their description
        elif any(eid.startswith(f'§{n}.') for n in range(3, 20)):
            # Check if it's a tier entry with stats
            mods = parse_stat_modifiers(desc)
            if mods:
                e['condition'] = {'anatomy_tier': e.get('title', '')}
                e['outcome'] = {'stat_modifiers': mods}
            elif 'none' in desc.lower() and 'baseline' in desc.lower():
                e['condition'] = {'anatomy_tier': e.get('title', '')}
                e['outcome'] = {'stat_modifiers': {}, 'baseline': True}

        result.append(e)
    return result


def enrich_extensibility(entries: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Enrich extensibility.yaml entries.
    
    Extensibility entries are mostly templates and definitions describing
    how to add custom content. Very few contain mechanical thresholds.
    """
    result = []
    for e in entries:
        e = copy.deepcopy(e)
        eid = e.get('id', '')

        # §4.modding-support has a table with extensibility layers
        if eid == '§4.modding-support':
            blocks = e.get('blocks', [])
            for b in blocks:
                if b.get('kind') == 'table':
                    for row in b.get('rows', []):
                        layer = row.get('Layer', row.get('Content Type', ''))
                        path = row.get('Path', row.get('Directory', ''))
                        if layer and path:
                            slug = layer.lower().replace(' ', '-')[:25]
                            sub = {
                                'id': f'§4.mod-layer.{slug}',
                                'section': '§4',
                                'title': f'Mod Layer — {layer}',
                                'type': 'definition',
                                'description': f'{layer}: {path}.',
                                'condition': {'formula': 'modding'},
                                'outcome': {'mod_path': path},
                            }
                            result.append(sub)
            result.append(e)
            continue

        # §5 has runtime assembly mechanics
        elif eid == '§5.llm-prompt-assembly-runtime':
            e['condition'] = {'formula': 'prompt_assembly'}
            e['outcome'] = {'effect': 'fragment_concatenation'}

        result.append(e)
    return result


# ─── Main pipeline ────────────────────────────────────────────────────

ENRICHERS = {
    'rules-v3': enrich_rules_v3,
    'risk-reward-and-hidden-depth': enrich_risk_reward,
    'async-time': enrich_async_time,
    'traps': enrich_traps,
    'archetypes': enrich_archetypes,
    'character-construction': enrich_character_construction,
    'items-pool': enrich_items_pool,
    'anatomy-parameters': enrich_anatomy_parameters,
    'extensibility': enrich_extensibility,
}


def count_enriched(entries: List[Dict[str, Any]]) -> Tuple[int, int]:
    """Returns (total_entries, enriched_count)."""
    total = len(entries)
    enriched = sum(1 for e in entries if 'condition' in e or 'outcome' in e)
    return total, enriched


def validate_vocabulary(entries: List[Dict[str, Any]], filename: str) -> List[str]:
    """Validate that all condition/outcome keys are from known vocabulary."""
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
        # Cross-chat shadow keys
        'shadow_dread', 'shadow_madness', 'shadow_overthinking',
        # Archetype keys
        'high_stats', 'key_stats', 'key_shadow',
        # Extensibility keys
        'mod_path',
        # Additional keys
        'opponent_action', 'actions', 'tier_stat_range',
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


def main():
    base_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'extracted')

    summary = []
    all_issues = []
    grand_total = 0
    grand_enriched = 0

    for name, enricher in ENRICHERS.items():
        src_path = os.path.join(base_dir, f'{name}.yaml')
        dst_path = os.path.join(base_dir, f'{name}-enriched.yaml')

        if not os.path.exists(src_path):
            print(f"SKIP: {src_path} not found")
            continue

        entries = load_yaml(src_path)
        enriched_entries = enricher(entries)
        save_yaml(dst_path, enriched_entries)

        total, enriched = count_enriched(enriched_entries)
        grand_total += total
        grand_enriched += enriched
        summary.append((f'{name}-enriched.yaml', total, enriched))

        issues = validate_vocabulary(enriched_entries, name)
        all_issues.extend(issues)

    # Print summary
    print("\nEnrichment Summary")
    print("=" * 60)
    for fname, total, enriched in summary:
        print(f"  {fname:<50} {total:>3} entries, {enriched:>3} enriched")
    print("=" * 60)
    print(f"  Total: {grand_total} entries, {grand_enriched} enriched")
    print()

    if all_issues:
        print(f"Vocabulary issues ({len(all_issues)}):")
        for issue in all_issues:
            print(f"  WARNING: {issue}")
    else:
        print("Vocabulary check: PASS (0 issues)")

    # Write summary to file
    summary_path = os.path.join(base_dir, 'enrichment-summary.txt')
    with open(summary_path, 'w') as f:
        f.write("Enrichment Summary\n")
        f.write("=" * 60 + "\n")
        for fname, total, enriched in summary:
            f.write(f"  {fname:<50} {total:>3} entries, {enriched:>3} enriched\n")
        f.write("=" * 60 + "\n")
        f.write(f"  Total: {grand_total} entries, {grand_enriched} enriched\n")

    return 0 if not all_issues else 1


if __name__ == '__main__':
    sys.exit(main())
