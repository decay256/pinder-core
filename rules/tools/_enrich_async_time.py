#!/usr/bin/env python3
# AUTO-GENERATED
import copy
import os
import re
from typing import Any, Dict, List, Tuple, Optional, Set
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers, load_yaml, save_yaml

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

