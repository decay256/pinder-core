#!/usr/bin/env python3
# AUTO-GENERATED - DO NOT EDIT MANUALLY
import re
from typing import Any, Dict, List, Optional
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers

def enrich_rules_v3_part2(e: Dict[str, Any], eid: str, desc: str, blocks: List[Dict[str, Any]]) -> Optional[List[Dict[str, Any]]]:
    res = []

    if eid == '§10.your-turn':
        e['condition'] = {'action': 'player_turn'}
        e['outcome'] = {'actions': ['Speak', 'Read', 'Recover', 'Wait']}
        return [e]

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
                        res.append(sub)
        res.append(e)
        return res

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
                    res.append(sub)
        res.append(e)
        return res

    elif eid == '§6.6-the-interest-meter':
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
                    res.append(sub)
        res.append(e)
        return res

    elif eid == '§9.horniness-penalizes-rizz':
        e['condition'] = {'shadow': 'Horniness', 'conversation_start': True}
        e['outcome'] = {'roll': '1d10', 'shadow': 'Horniness'}
        return [e]

    elif eid == '§11.item-slots':
        e['condition'] = {'formula': 'item_slots'}
        e['outcome'] = {'slots': ['Hat', 'Shirt', 'Trousers', 'Shoes', 'Accessory', 'Accessory', 'Frame']}
        return [e]

    elif eid == '§12.spending-build-points':
        e['condition'] = {'formula': 'build_points'}
        e['outcome'] = {'stat_cap': 6}
        return [e]

    elif eid == '§12.character-creation-level-1-starting-budget':
        e['condition'] = {'level_range': [1, 1]}
        e['outcome'] = {'build_points': 12, 'stat_cap': 4}
        return [e]

    elif eid == '§14.matchmaking':
        e['condition'] = {'formula': 'matchmaking'}
        e['outcome'] = {'level_range_offset': 2}
        return [e]

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
                    res.append(sub)
        res.append(e)
        return res

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
                    res.append(sub)
        res.append(e)
        return res

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
                        res.append(sub)
        res.append(e)
        return res

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
                        res.append(sub)
        res.append(e)
        return res

    elif '§7' in eid or '§8' in eid or '§9' in eid or '§10' in eid:
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
        return [e]

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
                            res.append(sub)
        res.append(e)
        return res

    return None
