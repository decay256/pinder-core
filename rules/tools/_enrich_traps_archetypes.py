#!/usr/bin/env python3
# AUTO-GENERATED
import copy
import os
import re
from typing import Any, Dict, List, Tuple, Optional, Set
from _enrich_shared import SHADOW_NAMES, STAT_NAMES, parse_stat_modifiers, load_yaml, save_yaml

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
            '§2.the-overshare': ('Honesty', 6, 1, 'datee_dc_increase', 'Chaos', 2),
            '§2.the-unhinged': ('Chaos', 6, 1, 'disadvantage', 'Chaos', 0),
            '§2.the-pretentious': ('Wit', 6, 1, 'datee_dc_increase', 'Rizz', 3),
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

