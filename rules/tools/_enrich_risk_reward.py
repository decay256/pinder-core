#!/usr/bin/env python3
# AUTO-GENERATED
import copy
import os
import re
from typing import Any, Dict, List, Tuple, Optional, Set
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers, load_yaml, save_yaml

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

