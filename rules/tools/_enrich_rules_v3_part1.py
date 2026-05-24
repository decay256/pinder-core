#!/usr/bin/env python3
# AUTO-GENERATED - DO NOT EDIT MANUALLY
import re
from typing import Any, Dict, List, Optional
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers

def enrich_rules_v3_part1(e: Dict[str, Any], eid: str, desc: str, blocks: List[Dict[str, Any]]) -> Optional[List[Dict[str, Any]]]:
    res = []

    if eid == '§4.shadow-penalty':
        e['condition'] = {'shadow': 'any', 'shadow_points_per_penalty': 3}
        e['outcome'] = {'stat_penalty_per_step': -1}
        return [e]

    elif eid == '§4.starting-stats':
        e['condition'] = {'conversation_start': True}
        e['outcome'] = {'base_positive_stats': 8, 'base_shadow_stats': 0}
        return [e]

    elif eid == '§5.dc-examples':
        e['condition'] = {'formula': 'defense_dc'}
        e['outcome'] = {'base_dc': 13, 'addend': 'opponent_stat_modifier'}
        return [e]

    elif eid == '§6.basic-roll':
        e['condition'] = {'formula': 'attack_roll'}
        e['outcome'] = {'roll': 'd20', 'addend': 'stat_modifier + level_bonus'}
        return [e]

    elif eid == '§6.natural-1-and-natural-20':
        res.append(e)
        nat1 = {
            'id': '§6.natural-1',
            'section': '§6',
            'title': 'Natural 1 — Auto-fail',
            'type': 'roll_modifier',
            'description': 'Nat 1: Auto-fail regardless of modifier. Legendary disaster.',
            'condition': {'natural_roll': 1},
            'outcome': {'tier': 'Legendary', 'interest_delta': -5, 'xp_payout': 10, 'trap': True}
        }
        res.append(nat1)
        nat20 = {
            'id': '§6.natural-20',
            'section': '§6',
            'title': 'Natural 20 — Auto-success',
            'type': 'roll_modifier',
            'description': 'Nat 20: Auto-success regardless of DC. Crit. +4 Interest. Advantage on next roll.',
            'condition': {'natural_roll': 20},
            'outcome': {'interest_delta': 4, 'effect': 'advantage_next_roll', 'xp_payout': 25}
        }
        res.append(nat20)
        return res

    elif eid == '§6.advantage-disadvantage':
        e['condition'] = {'effect': 'advantage_or_disadvantage'}
        e['outcome'] = {'roll': '2d20_take_best_or_worst'}
        return [e]

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
                    res.append(sub)
        res.append(e)
        return res

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
                    res.append(sub)
        res.append(e)
        return res

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
                    res.append(sub)
        res.append(e)
        return res

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
                        res.append(sub)
        res.append(e)
        return res

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
                        res.append(sub)
        res.append(e)
        return res

    elif eid.startswith('§9.') and eid.endswith(('-wit', '-charm', '-honesty', '-chaos', '-self-awareness')):
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
                    res.append(sub)
        res.append(e)
        return res

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
                                res.append(sub)
                            break
        res.append(e)
        return res

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
                        res.append(sub)
        res.append(e)
        return res

    return None
